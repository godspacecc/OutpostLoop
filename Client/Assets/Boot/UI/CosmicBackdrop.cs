using UnityEngine;
using UnityEngine.UI;

namespace Boot.UI
{
    /// <summary>开场共用的星海画面。只处理取景和缓慢推进，不驱动加载或玩法状态。</summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class CosmicBackdrop : MonoBehaviour
    {
        // 资源随宿主内置，Boot 在资源包初始化前也能显示。
        [SerializeField] private string sceneName = "Arrival";
        // 实际铺满画面的 UGUI 图像。
        private RawImage surface;
        // 用不受暂停影响的时间驱动轻微推进。
        private float enteredAt;

        /// <summary>创建铺满父节点的背景，底图保持比例，额外边缘用于微量镜头运动。</summary>
        public static CosmicBackdrop Create(Transform parent, string scene)
        {
            var root = new GameObject("Cosmic" + scene, typeof(RectTransform), typeof(RawImage));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = root.AddComponent<CosmicBackdrop>();
            view.sceneName = scene;
            view.Load();
            return view;
        }

        /// <summary>每次进入本阶段重新开始镜头运动。</summary>
        private void OnEnable() { enteredAt = Time.unscaledTime; Load(); }

        /// <summary>直接读取内置图像；不需要联网，不依赖业务程序集。</summary>
        private void Load()
        {
            surface = GetComponent<RawImage>();
            surface.texture = Resources.Load<Texture2D>("CosmicOpening/" + sceneName);
            surface.color = surface.texture ? Color.white : new Color(.025f, .04f, .06f);
            surface.raycastTarget = false;
        }

        /// <summary>按窗口比例居中裁切，始终保持图像比例与缓慢运动。</summary>
        private void Update()
        {
            if (!surface || !surface.texture) return;
            var size = surface.rectTransform.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            float aspect = size.x / size.y;
            float imageAspect = (float)surface.texture.width / surface.texture.height;
            float t = Time.unscaledTime - enteredAt;
            float advance = sceneName == "Crossing" ? Mathf.Clamp01(t / 2f) :
                sceneName == "Arrival" ? Mathf.Clamp01(t / 12f) : .5f + .5f * Mathf.Sin(t * .12f);
            float zoom = Mathf.Lerp(.99f, sceneName == "Crossing" ? .90f : .97f, advance);
            float width = Mathf.Min(1, aspect / imageAspect) * zoom;
            float height = Mathf.Min(1, imageAspect / aspect) * zoom;
            surface.uvRect = new Rect((1-width)*.5f, (1-height)*.5f, width, height);
        }
    }
}
