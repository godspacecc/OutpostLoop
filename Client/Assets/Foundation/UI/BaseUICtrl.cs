using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game
{
    public enum BackgroundClickEffect
    {
        None,
        CloseOnly,
        CloseThrough,
        ThroughOnly
    }

    public enum BackgroundColorType
    {
        Transparent,
        Gray,
        DarkGray,
        Black,
        Custom
    }

    [Serializable]
    public sealed class BackgroundConfig
    {
        public BackgroundColorType ColorType = BackgroundColorType.Transparent;
        public Color CustomColor = Color.clear;
        public BackgroundClickEffect ClickEffect = BackgroundClickEffect.CloseOnly;
        public float Alpha = 0.5f;

        public Color GetBackgroundColor()
        {
            var alpha = Mathf.Clamp01(Alpha);
            switch (ColorType)
            {
                case BackgroundColorType.Transparent:
                    return Color.clear;
                case BackgroundColorType.Gray:
                    return new Color(0.5f, 0.5f, 0.5f, alpha);
                case BackgroundColorType.DarkGray:
                    return new Color(0.3f, 0.3f, 0.3f, alpha);
                case BackgroundColorType.Black:
                    return new Color(0f, 0f, 0f, alpha);
                case BackgroundColorType.Custom:
                    return new Color(CustomColor.r, CustomColor.g, CustomColor.b, alpha);
                default:
                    return Color.clear;
            }
        }
    }

    /// <summary>管理独立界面的加载、显隐和同层背景配对；服从固定绘制层及浮层门禁，不裁决玩法业务。</summary>
    public abstract class BaseUICtrl : IController
    {
        protected BaseUI BaseUI;
        private bool _initialized;
        private bool _isActive;
        private bool _isApplicationQuitting;
        private bool _isQuitCallbackRegistered;
        private readonly Dictionary<BaseUI, BackgroundRuntime> _backgroundsByUi = new Dictionary<BaseUI, BackgroundRuntime>();

        protected abstract string Path();
        protected abstract void OnInit();
        protected abstract void OnShow();
        protected abstract void OnHide();
        protected virtual void OnUnload() { }
        /// <summary>普通关闭入口的门禁；默认允许，仅由需要独占显示的界面覆盖。</summary>
        protected virtual bool CanHide() { return true; }
        protected virtual BackgroundConfig GetBackgroundConfig() { return null; }
        protected virtual void OnBackgroundClick(PointerEventData eventData)
        {
            var runtime = TryGetCurrentBackground();
            if (runtime == null)
            {
                return;
            }

            switch (runtime.Config.ClickEffect)
            {
                case BackgroundClickEffect.CloseOnly:
                    Hide();
                    break;
                case BackgroundClickEffect.CloseThrough:
                    Hide();
                    PerformClickThrough(eventData);
                    break;
                case BackgroundClickEffect.ThroughOnly:
                    PerformClickThrough(eventData);
                    break;
            }
        }

        protected virtual void PerformClickThrough(PointerEventData eventData)
        {
            var eventSystem = ResolveEventSystem(eventData);
            if (eventData == null || eventSystem == null || BaseUI == null)
            {
                return;
            }

            var runtime = TryGetCurrentBackground();
            var results = new List<RaycastResult>();
            var seenHandlers = new HashSet<GameObject>();
            eventSystem.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                var target = result.gameObject;
                if (target == null || ShouldSkipClickThroughTarget(runtime, target))
                {
                    continue;
                }

                var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                if (handler == null || ShouldSkipClickThroughTarget(runtime, handler) || !seenHandlers.Add(handler))
                {
                    continue;
                }

                ExecuteEvents.Execute(handler, eventData, ExecuteEvents.pointerClickHandler);
                break;
            }
        }

        private static EventSystem ResolveEventSystem(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return null;
            }

            var field = typeof(BaseEventData).GetField(
                "m_EventSystem",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (field?.GetValue(eventData) as EventSystem) ?? EventSystem.current;
        }

        public abstract UILayer Layer();

        public virtual bool BlocksGame => true;

        /// <summary>临时菜单随 Top 窗口关闭且不能重开；持久进度和工具入口保留，由 Top 自然遮挡。</summary>
        public virtual bool IsTransientPopup => true;

        public bool IsShow()
        {
            return BaseUI != null && _isActive;
        }

        public void Show()
        {
            if (Layer() == UILayer.Popup && IsTransientPopup && !UIHelper.CanShowPopup()) return;
            EnsureLoaded(false);

            if (BaseUI == null)
            {
                return;
            }

            if (_isActive)
            {
                MoveToTop();
                return;
            }

            var runtime = TryGetCurrentBackground();
            if (runtime != null)
            {
                runtime.GameObject.SetActive(true);
            }

            BaseUI.gameObject.SetActive(true);
            MovePairToTop();
            _isActive = true;
            UIHelper.NotifyShown(this);
            OnShow();
        }

        /// <summary>提前装载并初始化隐藏窗口；不执行显示生命周期，也不改变输入阻断或窗口顺序。</summary>
        internal void Preload() => EnsureLoaded(true);

        /// <summary>显示和预加载共用实例及初始化路径；已有窗口的显隐状态不受预加载影响。</summary>
        private void EnsureLoaded(bool hideOnLoad)
        {
            if (BaseUI == null)
            {
                if (!ReferenceEquals(BaseUI, null))
                {
                    OnBaseUIDestroyed(BaseUI);
                }
                else
                {
                    ResetLoadedState();
                }

                Load();
            }

            if (BaseUI == null)
            {
                return;
            }

            try
            {
                if (!_initialized)
                {
                    OnInit();
                    _initialized = true;
                }
            }
            finally
            {
                // 先让初始化新增的销毁注销组件完成激活，再于同一调用内隐藏，保证未显示就销毁也能注销。
                if (hideOnLoad && BaseUI != null && !_isActive) BaseUI.gameObject.SetActive(false);
            }
        }

        public void Hide()
        {
            if (BaseUI == null || !_isActive || !CanHide())
            {
                return;
            }

            OnHide();
            var runtime = TryGetCurrentBackground();
            if (runtime != null)
            {
                runtime.GameObject.SetActive(false);
            }

            BaseUI.gameObject.SetActive(false);
            _isActive = false;
            UIHelper.NotifyHidden(this);
        }

        public void MoveToTop()
        {
            if (Layer() == UILayer.Popup && IsTransientPopup && !UIHelper.CanShowPopup()) return;
            if (BaseUI != null)
            {
                MovePairToTop();
                UIHelper.NotifyShown(this);
            }
        }

        public IArchitecture GetArchitecture()
        {
            return global::Game.Game.Interface;
        }

        private void Load()
        {
            var relativePath = Path();
            if (!ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab, relativePath, out var address, out var addressError))
            {
                Log.Error($"[BaseUICtrl] UI 相对路径无效：{relativePath ?? "<null>"}；{addressError}");
                return;
            }

            var prefab = UIHelper.LoadPrefab(address);
            if (prefab == null)
            {
                Log.Error($"[BaseUICtrl] UI Prefab 未找到：{address}");
                return;
            }

            var root = UIRoot.EnsureInstance();
            if (root == null)
            {
                return;
            }

            var layerObj = root.GetLayerObj(Layer());
            if (layerObj == null)
            {
                Log.Error($"[BaseUICtrl] UI 层级未找到：{Layer()}");
                return;
            }

            var gameObject = Object.Instantiate(prefab, layerObj, false);
            BaseUI = gameObject.GetComponent<BaseUI>();

            if (BaseUI == null)
            {
                Log.Error($"[BaseUICtrl] UI Prefab 缺少 BaseUI：{address}");
                Object.DestroyImmediate(gameObject);
                return;
            }

            // 安全区只约束可交互内容；UI 根、Prefab 全屏背景和运行时背景继续覆盖物理屏幕。
            var safeAreaFitter = gameObject.GetComponent<SafeAreaFitter>() ?? gameObject.AddComponent<SafeAreaFitter>();
            safeAreaFitter.ApplyNow();

            var loadedUI = BaseUI;
            EnsureBackgroundFor(loadedUI, layerObj);
            loadedUI.OnTriggerDestroyEvent.Register(() => OnBaseUIDestroyed(loadedUI));
        }

        private void OnBaseUIDestroyed(BaseUI destroyedUI)
        {
            CleanupBackgroundFor(destroyedUI);
            if (!ReferenceEquals(BaseUI, destroyedUI))
            {
                return;
            }

            ResetLoadedState();
            UIHelper.NotifyHidden(this);
            try
            {
                OnUnload();
            }
            catch (System.Exception exception)
            {
                Log.Error($"[BaseUICtrl] UI 实例卸载异常：{exception}");
            }
        }

        private void ResetLoadedState()
        {
            BaseUI = null;
            _initialized = false;
            _isActive = false;
        }

        private void EnsureBackgroundFor(BaseUI ui, Transform layerObj)
        {
            if (ui == null || _backgroundsByUi.ContainsKey(ui))
            {
                return;
            }

            var config = GetBackgroundConfig();
            if (config == null)
            {
                return;
            }

            var background = new GameObject($"{ui.name}_Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(layerObj, false);
            var rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetSiblingIndex(Mathf.Max(0, ui.transform.GetSiblingIndex()));

            var image = background.GetComponent<Image>();
            image.color = config.GetBackgroundColor();
            image.raycastTarget = true;

            var relay = background.AddComponent<BackgroundClickRelay>();
            relay.Init(this, ui);
            background.SetActive(false);

            _backgroundsByUi[ui] = new BackgroundRuntime(ui, background, relay, config);
            RegisterQuitCallbackIfNeeded();
        }

        private void CleanupBackgroundFor(BaseUI ui)
        {
            if (ReferenceEquals(ui, null) || !_backgroundsByUi.TryGetValue(ui, out var runtime))
            {
                return;
            }

            _backgroundsByUi.Remove(ui);
            if (runtime.Relay != null)
            {
                runtime.Relay.Clear();
            }

            if (_isApplicationQuitting)
            {
                return;
            }

            if (runtime.GameObject != null)
            {
                DestroyBackground(runtime.GameObject);
            }

            UnregisterQuitCallbackIfUnused();
        }

        private static void DestroyBackground(GameObject background)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(background);
            }
            else
            {
                Object.DestroyImmediate(background);
            }
        }

        private void RegisterQuitCallbackIfNeeded()
        {
            if (_isQuitCallbackRegistered)
            {
                return;
            }

            LifeCycle.RegisterQuit(OnApplicationQuit);
            _isQuitCallbackRegistered = true;
        }

        private void OnApplicationQuit()
        {
            _isApplicationQuitting = true;
            _isQuitCallbackRegistered = false;
        }

        private void UnregisterQuitCallbackIfUnused()
        {
            if (!_isQuitCallbackRegistered || _backgroundsByUi.Count > 0)
            {
                return;
            }

            LifeCycle.UnregisterQuit(OnApplicationQuit);
            _isQuitCallbackRegistered = false;
        }

        private BackgroundRuntime TryGetCurrentBackground()
        {
            return BaseUI != null && _backgroundsByUi.TryGetValue(BaseUI, out var runtime) ? runtime : null;
        }

        private void MovePairToTop()
        {
            var runtime = TryGetCurrentBackground();
            if (runtime != null && runtime.Transform != null)
            {
                runtime.Transform.SetAsLastSibling();
            }

            BaseUI.transform.SetAsLastSibling();
            if (runtime != null && runtime.Transform != null)
            {
                runtime.Transform.SetSiblingIndex(Mathf.Max(0, BaseUI.transform.GetSiblingIndex() - 1));
            }
        }

        private bool ShouldSkipClickThroughTarget(BackgroundRuntime runtime, GameObject target)
        {
            if (target == null)
            {
                return true;
            }

            if (runtime != null && runtime.GameObject != null && target.transform.IsChildOf(runtime.Transform))
            {
                return true;
            }

            return BaseUI != null && target.transform.IsChildOf(BaseUI.transform);
        }

        private sealed class BackgroundRuntime
        {
            public BackgroundRuntime(BaseUI owner, GameObject gameObject, BackgroundClickRelay relay, BackgroundConfig config)
            {
                Owner = owner;
                GameObject = gameObject;
                Relay = relay;
                Config = config;
            }

            public BaseUI Owner { get; }
            public GameObject GameObject { get; }
            public Transform Transform => GameObject != null ? GameObject.transform : null;
            public BackgroundClickRelay Relay { get; }
            public BackgroundConfig Config { get; }
        }

        private sealed class BackgroundClickRelay : MonoBehaviour, IPointerClickHandler
        {
            private BaseUICtrl owner;
            private BaseUI ui;

            public void Init(BaseUICtrl owner, BaseUI ui)
            {
                this.owner = owner;
                this.ui = ui;
            }

            public void Clear()
            {
                owner = null;
                ui = null;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (owner == null || ui == null || !ReferenceEquals(owner.BaseUI, ui))
                {
                    return;
                }

                owner.OnBackgroundClick(eventData);
            }
        }
    }
}
