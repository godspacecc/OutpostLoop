using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using QFramework;

namespace OutpostLoop
{
    /// <summary>临时玩法界面从当前会话推导的显示状态。</summary>
    internal enum PrototypeOverlay { None, Start, Paused, Upgrade, Result }
    /// <summary>独立建造面板的显示状态。</summary>
    internal enum ConstructionTab { Closed, Expansion, Turrets }

    /// <summary>独立原型表现入口：向玩法系统提交输入、同步临时画面。无旧项目资源。</summary>
    public sealed class PrototypeController : MonoBehaviour
    {
        // 本局领域状态、临时表现根和预分配敌人池。
        public WorldState World { get; private set; }
        private Transform worldRoot, worker, squad, resourceOrderMark;
        private LineRenderer squadShot;
        private Camera gameCamera;
        // 镜头始终对准基地，开局突出布防区域，滚轮可拉远到整张地图。
        private const float DefaultMapView = .6f;
        private const float ClosestMapView = .45f;
        private const float ZoomPerNotch = .1f;
        private float mapView = DefaultMapView;
        // 指挥视图和小地图共用同一套屏幕命中区域。
        private static readonly Rect StatusRect = new Rect(16, 16, 330, 165);
        private static readonly Rect MapRect = new Rect(1050, 18, 210, 235);
        private static readonly Vector2 MapCenter = new Vector2(1155, 140);
        private static float HudScale => Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        private readonly List<LineRenderer> turretShots = new List<LineRenderer>();
        private readonly List<Transform> turretModels = new List<Transform>();
        private readonly List<Transform> turretWorksites = new List<Transform>();
        // 每块地复用预建的临时模型，重建时切换显示即可。
        private readonly List<ExpansionPreview> expansionModels = new List<ExpansionPreview>();
        private readonly List<Transform> resources = new List<Transform>();
        private readonly List<Transform> enemies = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private bool started, shop;
        internal bool WantScrapPack { get; private set; }
        internal bool WantEnergyPack { get; private set; }
        internal string SupplyError { get; private set; }
        internal int SupplyCoins => session.SupplyCoins;
        internal bool VictoryRewardRecorded => session.VictoryRewardRecorded;
        // 面板选中状态与本局建筑所有权分离。
        internal ConstructionTab BuildTab { get; private set; }
        internal int SelectedTurretSlot { get; private set; } = -1;
        internal int SelectedExpansionSite { get; private set; }
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

        private void Awake() { Initialize(); }
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
        private void OnEnable(){if(initialized)Game.LifeCycle.RegisterUpdate(PollInputAndPresent);}
        private void OnDisable()
        {
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
            if(architecture.GetSystem<PrototypeSystem>()==null)architecture.RegisterSystem(new PrototypeSystem());
            session=architecture.GetSystem<PrototypeSystem>();
            architecture.GetUtility<Game.HotKeyActionRegistry>().Register(new Game.HotKeyAction
            {
                ActionId = Game.HotKeyActionIds.TogglePause,
                Context = Game.HotKeyContext.Gameplay,
                Priority = 10,
                CanExecute = CanTogglePause,
                Execute = TogglePause
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
            gameCamera=cameraObject.AddComponent<Camera>(); gameCamera.orthographic=true;
            gameCamera.transform.SetPositionAndRotation(new Vector3(0, 32, -14.92f), Quaternion.Euler(65, 0, 0));
            gameCamera.backgroundColor=groundColor; gameCamera.clearFlags=CameraClearFlags.SolidColor;
            gameCamera.nearClipPlane=0.1f; gameCamera.farClipPlane=160;
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
        public void Restart(bool begin)
        {
            initialized=false;
            if(worldRoot!=null){worldRoot.gameObject.SetActive(false);Release(worldRoot.gameObject);}
            foreach(var material in materials)Release(material);
            materials.Clear();resources.Clear();enemies.Clear();
            turretModels.Clear();
            turretShots.Clear();
            turretWorksites.Clear();
            expansionModels.Clear();
            CloseConstruction();
            SelectedExpansionSite = 0;
            WantScrapPack = WantEnergyPack = false;
            SupplyError = null;
            mapView = DefaultMapView;
            World=session.Begin(++seed,begin); started=begin;shop=false;startupError=null;
            worldRoot=new GameObject("TemporaryPreviewWorld").transform;worldRoot.SetParent(transform);
            var ground=Mat(groundColor);var stone=Mat(new Color(0.37f,0.40f,0.40f));
            var dark=Mat(new Color(0.11f,0.16f,0.17f));var amber=Mat(new Color(1,0.61f,0.13f));
            var cyan=Mat(new Color(0.15f,0.88f,0.84f));var rust=Mat(new Color(0.52f,0.24f,0.17f));
            Shape("Ground",worldRoot,PrimitiveType.Cube,new Vector3(0,-0.3f,0),new Vector3(96,0.5f,96),ground);
            Shape("HomePad",worldRoot,PrimitiveType.Cylinder,new Vector3(0,0,0),new Vector3(9,0.1f,9),dark);
            Shape("Shelter",worldRoot,PrimitiveType.Cube,new Vector3(0,1.15f,0),new Vector3(3.6f,2.3f,3),stone);
            Shape("Roof",worldRoot,PrimitiveType.Cube,new Vector3(0,2.5f,0),new Vector3(4,0.5f,3.5f),dark);
            Shape("Door",worldRoot,PrimitiveType.Cube,new Vector3(0,0.8f,-1.55f),new Vector3(0.9f,1.6f,0.1f),amber);
            for (int i = 0; i < World.Turrets.Count; i++)
            {
                var position = World.Turrets[i].Position;
                var turret = new GameObject("Turret" + i).transform;
                turret.SetParent(worldRoot, false);
                turret.localPosition = new Vector3(position.x, 0, position.y);
                turret.localRotation = Quaternion.LookRotation(new Vector3(position.x, 0, position.y));
                Shape("Mount", turret, PrimitiveType.Cylinder, new Vector3(0,.5f,0), new Vector3(1.5f,.5f,1.5f), stone);
                Shape("Head", turret, PrimitiveType.Cube, new Vector3(0,1.3f,0), new Vector3(.8f,.7f,.8f), dark);
                Shape("Barrel", turret, PrimitiveType.Cube, new Vector3(0,1.3f,.7f), new Vector3(.2f,.2f,1), amber);
                var shot = turret.gameObject.AddComponent<LineRenderer>();
                shot.sharedMaterial = amber;
                shot.positionCount = 2;
                shot.startWidth = .1f;
                shot.endWidth = .04f;
                shot.enabled = false;
                turretModels.Add(turret);
                turretShots.Add(shot);
                turretWorksites.Add(Shape("TurretWorksite" + i, worldRoot, PrimitiveType.Cube, At(position, .16f),
                    new Vector3(1.55f, .24f, 1.55f), amber));
            }
            worker = Shape("Worker", worldRoot, PrimitiveType.Capsule, Vector3.zero, new Vector3(1.1f, .9f, 1.1f), amber);
            squad = new GameObject("DefenseSquad").transform;
            squad.SetParent(worldRoot, false);
            var blue = Mat(new Color(.32f, .58f, 1));
            for (int i = -1; i <= 1; i++)
                Shape("Guard", squad, PrimitiveType.Capsule, new Vector3(i * .95f, 0, Mathf.Abs(i) * -.75f),
                    new Vector3(.75f, .8f, .75f), blue);
            squadShot = squad.gameObject.AddComponent<LineRenderer>();
            squadShot.sharedMaterial = blue;
            squadShot.positionCount = 2;
            squadShot.startWidth = .13f;
            squadShot.endWidth = .05f;
            resourceOrderMark = Shape("ResourceOrder", worldRoot, PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(2.6f, .08f, 2.6f), amber);
            CreateExpansionModels(stone, cyan, blue, rust, amber);
            foreach(var node in World.Nodes)
            {
                var item=Shape(node.Energy?"Energy":"Scrap",worldRoot,node.Energy?PrimitiveType.Cube:PrimitiveType.Cylinder,
                    new Vector3(node.Position.x,0.6f,node.Position.y),node.Energy?new Vector3(0.7f,1.2f,0.7f):new Vector3(1.6f,0.45f,1.6f),node.Energy?cyan:rust);
                if(node.Energy)item.rotation=Quaternion.Euler(0,45,20);
                resources.Add(item);
            }
            for(int i=0;i<World.Run.Config.MaxEnemies;i++)
            {
                var enemy=Shape("PooledEnemy",worldRoot,PrimitiveType.Capsule,Vector3.zero,new Vector3(0.65f,0.8f,0.65f),rust);
                enemy.gameObject.SetActive(false);enemies.Add(enemy);
            }
            // 树只作为边缘装饰，不制造看不见的阻挡。
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16;var pos=new Vector3(Mathf.Cos(angle)*48,1.5f,Mathf.Sin(angle)*48);
                Shape("Boundary",worldRoot,PrimitiveType.Cube,pos,new Vector3(2,3,2),stone);
            }
            feedback="";SyncView();initialized=true;
        }

        /// <summary>外围建筑用独立临时几何体区分功能，炮位仍走原有表现。</summary>
        private void CreateExpansionModels(Material pad, Material extractor, Material barracks, Material barricade, Material worksite)
        {
            for (int i = 0; i < World.ExpansionSites.Count; i++)
            {
                var root = new GameObject("ExpansionSite" + i).transform;
                root.SetParent(worldRoot, false);
                root.localPosition = At(World.ExpansionSites[i].Position, 0);
                root.localRotation = Quaternion.LookRotation(root.localPosition);
                Shape("Plot", root, PrimitiveType.Cube, new Vector3(0, .05f, 0), new Vector3(4, .2f, 4), pad);
                var foundation = Shape("ExpansionWorksite", root, PrimitiveType.Cube, new Vector3(0, .24f, 0),
                    new Vector3(3f, .24f, 2.3f), worksite);
                var station = Shape("Extractor", root, PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0),
                    new Vector3(2.25f, 1, 2.25f), extractor);
                var quarters = Shape("Barracks", root, PrimitiveType.Cube, new Vector3(0, 1.15f, 0),
                    new Vector3(2.8f, 2.2f, 1.8f), barracks);
                var wall = Shape("Barricade", root, PrimitiveType.Cube, new Vector3(0, .95f, .25f),
                    new Vector3(3.6f, 1.8f, .65f), barricade);
                var guard = Shape("Garrison", root, PrimitiveType.Capsule, new Vector3(1.35f, .8f, -1.65f),
                    new Vector3(.85f, .8f, .85f), barracks);
                var shot = root.gameObject.AddComponent<LineRenderer>();
                shot.sharedMaterial = barracks;
                shot.positionCount = 2;
                shot.startWidth = .13f;
                shot.endWidth = .05f;
                expansionModels.Add(new ExpansionPreview(foundation, station, quarters, wall, guard, shot));
            }
        }

        /// <summary>把单块地的建筑、训练状态与射击反馈映射到预建对象。</summary>
        private sealed class ExpansionPreview
        {
            private readonly Transform worksite, extractor, barracks, barricade, garrison;
            private readonly LineRenderer shot;

            internal ExpansionPreview(Transform worksite, Transform extractor, Transform barracks, Transform barricade, Transform garrison,
                LineRenderer shot)
            {
                this.worksite = worksite;
                this.extractor = extractor;
                this.barracks = barracks;
                this.barricade = barricade;
                this.garrison = garrison;
                this.shot = shot;
            }

            internal void Show(ExpansionSite site, float time)
            {
                bool building = site.IsUnderConstruction;
                worksite.gameObject.SetActive(building);
                extractor.gameObject.SetActive(!building && site.Kind == ExpansionKind.Extractor);
                barracks.gameObject.SetActive(!building && site.Kind == ExpansionKind.Barracks);
                barricade.gameObject.SetActive(!building && site.Kind == ExpansionKind.Barricade);
                garrison.gameObject.SetActive(site.HasGarrison);
                shot.enabled = time - site.LastShotTime < .12f;
                if (!shot.enabled) return;
                shot.SetPosition(0, garrison.position);
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
                CloseConstruction();
                return;
            }
            if(Input.GetKeyDown(KeyCode.B)) ToggleUpgrade();
            if (Input.GetKeyDown(KeyCode.V)) ToggleConstruction();
            if (Overlay == PrototypeOverlay.None && EventSystem.current?.IsPointerOverGameObject() != true)
            {
                if (Input.GetMouseButtonDown(0)) OrderResourceAt(Input.mousePosition);
                ChangeMapZoom(Input.mouseScrollDelta.y);
            }
            SyncView();
            RefreshOverlay();
        }

        /// <summary>向前滚动拉近，向后滚动可看到地图全貌。</summary>
        private void ChangeMapZoom(float wheelDelta)
        {
            mapView = Mathf.Clamp(mapView - wheelDelta * ZoomPerNotch, ClosestMapView, 1f);
        }

        /// <summary>主视图或小地图点选同一资源，空白处不改变工人任务。</summary>
        public bool OrderResourceAt(Vector2 screenPoint)
        {
            if (!initialized || Overlay != PrototypeOverlay.None || UtilityWindowOpen) return false;
            int node = PickResource(screenPoint);
            return node >= 0 && session.AssignResource(node);
        }

        /// <summary>局内四向按钮各提交一条驻防命令。</summary>
        internal void DeploySquad(int sector)
        {
            if (initialized && Overlay == PrototypeOverlay.None && !UtilityWindowOpen)
                session.DeploySquad(sector);
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
            shop ? PrototypeOverlay.Upgrade : World.Run.Paused ? PrototypeOverlay.Paused : PrototypeOverlay.None;

        internal string ActionFeedback => Time.realtimeSinceStartup < feedbackUntil ? feedback : "";

        /// <summary>键盘与临时按钮共用同一个基地入口，购买权限仍由玩法系统判断。</summary>
        internal void ToggleUpgrade()
        {
            if (UtilityWindowOpen) return;
            if (!initialized || (Overlay != PrototypeOverlay.None && Overlay != PrototypeOverlay.Upgrade)) return;
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
                    if (!session.StartWithSupplies(WantScrapPack, WantEnergyPack))
                    {
                        SupplyError = "SupplyPaymentFailed";
                        break;
                    }
                    started = true;
                    break;
                case PrototypeOverlay.Paused:
                case PrototypeOverlay.Upgrade:
                    shop = false;
                    session.SetPaused(false);
                    break;
                case PrototypeOverlay.Result:
                    if (World.Run.Outcome <= 0 || session.TryRecordVictory())
                        Restart(false);
                    break;
            }
            RefreshOverlay();
        }

        /// <summary>只在开局准备页选择补给，选择时不扣币。</summary>
        internal void ToggleSupply(bool energy)
        {
            if (UtilityWindowOpen || Overlay != PrototypeOverlay.Start) return;
            bool selected = energy ? WantEnergyPack : WantScrapPack;
            int committed = (WantScrapPack ? PrototypeWallet.SupplyPrice : 0) +
                            (WantEnergyPack ? PrototypeWallet.SupplyPrice : 0);
            if (!selected && SupplyCoins < committed + PrototypeWallet.SupplyPrice)
                SupplyError = "SupplyInsufficient";
            else
            {
                if (energy) WantEnergyPack = !selected;
                else WantScrapPack = !selected;
                SupplyError = null;
            }
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

        /// <summary>建造入口与暂停升级独立，并保留玩家本局最后选择的地块。</summary>
        internal void ToggleConstruction()
        {
            if (!CanBuild) return;
            if (BuildTab != ConstructionTab.Closed)
            {
                CloseConstruction();
                return;
            }
            BuildTab = ConstructionTab.Expansion;
            feedback = "";
            RefreshOverlay();
        }

        private bool CanBuild => initialized && Overlay == PrototypeOverlay.None && !UtilityWindowOpen;

        /// <summary>在建造面板内切换设施和炮位，取消上一个炮位的迁移选中。</summary>
        internal void SelectConstructionTab(ConstructionTab tab)
        {
            if (!CanBuild || BuildTab == ConstructionTab.Closed || BuildTab == tab) return;
            BuildTab = tab;
            SelectedTurretSlot = -1;
            feedback = "";
            RefreshOverlay();
        }

        /// <summary>点已有炮塔选作迁移来源，点空位时迁移或建造。</summary>
        internal void ActOnTurretSlot(int index)
        {
            if (!CanBuild || BuildTab != ConstructionTab.Turrets) return;
            if (index < 0 || index >= World.Turrets.Count) return;
            if (World.Turrets[index].IsUnderConstruction) return;
            if (World.Turrets[index].Occupied)
            {
                SelectedTurretSlot = SelectedTurretSlot == index ? -1 : index;
                feedback = "";
            }
            else
            {
                bool moving = SelectedTurretSlot >= 0;
                bool success = moving ? session.MoveTurret(SelectedTurretSlot, index) : session.BuildTurret(index);
                feedback = Game.Loc.T(success ? (moving ? "TurretMoved" : "ConstructionStarted") : "TurretUnavailable");
                feedbackUntil = Time.realtimeSinceStartup + 2;
                if (success)
                {
                    SelectedTurretSlot = -1;
                    SyncView();
                }
            }
            RefreshOverlay();
        }

        /// <summary>选中一块外围地块，继续显示它的可建项目或建筑状态。</summary>
        internal void SelectExpansionSite(int sector)
        {
            if (!CanBuild || BuildTab != ConstructionTab.Expansion ||
                sector < 0 || sector >= World.ExpansionSites.Count) return;
            SelectedExpansionSite = sector;
            feedback = "";
            RefreshOverlay();
        }

        /// <summary>将选中地块和建筑类型交给规则层扣费，成功后立即刷新模型。</summary>
        internal void PurchaseExpansion(ExpansionKind kind)
        {
            if (!CanBuild || BuildTab != ConstructionTab.Expansion) return;
            bool built = session.BuildExpansion(SelectedExpansionSite, kind);
            feedback = Game.Loc.T(built ? "ConstructionStarted" : "ExpansionUnavailable");
            feedbackUntil = Time.realtimeSinceStartup + 2;
            if (built) SyncView();
            RefreshOverlay();
        }

        /// <summary>从当前兵营下独立订单，库存不足时显示缺口。</summary>
        internal void OrderGarrison()
        {
            if (!CanBuild || BuildTab != ConstructionTab.Expansion || !World.ExpansionSites[SelectedExpansionSite].CanTrain) return;
            if (session.TrainGarrison(SelectedExpansionSite))
                feedback = Game.Loc.T("GarrisonTrainingStarted");
            else
                feedback = Game.Loc.T("GarrisonTrainingShortfall",
                    Mathf.Max(0, ExpansionSite.TrainingScrap - World.Run.Scrap),
                    Mathf.Max(0, ExpansionSite.TrainingEnergy - World.Run.Energy));
            feedbackUntil = Time.realtimeSinceStartup + 2;
            RefreshOverlay();
        }

        /// <summary>关闭侧栏时结束炮位迁移选择，保留本局选中的扩建地块。</summary>
        internal void CloseConstruction()
        {
            if (BuildTab == ConstructionTab.Closed) return;
            BuildTab = ConstructionTab.Closed;
            SelectedTurretSlot = -1;
            feedback = "";
            construction?.Hide();
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
                hud.Refresh(World.SquadSector);
                if (BuildTab != ConstructionTab.Closed)
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
            worker.position = At(World.WorkerPosition, .9f);
            // 待命时三人站在基地南侧，收到命令后按领域位置移动。
            squad.position = At(World.SquadPosition, .8f);
            squadShot.enabled = World.Run.Elapsed - World.SquadShotTime < .12f;
            if (squadShot.enabled)
            {
                squadShot.SetPosition(0, squad.position);
                squadShot.SetPosition(1, At(World.SquadShotTarget, .8f));
            }
            resourceOrderMark.gameObject.SetActive(World.ResourceOrder >= 0);
            if (World.ResourceOrder >= 0) resourceOrderMark.position = At(World.Nodes[World.ResourceOrder].Position, .04f);
            for(int i=0;i<resources.Count;i++)resources[i].gameObject.SetActive(World.Nodes[i].Remaining>0);
            for (int i = 0; i < expansionModels.Count; i++)
                expansionModels[i].Show(World.ExpansionSites[i], World.Run.Elapsed);
            for(int i=0;i<enemies.Count;i++)
            {
                bool active=i<World.Enemies.Count;enemies[i].gameObject.SetActive(active);
                if(active){var p=World.Enemies[i].Position;enemies[i].position=new Vector3(p.x,0.8f,p.y);}
            }
            gameCamera.orthographicSize = (World.Run.Config.MapRadius + 2) * mapView / Mathf.Min(1, gameCamera.aspect);
            for (int i = 0; i < turretModels.Count; i++)
            {
                var state = World.Turrets[i];
                var model = turretModels[i];
                var shot = turretShots[i];
                model.gameObject.SetActive(state.Occupied);
                turretWorksites[i].gameObject.SetActive(state.IsUnderConstruction);
                shot.enabled = state.Occupied && World.Run.Elapsed - state.LastShotTime < .12f;
                if (!shot.enabled) continue;
                var target = state.LastShotTarget;
                var direction = target - state.Position;
                if (direction.sqrMagnitude > 0)
                    model.localRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y));
                shot.SetPosition(0, model.position + Vector3.up * 1.3f + model.forward);
                shot.SetPosition(1, new Vector3(target.x, .8f, target.y));
            }
        }

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
            GUI.Label(new Rect(30,26,305,30),Game.Loc.T("HudTitle"));
            GUI.Label(new Rect(30,61,300,28),Game.Loc.T("HudTime", Mathf.CeilToInt(s.Config.Duration-s.Elapsed), World.Wave, s.Config.WaveTimes.Count));
            GUI.Label(new Rect(30,92,300,28),Game.Loc.T("HudHealth", s.Health, World.Kills));
            GUI.Label(new Rect(30,122,300,28),Game.Loc.T("HudStock", s.Scrap, s.Energy));
            GUI.Label(new Rect(30,151,300,28),Game.Loc.T("HudCargo", s.CargoScrap+s.CargoEnergy, s.Capacity));
            // 对话框的设置入口占用右上角，小地图仅在指挥状态显示和接收点选。
            if (Overlay != PrototypeOverlay.None || UtilityWindowOpen) return;
            DrawMap();
            if (Event.current.type == EventType.Repaint && EventSystem.current?.IsPointerOverGameObject() != true)
                DrawResourceHint(Input.mousePosition);
        }

        /// <summary>用工地世界坐标投影施工条，关闭建造侧栏后也可查看。</summary>
        private void DrawWorksiteProgress(float scale)
        {
            var color = GUI.color;
            foreach (var site in World.ExpansionSites)
                if (site.IsUnderConstruction)
                    DrawWorksiteBar(site.Position, site.ConstructionLeft, ExpansionSite.SpecFor(site.Kind).BuildSeconds, scale);
            foreach (var turret in World.Turrets)
                if (turret.IsUnderConstruction)
                    DrawWorksiteBar(turret.Position, turret.ConstructionLeft, TurretSlot.BuildSeconds, scale);
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
                if (World.Nodes[i].Remaining <= 0) continue;
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
            var point = gameCamera.WorldToScreenPoint(resources[index].position);
            return new Vector2(point.x, Screen.height - point.y) / HudScale;
        }

        /// <summary>指向资源时显示余量，区分可派遣和当前任务。</summary>
        private void DrawResourceHint(Vector2 screenPoint)
        {
            int index = PickResource(screenPoint);
            if (index < 0) return;
            var target = World.Nodes[index];
            string name = Game.Loc.T(target.Energy ? "ResourceMarkerEnergy" : "ResourceMarkerScrap");
            string title = Game.Loc.T("ResourceMarkerRemaining", name, target.Remaining);
            string hint = Game.Loc.T(index == World.ResourceOrder ? "StrategyResourceAssigned" : "ResourceMarkerGather");
            const float width = 250, height = 54;
            var cursor = new Vector2(screenPoint.x, Screen.height - screenPoint.y) / HudScale;
            float x = Mathf.Clamp(cursor.x - width * .5f, 0, Screen.width / HudScale - width);
            float y = Mathf.Max(0, cursor.y - height - 12);
            var oldColor = GUI.contentColor;
            var oldTint = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.20f, .29f, .28f, 1);
            GUI.contentColor = target.Energy ? new Color(.57f, 1, 1) : new Color(1, .82f, .52f);
            GUI.Box(new Rect(x, y, width, height), title + "\n" + hint);
            GUI.contentColor = oldColor;
            GUI.backgroundColor = oldTint;
        }

        private void DrawMap()
        {
            var old=GUI.color;GUI.Box(MapRect,Game.Loc.T("HudMap"));
            for (int i = 0; i < World.Nodes.Count; i++)
            {
                var node = World.Nodes[i];
                if (node.Remaining > 0) Dot(node.Position, node.Energy ? Color.cyan : new Color(.8f, .5f, .2f),
                    i == World.ResourceOrder ? 9 : 4);
            }
            foreach(var enemy in World.Enemies)Dot(enemy.Position,Color.red,5);
            foreach (var site in World.ExpansionSites)
                Dot(site.Position, site.Kind switch
                {
                    ExpansionKind.Extractor => new Color(.2f, 1, .65f),
                    ExpansionKind.Barracks => new Color(.32f, .58f, 1),
                    ExpansionKind.Barricade => new Color(.85f, .38f, .22f),
                    _ => Color.gray
                }, site.Kind == ExpansionKind.Empty ? 4 : 8);
            Dot(Vector2.zero, Color.white, 9);
            Dot(World.SquadPosition, new Color(.32f, .58f, 1), 7);
            Dot(World.WorkerPosition, Color.yellow, 7);
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
        private void OnDestroy()
        {
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            modal?.Detach(this);
            hud?.Detach(this);
            construction?.Detach(this);
            foreach(var material in materials)Release(material);
            if(font!=null)Release(font);
        }
    }
}
