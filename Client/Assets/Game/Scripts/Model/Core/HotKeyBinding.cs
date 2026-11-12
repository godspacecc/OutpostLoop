using System;
using Newtonsoft.Json;

namespace Game
{
    /// <summary>
    /// 定义热键生效上下文的优先级边界，用于区分全局、UI 和游戏内输入。
    /// </summary>
    public enum HotKeyContext
    {
        Global,
        UI,
        Gameplay
    }

    /// <summary>
    /// 表示单个热键动作与绑定路径的持久化关系，克隆后可用于设置事务隔离。
    /// </summary>
    [Serializable]
    public sealed class HotKeyBinding
    {
        [JsonProperty("actionId")]
        public string ActionId { get; set; }

        [JsonProperty("context")]
        public HotKeyContext Context { get; set; }

        [JsonProperty("bindingPath")]
        public string BindingPath { get; set; }

        public HotKeyBinding Clone()
        {
            return new HotKeyBinding
            {
                ActionId = ActionId,
                Context = Context,
                BindingPath = BindingPath
            };
        }
    }
}
