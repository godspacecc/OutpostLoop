using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using QFramework;
using BattleTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OutpostLoop
{
    /// <summary>临时玩法界面从当前会话推导的显示状态。</summary>
    internal enum PrototypeOverlay { None, Start, Paused, Upgrade, Result, Research, ReturnConfirm }

    /// <summary>独立原型表现入口：向玩法系统提交输入、同步临时画面。无旧项目资源。</summary>
    public sealed partial class PrototypeController : MonoBehaviour
    {
        // 本局领域状态、临时表现根和预分配敌人池。
        public WorldState World { get; private set; }
        private Transform worldRoot, commander, headquarters;
        private Camera gameCamera;
        // 透视镜头绕地面焦点转动；拉到最远时重新收拢到基地中心以框住全图。
        private const float CameraPitch = 55f;
        private const float CameraFieldOfView = 40f;
        private const float DefaultMapView = .23f;
        private const float ClosestMapView = .16f;
        private const float ZoomFactorPerNotch = 1.2f;
        private const float ScrollUnitsPerNotch = 120f;
        private const float CameraPanSpeed = 24f;
        private const float CameraTurnPerPixel = .25f;
        private Vector2 cameraFocus;
        private float cameraYaw;
        private float mapView = DefaultMapView;
        private bool orbitingCamera;
        // 单指抬手才提交点击；一旦检测到双指，直到所有触点结束都不再派发点击。
        private readonly List<RaycastResult> touchRaycasts = new List<RaycastResult>();
        private int tapTouchId, pinchFirstId, pinchSecondId;
        // 触点抬起前若切换选址状态，放弃本次点击。
        private bool tapBeganWithPlacement;
        private float pinchDistance;
        private bool suppressBattleTap, ignoreTouchUntilRelease;
        // 指挥视图和小地图共用同一套屏幕命中区域。
        private Rect StatusRect => new Rect(16, 16, 330, World?.Expedition != null ? 194 : 165);
        private static readonly Rect MapRect = new Rect(1050, 18, 210, 235);
        private static readonly Vector2 MapCenter = new Vector2(1155, 140);
        private static float HudScale => Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        // 临时选址标记和敌种材质仅属于视图。
        private Transform placementGhost, bindingMark, buildRange;
        private readonly Renderer[] placementTiles = new Renderer[9];
        private Material validPlacement, invalidPlacement;
        private Material[] enemyMaterials;
        private Material visibleOre, rememberedOre;
        private BattleFogPreview fog;
        private readonly List<Transform> boundaries = new List<Transform>();
        private readonly List<ExpansionPreview> expansionModels = new List<ExpansionPreview>();
        private readonly List<Renderer> resources = new List<Renderer>();
        private readonly List<Transform> enemies = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private bool started, shop;
        internal int Wins => session.Wins;
        internal int StartingFunds => session.StartingFunds;
        internal bool ConfirmingRestart { get; private set; }
        private int selectedSquad = -1;
        internal SitePersonnel SelectedSquad => selectedSquad < 0 || World.Buildings[selectedSquad].Kind != ExpansionKind.Barracks
            ? null : World.Buildings[selectedSquad].Personnel;
        internal bool VictoryRewardRecorded => session.VictoryRewardRecorded;
        // 面板选中状态与本局建筑所有权分离。
        internal bool ConstructionOpen { get; private set; }
        internal ExpansionKind PlacementKind { get; private set; }
        internal int SelectedBuilding { get; private set; } = -1;
        internal int MovingTurret { get; private set; } = -1;
        internal bool HasPlacement => PlacementKind != ExpansionKind.Empty;
        internal PlacementIssue? HoverIssue { get; private set; }
        internal int HoverResource { get; private set; } = -1;
        private bool rotatedPlacement;
        // 避免同一次 Escape 在原型输入和基础热键中各触发一次。
        private int cancelPlacementFrame = -1;
        private int seed = 731;
        private Font font;
        private string feedback = "";
        private float feedbackUntil;
        // 主场景窗口随基础会话清理；临时截图工具仍可仅初始化世界。
        private PrototypeModalCtrl modal;
        private PrototypeHudCtrl hud;
        private PrototypeConstructionCtrl construction;
        // 权威规则由架构系统持有，视图只转发输入及读取快照。
        private PrototypeSystem session;
        private string startupError;
        // 显式场景依赖，确保资源包携带临时表现所需 Shader。
        [SerializeField] private Shader previewShader;
        // Main 显式引用已确认用于开始界面的前哨概念图，随场景资源加载。
        [SerializeField] private Texture2D startArtwork;
        internal Texture2D StartArtwork => startArtwork;
        // 仅领域与表现均初始化成功后，允许更新及通知 Boot 就绪。
        private bool initialized;
        private readonly Color groundColor = new Color(0.16f,0.25f,0.24f);

        private void Awake()
        {
            // 基础启动失败已交给 Boot 展示，玩法入口不能重新创建已回滚的架构。
            if (Game.FoundationRuntime.IsInitialized) Initialize();
        }
        /// <summary>Main 完成真实玩法初始化后才释放 Boot 启动界面。</summary>
        private void Start()
        {
            if (initialized)
            {
                try
                {
                    RefreshOverlay();
                    if (modal?.IsShow() == true)
                    {
                        Boot.BootHost.ReportReady();
                        return;
                    }
                    startupError = "临时玩法界面未能显示";
                }
                catch (System.Exception error)
                {
                    startupError = error.Message;
                    Game.Log.Error("临时玩法界面初始化失败：" + error);
                }
                initialized = false;
            }
            Boot.BootHost.ReportFailure(new Dictionary<string,string>{{"reason",startupError ?? "Prototype initialization failed"}});
        }
        // 失活时暂停本局，避免隐藏视图仍推进采运和防守。
        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            if (initialized) Game.LifeCycle.RegisterUpdate(PollInputAndPresent);
        }
        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            ignoreTouchUntilRelease = true;
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            modal?.Hide();
            hud?.Hide();
            CloseConstruction();
            session?.SetPaused(true);
        }

        /// <summary>创建全新临时几何体预览；可供编辑器截图入口调用。</summary>
        public void Initialize()
        {
            if(worldRoot != null)return;
            var architecture=Game.Game.Interface;
            if(architecture.GetModel<PrototypeModel>()==null)architecture.RegisterModel(new PrototypeModel());
            if(architecture.GetUtility<PrototypeWallet>()==null)architecture.RegisterUtility(new PrototypeWallet());
            if (architecture.GetUtility<ExpeditionProfile>() == null) architecture.RegisterUtility(new ExpeditionProfile());
            if(architecture.GetSystem<PrototypeSystem>()==null)architecture.RegisterSystem(new PrototypeSystem());
            session=architecture.GetSystem<PrototypeSystem>();
            architecture.GetUtility<Game.HotKeyActionRegistry>().Register(new Game.HotKeyAction
            {
                ActionId = Game.HotKeyActionIds.TogglePause,
                Context = Game.HotKeyContext.Gameplay,
                Priority = 10,
                CanExecute = () => CanTogglePause() && cancelPlacementFrame != Time.frameCount,
                Execute = () => { if (HasPlacement) CancelPlacementFromInput(); else TogglePause(); }
            });


            architecture.GetUtility<Game.HotKeyActionRegistry>().Register(new Game.HotKeyAction
            {
                ActionId = Game.HotKeyActionIds.ToggleRuntimeLog,
                Context = Game.HotKeyContext.Global,
                Priority = 1,
                CanExecute = () => this != null && isActiveAndEnabled && initialized && Game.UIHelper.GetUICtrl<PrototypeSettingsCtrl>()?.IsShow() != true,
                Execute = () =>
                {
                    var log = Game.UIHelper.GetUICtrl<PrototypeLogCtrl>();
                    if (log?.IsShow() == true) log.Hide();
                    else Game.UIHelper.ShowUI<PrototypeLogCtrl>();
                }
            });

            font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",18);
            var cameraObject=new GameObject("PreviewCamera"); cameraObject.transform.SetParent(transform);
            cameraObject.AddComponent<AudioListener>();
            gameCamera=cameraObject.AddComponent<Camera>();
            gameCamera.fieldOfView=CameraFieldOfView;
            gameCamera.backgroundColor=groundColor; gameCamera.clearFlags=CameraClearFlags.SolidColor;
            gameCamera.nearClipPlane=0.3f;
            var lightObject=new GameObject("PreviewSun"); lightObject.transform.SetParent(transform);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f;
            light.transform.rotation=Quaternion.Euler(50,-35,0);
            RenderSettings.ambientLight=new Color(0.58f,0.63f,0.65f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            try
            {
                Restart(false);
                if(isActiveAndEnabled)Game.LifeCycle.RegisterUpdate(PollInputAndPresent);
            }
            catch(System.Exception e)
            {
                initialized=false;startupError=e.Message;
                Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
                session.SetPaused(true);
                Game.Log.Error("独立原型启动失败："+e);
            }
        }

        /// <summary>重开替换完整领域状态和表现，不保留上一局能力与敌人。</summary>
        public void Restart(bool begin) => ShowWorld(session.Begin(++seed, begin), begin);

        private void ShowWorld(WorldState world, bool begin)
        {
            initialized=false;
            if(worldRoot!=null){worldRoot.gameObject.SetActive(false);Release(worldRoot.gameObject);}
            foreach(var material in materials)Release(material);
            materials.Clear();resources.Clear();enemies.Clear();boundaries.Clear();
            ReleaseFog();
            expansionModels.Clear();
            CloseConstruction();
            ConfirmingRestart = false;
            selectedSquad = -1;
            World=world; started=begin;shop=false;startupError=null;
            researchOpen = returnConfirm = false;
            ignoreTouchUntilRelease = true;
            ResetBattleCamera();
            UpdateBattleCamera();
            worldRoot=new GameObject("TemporaryPreviewWorld").transform;worldRoot.SetParent(transform);
            var ground=Mat(World.Expedition?.Map == ExpeditionMap.Industrial ? new Color(.29f, .28f, .24f)
                : World.Expedition?.Map == ExpeditionMap.Beacon ? new Color(.24f, .28f, .31f) : groundColor);
            var stone=Mat(new Color(0.37f,0.40f,0.40f));
            var dark=Mat(new Color(0.11f,0.16f,0.17f));var amber=Mat(new Color(1,0.61f,0.13f));
            var cyan=Mat(new Color(0.15f,0.88f,0.84f));var rust=Mat(new Color(0.52f,0.24f,0.17f));
            Shape("Ground",worldRoot,PrimitiveType.Cube,new Vector3(0,-0.3f,0),new Vector3(96,0.5f,96),ground);
            fog = new BattleFogPreview(worldRoot, World.Vision);
            visibleOre = World.Expedition == null ? cyan : Mat(new Color(.46f, .62f, .30f));
            rememberedOre = Mat(new Color(.34f, .45f, .43f));
            headquarters = new GameObject("Headquarters").transform;
            headquarters.SetParent(worldRoot, false);
            Shape("HomePad", headquarters, PrimitiveType.Cube, new Vector3(0, -.02f, 0), new Vector3(5.9f, .12f, 5.9f), dark);
            Shape("Shelter", headquarters, PrimitiveType.Cube, new Vector3(0, 1.15f, 0), new Vector3(3.6f, 2.3f, 3), stone);
            Shape("Roof", headquarters, PrimitiveType.Cube, new Vector3(0, 2.5f, 0), new Vector3(4, .5f, 3.5f), dark);
            Shape("Door", headquarters, PrimitiveType.Cube, new Vector3(0, .8f, -1.55f), new Vector3(.9f, 1.6f, .1f), amber);
            commander = Shape("Commander", worldRoot, PrimitiveType.Capsule, Vector3.zero, new Vector3(1.1f, .9f, 1.1f), amber);
            var blue = Mat(new Color(.32f, .58f, 1));
            CreateExpeditionMarkers(blue, amber, stone, rust);
            CreateExpansionModels(stone, cyan, blue, rust, amber);
            validPlacement = Mat(new Color(.23f, .91f, .35f));
            invalidPlacement = Mat(new Color(.94f, .26f, .20f));
            placementGhost = new GameObject("PlacementFootprint").transform;
            placementGhost.SetParent(worldRoot, false);
            for (int i = 0; i < placementTiles.Length; i++)
                placementTiles[i] = Shape("GridCell", placementGhost, PrimitiveType.Cube, Vector3.zero,
                    new Vector3(1.88f, .13f, 1.88f), validPlacement).GetComponent<Renderer>();
            bindingMark = Shape("PlacementResource", worldRoot, PrimitiveType.Cylinder, Vector3.zero, new Vector3(3, .06f, 3), cyan);
            buildRange = CreateBuildRange(cyan);
            CancelPlacement();
            enemyMaterials = new[] { rust, amber, Mat(new Color(.62f, .17f, .31f)) };
            foreach(var node in World.Nodes)
            {
                var item = Shape("Mineral", worldRoot, World.Expedition == null ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                    At(node.Position, .6f), new Vector3(1.6f, .6f, 1.6f), visibleOre);
                resources.Add(item.GetComponent<Renderer>());
            }
            for(int i=0;i<(World.Expedition?.Rules.EnemyCap ?? World.Run.Config.MaxEnemies);i++)
            {
                var enemy=Shape("PooledEnemy",worldRoot,PrimitiveType.Capsule,Vector3.zero,new Vector3(0.65f,0.8f,0.65f),rust);
                enemy.gameObject.SetActive(false);enemies.Add(enemy);
            }
            // 树只作为边缘装饰，不制造看不见的阻挡。
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16;var pos=new Vector3(Mathf.Cos(angle)*48,1.5f,Mathf.Sin(angle)*48);
                boundaries.Add(Shape("Boundary",worldRoot,PrimitiveType.Cube,pos,new Vector3(2,3,2),stone));
            }
            feedback="";SyncView();initialized=true;
        }

        /// <summary>每座建筑复用按用途区分的临时几何体。</summary>
        private void CreateExpansionModels(Material pad, Material extractor, Material barracks, Material barricade, Material worksite)
        {
            for (int i = 0; i < World.Buildings.Count; i++)
            {
                var root = new GameObject("Building" + i).transform;
                root.SetParent(worldRoot, false);
                var foundation = Shape("Worksite", root, PrimitiveType.Cube, new Vector3(0, .24f, 0), Vector3.one, worksite);
                bool exploring = World.Expedition != null;
                var station = Shape("Extractor", root, exploring ? PrimitiveType.Cube : PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0),
                    exploring ? new Vector3(3.2f, 1.9f, 2.8f) : new Vector3(2.25f, 1, 2.25f), exploring ? barricade : extractor);
                var quarters = Shape("Barracks", root, PrimitiveType.Cube, new Vector3(0, 1.15f, 0), new Vector3(2.8f, 2.2f, 1.8f), barracks);
                var wall = Shape("Barricade", root, PrimitiveType.Cube, new Vector3(0, .95f, 0), Vector3.one, barricade);
                var turret = new GameObject("Turret").transform;
                turret.SetParent(root, false);
                Shape("Mount", turret, PrimitiveType.Cylinder, new Vector3(0, .4f, 0), new Vector3(1.5f, .4f, 1.5f), pad);
                Shape("Head", turret, PrimitiveType.Cube, new Vector3(0, 1.15f, 0), new Vector3(.8f, .6f, .8f), exploring ? barricade : pad);
                Shape("Barrel", turret, PrimitiveType.Cube, new Vector3(0, 1.15f, .7f), new Vector3(.2f, .2f, 1), worksite);
                if (exploring)
                    Shape("Bow", turret, PrimitiveType.Cube, new Vector3(0, 1.15f, .65f), new Vector3(1.68f, .15f, .18f), barricade);
                var guard = new GameObject("Garrison").transform;
                guard.SetParent(root, false);
                for (int member = -1; member <= 1; member++)
                    Shape("Guard", guard, PrimitiveType.Capsule, new Vector3(member * .55f, 0, Mathf.Abs(member) * -.45f),
                        new Vector3(.55f, .7f, .55f), barracks);
                var gatherer = Shape("Gatherer", root, PrimitiveType.Capsule, Vector3.zero, new Vector3(.85f, .7f, .85f), worksite);
                var shot = root.gameObject.AddComponent<LineRenderer>();
                shot.sharedMaterial = worksite;
                shot.positionCount = 2;
                shot.startWidth = .13f;
                shot.endWidth = .05f;
                var shelter = new GameObject("FieldShelter").transform;
                shelter.SetParent(root, false);
                Shape("Cabin", shelter, PrimitiveType.Cube, new Vector3(0, .9f, 0), new Vector3(3.4f, 1.8f, 3.4f), barricade);
                Shape("Roof", shelter, PrimitiveType.Cube, new Vector3(0, 1.92f, 0), new Vector3(3.8f, .24f, 3.8f), worksite);
                Shape("Door", shelter, PrimitiveType.Cube, new Vector3(0, .67f, -1.71f), new Vector3(.85f, 1.34f, .05f), worksite);
                expansionModels.Add(new ExpansionPreview(root, foundation, station, quarters, wall, turret, guard, gatherer, shelter, shot));
            }
        }

        /// <summary>选址时显示中央基地周围的施工范围。</summary>
        private Transform CreateBuildRange(Material material)
        {
            var ring = new GameObject("BuildRange").transform;
            ring.SetParent(worldRoot, false);
            var line = ring.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = 96;
            line.startWidth = line.endWidth = .09f;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, At(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * BattleGrid.BuildRadius, .04f));
            }
            return ring;
        }

        private sealed class ExpansionPreview
        {
            private readonly Transform root, worksite, extractor, barracks, barricade, turret, garrison, gatherer, shelter;
            private readonly LineRenderer shot;

            internal ExpansionPreview(Transform root, Transform worksite, Transform extractor, Transform barracks,
                Transform barricade, Transform turret, Transform garrison, Transform gatherer, Transform shelter, LineRenderer shot)
            {
                this.root = root;
                this.worksite = worksite;
                this.extractor = extractor;
                this.barracks = barracks;
                this.barricade = barricade;
                this.turret = turret;
                this.garrison = garrison;
                this.gatherer = gatherer;
                this.shelter = shelter;
                this.shot = shot;
            }

            internal void Show(ExpansionSite site, float time)
            {
                root.gameObject.SetActive(site.Kind != ExpansionKind.Empty);
                if (site.Kind == ExpansionKind.Empty) return;
                root.localPosition = At(site.Position, 0);
                bool building = site.IsUnderConstruction;
                var width = (Vector2)site.Footprint.size * BattleGrid.CellSize;
                worksite.localScale = new Vector3(width.x - .18f, .24f, width.y - .18f);
                barricade.localScale = new Vector3(width.x - .2f, 1.8f, width.y - .2f);
                worksite.gameObject.SetActive(building);
                extractor.gameObject.SetActive(!building && site.Kind == ExpansionKind.Extractor);
                barracks.gameObject.SetActive(!building && site.Kind == ExpansionKind.Barracks);
                barricade.gameObject.SetActive(!building && site.Kind == ExpansionKind.Barricade);
                shelter.gameObject.SetActive(!building && site.Kind == ExpansionKind.Shelter);
                bool isTurret = !building && site.Kind == ExpansionKind.Turret;
                turret.gameObject.SetActive(isTurret);
                bool hasPersonnel = site.Personnel != null;
                garrison.gameObject.SetActive(hasPersonnel && site.Kind == ExpansionKind.Barracks);
                gatherer.gameObject.SetActive(hasPersonnel && site.Kind == ExpansionKind.Extractor);
                if (hasPersonnel)
                {
                    if (site.Kind == ExpansionKind.Barracks) garrison.position = At(site.Personnel.Position, .7f);
                    else gatherer.position = At(site.Personnel.Position, .7f);
                }
                shot.enabled = time - site.LastShotTime < .12f;
                if (!shot.enabled) return;
                var direction = site.LastShotTarget - site.Position;
                if (isTurret && direction.sqrMagnitude > 0) turret.localRotation = Quaternion.LookRotation(At(direction, 0));
                shot.SetPosition(0, isTurret ? turret.position + Vector3.up * 1.15f + turret.forward : garrison.position);
                shot.SetPosition(1, At(site.LastShotTarget, .8f));
            }
        }

        /// <summary>Player 只使用序列化依赖；编辑器动态预览可使用内置 Shader。</summary>
        private Material Mat(Color color)
        {
#if UNITY_EDITOR
            if(previewShader==null)previewShader=Shader.Find("Standard");
#endif
            if(previewShader==null)throw new System.InvalidOperationException("Main 缺少临时表现 Shader 引用。");
            var m=new Material(previewShader);m.color=color;materials.Add(m);return m;
        }
        private Transform Shape(string label,Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=label;obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;
            Release(obj.GetComponent<Collider>());return obj.transform;
        }
        private void Release(Object obj){if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}

        private void PollInputAndPresent()
        {
            if(!initialized)return;
            if (UtilityWindowOpen)
            {
                if (!World.Run.Paused) World.HoldInteraction(false);
                PollBattleTouch(false, out _);
                orbitingCamera = false;
                CloseConstruction();
                return;
            }
            if(Input.GetKeyDown(KeyCode.B)) ToggleUpgrade();
            if (Input.GetKeyDown(KeyCode.T)) ToggleResearch();
            if (Input.GetKeyDown(KeyCode.V)) ToggleConstruction();
            bool wasPlacing = HasPlacement;
            if (wasPlacing)
            {
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) CancelPlacementFromInput();
                else if (PlacementKind == ExpansionKind.Barricade && Input.GetKeyDown(KeyCode.R)) rotatedPlacement = !rotatedPlacement;
            }
            bool overUI = EventSystem.current?.IsPointerOverGameObject() == true;
            bool playing = Overlay == PrototypeOverlay.None;
            if (World.Expedition != null && !World.Run.Paused)
            {
                if (playing && Input.GetKeyDown(KeyCode.E)) World.PressInteraction();
                World.HoldInteraction(playing && Input.GetKey(KeyCode.E));
            }
            var mousePosition = (Vector2)Input.mousePosition;
            bool touching = PollBattleTouch(playing, out var tap);
            if (playing) PollBattleCamera(overUI, touching);
            else orbitingCamera = false;
            UpdateBattleCamera();
            if (tap.HasValue)
            {
                if (TouchScoutingAvailable && !HasPlacement) MoveCommanderAt(tap.Value);
                else ActOnBattlefield(tap.Value);
            }
            else if (playing && !touching && !overUI && !orbitingCamera)
            {
                if (Input.GetMouseButtonDown(0)) ActOnBattlefield(mousePosition);
                else if (!wasPlacing && Input.GetMouseButtonDown(1)) MoveCommanderAt(mousePosition);
            }
            SyncView();
            UpdatePlacementPreview(mousePosition, overUI || touching && suppressBattleTap);
            RefreshOverlay();
        }

        /// <summary>读取本帧触点，双指手势优先于单指战场点击。</summary>
        private bool PollBattleTouch(bool playing, out Vector2? tap)
        {
            tap = null;
            var touches = BattleTouch.activeTouches;
            if (touches.Count == 0)
            {
                tapTouchId = pinchFirstId = pinchSecondId = 0;
                suppressBattleTap = ignoreTouchUntilRelease = false;
                return false;
            }

            ignoreTouchUntilRelease |= !playing;
            if (ignoreTouchUntilRelease) return true;

            if (touches.Count > 1)
            {
                suppressBattleTap = true;
                tapTouchId = 0;
                if (touches.Count == 2 && touches[0].inProgress && touches[1].inProgress)
                    ApplyBattlePinch(touches[0], touches[1]);
                else
                    pinchFirstId = pinchSecondId = 0;
                return true;
            }

            pinchFirstId = pinchSecondId = 0;
            if (suppressBattleTap) return true;
            var touch = touches[0];
            var position = touch.screenPosition;
            if (touch.phase == TouchPhase.Began && IsBattleTapPoint(position) && !TouchHitsUi(position))
            {
                tapTouchId = touch.touchId;
                tapBeganWithPlacement = HasPlacement;
            }
            if (touch.touchId != tapTouchId) return true;

            float tapSlop = 12f * HudScale;
            if ((position - touch.startScreenPosition).sqrMagnitude > tapSlop * tapSlop || touch.phase == TouchPhase.Canceled)
                tapTouchId = 0;
            else if (touch.phase == TouchPhase.Ended)
            {
                tapTouchId = 0;
                if ((!TouchScoutingAvailable || tapBeganWithPlacement == HasPlacement) &&
                    IsBattleTapPoint(position) && !TouchHitsUi(position))
                    tap = position;
            }
            return true;
        }

        /// <summary>两指都从战场开始才缩放，按相邻帧的间距比复用滚轮缩放上下限。</summary>
        private void ApplyBattlePinch(BattleTouch first, BattleTouch second)
        {
            float distance = Vector2.Distance(first.screenPosition, second.screenPosition);
            if (pinchFirstId != first.touchId || pinchSecondId != second.touchId)
            {
                pinchFirstId = first.touchId;
                pinchSecondId = second.touchId;
                var a = first.startScreenPosition;
                var b = second.startScreenPosition;
                pinchDistance = IsCameraPoint(a) && IsCameraPoint(b) && !TouchHitsUi(a) && !TouchHitsUi(b) ? distance : -1f;
                return;
            }
            if (pinchDistance < 0f) return;
            if (pinchDistance > 1f && distance > 1f)
                ScaleBattleCamera(pinchDistance / distance);
            pinchDistance = distance;
        }

        /// <summary>触点即时检测 UGUI，避免读到上一帧的指针悬停状态。</summary>
        private bool TouchHitsUi(Vector2 position)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, touchRaycasts);
            return touchRaycasts.Exists(hit => hit.module is GraphicRaycaster);
        }

        /// <summary>开局侦察点击避开状态栏和小地图，其他触屏点击沿用相机范围。</summary>
        private bool IsBattleTapPoint(Vector2 position) =>
            TouchScoutingAvailable ? IsCameraPoint(position) : gameCamera.pixelRect.Contains(position);

        /// <summary>缩放和旋转从主战场接收，避开小地图和状态栏。</summary>
        private bool IsCameraPoint(Vector2 position)
        {
            var hudPoint = new Vector2(position.x, Screen.height - position.y) / HudScale;
            return gameCamera.pixelRect.Contains(position) && !MapRect.Contains(hudPoint) && !StatusRect.Contains(hudPoint);
        }

        /// <summary>滚轮和触屏复用同一缩放范围。</summary>
        private void ScaleBattleCamera(float factor) => mapView = Mathf.Clamp(mapView * factor, ClosestMapView, 1f);

        /// <summary>键盘相对当前镜头平移；中键只从战场内开始旋转，松开立即结束。</summary>
        private void PollBattleCamera(bool overUI, bool touching)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            orbitingCamera = !touching && mouse?.middleButton.isPressed == true && orbitingCamera;
            if (keyboard?.hKey.wasPressedThisFrame == true)
            {
                ResetBattleCamera();
                return;
            }
            if (mouse != null && !touching)
            {
                var position = mouse.position.ReadValue();
                bool pointerOnBattlefield = !overUI && IsCameraPoint(position);
                orbitingCamera |= pointerOnBattlefield && mouse.middleButton.wasPressedThisFrame;
                if (orbitingCamera)
                    cameraYaw = Mathf.Repeat(cameraYaw + mouse.delta.x.ReadValue() * CameraTurnPerPixel / HudScale, 360f);
                // 按当前距离等比例缩放，让近景也能分多步拉近。
                float wheelNotches = mouse.scroll.y.ReadValue() / ScrollUnitsPerNotch;
                if (pointerOnBattlefield && wheelNotches != 0)
                    ScaleBattleCamera(Mathf.Pow(ZoomFactorPerNotch, -wheelNotches));
            }

            if (keyboard != null)
            {
                float x = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
                float z = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
                var movement = Quaternion.Euler(0, cameraYaw, 0) * Vector3.ClampMagnitude(new Vector3(x, 0, z), 1f);
                cameraFocus += new Vector2(movement.x, movement.z) * (CameraPanSpeed * Time.unscaledDeltaTime);
            }
            float panRadius = World.Run.Config.MapRadius * Mathf.InverseLerp(1f, DefaultMapView, mapView);
            cameraFocus = Vector2.ClampMagnitude(cameraFocus, panRadius);
        }

        /// <summary>开新局或按 H 时恢复基地中心、默认朝向和缩放。</summary>
        private void ResetBattleCamera()
        {
            cameraFocus = World.BasePosition;
            cameraYaw = 0;
            mapView = DefaultMapView;
            orbitingCamera = false;
        }

        /// <summary>以基地附近为默认取景，远景按当前朝向框住整个地面。</summary>
        private void UpdateBattleCamera()
        {
            float pitch = CameraPitch * Mathf.Deg2Rad;
            float tanHalfFov = Mathf.Tan(CameraFieldOfView * .5f * Mathf.Deg2Rad);
            float tanHalfHorizontalFov = tanHalfFov * gameCamera.aspect;
            float cosPitch = Mathf.Cos(pitch);
            float verticalFit = cosPitch + Mathf.Sin(pitch) / tanHalfFov;
            float horizontalFit = 1f / tanHalfHorizontalFov;
            horizontalFit = Mathf.Sqrt(horizontalFit * horizontalFit + cosPitch * cosPitch);
            float radius = World.Run.Config.MapRadius + 2f;
            float baseDistance = radius * Mathf.Max(verticalFit, horizontalFit);
            var rotation = Quaternion.Euler(CameraPitch, cameraYaw, 0);
            float distance = baseDistance * mapView;
            if (mapView > DefaultMapView)
            {
                var worldToView = Quaternion.Inverse(rotation);
                float fullDistance = baseDistance;
                float edge = radius + 2f;
                // 四角在相机空间的深度各不相同，逐角计算进入视锥所需的距离。
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = worldToView * new Vector3(x * edge, 0, z * edge);
                    float requiredDepth = Mathf.Max(Mathf.Abs(corner.x) / tanHalfHorizontalFov, Mathf.Abs(corner.y) / tanHalfFov);
                    fullDistance = Mathf.Max(fullDistance, requiredDepth - corner.z);
                }
                distance += (fullDistance - baseDistance) * Mathf.InverseLerp(DefaultMapView, 1f, mapView);
            }
            gameCamera.transform.SetPositionAndRotation(At(cameraFocus, 0) - rotation * Vector3.forward * distance, rotation);
            gameCamera.farClipPlane = distance + 2f * radius;
        }

        /// <summary>主基地落位前，触屏单击可复用指挥员移动入口。</summary>
        internal bool TouchScoutingAvailable => World != null && World.Expedition == null && !World.Headquarters.HasValue;

        /// <summary>基地尚未部署时，将屏幕上的地面位置转为指挥员移动指令。</summary>
        public bool MoveCommanderAt(Vector2 screenPoint)
        {
            return CanBuild && !HasPlacement && TryBattlefieldPoint(screenPoint, out var point) &&
                session.MoveCommander(point);
        }

        internal void DeploySquad(int sector)
        {
            if (CanBuild && SelectedSquad != null) session.DeploySquad(selectedSquad, sector);
        }

        /// <summary>设置和日志窗口打开时，暂停热键与局内按钮均不响应。</summary>
        private bool CanTogglePause() => this != null && isActiveAndEnabled && initialized && started &&
            World.Run.Outcome == 0 && !UtilityWindowOpen;

        private bool UtilityWindowOpen => Game.UIHelper.GetUICtrl<PrototypeSettingsCtrl>()?.IsShow() == true ||
            Game.UIHelper.GetUICtrl<PrototypeLogCtrl>()?.IsShow() == true;

        /// <summary>局内按钮只负责进入暂停，继续操作由暂停菜单处理。</summary>
        internal void PauseFromHud()
        {
            if (CanTogglePause() && Overlay == PrototypeOverlay.None) TogglePause();
        }

        /// <summary>触屏入口和暂停热键复用现有会话操作。</summary>
        private void TogglePause()
        {
            if (researchOpen) { ToggleResearch(); return; }
            if (returnConfirm) { CancelReturn(); return; }
            if (shop || World.Run.Paused) ActOnModal();
            else
            {
                CloseConstruction();
                session.SetPaused(true);
                RefreshOverlay();
            }
        }

        internal PrototypeOverlay Overlay => !started ? PrototypeOverlay.Start :
            World.Run.Outcome != 0 ? PrototypeOverlay.Result :
            returnConfirm ? PrototypeOverlay.ReturnConfirm : researchOpen ? PrototypeOverlay.Research :
            shop ? PrototypeOverlay.Upgrade : World.Run.Paused ? PrototypeOverlay.Paused : PrototypeOverlay.None;

        internal string ActionFeedback => Time.realtimeSinceStartup < feedbackUntil ? feedback : "";

        /// <summary>键盘与临时按钮共用同一个基地入口，购买权限仍由玩法系统判断。</summary>
        internal void ToggleUpgrade()
        {
            if (UtilityWindowOpen) return;
            if (!initialized || !World.Headquarters.HasValue || (Overlay != PrototypeOverlay.None && Overlay != PrototypeOverlay.Upgrade)) return;
            CloseConstruction();
            shop = !shop;
            feedback = "";
            session.SetPaused(shop);
            RefreshOverlay();
        }

        /// <summary>根据本局状态执行主按钮动作；窗口本身不改变规则数据。</summary>
        internal void ActOnModal()
        {
            if (UtilityWindowOpen) return;
            if (!initialized) return;
            switch (Overlay)
            {
                case PrototypeOverlay.Start:
                    if (!session.StartRun()) break;
                    started = true;
                    break;
                case PrototypeOverlay.Paused:
                case PrototypeOverlay.Upgrade:
                    ConfirmingRestart = false;
                    shop = false;
                    if (World.Expedition != null) World.HoldInteraction(Input.GetKey(KeyCode.E));
                    session.SetPaused(false);
                    break;
                case PrototypeOverlay.Research:
                    ToggleResearch();
                    break;
                case PrototypeOverlay.ReturnConfirm:
                    World.ReturnFromExpedition();
                    returnConfirm = false;
                    session.TryRecordVictory();
                    break;
                case PrototypeOverlay.Result:
                    if (World.Run.Outcome <= 0 || session.TryRecordVictory())
                        Restart(false);
                    break;
            }
            RefreshOverlay();
        }

        /// <summary>同一个重开按钮二次确认，退出暂停即取消确认。</summary>
        internal void RequestRestart()
        {
            if (UtilityWindowOpen || Overlay != PrototypeOverlay.Paused) return;
            if (World.Expedition?.BasicComplete == true) return;
            if (ConfirmingRestart)
            {
                if (World.Expedition != null) World.AbandonExpedition();
                else Restart(false);
            }
            else ConfirmingRestart = true;
            RefreshOverlay();
        }

        internal void PurchaseUpgrade(int route)
        {
            if (UtilityWindowOpen) return;
            if (!initialized || Overlay != PrototypeOverlay.Upgrade) return;
            feedback = session.Purchase(route) ? Game.Loc.T("PreviewText18") : Game.Loc.T("PreviewText19");
            feedbackUntil = Time.realtimeSinceStartup + 2;
            RefreshOverlay();
        }

        private bool CanBuild => initialized && Overlay == PrototypeOverlay.None && !UtilityWindowOpen;

        internal void ToggleConstruction()
        {
            if (!CanBuild) return;
            if (ConstructionOpen) CloseConstruction();
            else ConstructionOpen = true;
            RefreshOverlay();
        }

        /// <summary>侧栏选择只进入选址，地图左键才提交订单。</summary>
        internal void SelectPlacement(ExpansionKind kind)
        {
            if (!CanBuild || !ConstructionOpen) return;
            var spec = World.BuildingSpec(kind);
            if (spec.Health == 0 || !World.Run.CanAfford(spec.Cost)) return;
            if (World.Expedition == null && World.Headquarters.HasValue == (kind == ExpansionKind.Headquarters)) return;
            if (World.Expedition != null && (kind == ExpansionKind.Headquarters || kind == ExpansionKind.Barracks ||
                World.BuildingRequirement(kind).Length > 0)) return;
            barricadeDismantle = -1;
            CancelPlacement();
            SelectedBuilding = -1;
            selectedSquad = -1;
            PlacementKind = kind;
            RefreshOverlay();
        }

        internal void BeginTurretMove()
        {
            if (!CanBuild || World.Expedition != null || SelectedBuilding < 0) return;
            var site = World.Buildings[SelectedBuilding];
            if (site.Kind != ExpansionKind.Turret || site.IsUnderConstruction) return;
            CancelPlacement();
            MovingTurret = SelectedBuilding;
            PlacementKind = ExpansionKind.Turret;
            RefreshOverlay();
        }

        /// <summary>触屏按钮退出选址，结束当前触点，避免抬手时转成移动命令。</summary>
        internal void CancelPlacementFromHud()
        {
            if (!CanBuild || !TouchScoutingAvailable || !HasPlacement) return;
            CancelPlacementFromInput();
            RefreshOverlay();
        }

        private void CancelPlacementFromInput()
        {
            cancelPlacementFrame = Time.frameCount;
            if (TouchScoutingAvailable)
            {
                tapTouchId = 0;
                ignoreTouchUntilRelease = true;
            }
            CancelPlacement();
        }

        private void CancelPlacement()
        {
            PlacementKind = ExpansionKind.Empty;
            MovingTurret = -1;
            rotatedPlacement = false;
            HoverIssue = null;
            HoverResource = -1;
            feedback = "";
            if (placementGhost != null) placementGhost.gameObject.SetActive(false);
            if (bindingMark != null) bindingMark.gameObject.SetActive(false);
            if (buildRange != null) buildRange.gameObject.SetActive(false);
        }

        internal void CloseConstruction()
        {
            barricadeDismantle = -1;
            if (!ConstructionOpen && !HasPlacement) return;
            CancelPlacement();
            SelectedBuilding = -1;
            ConstructionOpen = false;
            construction?.Hide();
        }

        /// <summary>建筑落位使用吸附格，指挥员移动使用原始地面投影。</summary>
        private bool TryBattlefieldCell(Vector2 screenPoint, out Vector2Int cell)
        {
            cell = default;
            if (!TryBattlefieldPoint(screenPoint, out var point)) return false;
            cell = BattleGrid.Cell(point);
            return true;
        }

        private bool TryBattlefieldPoint(Vector2 screenPoint, out Vector2 point)
        {
            point = default;
            var hudPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y) / HudScale;
            if (!gameCamera.pixelRect.Contains(screenPoint) || MapRect.Contains(hudPoint) || StatusRect.Contains(hudPoint)) return false;
            var ray = gameCamera.ScreenPointToRay(screenPoint);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return false;
            var hit = ray.GetPoint(distance);
            point = new Vector2(hit.x, hit.z);
            return true;
        }

        /// <summary>真实左键入口。选址时提交落位，平时点选设施或守军。</summary>
        public bool ActOnBattlefield(Vector2 screenPoint)
        {
            if (!CanBuild) return false;
            if (!TryBattlefieldCell(screenPoint, out var cell)) return false;
            if (HasPlacement)
            {
                bool moving = MovingTurret >= 0;
                bool establishing = PlacementKind == ExpansionKind.Headquarters;
                var issue = moving ? session.MoveTurret(MovingTurret, cell) : session.Build(PlacementKind, cell, rotatedPlacement);
                if (issue != PlacementIssue.None)
                {
                    ShowFeedback(PlacementMessage(issue));
                    return false;
                }
                CancelPlacement();
                SelectedBuilding = World.FindBuilding(cell);
                selectedSquad = SelectedBuilding >= 0 && World.Buildings[SelectedBuilding].Kind == ExpansionKind.Barracks ? SelectedBuilding : -1;
                if (establishing)
                {
                    ResetBattleCamera();
                    UpdateBattleCamera();
                }
                ShowFeedback(Game.Loc.T(establishing ? "HeadquartersBuilt" : moving ? "TurretMoved" : "ConstructionStarted"));
                SyncView();
                RefreshOverlay();
                return true;
            }
            int index = World.FindBuilding(cell);
            if (index < 0) index = World.FindSquad(BattleGrid.Center(cell));
            if (index < 0)
            {
                if (World.Headquarters?.Contains(cell) != true) return false;
                ToggleUpgrade();
                return true;
            }
            SelectedBuilding = index;
            barricadeDismantle = -1;
            selectedSquad = World.Buildings[index].Kind == ExpansionKind.Barracks ? index : -1;
            ConstructionOpen = true;
            feedback = "";
            RefreshOverlay();
            return true;
        }

        private void UpdatePlacementPreview(Vector2 screenPoint, bool overUI)
        {
            buildRange.gameObject.SetActive(HasPlacement && World.Headquarters.HasValue);
            placementGhost.gameObject.SetActive(false);
            bindingMark.gameObject.SetActive(false);
            HoverIssue = null;
            HoverResource = -1;
            if (!HasPlacement || overUI || !TryBattlefieldCell(screenPoint, out var cell)) return;
            var issue = World.PreviewPlacement(PlacementKind, cell, rotatedPlacement, MovingTurret, out var footprint, out int node);
            HoverIssue = issue;
            HoverResource = node;
            placementGhost.gameObject.SetActive(true);
            var material = issue == PlacementIssue.None ? validPlacement : invalidPlacement;
            int index = 0;
            foreach (var part in footprint.allPositionsWithin)
            {
                var tile = placementTiles[index++];
                tile.transform.position = At(BattleGrid.Center(part), .13f);
                tile.sharedMaterial = material;
                tile.gameObject.SetActive(true);
            }
            for (; index < placementTiles.Length; index++) placementTiles[index].gameObject.SetActive(false);
            if (node < 0) return;
            bindingMark.position = At(World.Nodes[node].Position, .08f);
            bindingMark.gameObject.SetActive(true);
        }

        internal string PlacementMessage(PlacementIssue issue)
        {
            if (issue == PlacementIssue.MissingResearch) return World.BuildingRequirement(PlacementKind);
            if (issue == PlacementIssue.BlocksAccess) return "会封住资源或任务的入口";
            if (PlacementKind == ExpansionKind.Headquarters && issue == PlacementIssue.Outside)
                return Game.Loc.T("HeadquartersOutside");
            if (PlacementKind == ExpansionKind.Headquarters && issue == PlacementIssue.NoResource)
                return Game.Loc.T("HeadquartersNoMine");
            return Game.Loc.T("Placement" + issue);
        }

        private void ShowFeedback(string message)
        {
            feedback = message;
            feedbackUntil = Time.realtimeSinceStartup + 2;
        }

        /// <summary>为选中的完工生产设施提交采集员或守军订单。</summary>
        internal void OrderPersonnel()
        {
            if (!CanBuild || HasPlacement || SelectedBuilding < 0 || !World.Buildings[SelectedBuilding].CanProduce) return;
            if (session.Produce(SelectedBuilding)) ShowFeedback(Game.Loc.T("ProductionStarted"));
            RefreshOverlay();
        }

        /// <summary>仅在基础会话就绪时显示窗口，退出后不重新生成 UI。</summary>
        private void RefreshOverlay()
        {
            if (!Application.isPlaying || !Game.FoundationRuntime.IsInitialized) return;
            var mode = Overlay;
            if (mode != PrototypeOverlay.None)
            {
                CloseConstruction();
                if (modal?.IsShow() != true) modal = Game.UIHelper.ShowUI<PrototypeModalCtrl>();
                modal.Bind(this, font);
                modal.Refresh(this);
            }
            else modal?.Hide();

            if (mode == PrototypeOverlay.None)
            {
                if (hud?.IsShow() != true) hud = Game.UIHelper.ShowUI<PrototypeHudCtrl>();
                hud.Bind(this, font);
                hud.Refresh(World.Headquarters.HasValue, SelectedSquad);
                if (ConstructionOpen)
                {
                    if (construction?.IsShow() != true) construction = Game.UIHelper.ShowUI<PrototypeConstructionCtrl>();
                    construction.Bind(this, font);
                    construction.Refresh(this);
                }
            }
            else hud?.Hide();
        }

        /// <summary>表现仅映射已结算状态；敌人采用固定池，不按波次无限创建。</summary>
        public void SyncView()
        {
            bool built = World.Headquarters.HasValue;
            commander.gameObject.SetActive(!built);
            commander.position = At(World.CommanderPosition, .9f);
            SyncExpeditionMarkers();
            headquarters.gameObject.SetActive(built);
            headquarters.position = At(World.BasePosition, 0);
            buildRange.position = At(World.BasePosition, 0);
            fog.Sync(World.Vision);
            for (int i = 0; i < resources.Count; i++)
            {
                var model = resources[i];
                model.gameObject.SetActive(World.KnownOreRemaining(i) > 0);
                if (model.gameObject.activeSelf)
                    model.sharedMaterial = World.Vision.IsVisible(World.Nodes[i].Position) ? visibleOre : rememberedOre;
            }
            foreach (var boundary in boundaries)
                boundary.gameObject.SetActive(World.Vision.IsExplored(new Vector2(boundary.position.x, boundary.position.z)));
            for (int i = 0; i < expansionModels.Count; i++)
                expansionModels[i].Show(World.Buildings[i], World.Run.Elapsed);
            for(int i=0;i<enemies.Count;i++)
            {
                bool active = i < World.Enemies.Count && EnemyVisible(World.Enemies[i]);
                enemies[i].gameObject.SetActive(active);
                if (!active) continue;
                var state = World.Enemies[i];
                var model = enemies[i];
                model.position = At(state.Position, state.Kind == EnemyKind.Siege ? 1.05f : .8f);
                Vector3 size = state.Kind switch
                {
                    EnemyKind.Fast => new Vector3(.48f, .78f, .48f),
                    EnemyKind.Siege => new Vector3(1.08f, 1.1f, 1.08f),
                    _ => new Vector3(.65f, .8f, .65f)
                };
                // 攻击落下时短促前冲，攻坚敌人的冲击幅度更大。
                float attack = Mathf.Clamp01(1 - (World.Run.Elapsed - state.LastAttackTime) / .18f);
                model.localScale = size + new Vector3(.16f, -.1f, .16f) * (attack * (state.Kind == EnemyKind.Siege ? 2 : 1));
                model.GetComponent<Renderer>().sharedMaterial = enemyMaterials[(int)state.Kind];
            }
        }

        private bool EnemyVisible(EnemyState enemy) => enemy.Health > 0 && World.Vision.IsVisible(enemy.Position);

        private static Vector3 At(Vector2 point, float height) => new Vector3(point.x, height, point.y);

        private void OnGUI()
        {
            if(!initialized){GUI.Label(new Rect(20,20,1000,120),"启动失败，请先运行配置生成并查看Console："+startupError);return;}
            if (!started) return;
            float scale=HudScale;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            GUI.skin.font=font;GUI.skin.label.fontSize=18;GUI.skin.button.fontSize=19;GUI.skin.box.fontSize=18;
            var s=World.Run;
            if (Event.current.type == EventType.Repaint && s.Outcome == 0) DrawWorksiteProgress(scale);
            GUI.Box(StatusRect,"");
            if (World.Expedition != null) DrawExpeditionStatus();
            else
            {
                GUI.Label(new Rect(30,26,305,30),Game.Loc.T("HudTitle"));
                GUI.Label(new Rect(30,61,300,28),Game.Loc.T("FundsStock", s.Funds));
                if (World.Headquarters.HasValue)
                {
                    GUI.Label(new Rect(30,92,300,28),Game.Loc.T("WaveProgress", World.DiscoveredWave, s.Config.WaveTimes.Count));
                    GUI.Label(new Rect(30,122,300,28),Game.Loc.T("HudHealth", s.Health, World.Kills));
                }
                else GUI.Label(new Rect(30,92,300,62),Game.Loc.T("HeadquartersGuide"));
            }
            // 对话框的设置入口占用右上角，小地图仅在指挥状态显示和接收点选。
            if (Overlay != PrototypeOverlay.None || UtilityWindowOpen) return;
            DrawMap();
            DrawBattleFeedback();
            if (!HasPlacement && Event.current.type == EventType.Repaint && EventSystem.current?.IsPointerOverGameObject() != true)
                DrawResourceHint(Input.mousePosition);
        }

        /// <summary>用工地世界坐标投影施工条，关闭建造侧栏后也可查看。</summary>
        private void DrawWorksiteProgress(float scale)
        {
            var color = GUI.color;
            foreach (var site in World.Buildings)
            {
                if (site.IsUnderConstruction)
                    DrawWorksiteBar(site.Position, site.ConstructionLeft, World.BuildingSpec(site.Kind).BuildSeconds, scale);
                else if (site.ProductionLeft > 0)
                    DrawWorksiteBar(site.Position, site.ProductionLeft, site.ProductionDuration, scale);
            }
            GUI.color = color;
        }

        private void DrawWorksiteBar(Vector2 position, float remaining, float duration, float scale)
        {
            Vector3 point = gameCamera.WorldToScreenPoint(At(position, 2.4f));
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            var bar = new Rect(point.x / scale - 34, (Screen.height - point.y) / scale - 7, 68, 8);
            GUI.color = new Color(.08f, .13f, .14f, 1);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(1, .66f, .2f, 1);
            float progress = Mathf.Clamp01(1 - remaining / duration);
            GUI.DrawTexture(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * progress, bar.height - 4),
                Texture2D.whiteTexture);
        }

        /// <summary>以可见资源的投影和小地图点为命中中心，重叠时选距离最近的一项。</summary>
        private int PickResource(Vector2 screenPoint)
        {
            float scale = HudScale;
            var cursor = new Vector2(screenPoint.x, Screen.height - screenPoint.y) / scale;
            if (StatusRect.Contains(cursor)) return -1;
            bool onMap = MapRect.Contains(cursor);
            float closest = onMap ? 9 * 9 : 16 * 16;
            int target = -1;
            for (int i = 0; i < World.Nodes.Count; i++)
            {
                if (World.KnownOreRemaining(i) <= 0) continue;
                Vector2 center = onMap ? MapPoint(World.Nodes[i].Position) : ResourceScreenPoint(i);
                float distance = (center - cursor).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                target = i;
            }
            return target;
        }

        private Vector2 ResourceScreenPoint(int index)
        {
            var point = gameCamera.WorldToScreenPoint(resources[index].transform.position);
            return new Vector2(point.x, Screen.height - point.y) / HudScale;
        }

        /// <summary>指向矿点时显示余量。</summary>
        private void DrawResourceHint(Vector2 screenPoint)
        {
            int index = PickResource(screenPoint);
            if (index < 0) return;
            string title = ResourceStatus(index);
            const float width = 300, height = 30;
            var cursor = new Vector2(screenPoint.x, Screen.height - screenPoint.y) / HudScale;
            float x = Mathf.Clamp(cursor.x - width * .5f, 0, Screen.width / HudScale - width);
            float y = Mathf.Max(0, cursor.y - height - 12);
            var oldColor = GUI.contentColor;
            var oldTint = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.20f, .29f, .28f, 1);
            GUI.contentColor = new Color(.57f, 1, 1);
            GUI.Box(new Rect(x, y, width, height), title);
            GUI.contentColor = oldColor;
            GUI.backgroundColor = oldTint;
        }

        internal string ResourceStatus(int index) =>
            Game.Loc.T(World.Vision.IsVisible(World.Nodes[index].Position) ? "MineRemaining" : "MineRemembered",
                World.KnownOreRemaining(index));

        private void DrawBattleFeedback()
        {
            float time = World.Run.Elapsed;
            float y = 188;
            for (int i = World.Sightings.Count - 1; i >= 0; i--)
            {
                var sighting = World.Sightings[i];
                if (time - sighting.Time >= 5) break;
                GUI.Box(new Rect(16, y, 330, 30), Game.Loc.T("WaveSpotted", sighting.Wave,
                    Game.Loc.T(PrototypeOverlayView.SectorNames[sighting.Sector])));
                y += 34;
            }
            if (time - World.LastDestroyedAt < 5)
            {
                var position = World.LastDestroyedPosition - World.BasePosition;
                int side = Mathf.Abs(position.x) >= Mathf.Abs(position.y) ? (position.x < 0 ? 0 : 1) : (position.y > 0 ? 2 : 3);
                GUI.Box(new Rect(16, y, 330, 30), Game.Loc.T("BuildingLost", Game.Loc.T(PrototypeOverlayView.SectorNames[side]),
                    PrototypeOverlayView.BuildingName(World.LastDestroyedKind)));
                y += 34;
            }
            if (time - World.LastMineDepletedAt < 5)
            {
                GUI.Box(new Rect(16, y, 330, 30), Game.Loc.T("MineDepleted"));
                y += 34;
            }
            foreach (var site in World.Buildings)
            {
                if (!site.PersonnelWaiting && site.Personnel?.Blocked != true) continue;
                string reason = site.PersonnelWaiting ? "PersonnelExitBlocked" :
                    site.Kind == ExpansionKind.Extractor ? "WorkerBlocked" : "SquadBlocked";
                GUI.Box(new Rect(16, y, 330, 30), Game.Loc.T(reason));
                y += 34;
                break;
            }
            if (World.Headquarters.HasValue && time < 35)
                GUI.Box(new Rect(16, y, 330, 40), Game.Loc.T("FirstMineGuide"));
        }

        private void DrawMap()
        {
            var old=GUI.color;GUI.Box(MapRect,Game.Loc.T("HudMap"));
            float span = World.Vision.Span * 2;
            GUI.DrawTexture(new Rect(MapCenter.x - span, MapCenter.y - span, span * 2, span * 2), fog.Texture);
            for (int i = 0; i < World.Nodes.Count; i++)
            {
                var node = World.Nodes[i];
                if (World.KnownOreRemaining(i) > 0)
                    Dot(node.Position, World.Vision.IsVisible(node.Position) ? Color.cyan : new Color(.34f, .54f, .52f), 4);
            }
            foreach (var enemy in World.Enemies)
                if (EnemyVisible(enemy)) Dot(enemy.Position, enemy.Kind == EnemyKind.Fast ? new Color(1, .61f, .13f) : Color.red,
                    enemy.Kind == EnemyKind.Siege ? 7 : 5);
            foreach (var site in World.Buildings)
                if (site.Kind != ExpansionKind.Empty) Dot(site.Position, site.Kind switch
                {
                    ExpansionKind.Extractor => new Color(.2f, 1, .65f),
                    ExpansionKind.Barracks => new Color(.32f, .58f, 1),
                    ExpansionKind.Barricade => new Color(.85f, .38f, .22f),
                    _ => Color.gray
                }, 8);
            foreach (var site in World.Buildings)
                if (site.Personnel != null)
                    Dot(site.Personnel.Position, site.Kind == ExpansionKind.Extractor ? Color.yellow : new Color(.32f, .58f, 1), 6);
            Dot(World.BasePosition, World.Headquarters.HasValue ? Color.white : Color.yellow, 9);
            if (World.Expedition is { } expedition)
            {
                if (!expedition.FoundClue && World.Vision.IsExplored(expedition.CluePosition))
                    Dot(expedition.CluePosition, new Color(.32f, .58f, 1), 7);
                DrawExpeditionMapMarkers(expedition);
            }
            GUI.color=old;
        }
        private static Vector2 MapPoint(Vector2 point) => MapCenter + new Vector2(point.x, -point.y) * 2;
        private void Dot(Vector2 p,Color color,float size)
        {
            var center = MapPoint(p);
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size / 2, center.y - size / 2, size, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        private void ReleaseFog()
        {
            if (fog == null) return;
            Release(fog.Texture);
            Release(fog.Mesh);
            Release(fog.Material);
            fog = null;
        }

        private void OnDestroy()
        {
            ReleaseFog();
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            modal?.Detach(this);
            hud?.Detach(this);
            construction?.Detach(this);
            foreach(var material in materials)Release(material);
            if(font!=null)Release(font);
        }
    }
}
