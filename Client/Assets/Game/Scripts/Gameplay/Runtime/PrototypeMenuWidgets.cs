using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>暂停、帮助和设置共用的 uGUI 控件样式，具体页面所有者负责交互。</summary>
    internal sealed class PrototypeMenuWidgets
    {
        internal static readonly Color Shade = new(.026f, .041f, .046f, .89f);
        internal static readonly Color Surface = new(.074f, .105f, .105f);
        internal static readonly Color Well = new(.044f, .066f, .069f);
        internal static readonly Color Line = new(.27f, .37f, .35f);
        internal static readonly Color TextColor = new(.93f, .95f, .92f);
        internal static readonly Color Muted = new(.68f, .77f, .73f);
        internal static readonly Color Focus = new(.34f, .57f, .49f);
        internal static readonly Color Warning = new(.99f, .76f, .46f);

        private readonly TMP_FontAsset font = UnityEngine.Resources.Load<TMP_FontAsset>("RiverValleyInventoryFont");

        /// <summary>坐标从面板左上角起算，统一跟随画布缩放。</summary>
        internal static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var node = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            node.SetParent(parent, false);
            node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1);
            node.anchoredPosition = new Vector2(x, -y);
            node.sizeDelta = new Vector2(width, height);
            return node;
        }

        internal static void Stretch(RectTransform node)
        {
            node.anchorMin = Vector2.zero;
            node.anchorMax = Vector2.one;
            node.offsetMin = node.offsetMax = Vector2.zero;
        }

        internal static RectTransform Panel(Transform parent, string name, float width, float height)
        {
            var panel = Graphic(parent, name, 0, 0, width, height, Surface, true).rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one / 2;
            panel.anchoredPosition = Vector2.zero;
            return panel;
        }

        internal static Image Graphic(Transform parent, string name, float x, float y, float width, float height,
            Color color, bool hit = false)
        {
            var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = hit;
            return image;
        }

        internal TMP_Text Label(Transform parent, string name, float x, float y, float width, float height,
            string value, float size, Color color, bool wrap = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.fontSizeMin = Mathf.Max(12, size - 4);
            label.fontSizeMax = size;
            label.enableAutoSizing = !wrap;
            label.enableWordWrapping = wrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = alignment;
            label.text = value;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        internal Button Button(Transform parent, string name, float x, float y, float width, float height,
            string caption, Action click, float size = 22, bool primary = false)
        {
            var graphic = Graphic(parent, name, x, y, width, height, Color.white, true);
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.navigation = new Navigation {mode = Navigation.Mode.None};
            Style(button, primary);
            Label(graphic.transform, "Label", 12, 0, width - 24, height, caption, size, TextColor,
                alignment: TextAlignmentOptions.Center);
            button.onClick.AddListener(() => click());
            return button;
        }

        internal static void Style(Button button, bool primary)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = primary ? new Color(.25f, .43f, .37f) : new Color(.16f, .24f, .23f);
            colors.highlightedColor = Focus;
            colors.pressedColor = new Color(.20f, .34f, .29f);
            colors.disabledColor = new Color(.13f, .16f, .16f);
            colors.fadeDuration = 0;
            button.colors = colors;
            // 默认色须立即生效，分页换选中态时无需等待指针移动。
            button.targetGraphic.CrossFadeColor(button.IsInteractable() ? colors.normalColor : colors.disabledColor, 0, true, true);
        }

        internal static void Caption(Button button, string value) =>
            button.GetComponentInChildren<TMP_Text>().text = value;
    }
}
