using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 维护红点树节点、叶子状态和聚合计数，变更后发布不可变状态通知；业务节点由使用方注册。
    /// </summary>
    public sealed class RedPointSystem : AbstractSystem
    {
        private readonly Dictionary<string, RedPointNode> nodes = new Dictionary<string, RedPointNode>(StringComparer.Ordinal);
        private bool flushScheduled;

        protected override void OnInit()
        {
            // 保持空树，使通用红点基础不依赖启动页或资源更新功能。
        }

        public bool Register(string id, string parentId = null)
        {
            if (string.IsNullOrWhiteSpace(id) || nodes.ContainsKey(id))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(parentId) && !nodes.ContainsKey(parentId))
            {
                return false;
            }

            var node = new RedPointNode(id, parentId);
            nodes.Add(id, node);
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                nodes[parentId].Children.Add(id);
                MarkDirty(parentId);
            }

            return true;
        }

        public void SetLeaf(string id, bool active, int count = 0)
        {
            if (!nodes.TryGetValue(id, out var node))
            {
                return;
            }

            var normalizedCount = Math.Max(0, count);
            if (node.LeafActive == active && node.LeafCount == normalizedCount)
            {
                return;
            }

            node.LeafActive = active;
            node.LeafCount = normalizedCount;
            MarkDirty(id);
        }

        public RedPointState GetState(string id)
        {
            return nodes.TryGetValue(id, out var node) ? node.State : new RedPointState(id, false, 0);
        }

        public bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !nodes.TryGetValue(id, out var node))
            {
                return false;
            }

            foreach (var child in node.Children.ToArray())
            {
                Unregister(child);
            }

            nodes.Remove(id);
            if (!string.IsNullOrWhiteSpace(node.ParentId) && nodes.TryGetValue(node.ParentId, out var parent))
            {
                parent.Children.Remove(id);
                MarkDirty(parent.Id);
            }

            return true;
        }

        private void MarkDirty(string id)
        {
            while (!string.IsNullOrWhiteSpace(id) && nodes.TryGetValue(id, out var node))
            {
                node.Dirty = true;
                id = node.ParentId;
            }

            if (!flushScheduled)
            {
                flushScheduled = true;
                LifeCycle.ScheduleFrameEnd(this, FlushDirty);
            }
        }

        private void FlushDirty()
        {
            flushScheduled = false;
            var changed = new List<RedPointState>();
            foreach (var node in nodes.Values)
            {
                RefreshNode(node.Id, changed);
            }

            foreach (var state in changed)
            {
                this.SendEvent(new RedPointChangedEvent(state.Id, state.Active, state.Count));
            }
        }

        private RedPointState RefreshNode(string id, List<RedPointState> changed)
        {
            var node = nodes[id];
            var count = node.LeafActive ? Math.Max(1, node.LeafCount) : 0;
            var active = node.LeafActive;
            foreach (var childId in node.Children)
            {
                var childState = RefreshNode(childId, changed);
                if (childState.Active)
                {
                    active = true;
                    count += Math.Max(1, childState.Count);
                }
            }

            var next = new RedPointState(node.Id, active, count);
            if (node.Dirty && (node.State.Active != next.Active || node.State.Count != next.Count))
            {
                changed.Add(next);
            }

            node.State = next;
            node.Dirty = false;
            return next;
        }
    }
}
