using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 集中定义项目内置热键动作 ID，避免设置文件和运行时代码使用散落字符串。
    /// </summary>
    public static class HotKeyActionIds
    {
        public const string TogglePause = "game.pause.toggle";
        public const string ToggleRuntimeLog = "runtime-log.toggle";
    }

    /// <summary>
    /// 提供默认热键绑定集合，是设置清洗和首次启动时补齐绑定的来源。
    /// </summary>
    public static class HotKeyDefaults
    {
        public const string Escape = "<Keyboard>/escape";
        public const string F1 = "<Keyboard>/f1";

        public static List<HotKeyBinding> Create()
        {
            return new List<HotKeyBinding>
            {
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.TogglePause,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = Escape
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleRuntimeLog,
                    Context = HotKeyContext.Global,
                    BindingPath = F1
                }
            };
        }
    }
}
