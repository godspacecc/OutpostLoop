using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 临时探索入口拥有独立状态和表现根，退出后恢复原开始页。
        private Transform scavengeRoot, scavengePlayer, scavengeExit;
        private readonly Transform[] scavengeEnemies = new Transform[2];
        private readonly Transform[] scavengeCrates = new Transform[ScavengeLayout.Caches.Length];
        private readonly List<Material> scavengeMaterials = new List<Material>();
        private ScavengeRun scavengeRun;
        private ScavengeCollection scavengeCollection;
        private bool scavengePaused, scavengeSaved;
        // 仓储与工作台界面只在玩家仍靠近可用据点时打开。
        private bool storageOpen;
        // 放置输入只在探索中接管鼠标，预览与工地由同一份领域规则驱动。
        private bool placingShelter;
        private Transform shelterPreview, shelterWorksite, shelterModel;
        private Material shelterPreviewMaterial;
        private ShelterPlacement shelterPlacement;
        private bool placingDefense;
        private Transform defenseModel, defenseWorksite;
        private LineRenderer defenseRadius, defenseShot;
        private Color previousCameraColor;
        private static readonly ScavengeItem[] ScavengeItems =
        {
            ScavengeItem.Supplies, ScavengeItem.Crowbar, ScavengeItem.Signal, ScavengeItem.Materials,
            ScavengeItem.DefenseParts, ScavengeItem.RepairMaterials, ScavengeItem.RepairKit
        };

        /// <summary>从开始页进入独立的首趟探索。</summary>
        internal void StartScavenge()
        {
            if (!initialized || scavengeRoot != null || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return;
            scavengeCollection ??= new ScavengeCollection();
            CreateScavengeMap();
            previousCameraColor = gameCamera.backgroundColor;
            gameCamera.backgroundColor = new Color(.095f, .122f, .128f);
            gameCamera.orthographic = true;
            gameCamera.farClipPlane = 100;
            worldRoot.gameObject.SetActive(false);
            modal?.Hide();
            RestartScavenge();
        }

        /// <summary>保持已撤离的收藏，重新开始空手行动。</summary>
        private void RestartScavenge()
        {
            scavengeRun = new ScavengeRun();
            scavengePaused = false;
            scavengeSaved = false;
            storageOpen = false;
            placingShelter = false;
            placingDefense = false;
            scavengeRoot.gameObject.SetActive(true);
            SyncScavenge();
        }

        private void LeaveScavenge()
        {
            Release(scavengeRoot.gameObject);
            scavengeRoot.gameObject.SetActive(false);
            scavengeRoot = null;
            ReleaseScavengeMaterials();
            gameCamera.orthographic = false;
            gameCamera.backgroundColor = previousCameraColor;
            worldRoot.gameObject.SetActive(true);
            UpdateBattleCamera();
            RefreshOverlay();
        }

        /// <summary>把键鼠输入交给领域状态，UI 区域点击优先处理拾取。</summary>
        private void PollScavenge()
        {
            if (UtilityWindowOpen) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring)
            {
                if (keyboard?.enterKey.wasPressedThisFrame == true && CanDepartScavenge) RestartScavenge();
                return;
            }
            bool escape = Input.GetKeyDown(KeyCode.Escape) || keyboard?.escapeKey.wasPressedThisFrame == true;
            if (escape && storageOpen) storageOpen = false;
            else if (escape && !placingShelter) scavengePaused = !scavengePaused;
            if (scavengePaused) return;

            var movement = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            scavengeRun.Tick(Mathf.Min(Time.unscaledDeltaTime, .05f), movement);
            if (!scavengeRun.CanUseStorage) storageOpen = false;
            if (!placingShelter && (Input.GetKeyDown(KeyCode.B) || keyboard?.bKey.wasPressedThisFrame == true))
                ToggleScavengeStorage();
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring) placingShelter = false;
            else if (!PollShelterPlacement(keyboard, mouse, escape))
            {
                if (!storageOpen && (Input.GetKeyDown(KeyCode.E) || keyboard?.eKey.wasPressedThisFrame == true)) InteractScavenge();
                if (!storageOpen && mouse?.leftButton.wasPressedThisFrame == true &&
                !ScavengeHudContains(mouse.position.ReadValue()) &&
                TryProjectBattleGround(mouse.position.ReadValue(), out var aim))
                    scavengeRun.Attack(aim - scavengeRun.Player);
            }
            SyncScavenge();
        }

        /// <summary>进入、取消或确认建造时吞掉对应的战场输入。</summary>
        private bool PollShelterPlacement(Keyboard keyboard, Mouse mouse, bool escape)
        {
            bool toggle = Input.GetKeyDown(KeyCode.V) || keyboard?.vKey.wasPressedThisFrame == true;
            if (toggle)
            {
                placingShelter = !placingShelter;
                if (placingShelter)
                {
                    scavengeRun.CloseSearch();
                    storageOpen = false;
                    placingDefense = scavengeRun.Shelter.HasValue;
                }
            }
            if (!placingShelter) return toggle;
            if (escape || mouse?.rightButton.wasPressedThisFrame == true)
            {
                placingShelter = false;
                return true;
            }
            if (keyboard?.digit1Key.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Alpha1)) placingDefense = false;
            if (keyboard?.digit2Key.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Alpha2)) placingDefense = true;
            var cursor = mouse?.position.ReadValue() ?? (Vector2)Input.mousePosition;
            if (!gameCamera.pixelRect.Contains(cursor) || ScavengeHudContains(cursor) ||
                !TryProjectBattleGround(cursor, out var point))
            {
                shelterPreview.SetPositionAndRotation(At(scavengeRun.Player, -.5f), Quaternion.identity);
                shelterPlacement = ShelterPlacement.Obstructed;
                return true;
            }
            shelterPlacement = placingDefense ? scavengeRun.CheckDefense(point) : scavengeRun.CheckShelter(point);
            shelterPreview.position = At(point, .055f);
            float size = placingDefense ? ScavengeRun.DefenseWidth : ScavengeRun.ShelterWidth;
            shelterPreview.localScale = new Vector3(size, .06f, size);
            shelterPreviewMaterial.color = shelterPlacement == ShelterPlacement.Available ?
                new Color(.29f, .82f, .57f) : new Color(.76f, .32f, .22f);
            if (!toggle && mouse?.leftButton.wasPressedThisFrame == true)
            {
                bool built = placingDefense ? scavengeRun.BuildDefense(point) : scavengeRun.BuildShelter(point);
                if (built) placingShelter = false;
            }
            return true;
        }

        /// <summary>键盘和画面按钮共用搜索及撤离入口。</summary>
        private void InteractScavenge()
        {
            if (scavengeRun.Interact() && scavengeRun.Outcome == ScavengeOutcome.Extracted)
                scavengeSaved = scavengeCollection.SaveReturn(scavengeRun);
        }

        /// <summary>仓储入口与搜索面板互斥，离开据点时自动关闭。</summary>
        private void ToggleScavengeStorage()
        {
            storageOpen = !storageOpen && scavengeRun.CanUseStorage;
            if (storageOpen) scavengeRun.CloseSearch();
        }

        /// <summary>画面只读取角色、敌情、探索物和撤离状态。</summary>
        private void SyncScavenge()
        {
            scavengePlayer.position = At(scavengeRun.Player, .76f);
            for (int i = 0; i < scavengeEnemies.Length; i++)
            {
                var patrol = scavengeRun.Patrols[i];
                var model = scavengeEnemies[i];
                model.gameObject.SetActive(patrol.Alive);
                model.position = At(patrol.Position, .7f);
                if (patrol.Facing != Vector2.zero) model.rotation = Quaternion.LookRotation(At(patrol.Facing, 0));
            }
            for (int i = 0; i < scavengeCrates.Length; i++)
                scavengeCrates[i].localScale = new Vector3(1.3f,
                    scavengeRun.IsSearched(i) && scavengeRun.VisibleContents(i) == ScavengeItem.None ? .33f : .66f, .82f);
            shelterPreview.gameObject.SetActive(placingShelter);
            bool building = scavengeRun.ShelterRemaining > 0;
            shelterWorksite.gameObject.SetActive(scavengeRun.Shelter.HasValue && building);
            shelterModel.gameObject.SetActive(scavengeRun.Shelter.HasValue && !building);
            if (scavengeRun.Shelter is Vector2 shelter)
            {
                shelterWorksite.position = At(shelter, .12f);
                shelterModel.position = At(shelter, 0);
            }
            SyncDefenseModels();
            var focus = new Vector2(scavengeRun.Player.x * .55f, scavengeRun.Player.y * .72f + 3.8f);
            gameCamera.orthographicSize = Mathf.Max(10.5f, 15f / gameCamera.aspect);
            var rotation = Quaternion.Euler(62, 0, 0);
            gameCamera.transform.SetPositionAndRotation(At(focus, 0) - rotation * Vector3.forward * 27f, rotation);
        }

        /// <summary>用临时几何体表达开阔路、仓门、矮墙和侧门。</summary>
        private void CreateScavengeMap()
        {
            scavengeRoot = new GameObject("ScavengeTrial").transform;
            scavengeRoot.SetParent(transform, false);
            var soil = ScavengeMaterial(new Color(.22f, .27f, .27f));
            var road = ScavengeMaterial(new Color(.31f, .37f, .37f));
            var concrete = ScavengeMaterial(new Color(.50f, .53f, .49f));
            var steel = ScavengeMaterial(new Color(.22f, .34f, .38f));
            var gold = ScavengeMaterial(new Color(.86f, .60f, .27f));
            var teal = ScavengeMaterial(new Color(.29f, .80f, .74f));
            var rust = ScavengeMaterial(new Color(.66f, .27f, .21f));
            var canvas = ScavengeMaterial(new Color(.91f, .87f, .67f));
            var pad = ScavengeMaterial(new Color(.31f, .42f, .36f));

            var outskirts = ScavengeMaterial(new Color(.17f, .21f, .21f));
            ScavengeBox("外围地面", new Vector3(0, -.26f, 0), new Vector3(80, .25f, 80), outskirts);
            ScavengeBox("地面", new Vector3(0, -.12f, .5f), new Vector3(24, .22f, 21), soil);
            ScavengeBox("开阔路", new Vector3(-6.9f, -.01f, .4f), new Vector3(4.6f, .045f, 18), road);
            ScavengeBox("仓库地坪", new Vector3(5.4f, 0, 1), new Vector3(9.2f, .05f, 12), steel);
            foreach (var ground in ScavengeLayout.ShelterGrounds)
                ScavengeBox("可建空地", At(ground.center, .012f), new Vector3(ground.width, .025f, ground.height), pad);
            for (int i = 0; i < 9; i++)
                ScavengeBox("道路标线", new Vector3(-7, .026f, i * 1.8f - 7), new Vector3(.13f, .026f, .8f), concrete);
            foreach (var wall in ScavengeLayout.Walls)
            {
                ScavengeBox("矮墙", At(wall.center, .67f), new Vector3(wall.width, 1.34f, wall.height), concrete);
                ScavengeBox("压顶", At(wall.center, 1.36f), new Vector3(wall.width + .08f, .1f, wall.height + .08f), steel);
            }
            ScavengeBox("仓门", new Vector3(.84f, .06f, -3.1f), new Vector3(.85f, .07f, 2.65f), gold);
            ScavengeBox("侧门", new Vector3(5.35f, .06f, 7), new Vector3(2.45f, .07f, .8f), gold);
            for (int i = 0; i < scavengeCrates.Length; i++)
                scavengeCrates[i] = ScavengeBox("可搜箱 " + i, At(ScavengeLayout.Caches[i], .36f), Vector3.one, gold);

            scavengeExit = ScavengeBox("路灯", At(ScavengeLayout.Exit, 1.15f), new Vector3(.16f, 2.3f, .16f), steel);
            ScavengeBox("灯箱", At(ScavengeLayout.Exit, 2.33f), new Vector3(.8f, .25f, .56f), teal);
            ScavengeBox("裂口底座", At(ScavengeLayout.Exit, .02f), new Vector3(2.4f, .04f, 2.4f), teal);
            scavengePlayer = Shape("探索角色", scavengeRoot, PrimitiveType.Capsule, Vector3.zero, new Vector3(.65f, .78f, .65f), canvas);
            for (int i = 0; i < scavengeEnemies.Length; i++)
            {
                scavengeEnemies[i] = Shape(i == 0 ? "巡逻敌人" : "路边巡逻敌人", scavengeRoot, PrimitiveType.Capsule,
                    Vector3.zero, new Vector3(.75f, .72f, .75f), rust);
                Shape("敌人朝向", scavengeEnemies[i], PrimitiveType.Cube, new Vector3(0, .55f, .43f),
                    new Vector3(.65f, .15f, .45f), gold);
            }
            CreateShelterModels(steel, gold, canvas);
            CreateDefenseModels(steel, gold);
        }

        /// <summary>建立装置、射程预览与自动射击反馈。</summary>
        private void CreateDefenseModels(Material steel, Material gold)
        {
            defenseWorksite = ScavengeBox("防御装置工地", Vector3.zero,
                new Vector3(ScavengeRun.DefenseWidth, .24f, ScavengeRun.DefenseWidth), gold);
            defenseModel = new GameObject("自动防御装置").transform;
            defenseModel.SetParent(scavengeRoot, false);
            Shape("装置基座", defenseModel, PrimitiveType.Cylinder, new Vector3(0, .12f, 0),
                new Vector3(.66f, .12f, .66f), steel);
            Shape("装置支架", defenseModel, PrimitiveType.Cube, new Vector3(0, .37f, 0),
                new Vector3(.18f, .49f, .18f), steel);
            Shape("装置发射器", defenseModel, PrimitiveType.Cube, new Vector3(0, .66f, .14f),
                new Vector3(.35f, .23f, .71f), gold);
            var lineMaterial = new Material(Shader.Find("Sprites/Default"));
            scavengeMaterials.Add(lineMaterial);
            defenseRadius = CreateDefenseLine("防御射程预览", lineMaterial, 48, true, new Color(.50f, .83f, .69f, .8f));
            defenseShot = CreateDefenseLine("防御射击", lineMaterial, 2, false, new Color(1, .80f, .35f));
        }

        private LineRenderer CreateDefenseLine(string name, Material material, int points, bool loop, Color color)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(scavengeRoot, false);
            line.sharedMaterial = material;
            line.positionCount = points;
            line.loop = loop;
            line.startWidth = line.endWidth = .036f;
            line.startColor = line.endColor = color;
            return line;
        }

        private void SyncDefenseModels()
        {
            bool present = scavengeRun.Defense.HasValue;
            bool building = scavengeRun.DefenseRemaining > 0;
            defenseWorksite.gameObject.SetActive(present && building);
            defenseModel.gameObject.SetActive(present && !building);
            if (scavengeRun.Defense is Vector2 site)
            {
                defenseWorksite.position = At(site, .12f);
                defenseModel.position = At(site, 0);
                if (scavengeRun.DefenseShotTarget is Vector2 target)
                {
                    var direction = target - site;
                    if (direction.sqrMagnitude > .001f) defenseModel.rotation = Quaternion.LookRotation(At(direction, 0));
                    defenseShot.SetPosition(0, At(site, .68f));
                    defenseShot.SetPosition(1, At(target, .60f));
                }
            }
            defenseShot.enabled = present && scavengeRun.DefenseShotRemaining > 0;
            defenseRadius.enabled = placingShelter && placingDefense && shelterPreview.position.y > 0;
            if (!defenseRadius.enabled) return;
            for (int i = 0; i < defenseRadius.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / defenseRadius.positionCount;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ScavengeRun.DefenseRange;
                defenseRadius.SetPosition(i, shelterPreview.position + offset);
            }
        }

        /// <summary>表现依次展示占地、工地和有两侧开放入口的休整棚。</summary>
        private void CreateShelterModels(Material steel, Material gold, Material canvas)
        {
            float width = ScavengeRun.ShelterWidth;
            shelterPreviewMaterial = ScavengeMaterial(Color.green);
            shelterPreview = ScavengeBox("避难所落位预览", Vector3.zero, new Vector3(width, .06f, width), shelterPreviewMaterial);
            shelterWorksite = ScavengeBox("避难所工地", Vector3.zero, new Vector3(width, .23f, width), gold);
            shelterModel = new GameObject("避难所").transform;
            shelterModel.SetParent(scavengeRoot, false);
            Shape("防潮地垫", shelterModel, PrimitiveType.Cube, new Vector3(0, .036f, 0),
                new Vector3(width, .07f, width), steel);
            Shape("篷顶", shelterModel, PrimitiveType.Cube, new Vector3(0, 1.34f, .16f),
                new Vector3(width, .14f, 1.1f), canvas);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Shape("支架", shelterModel, PrimitiveType.Cube, new Vector3(x * .72f, .68f, z * .44f + .16f),
                        new Vector3(.08f, 1.36f, .08f), steel);
        }

        private Transform ScavengeBox(string name, Vector3 point, Vector3 size, Material material) =>
            Shape(name, scavengeRoot, PrimitiveType.Cube, point, size, material);

        private Material ScavengeMaterial(Color color)
        {
            var material = new Material(previewShader) { color = color };
            scavengeMaterials.Add(material);
            return material;
        }

        private void ReleaseScavengeMaterials()
        {
            foreach (var material in scavengeMaterials) Release(material);
            scavengeMaterials.Clear();
        }

        private bool CanDepartScavenge => scavengeRun.Outcome == ScavengeOutcome.Died || scavengeSaved;
        private static Rect ScavengeInfoRect => new Rect(20, 18, 338, 128);
        private static Rect ScavengeMapRect(float width) => new Rect(width - 172, 18, 152, 136);
        private static Rect ScavengeActionRect(float width, float height) => new Rect(width / 2 - 208, height - 65, 416, 38);
        private static Rect ScavengeBuildRect(float width) => new Rect(width / 2 - 196, 18, 392, 45);
        private static Rect ScavengeStorageButton(float width) => new Rect(width - 207, 164, 187, 37);
        private static Rect ScavengeStorageRect => new Rect(20, 156, 512, 318);

        private bool ScavengeHudContains(Vector2 mouse)
        {
            float scale = HudScale;
            var point = new Vector2(mouse.x, Screen.height - mouse.y) / scale;
            return ScavengeInfoRect.Contains(point) || ScavengeMapRect(Screen.width / scale).Contains(point) ||
                ScavengeActionRect(Screen.width / scale, Screen.height / scale).Contains(point) ||
                (placingShelter && ScavengeBuildRect(Screen.width / scale).Contains(point)) ||
                (scavengeRun.CanUseStorage && ScavengeStorageButton(Screen.width / scale).Contains(point));
        }

        /// <summary>绘制临时探索界面，保留视野中心供玩家观察道路。</summary>
        private void DrawScavenge()
        {
            float scale = HudScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.skin.font = font;
            GUI.skin.label.fontSize = 18;
            GUI.skin.button.fontSize = 18;
            var run = scavengeRun;
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            if (run.Outcome != ScavengeOutcome.Exploring || scavengePaused)
            {
                DrawScavengeSpace(width, height);
                return;
            }

            GUI.Box(ScavengeInfoRect, "");
            GUI.Label(new Rect(35, 30, 300, 28), $"生命 {run.Health}/3    背包 {run.Count}/{ScavengeRun.Capacity}");
            GUI.Label(new Rect(35, 63, 310, 28), DescribeItems(run.Inventory));
            GUI.Label(new Rect(35, 99, 315, 28), "WASD 移动   E 交互   V 建造   Esc 暂停");
            if (placingShelter)
            {
                var menu = ScavengeBuildRect(width);
                GUI.Box(menu, "");
                if (GUI.Button(new Rect(menu.x + 8, menu.y + 6, 176, 33), "1  避难所")) placingDefense = false;
                if (GUI.Button(new Rect(menu.x + 193, menu.y + 6, 191, 33), "2  自动防御装置")) placingDefense = true;
                var action = ScavengeActionRect(width, height);
                GUI.Box(action, "");
                GUI.Label(new Rect(action.x + 12, action.y + 3, action.width - 24, 31),
                    shelterPlacement switch
                    {
                        ShelterPlacement.Available => placingDefense ? "左键安装 · 预览圈内可射击，墙会遮挡" : "左键搭建避难所 · 右键或 Esc 取消",
                        ShelterPlacement.MissingMaterials => placingDefense ? "需要防守零件 · 搜索仓门外工具箱" : "需要建材 · 可以在路边箱里找",
                        ShelterPlacement.AlreadyPlaced => "这一趟已经放置过",
                        ShelterPlacement.TooFar => placingDefense ? "在避难所附近的空地安装" : "靠近空地再建造",
                        ShelterPlacement.NeedsShelter => "先建避难所，再安装防御装置",
                        _ => "把整块占地放在空地内 · 避开自己"
                    });
            }
            else if (storageOpen) DrawScavengeStorage();
            else if (run.OpenCache >= 0) DrawScavengeCache();
            else
            {
                var action = ScavengeActionRect(width, height);
                if (run.AtExit || run.NearbyCache >= 0)
                {
                    if (GUI.Button(action, run.AtExit ? "撤离  E" : "搜索箱子  E")) InteractScavenge();
                }
                else if (run.NearShelter)
                {
                    string rest = run.ShelterRemaining > 0 ? $"避难所施工中 · {run.ShelterRemaining:0.0} 秒" :
                        run.Rested ? "这趟已经休整过" : run.Health >= 3 ? "生命已满 · 可留待受伤后休整" : "休整  E · 恢复一格生命";
                    if (run.ShelterRemaining <= 0 && !run.Rested && run.Health < 3)
                    {
                        if (GUI.Button(action, rest)) InteractScavenge();
                    }
                    else GUI.Box(action, rest);
                }
                else
                {
                    GUI.Box(action, "");
                    GUI.Label(new Rect(action.x + 15, action.y + 3, action.width - 30, 31),
                        run.Alerted ? "被发现了 · 绕到遮挡后拉开距离" : "找点能带走的东西，或者去灯下看看");
                }
            }
            if (!placingShelter && run.CanUseStorage &&
                GUI.Button(ScavengeStorageButton(width), storageOpen ? "关闭据点  B" : "仓储与工作台  B"))
                ToggleScavengeStorage();
            if ((run.Inventory & ScavengeItem.Crowbar) != 0)
                GUI.Label(new Rect(35, height - 51, 310, 29), "鼠标指向 · 左键挥动撬棍");
            if (!placingShelter)
            {
                string notice = run.ShelterLost ? "避难所失守 · 仍可撤离" :
                    run.ShelterHit ? $"避难所受击 · {run.ShelterHealth}/{ScavengeRun.ShelterMaxHealth}" :
                    run.Alerted ? "巡逻者发现了目标，正在接近" :
                    run.ConstructionInvestigated ? "巡逻者听到了施工声，正在查看" : "";
                if (notice.Length > 0) GUI.Box(new Rect(width / 2 - 150, 23, 300, 32), notice);
            }
            if (run.ExitKnown) DrawScavengeExitLabel();
            DrawScavengeMap(width);
        }

        /// <summary>以地标和已发现出口提供方向，敌人的位置由玩家现场观察。</summary>
        private void DrawScavengeMap(float width)
        {
            var panel = ScavengeMapRect(width);
            GUI.Box(panel, "");
            var map = new Rect(panel.x + 9, panel.y + 8, 134, 118);
            foreach (var wall in ScavengeLayout.Walls)
            {
                var topLeft = ScavengeMapPoint(map, new Vector2(wall.xMin, wall.yMax));
                ScavengeFill(new Rect(topLeft, new Vector2(wall.width * map.width / 24,
                    wall.height * map.height / 21)), new Color(.48f, .55f, .54f));
            }
            if (scavengeRun.ExitKnown)
                ScavengeFill(new Rect(ScavengeMapPoint(map, ScavengeLayout.Exit) - Vector2.one * 4, Vector2.one * 8),
                    new Color(.29f, .80f, .74f));
            if (scavengeRun.Shelter is Vector2 shelter)
                ScavengeFill(new Rect(ScavengeMapPoint(map, shelter) - Vector2.one * 3, Vector2.one * 6),
                    new Color(.66f, .88f, .52f));
            ScavengeFill(new Rect(ScavengeMapPoint(map, scavengeRun.Player) - Vector2.one * 3, Vector2.one * 6),
                new Color(1, .94f, .7f));
        }

        private static Vector2 ScavengeMapPoint(Rect map, Vector2 position) =>
            new Vector2(map.x + (position.x + 12) * map.width / 24, map.y + (11 - position.y) * map.height / 21);

        private static void ScavengeFill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawScavengeCache()
        {
            var run = scavengeRun;
            GUI.Box(new Rect(20, 154, 338, 350), "");
            GUI.Label(new Rect(35, 163, 315, 29), run.SearchRemaining > 0 ? "正在搜索…" : "箱子 · E 关闭");
            if (run.SearchRemaining > 0) return;
            float y = 199;
            foreach (var item in ScavengeItems)
            {
                bool inBox = (run.VisibleContents(run.OpenCache) & item) != 0;
                bool carried = (run.Inventory & item) != 0;
                if (!inBox && !carried) continue;
                bool enabled = GUI.enabled;
                GUI.enabled = !inBox || run.Count < ScavengeRun.Capacity;
                string action = inBox ? GUI.enabled ? "拿取" : "背包已满" : "放回箱中";
                if (GUI.Button(new Rect(35, y, 308, 36), action + " · " + ScavengeItemName(item)))
                    run.Transfer(item, inBox);
                GUI.enabled = enabled;
                y += 42;
            }
            if (y == 199) GUI.Label(new Rect(35, y, 290, 28), "箱子空了");
        }

        /// <summary>左侧为随身背包，右侧为仓库，底部操作工作台与据点维修。</summary>
        private void DrawScavengeStorage()
        {
            var run = scavengeRun;
            var panel = ScavengeStorageRect;
            GUI.Box(panel, "");
            GUI.Label(new Rect(panel.x + 14, panel.y + 10, 230, 28), $"背包 {run.Count}/{ScavengeRun.Capacity}");
            GUI.Label(new Rect(panel.x + 260, panel.y + 10, 238, 28), $"仓库 {run.StorageUsed}/{ScavengeRun.StorageCapacity}");
            DrawStorageColumn(run.Inventory, false, panel.x + 14, panel.y + 46);
            DrawStorageColumn(run.Stored, true, panel.x + 260, panel.y + 46);
            if (run.WorkshopRemaining > 0)
                GUI.Label(new Rect(panel.x + 260, panel.y + 169, 238, 28), "已预留一格给维修件");
            GUI.Label(new Rect(panel.x + 14, panel.y + 205, 484, 29),
                $"避难所耐久 {run.ShelterHealth}/{ScavengeRun.ShelterMaxHealth}");
            var recipe = new Rect(panel.x + 14, panel.y + 246, 233, 46);
            if (run.WorkshopRemaining > 0) GUI.Box(recipe, $"加工中 · {run.WorkshopRemaining:0.0} 秒");
            else if (run.CanStartWorkshop)
            {
                if (GUI.Button(recipe, "加工维修件")) run.StartWorkshop();
            }
            else GUI.Box(recipe, run.WorkshopUsed ? "这趟加工已完成" : "加工需要仓内有维修材料");
            var repair = new Rect(panel.x + 260, panel.y + 246, 238, 46);
            if (run.CanRepairShelter)
            {
                if (GUI.Button(repair, "使用维修件 · 恢复两格")) run.RepairShelter();
            }
            else GUI.Box(repair, !run.HasRepairKit ? "修理需要维修件" : "耐久已满");
        }

        private void DrawStorageColumn(ScavengeItem items, bool take, float x, float y)
        {
            foreach (var item in ScavengeItems)
            {
                if ((items & item) == 0) continue;
                bool enabled = GUI.enabled;
                GUI.enabled = take ? scavengeRun.Count < ScavengeRun.Capacity :
                    scavengeRun.StorageUsed < ScavengeRun.StorageCapacity;
                string action = GUI.enabled ? take ? "取出" : "存入" : take ? "背包满" : "仓库满";
                if (GUI.Button(new Rect(x, y, 233, 33), action + " · " + ScavengeItemName(item)))
                    scavengeRun.TransferStorage(item, take);
                GUI.enabled = enabled;
                y += 39;
            }
            if (items == ScavengeItem.None) GUI.Label(new Rect(x, y, 233, 28), "空");
        }

        private void DrawScavengeExitLabel()
        {
            var point = gameCamera.WorldToScreenPoint(scavengeExit.position + Vector3.up * 1.8f);
            float scale = HudScale;
            float x = Mathf.Clamp(point.x / scale - 34, 366, Screen.width / scale - 80);
            float y = Mathf.Clamp((Screen.height - point.y) / scale - 16, 14, Screen.height / scale - 104);
            GUI.Box(new Rect(x, y, 72, 27), "撤离点");
        }

        private void DrawScavengeSpace(float width, float height)
        {
            ScavengeFill(new Rect(0, 0, width, height), scavengePaused ?
                new Color(.04f, .06f, .07f, .62f) : new Color(.065f, .095f, .105f));
            float x = (width - 472) / 2, y = (height - 310) / 2;
            GUI.Box(new Rect(x, y, 472, 310), "");
            if (scavengePaused)
            {
                GUI.Label(new Rect(x + 26, y + 25, 419, 36), "探索已暂停");
                if (GUI.Button(new Rect(x + 26, y + 72, 420, 42), "继续探索")) scavengePaused = false;
                if (GUI.Button(new Rect(x + 26, y + 131, 420, 42), "结束本趟并返回开始页")) LeaveScavenge();
                return;
            }

            bool died = scavengeRun.Outcome == ScavengeOutcome.Died;
            GUI.Label(new Rect(x + 26, y + 23, 420, 34), died ? "你在陌生的空间醒来" : "已撤离 · 空间");
            GUI.Label(new Rect(x + 26, y + 67, 420, 29), died ? "阵亡 · 本趟物品全部失去" :
                "带回 · " + DescribeItems(scavengeRun.CarriedOut));
            bool wrap = GUI.skin.label.wordWrap;
            GUI.skin.label.wordWrap = true;
            GUI.Label(new Rect(x + 26, y + 106, 420, 51), "已登记 · " + DescribeItems(scavengeCollection.Items));
            GUI.skin.label.wordWrap = wrap;
            if ((scavengeCollection.Items & ScavengeItem.Signal) != 0)
                GUI.Label(new Rect(x + 26, y + 154, 420, 29), "线索 · 物件上的刻痕与裂口边缘一致");
            if (!CanDepartScavenge)
            {
                if (GUI.Button(new Rect(x + 26, y + 189, 420, 42), "登记失败 · 重试保存"))
                    scavengeSaved = scavengeCollection.SaveReturn(scavengeRun);
                return;
            }
            if (GUI.Button(new Rect(x + 26, y + 189, 420, 42), "再次出发  Enter")) RestartScavenge();
            if (GUI.Button(new Rect(x + 26, y + 248, 420, 38), "回到开始页")) LeaveScavenge();
        }

        private static string DescribeItems(ScavengeItem items)
        {
            string result = "";
            foreach (var item in ScavengeItems)
                if ((items & item) != 0)
                    result += (result.Length == 0 ? "" : "、") + ScavengeItemName(item);
            return result.Length == 0 ? "空" : result;
        }

        private static string ScavengeItemName(ScavengeItem item) => item switch
        {
            ScavengeItem.Supplies => "补给包",
            ScavengeItem.Crowbar => "撬棍",
            ScavengeItem.Signal => "异常物件",
            ScavengeItem.Materials => "建材",
            ScavengeItem.DefenseParts => "防守零件",
            ScavengeItem.RepairMaterials => "维修材料",
            ScavengeItem.RepairKit => "维修件",
            _ => ""
        };
    }
}
