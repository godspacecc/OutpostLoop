using System.Collections.Generic;
using UnityEngine;
using QFramework;

namespace OutpostLoop
{
    /// <summary>临时玩法界面从当前会话推导的显示状态。</summary>
    internal enum PrototypeOverlay { None, Start, Paused, Upgrade, Result }

    /// <summary>独立原型表现入口：向玩法系统提交输入、同步临时画面。无旧项目资源。</summary>
    public sealed class PrototypeController : MonoBehaviour
    {
        // 本局领域状态、临时表现根和预分配敌人池。
        public WorldState World { get; private set; }
        private Transform worldRoot, player, baseRoof;
        private Camera gameCamera;
        private LineRenderer shotLine;
        private readonly List<Transform> resources = new List<Transform>();
        private readonly List<Transform> enemies = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private bool started, shop;
        private int seed = 731;
        private Font font;
        private string feedback = "";
        private float feedbackUntil;
        // 主场景窗口随基础会话清理；临时截图工具仍可仅初始化世界。
        private PrototypeModalCtrl modal;
        private PrototypeUpgradeButtonCtrl upgradeEntry;
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
        // 失活时停止接受输入并暂停，避免隐藏视图仍带着上一帧输入推进。
        private void OnEnable(){if(initialized)Game.LifeCycle.RegisterUpdate(PollInputAndPresent);}
        private void OnDisable()
        {
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            modal?.Hide();
            upgradeEntry?.Hide();
            if(session!=null){session.SubmitInput(Vector2.zero,false);session.SetPaused(true);}
        }

        /// <summary>创建全新临时几何体预览；可供编辑器截图入口调用。</summary>
        public void Initialize()
        {
            if(worldRoot != null)return;
            var architecture=Game.Game.Interface;
            if(architecture.GetModel<PrototypeModel>()==null)architecture.RegisterModel(new PrototypeModel());
            if(architecture.GetSystem<PrototypeSystem>()==null)architecture.RegisterSystem(new PrototypeSystem());
            session=architecture.GetSystem<PrototypeSystem>();
            architecture.GetUtility<Game.HotKeyActionRegistry>().Register(new Game.HotKeyAction
            {
                ActionId = Game.HotKeyActionIds.TogglePause,
                Context = Game.HotKeyContext.Gameplay,
                Priority = 10,
                CanExecute = () => this != null && isActiveAndEnabled && initialized && started &&
                    World.Run.Outcome == 0 && !UtilityWindowOpen,
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
            gameCamera=cameraObject.AddComponent<Camera>(); gameCamera.orthographic=true; gameCamera.orthographicSize=17;
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
                session.SubmitInput(Vector2.zero,false);session.SetPaused(true);
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
            World=session.Begin(++seed,begin); started=begin;shop=false;startupError=null;
            worldRoot=new GameObject("TemporaryPreviewWorld").transform;worldRoot.SetParent(transform);
            var ground=Mat(groundColor);var stone=Mat(new Color(0.37f,0.40f,0.40f));
            var dark=Mat(new Color(0.11f,0.16f,0.17f));var amber=Mat(new Color(1,0.61f,0.13f));
            var cyan=Mat(new Color(0.15f,0.88f,0.84f));var rust=Mat(new Color(0.52f,0.24f,0.17f));
            var lineObject=new GameObject("TemporaryShotPreview");lineObject.transform.SetParent(worldRoot);
            shotLine=lineObject.AddComponent<LineRenderer>();shotLine.sharedMaterial=amber;shotLine.positionCount=2;
            shotLine.startWidth=0.1f;shotLine.endWidth=0.04f;shotLine.enabled=false;
            Shape("Ground",worldRoot,PrimitiveType.Cube,new Vector3(0,-0.3f,0),new Vector3(96,0.5f,96),ground);
            Shape("HomePad",worldRoot,PrimitiveType.Cylinder,new Vector3(0,0,0),new Vector3(9,0.1f,9),dark);
            Shape("Shelter",worldRoot,PrimitiveType.Cube,new Vector3(0,1.15f,0),new Vector3(3.6f,2.3f,3),stone);
            baseRoof=Shape("Roof",worldRoot,PrimitiveType.Cube,new Vector3(0,2.5f,0),new Vector3(4,0.5f,3.5f),dark);
            Shape("Door",worldRoot,PrimitiveType.Cube,new Vector3(0,0.8f,-1.55f),new Vector3(0.9f,1.6f,0.1f),amber);
            for(int i=-1;i<=1;i+=2)
            {
                var turret=Shape("Turret",worldRoot,PrimitiveType.Cylinder,new Vector3(i*4,0.5f,-1),new Vector3(1.5f,0.5f,1.5f),stone);
                Shape("Head",turret,PrimitiveType.Cube,new Vector3(0,1.3f,0),new Vector3(0.8f,0.7f,0.8f),dark);
                Shape("Barrel",turret,PrimitiveType.Cube,new Vector3(0,1.3f,-0.7f),new Vector3(0.2f,0.2f,1),amber);
            }
            player=new GameObject("Collector").transform; player.SetParent(worldRoot);
            Shape("Coat",player,PrimitiveType.Capsule,new Vector3(0,0.8f,0),new Vector3(0.65f,0.7f,0.65f),amber);
            Shape("Hood",player,PrimitiveType.Cube,new Vector3(0,1.6f,0),new Vector3(0.7f,0.65f,0.6f),amber);
            Shape("Face",player,PrimitiveType.Cube,new Vector3(0,1.6f,-0.32f),new Vector3(0.45f,0.35f,0.08f),dark);
            Shape("Pack",player,PrimitiveType.Cube,new Vector3(0,0.9f,0.4f),new Vector3(0.7f,0.8f,0.35f),cyan);
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
                session.SubmitInput(Vector2.zero, false);
                return;
            }
            if(Input.GetKeyDown(KeyCode.B)) ToggleUpgrade();
            if(started&&!World.Run.Paused&&World.Run.Outcome==0)
            {
                var move=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
                session.SubmitInput(move,Input.GetKey(KeyCode.E));
                if(move.sqrMagnitude>0.01f)player.rotation=Quaternion.LookRotation(new Vector3(-move.x,0,-move.y));
            }
            SyncView();
            RefreshOverlay();
        }

        /// <summary>暂停热键与界面操作共用同一个入口，重绑后由热键系统调用。</summary>
        private bool UtilityWindowOpen => Game.UIHelper.GetUICtrl<PrototypeSettingsCtrl>()?.IsShow() == true ||
            Game.UIHelper.GetUICtrl<PrototypeLogCtrl>()?.IsShow() == true;

        private void TogglePause()
        {
            if (shop || World.Run.Paused) ActOnModal();
            else session.SetPaused(true);
        }

        internal PrototypeOverlay Overlay => !started ? PrototypeOverlay.Start :
            World.Run.Outcome != 0 ? PrototypeOverlay.Result :
            shop ? PrototypeOverlay.Upgrade : World.Run.Paused ? PrototypeOverlay.Paused : PrototypeOverlay.None;

        internal string UpgradeFeedback => Time.realtimeSinceStartup < feedbackUntil ? feedback : "";

        /// <summary>键盘与临时按钮共用同一个基地入口，购买权限仍由玩法系统判断。</summary>
        internal void ToggleUpgrade()
        {
            if (UtilityWindowOpen) return;
            if (!initialized || !started || World.Run.Outcome != 0 || World.Player.magnitude >= 5) return;
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
                    started = true;
                    session.SetPaused(false);
                    break;
                case PrototypeOverlay.Paused:
                case PrototypeOverlay.Upgrade:
                    shop = false;
                    session.SetPaused(false);
                    break;
                case PrototypeOverlay.Result:
                    Restart(true);
                    break;
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

        /// <summary>仅在基础会话就绪时显示窗口，退出后不重新生成 UI。</summary>
        private void RefreshOverlay()
        {
            if (!Application.isPlaying || !Game.FoundationRuntime.IsInitialized) return;
            var mode = Overlay;
            if (mode != PrototypeOverlay.None)
            {
                if (modal?.IsShow() != true) modal = Game.UIHelper.ShowUI<PrototypeModalCtrl>();
                modal.Bind(this, font);
                modal.Refresh(this);
            }
            else modal?.Hide();

            if (mode == PrototypeOverlay.None && World.Player.magnitude < 5)
            {
                if (upgradeEntry?.IsShow() != true) upgradeEntry = Game.UIHelper.ShowUI<PrototypeUpgradeButtonCtrl>();
                upgradeEntry.Bind(this, font);
            }
            else upgradeEntry?.Hide();
        }

        /// <summary>表现仅映射已结算状态；敌人采用固定池，不按波次无限创建。</summary>
        public void SyncView()
        {
            player.position=new Vector3(World.Player.x,0,World.Player.y);
            for(int i=0;i<resources.Count;i++)resources[i].gameObject.SetActive(World.Nodes[i].Remaining>0);
            for(int i=0;i<enemies.Count;i++)
            {
                bool active=i<World.Enemies.Count;enemies[i].gameObject.SetActive(active);
                if(active){var p=World.Enemies[i].Position;enemies[i].position=new Vector3(p.x,0.8f,p.y);}
            }
            gameCamera.transform.position=player.position+new Vector3(0,32,-14.92f);
            gameCamera.transform.rotation=Quaternion.Euler(65,0,0);
            shotLine.enabled=World.Run.Elapsed-World.LastShotTime<0.12f;
            if(shotLine.enabled)
            {
                var target=World.LastShotTarget;
                shotLine.SetPosition(0,new Vector3(target.x<0?-4:4,1.4f,-1));
                shotLine.SetPosition(1,new Vector3(target.x,0.8f,target.y));
            }
        }

        private void OnGUI()
        {
            if(!initialized){GUI.Label(new Rect(20,20,1000,120),"启动失败，请先运行配置生成并查看Console："+startupError);return;}
            if (!started) return;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            GUI.skin.font=font;GUI.skin.label.fontSize=18;GUI.skin.button.fontSize=19;GUI.skin.box.fontSize=18;
            var s=World.Run;
            GUI.Box(new Rect(16,16,330,168),"");
            GUI.Label(new Rect(30,26,305,30),Game.Loc.T("HudTitle"));
            GUI.Label(new Rect(30,61,300,28),Game.Loc.T("HudTime", Mathf.CeilToInt(s.Config.Duration-s.Elapsed), World.Wave, s.Config.WaveTimes.Count));
            GUI.Label(new Rect(30,92,300,28),Game.Loc.T("HudHealth", s.Health, World.Kills));
            GUI.Label(new Rect(30,122,300,28),Game.Loc.T("HudStock", s.Scrap, s.Energy));
            GUI.Label(new Rect(30,151,300,28),Game.Loc.T("HudCargo", s.CargoScrap+s.CargoEnergy, s.Capacity));
            GUI.Box(new Rect(16,640,850,62),Game.Loc.T("HudControls"));
            DrawMap();
        }

        private void DrawMap()
        {
            var old=GUI.color;GUI.Box(new Rect(1050,18,210,235),Game.Loc.T("HudMap"));
            foreach(var node in World.Nodes)if(node.Remaining>0)Dot(node.Position,node.Energy?Color.cyan:new Color(0.8f,0.5f,0.2f),4);
            foreach(var enemy in World.Enemies)Dot(enemy.Position,Color.red,5);
            Dot(Vector2.zero,Color.white,9);Dot(World.Player,Color.yellow,7);GUI.color=old;
        }
        private void Dot(Vector2 p,Color color,float size)
        {GUI.color=color;GUI.DrawTexture(new Rect(1155+p.x*2-size/2,140-p.y*2-size/2,size,size),Texture2D.whiteTexture);GUI.color=Color.white;}
        private void OnDestroy()
        {
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            modal?.Detach(this);
            upgradeEntry?.Detach(this);
            foreach(var material in materials)Release(material);
            if(font!=null)Release(font);
        }
    }
}
