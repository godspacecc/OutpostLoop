using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 负责存档文件读写、索引摘要、损坏文件识别和版本迁移调用，不持有当前运行存档。
    /// </summary>
    public sealed class SaveFileStore
    {
        private readonly string rootDirectory;
        private readonly SaveMigrationRegistry migrations;
        private readonly Dictionary<string, SlotCache> slotCache = new Dictionary<string, SlotCache>(StringComparer.Ordinal);
        private sealed class SlotCache
        {
            internal long Length, Modified;
            internal SaveSlotInfo Slot;
        }

        private static SaveSlotInfo CloneSlot(SaveSlotInfo slot) => new SaveSlotInfo {
            SaveId = slot.SaveId, SaveName = slot.SaveName, CreatedAtUtc = slot.CreatedAtUtc,
            UpdatedAtUtc = slot.UpdatedAtUtc, MapId = slot.MapId, IsCorrupted = slot.IsCorrupted, Error = slot.Error
        };

        public SaveFileStore(SaveMigrationRegistry migrations = null) : this(Application.persistentDataPath, migrations)
        {
        }

        public SaveFileStore(string rootDirectory, SaveMigrationRegistry migrations = null)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? Application.persistentDataPath : rootDirectory;
            this.migrations = migrations ?? new SaveMigrationRegistry();
        }

        public string SavesDirectory => Path.Combine(rootDirectory, "Saves");

        public IReadOnlyList<SaveSlotInfo> ListSlots()
        {
            Directory.CreateDirectory(SavesDirectory);
            var slots = new List<SaveSlotInfo>();
            var paths = new HashSet<string>(Directory.GetFiles(SavesDirectory, "*.json"), StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".backup.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (slotCache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc.Ticks)
                {
                    slots.Add(CloneSlot(cached.Slot));
                    continue;
                }
                slotCache.Remove(path);
                if (TryLoad(path, out var data, out var error))
                {
                    slots.Add(ToSlot(data, false, null));
                }
                else
                {
                    slots.Add(new SaveSlotInfo
                    {
                        SaveId = Path.GetFileNameWithoutExtension(path),
                        SaveName = Path.GetFileNameWithoutExtension(path),
                        IsCorrupted = true,
                        Error = error
                    });
                }
                slotCache[path] = new SlotCache {
                    Length = info.Length, Modified = info.LastWriteTimeUtc.Ticks,
                    Slot = CloneSlot(slots[slots.Count - 1])
                };
            }

            // 缓存只保存摘要，文件变化后重新校验；返回副本避免界面污染缓存。
            foreach (var stale in slotCache.Keys.Where(path => !paths.Contains(path)).ToArray()) slotCache.Remove(stale);
            return slots.OrderByDescending(slot => slot.UpdatedAtUtc ?? string.Empty, StringComparer.Ordinal).ToArray();
        }

        public bool TryLoadById(string saveId, out SaveGameData data, out string error)
        {
            var path = GetPath(saveId);
            if (TryLoad(path, out data, out error))
            {
                return true;
            }

            var primaryError = error;
            var backup = GetBackupPath(saveId);
            if (TryLoad(backup, out data, out error))
            {
                return true;
            }

            error = File.Exists(path) ? primaryError : error;
            return false;
        }

        public bool Save(SaveGameData data, out string error)
        {
            error = null;
            try
            {
                if (data == null || !IsSafeId(data.SaveId))
                {
                    error = "存档 ID 无效";
                    return false;
                }

                Directory.CreateDirectory(SavesDirectory);
                var target = GetPath(data.SaveId);
                slotCache.Remove(target);
                var temp = GetTempPath(data.SaveId);
                var backup = GetBackupPath(data.SaveId);
                var json = JsonConvert.SerializeObject(data, Formatting.None);
                File.WriteAllText(temp, json);
                if (!TryLoad(temp, out _, out error))
                {
                    TryDelete(temp);
                    return false;
                }

                if (File.Exists(target))
                {
                    TryDelete(backup);
                    File.Replace(temp, target, backup, true);
                }
                else
                {
                    File.Move(temp, target);
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"写入存档文件异常：{e.Message}";
                if (data != null)
                {
                    TryDelete(GetTempPath(data.SaveId));
                }

                return false;
            }
        }

        public bool Delete(string saveId, out string error)
        {
            error = null;
            try
            {
                slotCache.Remove(GetPath(saveId));
                TryDelete(GetPath(saveId));
                TryDelete(GetTempPath(saveId));
                TryDelete(GetBackupPath(saveId));
                return true;
            }
            catch (Exception e)
            {
                error = $"删除存档文件异常：{e.Message}";
                return false;
            }
        }

        public bool ClearAll(out string error)
        {
            error = null;
            try
            {
                slotCache.Clear();
                if (Directory.Exists(SavesDirectory))
                {
                    Directory.Delete(SavesDirectory, true);
                }

                Directory.CreateDirectory(SavesDirectory);
                return true;
            }
            catch (Exception e)
            {
                error = $"清空存档目录异常：{e.Message}";
                return false;
            }
        }

        public string GetPath(string saveId)
        {
            return Path.Combine(SavesDirectory, $"{saveId}.json");
        }

        public string GetTempPath(string saveId)
        {
            return Path.Combine(SavesDirectory, $"{saveId}.tmp.json");
        }

        public string GetBackupPath(string saveId)
        {
            return Path.Combine(SavesDirectory, $"{saveId}.backup.json");
        }

        public static SaveSlotInfo ToSlot(SaveGameData data, bool corrupted, string error)
        {
            return new SaveSlotInfo
            {
                SaveId = data?.SaveId,
                SaveName = data?.SaveName,
                CreatedAtUtc = data?.CreatedAtUtc,
                UpdatedAtUtc = data?.UpdatedAtUtc,
                MapId = data?.MapContext?.MapId,
                IsCorrupted = corrupted,
                Error = error
            };
        }

        private bool TryLoad(string path, out SaveGameData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "存档文件不存在";
                    return false;
                }

                var raw = JObject.Parse(File.ReadAllText(path));
                if (!migrations.TryMigrate(raw, out var migrated, out error))
                {
                    return false;
                }

                data = migrated.ToObject<SaveGameData>();
                if (data == null || !IsSafeId(data.SaveId))
                {
                    error = "存档数据无效";
                    return false;
                }

                data.Sections ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
                data.MapContext ??= new MapSaveContext();
                data.Version = SaveGameData.CurrentVersion;
                return true;
            }
            catch (Exception e)
            {
                error = $"读取存档文件异常：{e.Message}";
                return false;
            }
        }

        private static bool IsSafeId(string saveId)
        {
            return !string.IsNullOrWhiteSpace(saveId) &&
                   saveId.Length == 32 &&
                   saveId.All(Uri.IsHexDigit);
        }

        private static void TryDelete(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
