using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 描述一个可注册热键动作的 ID、上下文和执行回调；绑定路径由设置数据提供。
    /// </summary>
    public sealed class HotKeyAction
    {
        public string ActionId { get; set; }
        public HotKeyContext Context { get; set; }
        public int Priority { get; set; }
        public Func<bool> CanExecute { get; set; }
        public Action Execute { get; set; }
    }

    /// <summary>
    /// 维护热键动作注册表并阻止重复 ID，供 HotKeySystem 重建输入动作时查询。
    /// </summary>
    public sealed class HotKeyActionRegistry : IUtility
    {
        private readonly List<HotKeyAction> actions = new List<HotKeyAction>();

        public IReadOnlyList<HotKeyAction> Actions => actions;

        public void Register(HotKeyAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
            {
                return;
            }

            actions.RemoveAll(item => item.ActionId == action.ActionId);
            actions.Add(action);
        }

        public void Clear()
        {
            actions.Clear();
        }

        public bool TryExecute(IEnumerable<string> actionIds)
        {
            var idSet = new HashSet<string>(actionIds ?? Array.Empty<string>());
            foreach (var action in actions
                         .Where(item => idSet.Contains(item.ActionId))
                         .OrderByDescending(item => item.Priority))
            {
                if (action.CanExecute != null && !action.CanExecute())
                {
                    continue;
                }

                action.Execute?.Invoke();
                return true;
            }

            return false;
        }
    }
}
