using System.Collections.Generic;
using UnityEngine;
using QFramework;

namespace OutpostLoop
{
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
        // 权威规则由架构系统持有，视图只转发输入及读取快照。
        private PrototypeSystem session;
        private string startupError;
        private readonly Color groundColor = new Color(0.16f,0.25f,0.24f);

        private void Awake() { Initialize(); }
        // 失活时停止接受输入并暂停，避免隐藏视图仍带着上一帧输入推进。
        private void OnEnable(){if(session!=null)Game.LifeCycle.RegisterUpdate(PollInputAndPresent);}
        private void OnDisable()
        {
            Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);
            if(session!=null){session.SubmitInput(Vector2.zero,false);session.SetPaused(true);}
        }

        /// <summary>创建全新临时几何体预览；可供编辑器截图入口调用。</summary>
        public void Initialize()
        {
            if(worldRoot != null)return;
            if(GetComponent<Game.FoundationLoop>()==null)gameObject.AddComponent<Game.FoundationLoop>();
            var architecture=Game.Game.Interface;
            if(architecture.GetModel<PrototypeModel>()==null)architecture.RegisterModel(new PrototypeModel());
            if(architecture.GetSystem<PrototypeSystem>()==null)architecture.RegisterSystem(new PrototypeSystem());
            session=architecture.GetSystem<PrototypeSystem>();
            if(isActiveAndEnabled)Game.LifeCycle.RegisterUpdate(PollInputAndPresent);
#if UNITY_EDITOR
            Game.UIHelper.SetPrefabLoader(path=>UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+path+".prefab"));
#endif
            font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",18);
            var cameraObject=new GameObject("PreviewCamera"); cameraObject.transform.SetParent(transform);
            gameCamera=cameraObject.AddComponent<Camera>(); gameCamera.orthographic=true; gameCamera.orthographicSize=17;
            gameCamera.backgroundColor=groundColor; gameCamera.clearFlags=CameraClearFlags.SolidColor;
            gameCamera.nearClipPlane=0.1f; gameCamera.farClipPlane=160;
            var lightObject=new GameObject("PreviewSun"); lightObject.transform.SetParent(transform);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f;
            light.transform.rotation=Quaternion.Euler(50,-35,0);
            RenderSettings.ambientLight=new Color(0.58f,0.63f,0.65f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            try{Restart(false);}catch(System.Exception e){startupError=e.Message;Game.Log.Error("独立原型启动失败："+e.Message);}
        }

        /// <summary>重开替换完整领域状态和表现，不保留上一局能力与敌人。</summary>
        public void Restart(bool begin)
        {
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
            feedback="";SyncView();
        }

        private Material Mat(Color color){var m=new Material(Shader.Find("Standard"));m.color=color;materials.Add(m);return m;}
        private Transform Shape(string label,Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=label;obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;
            Release(obj.GetComponent<Collider>());return obj.transform;
        }
        private void Release(Object obj){if(obj==null)return;if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}

        private void PollInputAndPresent()
        {
            if(World==null)return;
            if(Input.GetKeyDown(KeyCode.Escape)&&started&&World.Run.Outcome==0){shop=false;World.Run.Paused=!World.Run.Paused;}
            if(Input.GetKeyDown(KeyCode.B)&&started&&World.Run.Outcome==0&&World.Player.magnitude<5)
            {shop=!shop;World.Run.Paused=shop;}
            if(started&&!World.Run.Paused&&World.Run.Outcome==0)
            {
                var move=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));
                session.SubmitInput(move,Input.GetKey(KeyCode.E));
                if(move.sqrMagnitude>0.01f)player.rotation=Quaternion.LookRotation(new Vector3(-move.x,0,-move.y));
            }
            SyncView();
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
            if(World==null){GUI.Label(new Rect(20,20,1000,120),"启动失败，请先运行配置生成并查看Console："+startupError);return;}
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            GUI.skin.font=font;GUI.skin.label.fontSize=18;GUI.skin.button.fontSize=19;GUI.skin.box.fontSize=18;
            var s=World.Run;
            GUI.Box(new Rect(16,16,330,168),"");
            GUI.Label(new Rect(30,26,305,30),"OUTPOST LOOP · 独立可玩预览");
            GUI.Label(new Rect(30,61,300,28),"剩余 "+Mathf.CeilToInt(s.Config.Duration-s.Elapsed)+" 秒   波次 "+World.Wave+" / "+s.Config.WaveTimes.Count);
            GUI.Label(new Rect(30,92,300,28),"基地 "+s.Health+"   击退 "+World.Kills);
            GUI.Label(new Rect(30,122,300,28),"库存：废料 "+s.Scrap+"  能源 "+s.Energy);
            GUI.Label(new Rect(30,151,300,28),"背包 "+(s.CargoScrap+s.CargoEnergy)+" / "+s.Capacity);
            GUI.Box(new Rect(16,640,850,62),"WASD 移动 · 靠近资源按住 E 采集 · 回基地自动卸货\n基地附近 B 升级 · ESC 暂停 · 12 分钟守住最终进攻即获胜");
            DrawMap();
            if(started&&s.Outcome==0&&World.Player.magnitude<5&&!shop&&!s.Paused)
                if(GUI.Button(new Rect(900,640,330,52),"打开基地升级 [B]")){shop=true;s.Paused=true;}
            if(Time.realtimeSinceStartup<feedbackUntil)GUI.Label(new Rect(380,590,700,36),feedback);
            if(!started)
            {
                GUI.Box(new Rect(365,210,550,325),"探索 / 采集 / 升级 / 防守");
                GUI.Label(new Rect(400,265,490,135),"从中央基地出发，搜寻废料与能源。\n满载后回家卸货，升级采集能力或防线。\n炮塔自动战斗；提前准备比操作更重要。\n每局 12 分钟，最后一波在第 11 分钟。\n当前使用独立临时几何体，不是最终美术。");
                if(GUI.Button(new Rect(460,445,360,58),"开始这一局")){started=true;s.Paused=false;}
            }
            else if(s.Outcome!=0)
            {
                GUI.Box(new Rect(390,240,500,245),s.Outcome>0?"防守成功":"本局结束");
                GUI.Label(new Rect(425,300,450,60),"坚持 "+Mathf.FloorToInt(s.Elapsed)+" 秒，击退 "+World.Kills+" 个敌人\n换个资源布局，再试一条升级路线。");
                if(GUI.Button(new Rect(470,395,340,58),"新地图，再来一局"))Restart(true);
            }
            else if(shop)
            {
                GUI.Box(new Rect(350,210,580,350),"基地升级 · 选择期间暂停");
                string[] names={"采集 / 运输","炮塔火力 / 射程","基地耐久 / 修复"};
                for(int i=0;i<3;i++)
                {
                    var label=names[i]+"  "+s.Levels[i]+" / "+s.Config.MaxLevel;
                    label+=s.Levels[i]>=s.Config.MaxLevel?"  已满级":"   废料 "+s.ScrapCost(i)+" / 能源 "+s.EnergyCost(i);
                    if(GUI.Button(new Rect(375,270+i*65,530,53),label))
                    {
                        // 面板暂停世界，购买事务仍需在允许动作的状态下校验。
                        bool ok=session.Purchase(i);
                        feedback=ok?"升级完成":"资源不足或已满级";feedbackUntil=Time.realtimeSinceStartup+2;
                    }
                }
                if(GUI.Button(new Rect(505,485,270,48),"返回探索 [B]")){shop=false;s.Paused=false;}
            }
            else if(s.Paused)
            {
                GUI.Box(new Rect(450,270,380,180),"已暂停");
                if(GUI.Button(new Rect(490,345,300,60),"继续 [ESC]"))s.Paused=false;
            }
        }

        private void DrawMap()
        {
            var old=GUI.color;GUI.Box(new Rect(1050,18,210,235),"区域地图");
            foreach(var node in World.Nodes)if(node.Remaining>0)Dot(node.Position,node.Energy?Color.cyan:new Color(0.8f,0.5f,0.2f),4);
            foreach(var enemy in World.Enemies)Dot(enemy.Position,Color.red,5);
            Dot(Vector2.zero,Color.white,9);Dot(World.Player,Color.yellow,7);GUI.color=old;
        }
        private void Dot(Vector2 p,Color color,float size)
        {GUI.color=color;GUI.DrawTexture(new Rect(1155+p.x*2-size/2,140-p.y*2-size/2,size,size),Texture2D.whiteTexture);GUI.color=Color.white;}
        private void OnDestroy(){Game.LifeCycle.UnregisterUpdate(PollInputAndPresent);foreach(var material in materials)Release(material);if(font!=null)Release(font);}
    }
}
