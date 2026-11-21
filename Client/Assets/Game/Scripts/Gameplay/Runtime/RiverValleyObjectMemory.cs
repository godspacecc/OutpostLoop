using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>每类场景物件采用行动内可续玩的索引。</summary>
    internal enum RiverValleyObjectKind { Door, Cache, Drop, Vehicle }

    [Serializable]
    internal struct RiverValleyObservation
    {
        public RiverValleyObjectKind kind;
        public int index;
        public Vector3 position;
        public float yaw;
        public bool present;
        // 门记录横板，箱子记录外观是否为空；物品内容继续由领域层管理。
        public bool detail;
    }

    /// <summary>保存玩家观察过的物件状态。表现读取该记录，世界碰撞和交互继续读取实际状态。</summary>
    internal sealed class RiverValleyObjectMemory
    {
        private readonly Dictionary<(RiverValleyObjectKind Kind, int Index), RiverValleyObservation> observations =
            new Dictionary<(RiverValleyObjectKind, int), RiverValleyObservation>();

        internal RiverValleyObjectMemory(RiverValleyObservation[] saved)
        {
            if (saved == null) return;
            foreach (var item in saved) observations[(item.kind, item.index)] = item;
        }

        /// <summary>看见时记录当前状态，包括亲眼确认物件已消失；离开视线后沿用旧状态。</summary>
        internal RiverValleyObservation Observe(RiverValleyObjectKind kind, int index, bool visible, bool present,
            Vector3 position, float yaw = 0, bool detail = false)
        {
            var key = (kind, index);
            if (!visible) return observations.TryGetValue(key, out var known) ? known : default;
            var observation = new RiverValleyObservation
            {
                kind = kind, index = index, position = position, yaw = yaw, present = present, detail = detail
            };
            observations[key] = observation;
            return observation;
        }

        internal RiverValleyObservation[] Capture()
        {
            if (observations.Count == 0) return null;
            var result = new RiverValleyObservation[observations.Count];
            observations.Values.CopyTo(result, 0);
            return result;
        }
    }
}
