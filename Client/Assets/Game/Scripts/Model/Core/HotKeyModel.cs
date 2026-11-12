using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前热键绑定集合，系统会以动作 ID 合并默认绑定并覆盖运行时输入动作。
    /// </summary>
    public sealed class HotKeyModel : AbstractModel
    {
        private readonly List<HotKeyBinding> bindings = new List<HotKeyBinding>();

        public IReadOnlyList<HotKeyBinding> Bindings => bindings;

        protected override void OnInit()
        {
            Apply(HotKeyDefaults.Create());
        }

        public void Apply(IEnumerable<HotKeyBinding> source)
        {
            bindings.Clear();
            bindings.AddRange(SettingsData.SanitizeHotKeys(source?.Select(item => item?.Clone())));
        }

        public bool TryGet(string actionId, out HotKeyBinding binding)
        {
            binding = bindings.FirstOrDefault(item => item.ActionId == actionId);
            return binding != null;
        }

        public bool HasConflict(HotKeyBinding candidate)
        {
            return candidate != null && bindings.Any(item =>
                item.ActionId != candidate.ActionId &&
                item.Context == candidate.Context &&
                item.BindingPath == candidate.BindingPath);
        }

        public bool TryGetConflict(HotKeyBinding candidate, out HotKeyBinding conflict)
        {
            conflict = candidate == null
                ? null
                : bindings.FirstOrDefault(item =>
                    item.ActionId != candidate.ActionId &&
                    item.Context == candidate.Context &&
                    item.BindingPath == candidate.BindingPath);
            return conflict != null;
        }

        public bool TryReplace(HotKeyBinding candidate)
        {
            return TryReplace(candidate, false);
        }

        public bool TryReplace(HotKeyBinding candidate, bool replaceConflict)
        {
            if (candidate == null ||
                string.IsNullOrWhiteSpace(candidate.ActionId) ||
                string.IsNullOrWhiteSpace(candidate.BindingPath))
            {
                return false;
            }

            var index = bindings.FindIndex(item => item.ActionId == candidate.ActionId);
            if (TryGetConflict(candidate, out var conflict))
            {
                if (!replaceConflict || index < 0)
                {
                    return false;
                }

                var conflictIndex = bindings.FindIndex(item => item.ActionId == conflict.ActionId);
                bindings[conflictIndex] = new HotKeyBinding
                {
                    ActionId = conflict.ActionId,
                    Context = conflict.Context,
                    BindingPath = bindings[index].BindingPath
                };
            }

            var replacement = candidate.Clone();
            if (index >= 0)
            {
                bindings[index] = replacement;
            }
            else
            {
                bindings.Add(replacement);
            }

            return true;
        }

        public void ResetToDefault()
        {
            Apply(HotKeyDefaults.Create());
        }

        public List<HotKeyBinding> ToData()
        {
            return bindings.Select(item => item.Clone()).ToList();
        }
    }
}
