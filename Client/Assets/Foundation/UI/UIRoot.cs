using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game
{
    /// <summary>读取场景中固定的 UI 层与输入组件；负责层级入口，不裁决窗口业务或玩法输入。</summary>
    public class UIRoot : MonoBehaviour
    {
        private static readonly Dictionary<UILayer, string> LayerNameDict = new Dictionary<UILayer, string>
        {
            [UILayer.HUD] = "HUD",
            [UILayer.Dialog] = "Dialog",
            [UILayer.Popup] = "Popup",
            [UILayer.Tips] = "Tips",
            [UILayer.Top] = "Top",
        };

        private static UIRoot _instance;
        private readonly Dictionary<UILayer, RectTransform> _layerMap = new Dictionary<UILayer, RectTransform>();
        private bool _initialized;

        public static UIRoot Instance => EnsureInstance();

        public static UIRoot EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = Object.FindObjectOfType<UIRoot>();
            if (existing != null)
            {
                _instance = existing;
                _instance.Initialize();
                return _instance;
            }

            Log.Error("[UIRoot] 缺少必需场景对象：UIRoot。");
            return null;
        }

        public static void ClearForTests()
        {
            _instance = null;
        }

        public Transform GetLayerObj(UILayer layer)
        {
            Initialize();
            return _layerMap.TryGetValue(layer, out var layerTransform) ? layerTransform : null;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }

            _instance = this;
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            ValidateComponent<Canvas>("Canvas");
            ConfigureCanvasScaler();
            ValidateComponent<GraphicRaycaster>("GraphicRaycaster");
            EnsureEventSystem();
            LoadFixedLayers();

            _initialized = true;
        }

        private void ConfigureCanvasScaler()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error("[UIRoot] 缺少必需组件：UIRoot/CanvasScaler。");
                return;
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        private void ValidateComponent<T>(string componentName) where T : Component
        {
            if (GetComponent<T>() == null)
            {
                Log.Error($"[UIRoot] 缺少必需组件：UIRoot/{componentName}。");
            }
        }

        private void LoadFixedLayers()
        {
            _layerMap.Clear();
            foreach (var pair in LayerNameDict)
            {
                var layerTransform = transform.Find(pair.Value);
                if (layerTransform == null)
                {
                    Log.Error($"[UIRoot] 缺少必需场景对象：UIRoot/{pair.Value}。");
                    continue;
                }

                var rect = layerTransform.GetComponent<RectTransform>();
                if (rect == null)
                {
                    Log.Error($"[UIRoot] 缺少必需组件：UIRoot/{pair.Value}/RectTransform。");
                    continue;
                }

                _layerMap[pair.Key] = rect;
            }
        }

        private void EnsureEventSystem()
        {
            var eventSystem = GetComponentInChildren<EventSystem>(true);
            if (eventSystem == null)
            {
                Log.Error("[UIRoot] 缺少必需场景对象：UIRoot/EventSystem。");
                return;
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                Log.Error("[UIRoot] 缺少必需组件：UIRoot/EventSystem/InputSystemUIInputModule。");
            }
        }
    }
}
