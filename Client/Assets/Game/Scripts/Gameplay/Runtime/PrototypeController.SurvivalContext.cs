using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 互动按钮与搜索、持续动作共用底部容器。文本沿用物品界面的字体。
        private RectTransform survivalContextPanel;
        private Button survivalContextButton;
        private TMP_Text survivalContextLabel;
        private Image survivalContextTrack, survivalContextFill;
        private string survivalContextShownAction;
        private static Rect SurvivalContext(float width, float height) => new(width / 2 - 175, height - 61, 350, 40);

        /// <summary>点击拦截只覆盖当前可见的底部操作或进度。</summary>
        private bool SurvivalContextContains(Vector2 point, float width, float height) =>
            CanShowSurvivalHud() && SurvivalContext(width, height).Contains(point) &&
            (SurvivalContextHasProgress || SurvivalInteraction() != null);

        private bool SurvivalContextHasProgress => scavengeRun.SearchRemaining > 0 ||
            scavengeRun.Survival.action != SurvivalAction.None;

        /// <summary>模态窗口隐藏提示。进行中的操作在同一位置读取领域计时。</summary>
        private void UpdateSurvivalContext()
        {
            bool hud = CanShowSurvivalHud();
            bool progress = hud && SurvivalContextHasProgress;
            string action = hud && !progress ? SurvivalInteraction() : null;
            survivalContextShownAction = action;
            if (action == null && !progress)
            {
                if (survivalContextPanel) survivalContextPanel.gameObject.SetActive(false);
                return;
            }

            if (!survivalContextPanel) CreateSurvivalContext();
            survivalContextPanel.gameObject.SetActive(true);
            survivalContextButton.interactable = action != null;
            survivalContextTrack.gameObject.SetActive(progress);
            if (!progress)
            {
                survivalContextLabel.text = "E  " + action;
                return;
            }

            var state = scavengeRun.Survival;
            bool searching = scavengeRun.SearchRemaining > 0;
            float remaining = searching ? scavengeRun.SearchRemaining : state.actionTime;
            float duration = searching ? ScavengeRun.SearchSeconds : state.actionDuration;
            survivalContextLabel.text = searching ? Loc.T("InventorySearching", remaining) :
                Loc.T("ContextActionTime", Loc.T("ContextAction." + state.action), remaining);
            float fraction = duration > 0 ? Mathf.Clamp01(1 - remaining / duration) : 0;
            survivalContextFill.rectTransform.anchorMax = new Vector2(fraction, 1);
        }

        /// <summary>共用 HUD 缩放，底部正中锚定。只有操作按钮接收指针点击。</summary>
        private void CreateSurvivalContext()
        {
            var root = new GameObject("RiverValleyContextCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = new GameObject("Interaction", typeof(Image), typeof(Button));
            survivalContextPanel = (RectTransform)panel.transform;
            survivalContextPanel.SetParent(root.transform, false);
            survivalContextPanel.anchorMin = survivalContextPanel.anchorMax = new Vector2(.5f, 0);
            Rect bounds = SurvivalContext(1280, 720);
            survivalContextPanel.sizeDelta = bounds.size;
            survivalContextPanel.anchoredPosition = new Vector2(0, 720 - bounds.center.y);
            var frame = panel.GetComponent<Image>();
            frame.color = LootBackground;
            survivalContextButton = panel.GetComponent<Button>();
            survivalContextButton.targetGraphic = frame;
            survivalContextButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = ColorBlock.defaultColorBlock;
            colors.disabledColor = Color.white;
            survivalContextButton.colors = colors;
            survivalContextButton.onClick.AddListener(() =>
            {
                if (CanShowSurvivalHud() && !SurvivalContextHasProgress && survivalContextShownAction != null &&
                    survivalContextShownAction == SurvivalInteraction()) InteractSurvival();
            });

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
