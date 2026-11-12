using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前语言代码；翻译表加载、校验和格式化由本地化工具承担。
    /// </summary>
    public sealed class LocalizationModel : AbstractModel
    {
        [System.Obsolete("Runtime settings are stored in settings.json.")]
        public const string PlayerPrefsKey = "";

        private const string FallbackDefaultLanguageCode = "zh-CN";

        public BindableProperty<string> CurrentLanguageCode { get; } =
            new BindableProperty<string>(FallbackDefaultLanguageCode);

        protected override void OnInit()
        {
        }

        public LocalizationLanguage GetCurrentLanguage()
        {
            return LocalizationLanguages.FindOrDefault(CurrentLanguageCode.Value);
        }

        public void SetLanguageCode(string languageCode)
        {
            CurrentLanguageCode.Value = LocalizationLanguages.FindOrDefault(languageCode).Code;
        }
    }
}
