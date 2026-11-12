using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 抽象设置保存的时间来源，用于延迟写盘节流和测试中的可控时间推进。
    /// </summary>
    public interface ISettingsClock
    {
        float Now { get; }
    }

    /// <summary>
    /// 设置系统默认时间源，使用 Unity 实时时间驱动延迟保存。
    /// </summary>
    public sealed class UnityRealtimeSettingsClock : ISettingsClock
    {
        public float Now => Time.realtimeSinceStartup;
    }

    /// <summary>
    /// 在资源与本地化配置就绪后显式加载并应用用户设置，运行期合并频繁改动并延迟写盘，反初始化时执行最终 Flush。
    /// </summary>
    public sealed class SettingsSystem : AbstractSystem
    {
        /// <summary>合并连续设置操作的保存间隔（秒）。</summary>
        private const float SaveDelaySeconds = 0.5f;
        /// <summary>可替换时间源，避免测试依赖真实等待。</summary>
        private ISettingsClock clock = new UnityRealtimeSettingsClock();
        /// <summary>是否存在尚未成功保存的修改。</summary>
        private bool dirty;
        /// <summary>最近一次修改的实时时间。</summary>
        private float dirtyAt;
        /// <summary>本次启动采用主文件、备份或默认值的结果。</summary>
        private SettingsLoadResult lastLoadResult;

        public SettingsLoadResult LastLoadResult => lastLoadResult;

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            Flush();
            LifeCycle.UnregisterUpdate(Update);
        }

        public void SetClockForTests(ISettingsClock clock)
        {
            this.clock = clock ?? new UnityRealtimeSettingsClock();
        }

        /// <summary>
        /// 启动阶段读取设置文件并应用到模型、显示和热键；文件缺失或损坏时由存储层回退为默认设置。
        /// </summary>
        public void LoadAndApply()
        {
            var data = this.GetUtility<ISettingsFileStore>().LoadOrDefault(out lastLoadResult);
            Apply(data);

            var displayModel = this.GetModel<DisplaySettingsModel>();
            this.GetUtility<IDisplayResolutionApplier>().Apply(
                displayModel.GetCurrentResolution(),
                displayModel.CurrentWindowMode.Value);
        }

        /// <summary>清洗设置并同步语言、显示、音量和热键模型。</summary>
        public void Apply(SettingsData data)
        {
            data = (data ?? SettingsData.CreateDefault()).Sanitize();
            this.GetModel<LocalizationModel>().SetLanguageCode(data.LanguageCode);
            this.GetModel<DisplaySettingsModel>().Apply(data.Display);
            this.GetModel<AudioSettingsModel>().Apply(data.Audio);
            this.GetModel<HotKeyModel>().Apply(data.HotKeys);
            this.GetSystem<HotKeySystem>().RebuildActions();
        }

        /// <summary>登记修改并重新开始延迟保存计时。</summary>
        public void MarkDirty()
        {
            dirty = true;
            dirtyAt = clock.Now;
        }

        /// <summary>立即保存待写数据，失败时保留脏标记供后续重试。</summary>
        public bool Flush()
        {
            if (!dirty)
            {
                return true;
            }

            var ok = SaveCurrent();
            if (ok)
            {
                dirty = false;
            }

            return ok;
        }

        /// <summary>恢复参考框架的默认模型设置并保存。</summary>
        public void ResetAllToDefault()
        {
            Apply(SettingsData.CreateDefault());
            MarkDirty();
            Flush();
        }

        /// <summary>由基础生命周期驱动到期保存。</summary>
        private void Update()
        {
            if (dirty && clock.Now - dirtyAt >= SaveDelaySeconds)
            {
                Flush();
            }
        }

        /// <summary>
        /// 将当前模型中的设置快照写入本地文件；保存失败只返回 false 并记录诊断，不改变脏标记的后续重试语义。
        /// </summary>
        public bool SaveCurrent()
        {
            var data = new SettingsData
            {
                LanguageCode = this.GetModel<LocalizationModel>().CurrentLanguageCode.Value,
                Display = this.GetModel<DisplaySettingsModel>().ToData(),
                Audio = this.GetModel<AudioSettingsModel>().ToData(),
                HotKeys = this.GetModel<HotKeyModel>().ToData()
            }.Sanitize();

            if (!this.GetUtility<ISettingsFileStore>().Save(data, out var error))
            {
                Log.Error($"[SettingsSystem] 保存设置失败：{error}");
                return false;
            }

            return true;
        }
    }
}
