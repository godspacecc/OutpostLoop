using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 底部只反馈需要等待的动作。现场标签和进度均不接收指针输入。
        private RectTransform survivalContextPanel;
        private TMP_Text survivalContextLabel;
        private Image survivalContextTrack, survivalContextFill;
        private Rect SurvivalContextRect
        {
            get
            {
                var bounds = PerceptionRect(PerceptionGroup.Interaction);
                float visualHeight = bounds.height * (40f / PerceptionGeometry.BaseSize(PerceptionGroup.Interaction).y);
                return new Rect(bounds.x, bounds.yMax - visualHeight, bounds.width, visualHeight);
            }
        }

        private bool SurvivalContextHasProgress => scavengeRun.SearchRemaining > 0 ||
            scavengeRun.Survival.action != SurvivalAction.None;

        /// <summary>短暂观察后才显示进度，立即完成的操作由场景反馈。</summary>
        private void UpdateSurvivalContext()
        {
            if (!CanShowSurvivalHud() || !SurvivalContextHasProgress)
            {
                if (survivalContextPanel) survivalContextPanel.gameObject.SetActive(false);
                return;
            }
            var state = scavengeRun.Survival;
            bool searching = scavengeRun.SearchRemaining > 0;
            float remaining = searching ? scavengeRun.SearchRemaining : state.actionTime;
            float duration = searching ? ScavengeRun.SearchSeconds : state.actionDuration;
            if (duration - remaining < .15f)
            {
                if (survivalContextPanel) survivalContextPanel.gameObject.SetActive(false);
                return;
            }

            if (!survivalContextPanel) CreateSurvivalContext();
            PerceptionGeometry.Place(survivalContextPanel, SurvivalContextRect, new Vector2(350, 40));
            survivalContextPanel.gameObject.SetActive(true);
            survivalContextLabel.text = searching ? Loc.T("InventorySearching", remaining) :
                Loc.T("ContextActionTime", Loc.T("ContextAction." + state.action), remaining);
            float fraction = duration > 0 ? Mathf.Clamp01(1 - remaining / duration) : 0;
            survivalContextFill.rectTransform.anchorMax = new Vector2(fraction, 1);
        }

        /// <summary>进度条沿用感知界面布局里的位置与缩放。</summary>
        private void CreateSurvivalContext()
        {
            var root = new GameObject("RiverValleyContextCanvas", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = new GameObject("Interaction", typeof(Image));
            survivalContextPanel = (RectTransform)panel.transform;
            survivalContextPanel.SetParent(root.transform, false);
            var frame = panel.GetComponent<Image>();
            frame.color = LootBackground;
            frame.raycastTarget = false;

            survivalContextLabel = new GameObject("Label", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            survivalContextLabel.transform.SetParent(panel.transform, false);
            survivalContextLabel.font = UnityEngine.Resources.Load<TMP_FontAsset>("RiverValleyInventoryFont");
            survivalContextLabel.fontSize = 20;
            survivalContextLabel.alignment = TextAlignmentOptions.Center;
            survivalContextLabel.color = LootText;
            survivalContextLabel.raycastTarget = false;
            var labelRect = survivalContextLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 5);
            labelRect.offsetMax = new Vector2(-8, 0);

            survivalContextTrack = new GameObject("Progress", typeof(Image)).GetComponent<Image>();
            survivalContextTrack.transform.SetParent(panel.transform, false);
            survivalContextTrack.color = new Color(.18f, .24f, .23f);
            survivalContextTrack.raycastTarget = false;
            survivalContextTrack.rectTransform.anchorMin = Vector2.zero;
            survivalContextTrack.rectTransform.anchorMax = Vector2.right;
            survivalContextTrack.rectTransform.offsetMin = new Vector2(8, 3);
            survivalContextTrack.rectTransform.offsetMax = new Vector2(-8, 7);

            survivalContextFill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
            survivalContextFill.transform.SetParent(survivalContextTrack.transform, false);
            survivalContextFill.raycastTarget = false;
            survivalContextFill.color = new Color(.35f, .78f, .68f);
            survivalContextFill.rectTransform.anchorMin = Vector2.zero;
            survivalContextFill.rectTransform.anchorMax = Vector2.one;
            survivalContextFill.rectTransform.offsetMin = survivalContextFill.rectTransform.offsetMax = Vector2.zero;
            // 纯色 Image 没有 Sprite，使用相同的锚点来显示填充比例。
        }
    }
}
