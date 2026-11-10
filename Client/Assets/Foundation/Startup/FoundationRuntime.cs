using System;
using QFramework;
using OutpostLoop.Resources;
namespace Game
{
    /// <summary>基础组合入口：先准备资源和容器，再由项目接入玩法；不依赖 Prototype。</summary>
    public static class FoundationRuntime
    {
        // 保存本次初始化取得的架构，退出时不通过 Interface 意外新建容器。
        private static IArchitecture architecture;
        /// <summary>基础会话是否就绪；重复初始化不会重置已有加载器或窗口。</summary>
        public static bool IsInitialized { get; private set; }
        /// <summary>编辑器默认源后端；其它环境须由资源启动阶段先配置公共加载器。</summary>
        public static void Initialize()
        {
            if (IsInitialized) return;
            try
            {
                if (!ResourceLoader.IsConfigured)
                {
                    if (Boot.BootHost.TryGetResourceInfo(out _))
                        ResourceLoader.Use(new YooAssetResourceLoader(YooAsset.YooAssets.GetPackage("OutpostLoop")));
                }
                if (!ResourceLoader.IsConfigured)
                {
#if UNITY_EDITOR
                    ResourceLoader.Use(new EditorSourceResourceLoader());
#else
                    throw new InvalidOperationException("基础启动失败：资源后端尚未就绪。");
#endif
                }
                UIHelper.SetPrefabLoader(null);
                // 资源先就绪，再重载语言配置并应用本项目持久目录中的设置。
                LocalizationLanguages.ResetCache();
                architecture = Game.Interface;
                architecture.GetUtility<LocalizationUtility>().InitializeConfiguredTables();
                architecture.GetSystem<SettingsSystem>().LoadAndApply();
                architecture.GetSystem<AudioSystem>().ApplyVolumes();
                if (Boot.BootHost.TryGetResourceInfo(out var startupInfo))
                    architecture.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(startupInfo);
                IsInitialized = true;
            }
            catch
            {
                ReleaseSession();
                throw;
            }
        }
        /// <summary>退出按逆序释放界面、玩法容器、计时器和事件，最后断开资源入口；重复调用无副作用。</summary>
        public static void Shutdown()
        {
            if (IsInitialized) ReleaseSession();
        }

        /// <summary>正常退出和部分初始化失败使用同一条清理路径。</summary>
        private static void ReleaseSession()
        {
            IsInitialized = false;
            var current = architecture;
            architecture = null;
            Cleanup(UIHelper.Shutdown);
            Cleanup(() => current?.Deinit());
            Cleanup(LifeCycle.Quit);
            Cleanup(LocalizationLanguages.ResetCache);
            Cleanup(ResourceLoader.Shutdown);
        }
        /// <summary>一个模块清理失败不阻断其它模块退出，同时保留错误原因。</summary>
        private static void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { Log.Error("[FoundationRuntime] 基础清理失败：" + error); }
        }
    }
}
