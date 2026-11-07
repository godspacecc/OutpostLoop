using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 将独立 UI 的可交互内容映射到 Screen.safeArea。
    /// 全屏 UI 根与其全屏 Graphic 覆盖物始终铺满物理屏幕，避免安全区外露出游戏世界。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private bool originalLayoutCaptured;
        private LayoutSnapshot originalLayout;

        /// <summary>全屏根的直属内容布局；Key 的生命周期不超过当前 UI 实例。</summary>
        private readonly Dictionary<RectTransform, LayoutSnapshot> childLayouts =
            new Dictionary<RectTransform, LayoutSnapshot>();

        private void OnEnable()
        {
            ApplyNow();
        }

        private void Update()
        {
            if (lastScreenSize.x != Screen.width ||
                lastScreenSize.y != Screen.height ||
                lastSafeArea != Screen.safeArea)
            {
                ApplyNow();
            }
        }

        /// <summary>启动、尺寸、方向或安全区变化时幂等更新锚点。</summary>
        public void ApplyNow()
        {
            rectTransform = rectTransform != null ? rectTransform : transform as RectTransform;
            if (rectTransform == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            if (!originalLayoutCaptured)
            {
                originalLayout = LayoutSnapshot.Capture(rectTransform);
                originalLayoutCaptured = true;
            }

            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastSafeArea = Screen.safeArea;
            CalculateAnchors(lastSafeArea, lastScreenSize, out var safeAnchorMin, out var safeAnchorMax);
            ApplyAnchors(safeAnchorMin, safeAnchorMax);
        }

        /// <summary>
        /// 应用已经归一化的安全区锚点。全屏根只收紧直属内容，不改变背景覆盖范围。
        /// </summary>
        internal void ApplyAnchors(Vector2 safeAnchorMin, Vector2 safeAnchorMax)
        {
            if (rectTransform == null)
            {
                rectTransform = transform as RectTransform;
            }

            if (rectTransform == null)
            {
                return;
            }

            if (!originalLayoutCaptured)
            {
                originalLayout = LayoutSnapshot.Capture(rectTransform);
                originalLayoutCaptured = true;
            }

            if (!originalLayout.IsFullScreen)
            {
                ApplyMappedLayout(rectTransform, originalLayout, safeAnchorMin, safeAnchorMax, true);
                return;
            }

            // 根节点保留物理全屏尺寸，使根 Image、Raycast 和阻塞边界不被安全区切掉。
            originalLayout.Restore(rectTransform);
            CaptureDirectChildren();
            foreach (var pair in childLayouts)
            {
                var child = pair.Key;
                if (child == null)
                {
                    continue;
                }

                var layout = pair.Value;
                if (layout.IsFullScreen && child.GetComponent<Graphic>() != null)
                {
                    // 背景、遮罩和全屏射线面继续覆盖刘海与圆角区域。
                    child.anchorMin = layout.AnchorMin;
                    child.anchorMax = layout.AnchorMax;
                    continue;
                }

                ApplyMappedLayout(child, layout, safeAnchorMin, safeAnchorMax, false);
            }
        }

        private void CaptureDirectChildren()
        {
            for (var index = 0; index < rectTransform.childCount; index++)
            {
                if (rectTransform.GetChild(index) is RectTransform child && !childLayouts.ContainsKey(child))
                {
                    childLayouts.Add(child, LayoutSnapshot.Capture(child));
                }
            }
        }

        private static void ApplyMappedLayout(
            RectTransform target,
            LayoutSnapshot layout,
            Vector2 safeAnchorMin,
            Vector2 safeAnchorMax,
            bool restoreOffsets)
        {
            MapAnchorsIntoSafeArea(
                layout.AnchorMin,
                layout.AnchorMax,
                safeAnchorMin,
                safeAnchorMax,
                out var mappedAnchorMin,
                out var mappedAnchorMax);
            target.anchorMin = mappedAnchorMin;
            target.anchorMax = mappedAnchorMax;
            if (!restoreOffsets)
            {
                return;
            }

            target.anchoredPosition = layout.AnchoredPosition;
            target.sizeDelta = layout.SizeDelta;
        }

        internal static void MapAnchorsIntoSafeArea(
            Vector2 originalMin,
            Vector2 originalMax,
            Vector2 safeMin,
            Vector2 safeMax,
            out Vector2 mappedMin,
            out Vector2 mappedMax)
        {
            mappedMin = new Vector2(
                Mathf.Lerp(safeMin.x, safeMax.x, originalMin.x),
                Mathf.Lerp(safeMin.y, safeMax.y, originalMin.y));
            mappedMax = new Vector2(
                Mathf.Lerp(safeMin.x, safeMax.x, originalMax.x),
                Mathf.Lerp(safeMin.y, safeMax.y, originalMax.y));
        }

        internal static void CalculateAnchors(
            Rect safeArea,
            Vector2 screenSize,
            out Vector2 anchorMin,
            out Vector2 anchorMax)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }

            anchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screenSize.x),
                Mathf.Clamp01(safeArea.yMin / screenSize.y));
            anchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screenSize.x),
                Mathf.Clamp01(safeArea.yMax / screenSize.y));
        }

        private readonly struct LayoutSnapshot
        {
            private const float LayoutTolerance = 0.001f;

            public LayoutSnapshot(
                Vector2 anchorMin,
                Vector2 anchorMax,
                Vector2 anchoredPosition,
                Vector2 sizeDelta)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                AnchoredPosition = anchoredPosition;
                SizeDelta = sizeDelta;
            }

            public Vector2 AnchorMin { get; }
            public Vector2 AnchorMax { get; }
            public Vector2 AnchoredPosition { get; }
            public Vector2 SizeDelta { get; }

            /// <summary>零偏移、四向拉伸的布局代表物理全屏覆盖合同。</summary>
            public bool IsFullScreen =>
                Approximately(AnchorMin, Vector2.zero) &&
                Approximately(AnchorMax, Vector2.one) &&
                Approximately(AnchoredPosition, Vector2.zero) &&
                Approximately(SizeDelta, Vector2.zero);

            public static LayoutSnapshot Capture(RectTransform target)
            {
                return new LayoutSnapshot(
                    target.anchorMin,
                    target.anchorMax,
                    target.anchoredPosition,
                    target.sizeDelta);
            }

            public void Restore(RectTransform target)
            {
                target.anchorMin = AnchorMin;
                target.anchorMax = AnchorMax;
                target.anchoredPosition = AnchoredPosition;
                target.sizeDelta = SizeDelta;
            }

            private static bool Approximately(Vector2 left, Vector2 right)
            {
                return Mathf.Abs(left.x - right.x) <= LayoutTolerance &&
                       Mathf.Abs(left.y - right.y) <= LayoutTolerance;
            }
        }
    }
}
