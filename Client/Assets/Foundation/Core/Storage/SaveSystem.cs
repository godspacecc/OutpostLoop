using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存运行时存档事务快照，包含当前存档克隆和各分区快照，用于失败回滚。
    /// </summary>
    public sealed class SaveRuntimeSnapshot
    {
        internal SaveRuntimeSnapshot(string currentSaveId, SaveGameData currentData, Dictionary<string, JToken> providerData, bool dirty, float dirtyAt, float firstDirtyAt, float retryAt, HashSet<string> dirtySections, bool captureAll)
        {
            CurrentSaveId = currentSaveId;
            CurrentData = currentData?.Clone();
            ProviderData = SaveGameData.CloneSections(providerData);
            Dirty = dirty; DirtyAt = dirtyAt; FirstDirtyAt = firstDirtyAt; RetryAt = retryAt;
            DirtySections = new HashSet<string>(dirtySections, StringComparer.Ordinal); CaptureAll = captureAll;
        }

        public string CurrentSaveId { get; }
        public SaveGameData CurrentData { get; }
        internal Dictionary<string, JToken> ProviderData { get; }
        internal bool Dirty { get; }
        internal float DirtyAt { get; }
        internal float FirstDirtyAt { get; }
        internal float RetryAt { get; }
        internal HashSet<string> DirtySections { get; }
        internal bool CaptureAll { get; }
    }

    /// <summary>
    /// 管理存档创建、准备读取、提交、删除和分区快照事务；失败路径会恢复到进入事务前的稳定数据。
    /// </summary>
    public sealed class SaveSystem : AbstractSystem
    {
        public const int MaxSaveNameLength = 20;
        private const float AutoSaveDelay = 0.5f;
        private readonly Dictionary<string, ISaveSectionProvider> providers = new Dictionary<string, ISaveSectionProvider>(StringComparer.Ordinal);
        private SaveFileStore store;
        private ISaveClock clock = new UnitySaveClock();
        private bool dirty;
        private float dirtyAt;
        /// <summary>首次未保存修改及下次自动重试的实时秒数；连续修改最多延迟五秒。</summary>
        private float firstDirtyAt, retryAt;
        private const float MaximumSaveDelay = 5f, FailureRetryDelay = 5f;
        private readonly HashSet<string> dirtySections = new HashSet<string>(StringComparer.Ordinal);
        private bool captureAll;
        public bool HasPendingChanges => dirty;
        public string LastSaveError { get; private set; }
        public DateTime? LastSavedAtUtc { get; private set; }

        public enum SaveNameValidationResult
        {
            Valid,
            Empty,
            TooLong,
            Duplicate
        }

        protected override void OnInit()
        {
            store ??= new SaveFileStore();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            Flush(out _);
            LifeCycle.UnregisterUpdate(Update);
        }

        public void ConfigureForTests(string rootDirectory, ISaveClock clock = null, SaveMigrationRegistry migrations = null)
        {
            store = new SaveFileStore(rootDirectory, migrations);
            this.clock = clock ?? new UnitySaveClock();
        }

        public bool RegisterSection(ISaveSectionProvider provider, out string error)
        {
            error = null;
            if (provider == null || string.IsNullOrWhiteSpace(provider.SectionId))
            {
                error = "存档分区无效";
                return false;
            }

            if (providers.ContainsKey(provider.SectionId))
            {
                error = $"存档分区重复：{provider.SectionId}";
                return false;
            }

            providers.Add(provider.SectionId, provider);
            return true;
        }

        public IReadOnlyList<SaveSlotInfo> GetSlots()
        {
            return store.ListSlots();
        }

        public SaveNameValidationResult ValidateName(string name, string excludedSaveId = null)
        {
            var normalizedName = NormalizeName(name);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return SaveNameValidationResult.Empty;
            }

            if (normalizedName.Length > MaxSaveNameLength)
            {
                return SaveNameValidationResult.TooLong;
            }

            var duplicate = GetSlots().Any(slot =>
                slot != null &&
                !string.Equals(slot.SaveId, excludedSaveId, StringComparison.Ordinal) &&
                string.Equals(NormalizeName(slot.SaveName), normalizedName, StringComparison.OrdinalIgnoreCase));

            return duplicate ? SaveNameValidationResult.Duplicate : SaveNameValidationResult.Valid;
        }

        public string GetNextDefaultName(string localizedPrefix)
        {
            var prefix = string.IsNullOrWhiteSpace(localizedPrefix) ? "Save" : localizedPrefix.Trim();
            var pattern = $"^{Regex.Escape(prefix)} ([1-9][0-9]*)$";
            var max = 0;

            foreach (var slot in GetSlots())
            {
                var name = NormalizeName(slot?.SaveName);
                var match = Regex.Match(name, pattern, RegexOptions.CultureInvariant);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
                {
                    max = Math.Max(max, value);
                }
            }

            return $"{prefix} {max + 1}";
        }

        public SaveCreateResult CreateSlot(string name, string defaultMapId)
        {
            var normalizedName = NormalizeName(name);
            var validation = ValidateName(normalizedName);
            if (validation != SaveNameValidationResult.Valid)
            {
                return new SaveCreateResult { Success = false, Error = GetValidationErrorText(validation) };
            }

            SaveRuntimeSnapshot snapshot;
            try { snapshot = CaptureRuntimeSnapshot(); }
            catch (Exception exception)
            {
                return new SaveCreateResult { Error = "捕获存档快照失败：" + exception.Message };
            }

            var data = SaveGameData.Create(Guid.NewGuid().ToString("N"), normalizedName, defaultMapId, clock.UtcNow);
            string captureError = null;
            try
            {
                foreach (var provider in providers.Values)
                {
                    provider.Reset();
                }
                CaptureSections(data);
            }
            catch (Exception exception) { captureError = "初始化存档分区失败：" + exception.Message; }

            // 先恢复运行态再写盘，恢复失败时不留下被报告为成功的新槽。
            if (!RestoreRuntimeSnapshot(snapshot, out var restoreError))
                return new SaveCreateResult { Error = restoreError };
            if (captureError != null)
                return new SaveCreateResult { Error = captureError };
            if (!store.Save(data, out var error))
                return new SaveCreateResult { Error = error };
            return new SaveCreateResult
            {
                Success = true,
                Data = data.Clone(),
                Slot = SaveFileStore.ToSlot(data, false, null)
            };
        }

        /// <summary>
        /// 读取并校验指定存档，但不修改当前运行态；调用方成功后再进入地图与提交阶段。
        /// </summary>
        public SavePrepareResult PrepareLoad(string saveId)
        {
            if (!store.TryLoadById(saveId, out var data, out var error))
            {
                return new SavePrepareResult { Success = false, Error = error };
            }

            foreach (var provider in providers.Values)
            {
                data.Sections.TryGetValue(provider.SectionId, out var sectionData);
                if (!provider.Validate(sectionData, out error))
                {
                    return new SavePrepareResult { Success = false, Error = error, Data = data.Clone() };
                }
            }

            return new SavePrepareResult
            {
                Success = true,
                Data = data.Clone(),
                Slot = SaveFileStore.ToSlot(data, false, null)
            };
        }

        /// <summary>
        /// 将已准备好的存档提交到运行态；任一分区恢复失败时回滚模型和分区快照。
        /// </summary>
        public bool CommitPreparedSave(SaveGameData data, out string error)
        {
            error = null;
            if (data == null)
            {
                error = "缺少存档数据";
                return false;
            }

            var model = this.GetModel<SaveModel>();
            var oldData = model.CurrentData?.Clone();
            var captured = CaptureProviderState();
            try
            {
                foreach (var provider in providers.Values)
                {
                    data.Sections.TryGetValue(provider.SectionId, out var sectionData);
                    provider.Restore(sectionData);
                }

                model.SetCurrent(data);
                ResetPending();
                return true;
            }
            catch (Exception e)
            {
                try { RestoreProviderState(captured); }
                catch (Exception rollback)
                {
                    model.SetCurrent(oldData);
                    error = $"提交存档失败：{e.Message}；分区回滚失败：{rollback.Message}";
                    return false;
                }
                model.SetCurrent(oldData);
                error = $"提交存档运行态失败：{e.Message}";
                return false;
            }
        }

        public SaveRuntimeSnapshot CaptureRuntimeSnapshot()
        {
            var model = this.GetModel<SaveModel>();
            return new SaveRuntimeSnapshot(model.CurrentSaveId, model.CurrentData, CaptureProviderState(), dirty, dirtyAt, firstDirtyAt, retryAt, dirtySections, captureAll);
        }

        /// <summary>
        /// 恢复进入流程前捕获的运行时快照；目标恢复失败时会尝试回滚到调用前稳定状态。
        /// </summary>
        public bool RestoreRuntimeSnapshot(SaveRuntimeSnapshot snapshot, out string error)
        {
            error = string.Empty;
            if (snapshot == null)
            {
                error = "缺少运行时存档快照";
                return false;
            }

            var beforeRestore = CaptureRuntimeSnapshot();
            try
            {
                RestoreProviderState(snapshot.ProviderData);
                this.GetModel<SaveModel>().SetCurrent(snapshot.CurrentData);
                RestorePending(snapshot);
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    RestoreProviderState(beforeRestore.ProviderData);
                    this.GetModel<SaveModel>().SetCurrent(beforeRestore.CurrentData);
                    RestorePending(beforeRestore);
                }
                catch (Exception restoreException)
                {
                    error = $"恢复目标快照失败：{e.Message}；回滚旧运行时快照也失败：{restoreException.Message}";
                    return false;
                }

                error = $"恢复运行时存档快照失败：{e.Message}";
                return false;
            }
        }

        public bool Rename(string saveId, string name, out string error)
        {
            var normalizedName = NormalizeName(name);
            var validation = ValidateName(normalizedName, saveId);
            if (validation != SaveNameValidationResult.Valid)
            {
                error = GetValidationErrorText(validation);
                return false;
            }

            if (!store.TryLoadById(saveId, out var data, out error))
            {
                return false;
            }

            data.SaveName = normalizedName;
            data.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            return store.Save(data, out error);
        }

        public bool Delete(string saveId, out string error)
        {
            if (this.GetModel<SaveModel>().CurrentSaveId == saveId)
            {
                error = "运行中不能删除当前存档";
                return false;
            }

            return store.Delete(saveId, out error);
        }

        public bool ClearAll(out string error)
        {
            if (!string.IsNullOrWhiteSpace(this.GetModel<SaveModel>().CurrentSaveId))
            {
                error = "运行中存在当前存档，不能清空存档";
                return false;
            }

            return store.ClearAll(out error);
        }

        /// <summary>兼容旧调用者：标记所有分区，合并短时间修改但不延后最长保存期限。</summary>
        public void MarkDirty(string reason)
        {
            if (!MarkPending()) return;
            captureAll = true;
        }

        /// <summary>仅捕获指定分区；未知分区立即拒绝，避免漏存。旧 MarkDirty 仍执行完整捕获。</summary>
        public void MarkSectionDirty(string sectionId, string reason)
        {
            if (!providers.ContainsKey(sectionId)) throw new ArgumentException("存档分区未注册：" + sectionId, nameof(sectionId));
            if (MarkPending()) dirtySections.Add(sectionId);
        }

        private bool MarkPending()
        {
            if (string.IsNullOrWhiteSpace(this.GetModel<SaveModel>().CurrentSaveId)) return false;
            if (!dirty) firstDirtyAt = clock.RealtimeSinceStartup;
            dirty = true;
            dirtyAt = clock.RealtimeSinceStartup;
            return true;
        }

        /// <summary>显式保存不受退避限制；失败保留脏数据，自动保存五秒后重试。</summary>
        public bool Flush(out string error)
        {
            error = null;
            if (!dirty) return true;
            var model = this.GetModel<SaveModel>();
            if (model.CurrentData == null) { ResetPending(); return true; }
            try
            {
                var data = model.CurrentData.Clone();
                data.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
                foreach (var provider in providers.Values)
                    if (captureAll || dirtySections.Contains(provider.SectionId))
                        data.Sections[provider.SectionId] = provider.Capture()?.DeepClone();
                if (!store.Save(data, out error)) return SaveFailed(error);
                model.SetCurrent(data);
                LastSavedAtUtc = clock.UtcNow;
                LastSaveError = null;
                ResetPending();
                return true;
            }
            catch (Exception exception)
            {
                error = "存档采集或保存失败：" + exception.Message;
                return SaveFailed(error);
            }
        }

        private bool SaveFailed(string error)
        {
            LastSaveError = error;
            retryAt = clock.RealtimeSinceStartup + FailureRetryDelay;
            Log.Error($"[SaveSystem] 刷新存档失败：{error}");
            return false;
        }

        private void ResetPending()
        {
            dirty = false; captureAll = false; dirtySections.Clear(); retryAt = 0;
        }

        private void RestorePending(SaveRuntimeSnapshot snapshot)
        {
            dirty = snapshot.Dirty; dirtyAt = snapshot.DirtyAt; firstDirtyAt = snapshot.FirstDirtyAt;
            retryAt = snapshot.RetryAt; captureAll = snapshot.CaptureAll;
            dirtySections.Clear(); dirtySections.UnionWith(snapshot.DirtySections);
        }

        private void Update()
        {
            float now = clock.RealtimeSinceStartup;
            if (dirty && now >= retryAt &&
                (now - dirtyAt >= AutoSaveDelay || now - firstDirtyAt >= MaximumSaveDelay))
                Flush(out _);
        }

        private void CaptureSections(SaveGameData data)
        {
            data.Sections ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var provider in providers.Values)
            {
                data.Sections[provider.SectionId] = provider.Capture()?.DeepClone();
            }
        }

        private Dictionary<string, JToken> CaptureProviderState()
        {
            return providers.ToDictionary(pair => pair.Key, pair => pair.Value.Capture()?.DeepClone(), StringComparer.Ordinal);
        }

        private void RestoreProviderState(Dictionary<string, JToken> captured)
        {
            foreach (var provider in providers.Values)
            {
                captured.TryGetValue(provider.SectionId, out var data);
                provider.Restore(data);
            }
        }

        private static string NormalizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        private static string GetValidationErrorText(SaveNameValidationResult result)
        {
            switch (result)
            {
                case SaveNameValidationResult.Empty:
                    return "SaveNameEmpty";
                case SaveNameValidationResult.TooLong:
                    return "SaveNameTooLong";
                case SaveNameValidationResult.Duplicate:
                    return "SaveNameDuplicate";
                default:
                    return string.Empty;
            }
        }
    }
}
