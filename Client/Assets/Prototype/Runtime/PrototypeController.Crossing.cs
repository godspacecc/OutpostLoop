using System;
using System.Collections;
using Boot.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 过渡锁只由表现流程持有，防止双击启动两份世界。
        private bool crossing;
        // 独立顶层遮罩，先于同步地图创建显示。
        private GameObject crossingCanvas;
        // 关闭控制器时停止尚未完成的过渡。
        private Coroutine crossingRoutine;
        // 保存进入世界后的领域暂停状态，交接时恢复。
        private bool crossingRestorePause;
        /// <summary>供入口与验收观察的只读过渡状态。</summary>
        public bool IsCrossing => crossing;

        /// <summary>主菜单确认后才关闭旧续玩档，失败时保留菜单与原行动。</summary>
        internal bool TryRestartSurvival()
        {
            if (crossing || !initialized || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return false;
            if (!CloseSurvivalSave()) return false;
            CrossToWorld(StartSurvival);
            return true;
        }

        /// <summary>菜单统一出发入口；规则仍由传入的现有行动执行。</summary>
        internal void CrossToWorld(Action enter, bool resume = false)
        {
            if (crossing || !initialized || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return;
            crossing = true;
            crossingRestorePause = World.Run.Paused;
            crossingRoutine = StartCoroutine(CrossWorld(enter, resume, !resume && JourneyProgress.visitedSpace));
        }

        /// <summary>先让穿越画面实际绘制，再创建世界；仅在准备完成后交还控制。</summary>
        private IEnumerator CrossWorld(Action enter, bool resume, bool showCrossing)
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
                var backdrop = CosmicBackdrop.Create(crossingCanvas.transform, "Crossing");
                backdrop.GetComponent<RawImage>().raycastTarget = true;
            }
            else
                crossingCanvas.AddComponent<Image>().color = new Color32(9, 15, 24, 255);
            var labelRoot = new GameObject("CrossingStatus", typeof(RectTransform), typeof(Text));
            labelRoot.transform.SetParent(crossingCanvas.transform, false);
            var rect = (RectTransform)labelRoot.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f,0);
            rect.sizeDelta = new Vector2(1000,100);
            rect.anchoredPosition = new Vector2(0,100);
            var label = labelRoot.GetComponent<Text>();
            label.font = font;
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(225,235,243,255);
            label.text = showCrossing ? "正在穿越\n正在接通世界" : resume ? "正在恢复行动" : "正在进入游戏";
            label.raycastTarget = false;
            yield return null;
            yield return new WaitForSecondsRealtime(.45f);
            Exception failure = null;
            try
            {
                enter();
                crossingRestorePause = World.Run.Paused;
                session.SetPaused(true);
            }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                Debug.LogException(failure);
                label.text = "加载中断\n正在返回菜单";
                // 清理已创建的部分地图，让用户能再次从开始页进入。
                if (scavengeRoot != null) LeaveScavenge();
                Restart(false);
                crossingRestorePause = true;
            }
            else label.text = showCrossing ? "正在穿越\n即将抵达" : resume ? "正在恢复行动" : "即将醒来";
            yield return new WaitForSecondsRealtime(.55f);
            FinishCrossing();
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
            FinishCrossing();
        }
    }
}
