using Game;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>预览和运行时共享安全区、缩放和碰撞计算，坐标从左上起算。</summary>
    internal static class PerceptionGeometry
    {
        internal const int Count = 5;
        private const float Gap = 8;
        private static readonly Vector2[] Sizes =
        {
            new(320, 64), new(152, 154), new(374, 78), new(350, 72), new(240, 62)
        };

        internal static float ScreenScale => Mathf.Max(.01f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));

        internal static Rect ScreenArea
        {
            get
            {
                var safe = Screen.safeArea;
                return new Rect(safe.x / ScreenScale, (Screen.height - safe.yMax) / ScreenScale,
                    safe.width / ScreenScale, safe.height / ScreenScale);
            }
        }

        internal static Vector2 BaseSize(PerceptionGroup group) => Sizes[(int)group];

        /// <summary>保持期望位置，遇到冲突时选最近可放落点。背包入口有固定的保留区。</summary>
        internal static void Resolve(PerceptionLayout layout, Rect area, Rect[] result)
        {
            var usable = Inset(area);
            var pack = Pack(area);
            var center = new Rect(area.center.x - area.width * .12f, area.center.y - area.height * .12f,
                area.width * .24f, area.height * .24f);
            for (int i = 0; i < Count; i++)
            {
                var item = layout.Get((PerceptionGroup)i);
                var size = Fit(Sizes[i], item.Scale.Value, usable);
                var desired = new Vector2(usable.x + (usable.width - size.x) * item.X.Value,
                    usable.y + (usable.height - size.y) * item.Y.Value);
                var best = new Rect(desired, size);
                float distance = float.PositiveInfinity;
                // 任一最近可放点至少沿着一个障碍边界，另一轴可保持原值。
                for (int x = -5; x < i * 2; x++)
                for (int y = -5; y < i * 2; y++)
                {
                    var candidate = new Rect(Axis(x, desired.x, size.x, true, pack, center, result),
                        Axis(y, desired.y, size.y, false, pack, center, result), size.x, size.y);
                    if (!Inside(candidate, usable) || Conflicts(candidate, pack) || Conflicts(candidate, center)) continue;
                    bool occupied = false;
                    for (int n = 0; n < i; n++) occupied |= Conflicts(candidate, result[n]);
                    float delta = (candidate.position - desired).sqrMagnitude;
                    if (occupied || delta >= distance) continue;
                    distance = delta;
                    best = candidate;
                }
                result[i] = best;
            }
        }

        private static float Axis(int index, float desired, float length, bool horizontal,
            Rect pack, Rect center, Rect[] placed)
        {
            if (index == -5) return desired;
            var obstacle = index < -2 ? pack : index < 0 ? center : placed[index / 2];
            bool before = index % 2 == 0;
            if (horizontal) return before ? obstacle.xMin - Gap - length : obstacle.xMax + Gap;
            return before ? obstacle.yMin - Gap - length : obstacle.yMax + Gap;
        }

        private static bool Conflicts(Rect a, Rect b) =>
            a.xMin < b.xMax + Gap - .01f && a.xMax > b.xMin - Gap + .01f &&
            a.yMin < b.yMax + Gap - .01f && a.yMax > b.yMin - Gap + .01f;

        private static bool Inside(Rect r, Rect area) => r.xMin >= area.xMin && r.xMax <= area.xMax + .01f &&
            r.yMin >= area.yMin && r.yMax <= area.yMax + .01f;

        private static Rect Inset(Rect area) => new(area.x + 16, area.y + 16,
            Mathf.Max(1, area.width - 32), Mathf.Max(1, area.height - 32));

        private static Vector2 Fit(Vector2 size, float scale, Rect area) =>
            size * Mathf.Min(scale, area.width / size.x, area.height / size.y);

        internal static Rect Pack(Rect area) => new(area.x + 16, area.y + 92, 137, 25);

        internal static void Move(PerceptionPlacement item, Rect target, Rect area)
        {
            var usable = Inset(area);
            item.X = Mathf.Clamp01((target.x - usable.x) / Mathf.Max(1, usable.width - target.width));
            item.Y = Mathf.Clamp01((target.y - usable.y) / Mathf.Max(1, usable.height - target.height));
        }

        /// <summary>将参考矩形应用于左上锚定的 uGUI 容器。</summary>
        internal static void Place(RectTransform target, Rect rect, Vector2 baseSize)
        {
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(rect.x, -rect.y);
            target.sizeDelta = baseSize;
            target.localScale = Vector3.one * (rect.width / baseSize.x);
        }
    }
}
