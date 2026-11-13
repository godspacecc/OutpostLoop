using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OutpostLoop.RiverValley
{
    /// <summary>独立的河谷镇外景评审入口。坐标单位为米，图纸上方对应北方。</summary>
    public sealed partial class RiverValleyTownPreview : MonoBehaviour
    {
        [SerializeField] private TextAsset layoutAsset;
        [SerializeField] private Shader surfaceShader;
        // 游戏接入时只生成外景，不创建第二套角色、镜头和碰撞。
        private bool gameplayScenery;
        private Rect? reservedArea;

        private const float HalfExtent = 1000;
        private readonly List<Edge> water = new List<Edge>();
        private readonly List<Edge> roads = new List<Edge>();
        private readonly List<Edge> bridgeDecks = new List<Edge>();
        private readonly List<Footprint> footprints = new List<Footprint>();
        private RiverValleyMesh scenery;
        private Material surface;
        private Layout layout;
        private CharacterController walker;
        private Camera view;
        private float verticalSpeed;
        private bool mapView;
        private float viewDistance = 39;
        private GUIStyle hud;
        private GUIStyle mapLabel;
        private Font hudFont;
        private readonly List<Rect> labels = new List<Rect>();

        [Serializable]
        private sealed class Layout
        {
            public Building[] buildings;
            public Route[] routes;
            public Route[] lots;
            public Landmark[] landmarks;
            public Site[] sites;
        }

        [Serializable]
        private sealed class Building
        {
            public float x, y, width, depth, angle;
            public string style;
        }

        [Serializable]
        private sealed class Route
        {
            public float width;
            public string kind;
            public Vector2[] points;
        }

        [Serializable]
        private sealed class Landmark
        {
            public string name;
            public float x, y;
        }

        [Serializable]
        private sealed class Site
        {
            public float x, y, width, depth;
            public string color;
        }

        private readonly struct Edge
        {
            internal readonly Vector2 A, B;
            internal readonly float Width;
            internal Edge(Vector2 a, Vector2 b, float width)
            {
                A = a;
                B = b;
                Width = width;
            }

            internal Vector2 Nearest(Vector2 point)
            {
                Vector2 axis = B - A;
                float t = Mathf.Clamp01(Vector2.Dot(point - A, axis) / Mathf.Max(.001f, axis.sqrMagnitude));
                return A + t * axis;
            }

            internal float Distance(Vector2 point) => (point - Nearest(point)).magnitude;
        }

        private readonly struct Footprint
        {
            internal readonly Vector2 Center;
            internal readonly float Radius;
            internal Footprint(Vector2 center, float radius)
            {
                Center = center;
                Radius = radius;
            }
        }

        /// <summary>供试玩验证读取角色位置，实际步行仍走 CharacterController。</summary>
        public Vector3 WalkerPosition => walker.transform.position;

        private void Awake()
        {
            layout = JsonUtility.FromJson<Layout>(layoutAsset.text);
            surface = new Material(surfaceShader) { name = "河谷镇共用表面" };
            IndexRoutes();
            BuildTerrain();
            scenery = new RiverValleyMesh(Section("街区与设施"), surface, reservedArea);
            BuildRoutes();
            BuildBuildings();
            BuildSites();
            BuildLandscape();
            scenery.Finish();
            if (!gameplayScenery) { CreateWalker(); SetCamera(); }
        }

        private void Update()
        {
            if (gameplayScenery) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.mKey.wasPressedThisFrame) mapView = !mapView;
            if (mapView) return;
            if (Mouse.current != null)
                viewDistance = Mathf.Clamp(viewDistance - Mouse.current.scroll.ReadValue().y * .035f, 29, 62);
            int x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            int z = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            var direction = new Vector2(x, z);
            float speed = keyboard.shiftKey.isPressed ? 5.4f : 1.8f;
            Step(direction, speed, Time.deltaTime);
        }

        private void LateUpdate() => SetCamera();

        /// <summary>实际预览的移动入口，沿地面行走，过河走桥。</summary>
        public void Step(Vector2 direction, float speed, float seconds)
        {
            Vector2 motion = Vector2.ClampMagnitude(direction, 1) * speed * seconds;
            Vector2 current = Flat(walker.transform.position);
            Vector2 next = current + motion;
            if (Mathf.Abs(next.x) > HalfExtent - .8f || Mathf.Abs(next.y) > HalfExtent - .8f ||
                (WaterMargin(next) < 1.4f && !IsOnBridge(next)))
                motion = Vector2.zero;
            verticalSpeed = walker.isGrounded ? -3 : Mathf.Max(-25, verticalSpeed - 20 * seconds);
            walker.Move(new Vector3(motion.x, verticalSpeed * seconds, motion.y));
        }

        private void OnGUI()
        {
            if (walker == null) return;
            if (hud == null)
            {
                hudFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "PingFang SC" }, 16);
                hud = new GUIStyle(GUI.skin.box) { font = hudFont, fontSize = 15, alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(13, 13, 4, 4), normal = { textColor = Color.white } };
            }
            string area = "";
            float nearest = float.PositiveInfinity;
            foreach (var landmark in layout.landmarks)
            {
                float distance = Vector2.Distance(Flat(walker.transform.position), Map(landmark.x, landmark.y));
                if (distance >= nearest) continue;
                nearest = distance;
                area = landmark.name;
            }
            GUI.Box(new Rect(14, 14, 345, 33), $"河谷镇  /  {area}附近", hud);
            GUI.Box(new Rect(14, Screen.height - 47, 440, 33), "WASD 移动    Shift 跑步    滚轮 缩放    M 总览", hud);
            if (mapLabel == null)
                mapLabel = new GUIStyle(hud) { alignment = TextAnchor.MiddleCenter, fontSize = 12,
                    padding = new RectOffset(2, 2, 2, 2) };
            if (mapView)
            {
                labels.Clear();
                foreach (var landmark in layout.landmarks)
                {
                    var pin = view.WorldToScreenPoint(At(Map(landmark.x,landmark.y),0));
                    var label = new Rect(pin.x - 41,Screen.height-pin.y-10,82,20);
                    GUI.Label(new Rect(pin.x-5,Screen.height-pin.y-7,11,14),"•",mapLabel);
                    for (int shift = 0; shift < 5; shift++)
                    {
                        if (!labels.Exists(other => other.Overlaps(label)))
                        {
                            labels.Add(label);
                            GUI.Box(label,landmark.name,mapLabel);
                            break;
                        }
                        label.y += 21;
                    }
                }
                var self = view.WorldToScreenPoint(walker.transform.position);
                GUI.Box(new Rect(self.x-7,Screen.height-self.y-7,14,14),"●",mapLabel);
            }
            else
            {
                // 建筑挡住角色时标出当前位置，街巷评审仍能跟上移动。
                var head = walker.transform.position + Vector3.up * 1.55f;
                Vector3 ray = head - view.transform.position;
                if (Physics.Raycast(view.transform.position,ray.normalized,out var hit,ray.magnitude) &&
                    hit.collider != walker)
                {
                    var pin = view.WorldToScreenPoint(head);
                    GUI.Box(new Rect(pin.x-8,Screen.height-pin.y-8,16,16),"●",mapLabel);
                }
            }
        }

        private void CreateWalker()
        {
            view = new GameObject("河谷镇斜俯视镜头").AddComponent<Camera>();
            view.transform.SetParent(transform, false);
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Tint("#84948b");
            view.fieldOfView = 42;
            view.nearClipPlane = .2f;
            view.farClipPlane = 2700;
            view.allowHDR = false;

            var player = new GameObject("外景步行角色");
            player.transform.SetParent(transform, false);
            Vector2 spawn = Map(1890, 1885);
            player.transform.position = At(spawn, NaturalHeight(spawn) + .18f);
            walker = player.AddComponent<CharacterController>();
            walker.height = 1.76f;
            walker.radius = .28f;
            walker.center = Vector3.up * .88f;
            walker.stepOffset = .37f;
            walker.minMoveDistance = 0;
            Part(PrimitiveType.Capsule, new Vector3(0, .81f, 0), new Vector3(.53f, .56f, .42f), "#354347");
            Part(PrimitiveType.Sphere, new Vector3(0, 1.48f, 0), Vector3.one * .29f, "#c3a28b");
            Part(PrimitiveType.Cube, new Vector3(0, 1.05f, -.24f), new Vector3(.41f, .43f, .19f), "#a78658");

            void Part(PrimitiveType type, Vector3 position, Vector3 scale, string color)
            {
                var part = GameObject.CreatePrimitive(type);
                part.transform.SetParent(player.transform, false);
                part.transform.localPosition = position;
                part.transform.localScale = scale;
                part.GetComponent<Collider>().enabled = false;
                var material = new Material(Shader.Find("Standard")) { color = Tint(color) };
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
                playerMaterials.Add(material);
            }
        }

        private readonly List<Material> playerMaterials = new List<Material>();

        private void SetCamera()
        {
            if (view == null) return;
            view.orthographic = mapView;
            if (mapView)
            {
                view.orthographicSize = Mathf.Max(1070, 1070 / view.aspect);
                view.transform.SetPositionAndRotation(Vector3.up * 1400, Quaternion.Euler(90, 0, 0));
            }
            else
            {
                var rotation = Quaternion.Euler(43, 0, 0);
                var focus = walker.transform.position + new Vector3(0, 1.2f, 3f);
                view.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * viewDistance, rotation);
            }
        }

        private void OnDestroy()
        {
            foreach (var material in playerMaterials) Destroy(material);
            if (hudFont != null) Destroy(hudFont);
            if (surface != null) Destroy(surface);
        }

        private Transform Section(string title)
        {
            var section = new GameObject(title);
            section.transform.SetParent(transform, false);
            return section.transform;
        }

        private static Vector2 Map(float x, float y) => new Vector2(x - 2000, 2000 - y);
        private static Vector2 Map(Vector2 point) => Map(point.x, point.y);
        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        private static Vector3 At(Vector2 p, float height) => new Vector3(p.x, height, p.y);
        private static Color32 Tint(string hex) => ColorUtility.TryParseHtmlString(hex, out var value) ? value : Color.magenta;

        /// <summary>复用河谷镇原始布局，给生存街区留出模型区域；现有领域坐标和存档保持不变。</summary>
        internal static RiverValleyTownPreview CreateScenery(Transform parent, TextAsset data, Shader shader, Rect district)
        {
            var root = new GameObject("河谷镇周边外景");
            root.SetActive(false);
            var preview = root.AddComponent<RiverValleyTownPreview>();
            var anchor = Map(1890, 1885);
            preview.gameplayScenery = true;
            preview.layoutAsset = data;
            preview.surfaceShader = shader;
            preview.reservedArea = new Rect(district.position + anchor, district.size);
            root.SetActive(true);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(-anchor.x, -NaturalHeight(anchor), -anchor.y);
            return preview;
        }
    }
}
