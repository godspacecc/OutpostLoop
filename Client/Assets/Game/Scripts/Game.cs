using QFramework;
namespace Game
{
    /// <summary>独立技术容器。玩法模块由启动入口注册，不引用旧街机业务。</summary>
    public sealed class Game : Architecture<Game>
    {
        /// <summary>先注册日志模型再启动采集，领域模块随后由各入口接入。</summary>
        protected override void Init()
        {
            RegisterModel(new RuntimeLogModel());
            RegisterModel(new ResourceUpdateModel());
            RegisterModel(new SaveModel());
            RegisterModel(new LocalizationModel());
            RegisterModel(new DisplaySettingsModel());
            RegisterModel(new AudioSettingsModel());
            RegisterModel(new HotKeyModel());
            RegisterUtility<ISettingsFileStore>(new SettingsFileStore());
            RegisterUtility<IDisplayResolutionApplier>(new UnityDisplayResolutionApplier());
            RegisterUtility(new HotKeyActionRegistry());
            RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            RegisterSystem(new SettingsSystem());
            RegisterSystem(new DisplayPreviewTransaction());
            RegisterSystem(new HotKeySystem());
            RegisterSystem(new RuntimeLogSystem());
            RegisterSystem(new AudioSystem());
            RegisterSystem(new SaveSystem());
            RegisterSystem(new RedPointSystem());
            RegisterSystem(new DebugCommandSystem());
            RegisterUtility<IResourceUpdateRuntimeChecker>(new YooAssetResourceUpdateRuntimeChecker());
            RegisterUtility<IRemoteResourceVersionProbe>(new UnityWebRequestResourceVersionProbe());
            RegisterUtility<IResourceUpdateNoticeClient>(new UnityWebRequestResourceUpdateNoticeClient());
            RegisterUtility<IApplicationUpdateChecker>(new ApplicationUpdateChecker());
            RegisterUtility<IApplicationRestartUtility>(new ApplicationRestartUtility());
            RegisterSystem(new ResourceUpdateSystem());
        }
    }
}
