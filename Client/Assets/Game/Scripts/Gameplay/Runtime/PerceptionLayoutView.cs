using System;
using Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static OutpostLoop.PrototypeMenuWidgets;

namespace OutpostLoop
{
    /// <summary>设置内的布局编辑会话，只修改独立草稿。</summary>
    internal sealed class PerceptionLayoutView : MonoBehaviour
    {
        private static readonly string[] Keys =
            { "PerceptionAction", "PerceptionMap", "PerceptionBody", "PerceptionInteraction", "PerceptionEquipment" };

        private readonly Rect[] bounds = new Rect[PerceptionGeometry.Count];
        private readonly Image[] frames = new Image[PerceptionGeometry.Count];
        private readonly TMP_Text[] labels = new TMP_Text[PerceptionGeometry.Count];
        private PerceptionLayout draft;
        private PrototypeMenuWidgets widgets;
        private RectTransform root, preview;
        private TMP_Text selectedLabel, scaleLabel, errorLabel;
        private Button visible;
        private Slider size;
        private RawImage scenery;
        private RenderTexture picture;
        private Camera sourceCamera;
        private Action close;
        private PerceptionGroup selected;
        private int activePointer = int.MinValue;
        private Vector2 dragOrigin;
        private Rect dragRect;
        private PerceptionPlacement dragStart;
        private Rect lastArea;
        private Vector2Int lastScreen;
        private RectTransform packPreview;

        internal void Open(PerceptionLayout current, Camera camera, Action onClose)
        {
            draft = current.Clone();
            close = onClose;
            sourceCamera = camera;
            widgets = new PrototypeMenuWidgets();
            root = (RectTransform)transform;
            preview = Rect(root, "GamePreview", 0, 0, 1, 1);
            scenery = preview.gameObject.AddComponent<RawImage>();
            scenery.raycastTarget = false;
            for (int i = 0; i < frames.Length; i++)
            {
                var group = (PerceptionGroup)i;
                var dimensions = PerceptionGeometry.BaseSize(group);
                frames[i] = Graphic(preview, group.ToString(), 0, 0, dimensions.x, dimensions.y, Well, true);
                labels[i] = widgets.Label(frames[i].transform, "Preview", 6, 0, dimensions.x - 12, dimensions.y,
                    "", 18, TextColor, true, TextAlignmentOptions.Center);
                var handle = frames[i].gameObject.AddComponent<PerceptionDragHandle>();
                handle.Owner = this;
                handle.Group = group;
            }
            packPreview = widgets.Label(preview, "PackPreview", 0, 0, 137, 25,
                "Tab  2/6", 16, Muted).rectTransform;

            var top = Graphic(root, "Toolbar", 0, 0, 940, 48, Surface, true).rectTransform;
            top.anchorMin = top.anchorMax = new Vector2(.5f, 1);
            top.pivot = new Vector2(.5f, 1);
            top.anchoredPosition = new Vector2(0, -8);
            widgets.Label(top, "Title", 14, 0, 330, 48, Loc.T("PerceptionLayout"), 26, TextColor);
            widgets.Button(top, "Restore", 500, 3, 143, 42, Loc.T("PerceptionReset"), Restore, 20);
            widgets.Button(top, "Cancel", 652, 3, 135, 42, Loc.T("CommonCancel"), Return, 20);
            widgets.Button(top, "Apply", 796, 3, 136, 42, Loc.T("PerceptionApply"), Apply, 20, true);

            var footer = Graphic(root, "Options", 0, 0, 940, 50, Surface, true).rectTransform;
            footer.anchorMin = footer.anchorMax = new Vector2(.5f, 0);
            footer.pivot = new Vector2(.5f, 0);
            footer.anchoredPosition = new Vector2(0, 10);
            selectedLabel = widgets.Label(footer, "Selected", 14, 0, 171, 50, "", 20, TextColor);
            visible = widgets.Button(footer, "Visibility", 185, 4, 178, 42, "", Toggle, 18);
            scaleLabel = widgets.Label(footer, "Size", 385, 0, 140, 50, "", 20, TextColor);
            size = CreateSlider(footer);
            errorLabel = widgets.Label(footer, "Error", 733, 0, 200, 50, "", 17, Warning, true);
            RefreshPreview();
            Render();
        }

        private Slider CreateSlider(Transform parent)
        {
            var control = Rect(parent, "Scale", 523, 0, 190, 50);
            Graphic(control, "Track", 9, 23, 172, 4, Line);
            var travel = Rect(control, "Travel", 9, 0, 172, 50);
            var thumb = Graphic(travel, "Handle", 0, 15, 18, 20, Focus);
            thumb.rectTransform.pivot = new Vector2(.5f, 1);
            Graphic(control, "Hit", 0, 0, 190, 50, Color.clear, true);
            var slider = control.gameObject.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.handleRect = thumb.rectTransform;
            slider.minValue = 15;
            slider.maxValue = 28;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(value =>
            {
                draft.Get(selected).Scale = value / 20f;
                Render();
            });
            return slider;
        }

        private void LateUpdate()
        {
            if (lastArea == PerceptionGeometry.ScreenArea && lastScreen.x == Screen.width && lastScreen.y == Screen.height) return;
            StopDrag(true);
            RefreshPreview();
            Render();
        }

        private void RefreshPreview()
        {
            lastArea = PerceptionGeometry.ScreenArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            float scale = PerceptionGeometry.ScreenScale;
            var screen = new Vector2(Screen.width / scale, Screen.height / scale);
            var available = root.rect.size - new Vector2(32, 132);
            float zoom = Mathf.Max(.01f, Mathf.Min(available.x / screen.x, available.y / screen.y));
            preview.anchorMin = preview.anchorMax = preview.pivot = Vector2.one / 2;
            preview.anchoredPosition = Vector2.zero;
            preview.sizeDelta = screen;
            preview.localScale = Vector3.one * zoom;
            var pack = PerceptionGeometry.Pack(lastArea);
            PerceptionGeometry.Place(packPreview, pack, pack.size);
            scenery.color = sourceCamera ? Color.white : Well;
            if (!sourceCamera) return;

            ReleasePicture();
            picture = new RenderTexture(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), 24);
            var original = sourceCamera.targetTexture;
            try
            {
                sourceCamera.targetTexture = picture;
                sourceCamera.Render();
                scenery.texture = picture;
            }
            finally { sourceCamera.targetTexture = original; }
        }

        private void Render()
        {
            PerceptionGeometry.Resolve(draft, lastArea, bounds);
            var item = draft.Get(selected);
            selectedLabel.text = Loc.T(Keys[(int)selected]);
            Caption(visible, Loc.T(item.Visible == true ? "PerceptionShown" : "PerceptionHidden"));
            scaleLabel.text = Loc.T("PerceptionSize", Mathf.RoundToInt(item.Scale.Value * 100));
            size.SetValueWithoutNotify(item.Scale.Value * 20);
            for (int i = 0; i < frames.Length; i++)
            {
                var group = (PerceptionGroup)i;
                bool shown = draft.Get(group).Visible == true;
                var frame = frames[i];
                var outline = frame.GetComponent<Outline>();
                if (!outline) outline = frame.gameObject.AddComponent<Outline>();
                outline.effectDistance = Vector2.one * (group == selected ? 2 : 1);
                outline.effectColor = group == selected ? Focus : Line;
                frame.color = shown ? new Color(.045f, .07f, .07f, .9f) : new Color(.07f, .1f, .1f, .6f);
                labels[i].color = shown ? TextColor : Muted;
                labels[i].text = shown ? Loc.T(Keys[i] + "Sample") : Loc.T(Keys[i]);
                PerceptionGeometry.Place(frame.rectTransform, bounds[i], PerceptionGeometry.BaseSize(group));
            }
        }

        /// <summary>用预览局部坐标读取拖动，像素比例和缩放变化由布局计算负责。</summary>
        internal void BeginDrag(PerceptionGroup group, PointerEventData evt)
        {
            if (evt.button != PointerEventData.InputButton.Left || activePointer != int.MinValue) return;
            selected = group;
            activePointer = evt.pointerId;
            dragStart = draft.Get(group).Clean(0, 0);
            dragOrigin = Position(evt);
            dragRect = bounds[(int)group];
            Render();
        }

        internal void Drag(PointerEventData evt)
        {
            if (activePointer != evt.pointerId) return;
            var target = dragRect;
            target.position += Position(evt) - dragOrigin;
            PerceptionGeometry.Move(draft.Get(selected), target, lastArea);
            Render();
        }

        internal void EndDrag(PointerEventData evt)
        {
            if (activePointer == evt.pointerId) StopDrag(false);
        }

        private Vector2 Position(PointerEventData evt)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(preview, evt.position, evt.pressEventCamera, out var point);
            return new Vector2(point.x, -point.y);
        }

        private void StopDrag(bool revert)
        {
            if (activePointer == int.MinValue) return;
            if (revert)
            {
                var item = draft.Get(selected);
                item.X = dragStart.X;
                item.Y = dragStart.Y;
            }
            activePointer = int.MinValue;
        }

        internal void Escape()
        {
            if (activePointer == int.MinValue) { Return(); return; }
            StopDrag(true);
            Render();
        }

        private void Restore()
        {
            StopDrag(false);
            draft = new PerceptionLayout().Clone();
            errorLabel.text = "";
            Render();
        }

        private void Toggle()
        {
            var item = draft.Get(selected);
            item.Visible = item.Visible != true;
            Render();
        }

        private void Apply()
        {
            var settings = Game.Game.Interface.GetSystem<SettingsSystem>();
            if (settings.SavePerception(draft, out _)) Return();
            else errorLabel.text = Loc.T("PerceptionSaveFailed");
        }

        private void Return() => close?.Invoke();

        private void OnDestroy() => ReleasePicture();

        private void ReleasePicture()
        {
            if (!picture) return;
            picture.Release();
            Destroy(picture);
            picture = null;
        }
    }

    /// <summary>布局框的鼠标捕获由 EventSystem 管理，并由会话处理回滚。</summary>
    internal sealed class PerceptionDragHandle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        internal PerceptionLayoutView Owner;
        internal PerceptionGroup Group;
        public void OnPointerDown(PointerEventData eventData) => Owner.BeginDrag(Group, eventData);
        public void OnDrag(PointerEventData eventData) => Owner.Drag(eventData);
        public void OnPointerUp(PointerEventData eventData) => Owner.EndDrag(eventData);
    }
}
