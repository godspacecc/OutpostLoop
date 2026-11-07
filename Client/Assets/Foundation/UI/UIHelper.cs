using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>保存独立 UI 实例、显示顺序和玩法阻断快照；协调 Top 与临时浮层，不修改领域状态。</summary>
    public static class UIHelper
    {
        private static readonly Dictionary<string, BaseUICtrl> UICtrlDict = new Dictionary<string, BaseUICtrl>();
        private static readonly List<BaseUICtrl> ShowOrder = new List<BaseUICtrl>();
        private static Func<string, GameObject> _prefabLoader = DefaultLoadPrefab;
        private static bool lastBlockingUIOpen;
        public static event Action<bool> BlockingUIChanged;

        public static bool HasUIOpen
        {
            get
            {
                foreach (var ctrl in UICtrlDict.Values)
                {
                    if (ctrl != null && ctrl.BlocksGame && ctrl.IsShow())
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static BaseUICtrl GetTopmostBlockingUI()
        {
            BaseUICtrl selected = null;
            var selectedLayer = UILayer.Dialog;

            for (var i = ShowOrder.Count - 1; i >= 0; i--)
            {
                var ctrl = ShowOrder[i];
                if (ctrl == null || !ctrl.BlocksGame || !ctrl.IsShow())
                {
                    continue;
                }

                var layer = ctrl.Layer();
                if (selected == null || UILayerOrder.Get(layer) > UILayerOrder.Get(selectedLayer))
                {
                    selected = ctrl;
                    selectedLayer = layer;
                }
            }

            return selected;
        }

        public static T ShowUI<T>() where T : BaseUICtrl, new()
        {
            var ctrl = GetOrCreateUI<T>();
            ctrl.Show();
            return ctrl;
        }

        /// <summary>复用窗口所属控制器，提前创建隐藏实例；显示仍须经过正式 Show 流程。</summary>
        internal static T PreloadUI<T>() where T : BaseUICtrl, new()
        {
            var ctrl = GetOrCreateUI<T>();
            ctrl.Preload();
            return ctrl;
        }

        public static void HideUI<T>() where T : BaseUICtrl
        {
            if (UICtrlDict.TryGetValue(typeof(T).Name, out var ctrl))
            {
                ctrl.Hide();
            }
        }

        public static T GetUICtrl<T>() where T : BaseUICtrl
        {
            if (UICtrlDict.TryGetValue(typeof(T).Name, out var ctrl))
            {
                return ctrl as T;
            }

            return null;
        }

        /// <summary>注入本项目的界面资源加载入口；传入空值恢复未配置诊断。</summary>
        public static void SetPrefabLoader(Func<string, GameObject> prefabLoader)
        {
            _prefabLoader = prefabLoader ?? DefaultLoadPrefab;
        }

        public static void SetPrefabLoaderForTests(Func<string, GameObject> prefabLoader)
        {
            _prefabLoader = prefabLoader ?? DefaultLoadPrefab;
        }

        /// <summary>会话退出时卸载界面并清空静态缓存；不销毁场景作者配置的 UIRoot。</summary>
        public static void Shutdown()
        {
            foreach (var ctrl in new List<BaseUICtrl>(UICtrlDict.Values))
            {
                try { ctrl?.Unload(); }
                catch (Exception error) { Log.Error("[UIHelper] 窗口卸载失败：" + error); }
            }
            UICtrlDict.Clear();
            ShowOrder.Clear();
            GameObjectPoolHelper.Clear();
            _prefabLoader = DefaultLoadPrefab;
            lastBlockingUIOpen = false;
            BlockingUIChanged = null;
            UIRoot.ClearForTests();
        }

        public static void ResetForTests()
        {
            foreach (var ctrl in UICtrlDict.Values)
            {
                ctrl?.Hide();
            }

            UICtrlDict.Clear();
            ShowOrder.Clear();
            GameObjectPoolHelper.ClearForTests();
            TimerMgr.Instance.ClearForTests();
            _prefabLoader = DefaultLoadPrefab;
            lastBlockingUIOpen = false;
            BlockingUIChanged = null;
            ResourceLoader.ResetForTests();

            foreach (var uiRoot in Object.FindObjectsOfType<UIRoot>(true))
            {
                var scene = uiRoot.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path))
                {
                    Object.DestroyImmediate(uiRoot.gameObject);
                }
            }

            UIRoot.ClearForTests();
        }

        internal static void NotifyShown(BaseUICtrl ctrl)
        {
            if (ctrl == null)
            {
                return;
            }

            ShowOrder.RemoveAll(item => ReferenceEquals(item, ctrl));
            ShowOrder.Add(ctrl);
            // Top 独占交互时清理旧浮层；不能只依赖玩法阻断事件，因为窗口间切换可能一直处于阻断状态。
            if (ctrl.Layer() == UILayer.Top && ctrl.BlocksGame)
            {
                var popups = new List<BaseUICtrl>(UICtrlDict.Values);
                foreach (var popup in popups)
                    if (popup != null && popup.Layer() == UILayer.Popup && popup.IsTransientPopup) popup.HideForLayerBlocking();
            }
            NotifyBlockingStateChanged();
        }

        /// <summary>Top 阻断窗口可见期间拒绝普通浮层重新显示；不会改变窗口或玩法状态。</summary>
        internal static bool CanShowPopup()
        {
            foreach (var ctrl in UICtrlDict.Values)
                if (ctrl != null && ctrl.Layer() == UILayer.Top && ctrl.BlocksGame && ctrl.IsShow()) return false;
            return true;
        }

        internal static void NotifyHidden(BaseUICtrl ctrl)
        {
            if (ctrl == null)
            {
                return;
            }

            ShowOrder.RemoveAll(item => ReferenceEquals(item, ctrl));
            NotifyBlockingStateChanged();
        }

        public static GameObject LoadPrefab(string path)
        {
            return _prefabLoader(path);
        }

        private static T GetOrCreateUI<T>() where T : BaseUICtrl, new()
        {
            if (!UICtrlDict.TryGetValue(typeof(T).Name, out var ctrl))
            {
                ctrl = new T();
                UICtrlDict[typeof(T).Name] = ctrl;
            }

            return ctrl as T;
        }

        private static GameObject DefaultLoadPrefab(string path)
        {
            return ResourceLoader.LoadAsset<GameObject>(path);
        }

        private static void NotifyBlockingStateChanged()
        {
            var current = HasUIOpen;
            if (current == lastBlockingUIOpen) return;
            lastBlockingUIOpen = current;
            BlockingUIChanged?.Invoke(current);
        }
    }
}
