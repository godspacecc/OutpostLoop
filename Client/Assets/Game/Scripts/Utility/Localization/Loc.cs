namespace Game
{
    /// <summary>
    /// 本地化文本访问入口，只转发到 LocalizationUtility，不缓存或加载翻译表。
    /// </summary>
    public static class Loc
    {
        public static string T(string key, params object[] args)
        {
            var model = Game.Interface.GetModel<LocalizationModel>();
            var utility = Game.Interface.GetUtility<LocalizationUtility>();
            return utility.Translate(model.CurrentLanguageCode.Value, key, args);
        }
    }
}
