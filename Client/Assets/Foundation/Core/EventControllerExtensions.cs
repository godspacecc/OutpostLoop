using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QFramework
{
    public static class EventControllerExtensions
    {
        public static IUnRegister OnPointerClickEvent(this Component component, Action<PointerEventData> onPointerClick)
        {
            var ctrl = GetOrAddComponent<PointerClickEventCtrl>(component);
            ctrl.Args = null;
            return ctrl.OnPointerClickEvent.Register(onPointerClick);
        }

        public static IUnRegister OnPointerClickEvent(
            this Component component,
            Action<PointerEventData, object[]> onPointerClick,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<PointerClickEventCtrl>(component);
            ctrl.Args = args;
            return ctrl.OnPointerClickEventWithParams.Register(onPointerClick);
        }

        public static IUnRegister OnBeginDragEvent(
            this Component component,
            Action<PointerEventData> onBeginDrag)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.BeginDragArgs = null;
            return ctrl.OnBeginDragEvent.Register(onBeginDrag);
        }

        public static IUnRegister OnBeginDragEvent(
            this Component component,
            Action<PointerEventData, object[]> onBeginDrag,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.BeginDragArgs = args;
            return ctrl.OnBeginDragEventWithParams.Register(onBeginDrag);
        }

        public static IUnRegister OnDragEvent(
            this Component component,
            Action<PointerEventData> onDrag)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.DragArgs = null;
            return ctrl.OnDragEvent.Register(onDrag);
        }

        public static IUnRegister OnDragEvent(
            this Component component,
            Action<PointerEventData, object[]> onDrag,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.DragArgs = args;
            return ctrl.OnDragEventWithParams.Register(onDrag);
        }

        public static IUnRegister OnEndDragEvent(
            this Component component,
            Action<PointerEventData> onEndDrag)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.EndDragArgs = null;
            return ctrl.OnEndDragEvent.Register(onEndDrag);
        }

        public static IUnRegister OnEndDragEvent(
            this Component component,
            Action<PointerEventData, object[]> onEndDrag,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<DragEventCtrl>(component);
            ctrl.EndDragArgs = args;
            return ctrl.OnEndDragEventWithParams.Register(onEndDrag);
        }

        public static IUnRegister OnDropEvent(
            this Component component,
            Action<PointerEventData> onDrop)
        {
            var ctrl = GetOrAddComponent<DropEventCtrl>(component);
            ctrl.Args = null;
            return ctrl.OnDropEvent.Register(onDrop);
        }

        public static IUnRegister OnDropEvent(
            this Component component,
            Action<PointerEventData, object[]> onDrop,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<DropEventCtrl>(component);
            ctrl.Args = args;
            return ctrl.OnDropEventWithParams.Register(onDrop);
        }

        public static IUnRegister OnPointerEnterEvent(
            this Component component,
            Action<PointerEventData, object[]> onPointerEnter,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<PointerHoverEventCtrl>(component);
            ctrl.Args = args;
            return ctrl.OnPointerEnterEvent.Register(onPointerEnter);
        }

        public static IUnRegister OnPointerExitEvent(
            this Component component,
            Action<PointerEventData, object[]> onPointerExit,
            params object[] args)
        {
            var ctrl = GetOrAddComponent<PointerHoverEventCtrl>(component);
            ctrl.Args = args;
            return ctrl.OnPointerExitEvent.Register(onPointerExit);
        }

        public static IUnRegister OnClickEvent(this Button button, Action<object[]> onClick, params object[] args)
        {
            var ctrl = GetOrAddComponent<ButtonClickEventCtrl>(button);
            ctrl.Args = args;
            button.onClick.RemoveListener(ctrl.OnClick);
            button.onClick.AddListener(ctrl.OnClick);
            return ctrl.OnClickEvent.Register(onClick);
        }

        public static void RemoveAllOnClickEvents(this Button button)
        {
            button.onClick.RemoveAllListeners();
            var ctrl = button.GetComponent<ButtonClickEventCtrl>();
            if (ctrl == null)
            {
                return;
            }

            ctrl.OnClickEvent = new EasyEvent<object[]>();
            ctrl.Args = null;
        }

        public static void RemoveAllOnPointerClickEvents(this Component component)
        {
            var ctrl = component.GetComponent<PointerClickEventCtrl>();
            if (ctrl == null)
            {
                return;
            }

            ctrl.OnPointerClickEvent = new EasyEvent<PointerEventData>();
            ctrl.OnPointerClickEventWithParams = new EasyEvent<PointerEventData, object[]>();
            ctrl.Args = null;
        }

        public static void RemoveAllOnDragEvents(this Component component)
        {
            var ctrl = component.GetComponent<DragEventCtrl>();
            if (ctrl == null)
            {
                return;
            }

            ctrl.OnBeginDragEvent = new EasyEvent<PointerEventData>();
            ctrl.OnBeginDragEventWithParams = new EasyEvent<PointerEventData, object[]>();
            ctrl.OnDragEvent = new EasyEvent<PointerEventData>();
            ctrl.OnDragEventWithParams = new EasyEvent<PointerEventData, object[]>();
            ctrl.OnEndDragEvent = new EasyEvent<PointerEventData>();
            ctrl.OnEndDragEventWithParams = new EasyEvent<PointerEventData, object[]>();
            ctrl.BeginDragArgs = null;
            ctrl.DragArgs = null;
            ctrl.EndDragArgs = null;
        }

        public static void RemoveAllOnDropEvents(this Component component)
        {
            var ctrl = component.GetComponent<DropEventCtrl>();
            if (ctrl == null)
            {
                return;
            }

            ctrl.OnDropEvent = new EasyEvent<PointerEventData>();
            ctrl.OnDropEventWithParams = new EasyEvent<PointerEventData, object[]>();
            ctrl.Args = null;
        }

        public static void RemoveAllOnPointerHoverEvents(this Component component)
        {
            var ctrl = component.GetComponent<PointerHoverEventCtrl>();
            if (ctrl == null)
            {
                return;
            }

            ctrl.OnPointerEnterEvent = new EasyEvent<PointerEventData, object[]>();
            ctrl.OnPointerExitEvent = new EasyEvent<PointerEventData, object[]>();
            ctrl.Args = null;
        }

        public static void RemoveAllEvents(this Component component)
        {
            if (component is Button button)
            {
                button.RemoveAllOnClickEvents();
            }

            component.RemoveAllOnPointerClickEvents();
            component.RemoveAllOnDragEvents();
            component.RemoveAllOnDropEvents();
            component.RemoveAllOnPointerHoverEvents();
        }

        private static T GetOrAddComponent<T>(Component component) where T : Component
        {
            var existing = component.GetComponent<T>();
            return existing != null ? existing : component.gameObject.AddComponent<T>();
        }
    }
}
