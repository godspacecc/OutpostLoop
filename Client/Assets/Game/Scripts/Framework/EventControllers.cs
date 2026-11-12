using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QFramework
{
    public sealed class PointerClickEventCtrl : MonoBehaviour, IPointerClickHandler
    {
        public EasyEvent<PointerEventData> OnPointerClickEvent = new EasyEvent<PointerEventData>();
        public EasyEvent<PointerEventData, object[]> OnPointerClickEventWithParams =
            new EasyEvent<PointerEventData, object[]>();
        public object[] Args;

        public void OnPointerClick(PointerEventData eventData)
        {
            OnPointerClickEvent.Trigger(eventData);

            if (Args != null && Args.Length > 0)
            {
                OnPointerClickEventWithParams.Trigger(eventData, Args);
            }
        }
    }

    public sealed class DragEventCtrl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public EasyEvent<PointerEventData> OnBeginDragEvent = new EasyEvent<PointerEventData>();
        public EasyEvent<PointerEventData, object[]> OnBeginDragEventWithParams =
            new EasyEvent<PointerEventData, object[]>();
        public EasyEvent<PointerEventData> OnDragEvent = new EasyEvent<PointerEventData>();
        public EasyEvent<PointerEventData, object[]> OnDragEventWithParams =
            new EasyEvent<PointerEventData, object[]>();
        public EasyEvent<PointerEventData> OnEndDragEvent = new EasyEvent<PointerEventData>();
        public EasyEvent<PointerEventData, object[]> OnEndDragEventWithParams =
            new EasyEvent<PointerEventData, object[]>();
        public object[] BeginDragArgs;
        public object[] DragArgs;
        public object[] EndDragArgs;

        public void OnBeginDrag(PointerEventData eventData)
        {
            OnBeginDragEvent.Trigger(eventData);

            if (BeginDragArgs != null && BeginDragArgs.Length > 0)
            {
                OnBeginDragEventWithParams.Trigger(eventData, BeginDragArgs);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            OnDragEvent.Trigger(eventData);

            if (DragArgs != null && DragArgs.Length > 0)
            {
                OnDragEventWithParams.Trigger(eventData, DragArgs);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OnEndDragEvent.Trigger(eventData);

            if (EndDragArgs != null && EndDragArgs.Length > 0)
            {
                OnEndDragEventWithParams.Trigger(eventData, EndDragArgs);
            }
        }
    }

    public sealed class DropEventCtrl : MonoBehaviour, IDropHandler
    {
        public EasyEvent<PointerEventData> OnDropEvent = new EasyEvent<PointerEventData>();
        public EasyEvent<PointerEventData, object[]> OnDropEventWithParams =
            new EasyEvent<PointerEventData, object[]>();
        public object[] Args;

        public void OnDrop(PointerEventData eventData)
        {
            OnDropEvent.Trigger(eventData);

            if (Args != null && Args.Length > 0)
            {
                OnDropEventWithParams.Trigger(eventData, Args);
            }
        }
    }

    public sealed class PointerHoverEventCtrl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public EasyEvent<PointerEventData, object[]> OnPointerEnterEvent = new EasyEvent<PointerEventData, object[]>();
        public EasyEvent<PointerEventData, object[]> OnPointerExitEvent = new EasyEvent<PointerEventData, object[]>();
        public object[] Args;

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnPointerEnterEvent.Trigger(eventData, Args ?? Array.Empty<object>());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnPointerExitEvent.Trigger(eventData, Args ?? Array.Empty<object>());
        }
    }

    public sealed class ButtonClickEventCtrl : MonoBehaviour
    {
        public EasyEvent<object[]> OnClickEvent = new EasyEvent<object[]>();
        public object[] Args;

        public void OnClick()
        {
            OnClickEvent.Trigger(Args ?? Array.Empty<object>());
        }
    }

    public static class ArgsExtensions
    {
        public static T GetArg<T>(this object[] args, int index, T defaultValue = default)
        {
            if (args == null || index < 0 || index >= args.Length || args[index] == null)
            {
                return defaultValue;
            }

            return args[index] is T value ? value : defaultValue;
        }

        public static T GetArg<T>(this object[] args, int index)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (index < 0 || index >= args.Length)
            {
                throw new IndexOutOfRangeException($"Argument index out of range: {index}");
            }

            if (args[index] == null)
            {
                throw new ArgumentNullException($"args[{index}]");
            }

            return (T)args[index];
        }
    }
}
