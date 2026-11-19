using System;
using System.Collections;
using System.Collections.Generic;
using Boot.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 过渡锁只由表现流程持有，防止双击启动两份世界。
        private bool crossing;
        // 独立顶层遮罩，在地图分段构建期间持续显示。
        private GameObject crossingCanvas;
        // 关闭控制器时停止尚未完成的过渡。
        private Coroutine crossingRoutine;
        // 保存进入世界后的领域暂停状态，交接时恢复。
        private bool crossingRestorePause;
        /// <summary>供入口与验收观察的只读过渡状态。</summary>
        public bool IsCrossing => crossing;

        /// <summary>菜单统一出发入口；规则仍由传入的现有行动执行。</summary>
        internal void CrossToWorld(Action enter, bool resume = false) => BeginCrossing(SingleEntry(enter), resume);

        private static IEnumerable<(float value, string label)> SingleEntry(Action enter)
        {
            yield return (.35f, "正在生成行动场景");
            enter();
        }

        private void BeginCrossing(IEnumerable<(float value, string label)> steps, bool resume)
        {
            if (crossing || !initialized || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return;
            crossing = true;
            crossingRestorePause = World.Run.Paused;
            crossingRoutine = StartCoroutine(CrossWorld(steps, resume, !resume && JourneyProgress.visitedSpace));
        }

        /// <summary>在各段地图生成之间绘制进度，完成后再交还控制。</summary>
        private IEnumerator CrossWorld(IEnumerable<(float value, string label)> steps, bool resume, bool showCrossing)
        {
            crossingCanvas = new GameObject("WorldCrossing", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = crossingCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = crossingCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (showCrossing)
            {
                var backdrop = CosmicBackdrop.Create(crossingCanvas.transform, "Crossing",
                    Game.ResourceLoader.LoadAsset<Texture2D>(Game.GameplayArtAddress.Crossing));
                backdrop.GetComponent<RawImage>().raycastTarget = true;
            }
            else
                crossingCanvas.AddComponent<Image>().color = new Color32(9, 15, 24, 255);
            var labelRoot = new GameObject("CrossingStatus", typeof(RectTransform), typeof(Text));
            labelRoot.transform.SetParent(crossingCanvas.transform, false);
            var rect = (RectTransform)labelRoot.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f,0);
            rect.sizeDelta = new Vector2(1000,100);
            rect.anchoredPosition = new Vector2(0,152);
            var label = labelRoot.GetComponent<Text>();
            label.font = font;
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(225,235,243,255);
            label.text = resume ? "正在恢复行动" : showCrossing ? "正在接通世界" : "正在准备行动";
            label.raycastTarget = false;
            var (fill, percent) = CreateMapProgress(crossingCanvas.transform);
            yield return null;
            yield return new WaitForSecondsRealtime(.45f);
            Exception failure = null;
            using (var load = steps.GetEnumerator())
            {
                while (true)
                {
                    bool more;
                    try { more = load.MoveNext(); }
                    catch (Exception error) { failure = error; break; }
                    if (!more) break;
                    var (value, text) = load.Current;
                    label.text = text;
                    SetMapProgress(fill, percent, value);
                    yield return null;
                }
            }
            if (failure != null)
            {
                Debug.LogException(failure);
                label.text = "加载中断\n正在返回菜单";
                fill.parent.gameObject.SetActive(false);
                percent.gameObject.SetActive(false);
                // 清理已创建的部分地图，让用户能再次从开始页进入。
                if (scavengeRoot != null) LeaveScavenge();
                Restart(false);
                crossingRestorePause = true;
            }
            else if (resume && scavengeRun == null)
            {
                label.text = "没有可恢复的行动";
                fill.parent.gameObject.SetActive(false);
                percent.gameObject.SetActive(false);
            }
            else
            {
                crossingRestorePause = World.Run.Paused;
                session.SetPaused(true);
                label.text = showCrossing ? "即将抵达" : resume ? "行动已恢复" : "地图已就绪";
                SetMapProgress(fill, percent, 1f);
            }
            yield return new WaitForSecondsRealtime(.55f);
            FinishCrossing();
        }

        /// <summary>创建行动入口进度条，数值表示已完成的地图生成步骤。</summary>
        private (RectTransform fill, Text percent) CreateMapProgress(Transform parent)
        {
            var track = new GameObject("CrossingProgressTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);
            var rect = (RectTransform)track.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 105);
            rect.sizeDelta = new Vector2(560, 8);
            var background = track.GetComponent<Image>();
            background.color = new Color32(48, 70, 85, 255);
            background.raycastTarget = false;
            var bar = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(rect, false);
            var fill = (RectTransform)bar.transform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.up;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var graphic = bar.GetComponent<Image>();
            graphic.color = new Color32(137, 203, 232, 255);
            graphic.raycastTarget = false;
            var value = new GameObject("CrossingProgress", typeof(RectTransform), typeof(Text));
            value.transform.SetParent(parent, false);
            var valueRect = (RectTransform)value.transform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(.5f, 0);
            valueRect.anchoredPosition = new Vector2(0, 75);
            valueRect.sizeDelta = new Vector2(100, 34);
            var percent = value.GetComponent<Text>();
            percent.font = font;
            percent.fontSize = 22;
            percent.color = graphic.color;
            percent.alignment = TextAnchor.MiddleCenter;
            percent.raycastTarget = false;
            SetMapProgress(fill, percent, 0f);
            return (fill, percent);
        }

        private static void SetMapProgress(RectTransform fill, Text percent, float value)
        {
            fill.anchorMax = new Vector2(value, 1f);
            percent.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        /// <summary>正常完成、关闭控制器或退出 Play 都释放遮罩及输入锁。</summary>
        private void FinishCrossing()
        {
            if (!crossing) return;
            if (crossingCanvas) Destroy(crossingCanvas);
            crossingCanvas = null;
            crossing = false;
            crossingRoutine = null;
            session?.SetPaused(crossingRestorePause);
            if (initialized && scavengeRoot == null) RefreshOverlay();
        }

        /// <summary>生命周期中断时回收协程，释放遮罩与状态。</summary>
        private void CancelCrossing()
        {
            if (crossingRoutine != null) StopCoroutine(crossingRoutine);
            if (crossing && scavengeRoot != null && scavengeRun == null) LeaveScavenge();
            FinishCrossing();
        }
    }
}
