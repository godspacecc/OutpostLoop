using System;
using System.Collections.Generic;
using System.IO;
using Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 保存入口只处理三天试玩档案；现有探索和守城档案各自独立。
        private const string SurvivalSaveName = "town-survival-v1.json";
        private JsonFileStore<SurvivalSave> survivalStore;
        private SurvivalSave survivalResume;
        private bool survivalMode, survivalSaveChecked, survivalHasSave, survivalConfirmNew;
        private bool survivalPackOpen, survivalBuildOpen;
        private SurvivalContainer survivalContainer;
        private string survivalSaveError;
        private readonly Transform[] survivalDoors = new Transform[2];
        private readonly Transform[] survivalDoorBraces = new Transform[2];
        private readonly AudioSource[] survivalAlarms = new AudioSource[2];
        private readonly AudioSource[] survivalImpacts = new AudioSource[2];
        private readonly int[] survivalDoorDurability = new int[2];
        private readonly List<Transform> survivalDropMarkers = new List<Transform>();
        private Material survivalDropMaterial;
        private AudioClip survivalAlarmClip, survivalDoorImpact;
        private Light survivalSun, survivalTorch;
        private float survivalSunBefore;
        private Color survivalAmbientBefore;
        private Vector2 survivalScroll;

        internal bool HasSurvivalSave
        {
            get
            {
                if (!survivalSaveChecked)
                {
                    survivalSaveChecked = true;
                    survivalHasSave = ReadSurvivalSave() != null;
                }
                return survivalHasSave;
            }
        }

        private JsonFileStore<SurvivalSave> SurvivalStore =>
            survivalStore ??= new JsonFileStore<SurvivalSave>();

        private SurvivalSave ReadSurvivalSave()
        {
            var read = SurvivalStore.Read(SurvivalSaveName);
            if (read.Status != SaveFileStatus.Valid || !read.Data.available) return null;
            var save = read.Data;
            return save.run != null && save.run.elapsed >= 0 && save.run.elapsed < ScavengeSurvival.Deadline &&
                save.patrols?.Length == ScavengeTown.PatrolCount && save.run.rooms?.Length == SurvivalRooms.BuildingIds.Length &&
                save.run.caches?.Length == ScavengeTown.Caches.Length ? save : null;
        }

        internal void StartSurvival() => EnterSurvival(false);
        internal void ContinueSurvival() => EnterSurvival(true);

        /// <summary>继续档与新行动从相同的地图和角色入口进入。</summary>
        private void EnterSurvival(bool resume)
        {
            if (!initialized || scavengeRoot != null || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) return;
            survivalResume = resume ? ReadSurvivalSave() : null;
            if (resume && survivalResume == null)
            {
                survivalHasSave = false;
                RefreshOverlay();
                return;
            }
            survivalConfirmNew = !resume && HasSurvivalSave;
            survivalMode = true;
            StartScavenge(true);
        }

        private bool WriteSurvivalSave(bool available)
        {
            try
            {
                // 结束标记与续玩档使用相同的原子替换，避免读取已结算的旧行动。
                SurvivalStore.Save(SurvivalSaveName, available ? scavengeRun.CaptureSurvival() : new SurvivalSave());
                survivalHasSave = available;
                survivalSaveChecked = true;
                survivalSaveError = null;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException)
            {
                survivalSaveError = available ? "保存失败，仍在本趟行动中。可以重试。" : "旧续玩档未能关闭，请重试。";
                return false;
            }
        }

        private void SaveAndLeaveSurvival()
        {
            if (WriteSurvivalSave(true)) LeaveScavenge();
        }

        private bool CloseSurvivalSave() => !HasSurvivalSave || WriteSurvivalSave(false);

        private void ResetSurvivalPresentation()
        {
            ResetSurvivalJourney();
            survivalContainer = null;
            survivalPackOpen = survivalBuildOpen = false;
            survivalScroll = Vector2.zero;
            survivalSaveError = null;
            for (int i = 0; i < survivalDoorDurability.Length; i++)
                survivalDoorDurability[i] = scavengeRun.Survival.rooms[i].durability;
        }

        private void CreateSurvivalRoom(int room, Material walls, Material floor, Material wood, Material roof)
        {
            var rect = SurvivalRooms.Footprint(room);
            ScavengeBox("房间地面", At(rect.center, .035f), new Vector3(rect.width, .065f, rect.height), floor);
            foreach (var wall in SurvivalRooms.Walls(room))
            {
                var section = ScavengeBox("现有外墙", At(wall.center, 1.42f), new Vector3(wall.width, 2.84f, wall.height), walls);
                RegisterScavengeOccluder(section.GetComponent<Renderer>());
            }
            var cover = ScavengeBox("屋顶", At(rect.center, 2.92f), new Vector3(rect.width + .2f, .16f, rect.height + .2f), roof);
            RegisterScavengeOccluder(cover.GetComponent<Renderer>());
            var door = SurvivalRooms.Door(room);
            survivalDoors[room] = ScavengeBox("现有房门", At(door.center, 1.02f), new Vector3(.21f, 2.04f, door.height), wood);
            survivalDoorBraces[room] = Shape("加固横板", survivalDoors[room], PrimitiveType.Cube,
                new Vector3(.5f, .13f, 0), new Vector3(.35f, .13f, 1.08f), floor);
            var bed = SurvivalRooms.Bed(room);
            ScavengeBox("床架", At(bed, .20f), new Vector3(1.2f, .4f, 2), wood);
            ScavengeBox("床垫", At(bed, .48f), new Vector3(1.1f, .22f, 1.9f), floor);
            ScavengeBox("储物柜", At(SurvivalRooms.Cabinet(room), .52f), new Vector3(1.1f, 1.04f, .55f), wood);
        }

        private void CreateSurvivalPresentation()
        {
            survivalSun = transform.Find("PreviewSun")?.GetComponent<Light>();
            survivalSunBefore = survivalSun != null ? survivalSun.intensity : 0;
            survivalAmbientBefore = RenderSettings.ambientLight;
            survivalTorch = new GameObject("随身手电光束").AddComponent<Light>();
            survivalTorch.transform.SetParent(scavengeRoot, false);
            survivalTorch.type = LightType.Spot;
            survivalTorch.spotAngle = 57;
            survivalTorch.shadows = LightShadows.Hard;
            survivalAlarmClip = CreateSurvivalAlarmClip();
            survivalDoorImpact = TownNoise("入口撞击", .19f, true);
            survivalDropMaterial = ScavengeMaterial(new Color(.7f, .58f, .39f));
            for (int i = 0; i < survivalDoors.Length; i++)
            {
                var audio = survivalDoors[i].gameObject.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.loop = true;
                audio.spatialBlend = 1;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 2;
                audio.maxDistance = 20;
                audio.clip = survivalAlarmClip;
                survivalAlarms[i] = audio;
                var impact = survivalDoors[i].gameObject.AddComponent<AudioSource>();
                impact.playOnAwake = false;
                impact.spatialBlend = 1;
                impact.rolloffMode = AudioRolloffMode.Linear;
                impact.minDistance = 2;
                impact.maxDistance = 20;
                survivalImpacts[i] = impact;
            }
        }

        private static AudioClip CreateSurvivalAlarmClip()
        {
            const int rate = 22050;
            var samples = new float[rate / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Clamp01(Mathf.Min(t, .3f - t) * 80);
                samples[i] = Mathf.Sin(t * Mathf.PI * 2 * 730) * envelope * .2f;
            }
            var clip = AudioClip.Create("入口警报", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void SyncSurvivalPresentation()
        {
            var state = scavengeRun.Survival;
            float hour = state.Hour;
            float daylight = Mathf.Clamp01(Mathf.Min(hour - 5.5f, 19.5f - hour) / 1.5f);
            if (survivalSun != null) survivalSun.intensity = Mathf.Lerp(.18f, survivalSunBefore, daylight);
            RenderSettings.ambientLight = Color.Lerp(new Color(.075f, .095f, .145f), survivalAmbientBefore, daylight);
            survivalTorch.enabled = state.Light > 0;
            survivalTorch.range = Mathf.Lerp(3, 12, state.Light);
            survivalTorch.intensity = Mathf.Lerp(.4f, 2.6f, state.Light);
            survivalTorch.transform.SetPositionAndRotation(At(scavengeRun.Player, 1.05f),
                Quaternion.LookRotation(At(state.facing, -.12f)));
            for (int i = 0; i < survivalDoors.Length; i++)
            {
                var room = state.rooms[i];
                var hinge = SurvivalRooms.Door(i);
                var door = survivalDoors[i];
                var render = door.GetComponent<Renderer>();
                render.enabled = room.durability > 0;
                survivalDoorBraces[i].gameObject.SetActive(room.fortified && room.durability > 0);
                door.position = At(room.open ? new Vector2(hinge.center.x + .8f, hinge.yMin) : hinge.center, 1.02f);
                door.rotation = Quaternion.Euler(0, room.open ? 90 : 0, 0);
                var audio = survivalAlarms[i];
                if (room.durability < survivalDoorDurability[i] && !scavengePaused &&
                    Vector2.Distance(scavengeRun.Player, hinge.center) <= 12)
                    survivalImpacts[i].PlayOneShot(survivalDoorImpact, .8f);
                survivalDoorDurability[i] = room.durability;
                bool playing = room.ringing > 0 && !scavengePaused && scavengeRun.Outcome == ScavengeOutcome.Exploring &&
                    Vector2.Distance(scavengeRun.Player, hinge.center) <= 20;
                if (playing && !audio.isPlaying) audio.Play();
                else if (!playing && audio.isPlaying) audio.Stop();
            }
            for (int i = 0; i < state.drops.Count; i++)
            {
                var drop = state.drops[i];
                if (i == survivalDropMarkers.Count)
                    survivalDropMarkers.Add(ScavengeBox("地面物品", At(drop.point, .13f),
                        new Vector3(.43f, .26f, .37f), survivalDropMaterial));
                survivalDropMarkers[i].position = At(drop.point, .13f);
                survivalDropMarkers[i].gameObject.SetActive(drop.items.Count > 0);
            }
            for (int i = state.drops.Count; i < survivalDropMarkers.Count; i++)
                survivalDropMarkers[i].gameObject.SetActive(false);
        }

        private void ReleaseSurvivalPresentation()
        {
            if (survivalSun != null) survivalSun.intensity = survivalSunBefore;
            RenderSettings.ambientLight = survivalAmbientBefore;
            Release(survivalAlarmClip);
            Release(survivalDoorImpact);
            survivalAlarmClip = null;
            survivalDoorImpact = null;
            survivalDropMarkers.Clear();
            survivalMode = false;
            survivalConfirmNew = false;
        }

        /// <summary>键鼠事件由房间、容器和随身道具界面按当前上下文接收。</summary>
        private void PollSurvival()
        {
            if (survivalConfirmNew || scavengeRun.Outcome != ScavengeOutcome.Exploring) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (PollSurvivalJourney(keyboard)) return;
            if (keyboard?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape))
            {
                if (scavengeRun.Survival.action != SurvivalAction.None && !scavengePaused)
                    scavengeRun.CancelSurvivalAction();
                else if (survivalPackOpen || survivalBuildOpen || survivalContainer != null || scavengeRun.OpenCache >= 0)
                    CloseSurvivalPanels();
                else scavengePaused = !scavengePaused;
            }
            if (scavengePaused) { SyncSurvivalPresentation(); return; }

            Vector2 movement = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            scavengeRun.SurvivalSprint = movement.sqrMagnitude > 0 &&
                (keyboard?.leftShiftKey.isPressed == true || keyboard?.rightShiftKey.isPressed == true);
            var direction = ScavengeWorldMovement(movement);
            for (float remaining = Time.unscaledDeltaTime; remaining > 0 && scavengeRun.Outcome == ScavengeOutcome.Exploring;)
            {
                float step = Mathf.Min(remaining, .05f);
                scavengeRun.Tick(step, direction);
                remaining -= step;
            }
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring)
            {
                CloseSurvivalPanels();
                CloseSurvivalSave();
                SyncScavenge();
                return;
            }

            if (survivalContainer != null && Vector2.Distance(survivalContainer.point, scavengeRun.Player) > 1.8f)
                CloseSurvivalPanels();
            if (survivalBuildOpen && scavengeRun.NearbySurvivalDoor < 0) CloseSurvivalPanels();
            if (keyboard?.tabKey.wasPressedThisFrame == true) OpenSurvivalPack();
            if (keyboard?.vKey.wasPressedThisFrame == true) OpenSurvivalBuild();
            if (keyboard?.eKey.wasPressedThisFrame == true) InteractSurvival();
            if (keyboard?.fKey.wasPressedThisFrame == true) scavengeRun.ToggleSurvivalTorch();
            if (keyboard?.rKey.wasPressedThisFrame == true) StartSurvivalAction(SurvivalAction.Reload);
            if (mouse != null && TryProjectBattleGround(mouse.position.ReadValue(), out var aim) &&
                !SurvivalHudContains(mouse.position.ReadValue()))
            {
                if ((aim - scavengeRun.Player).sqrMagnitude > .01f)
                    scavengeRun.Survival.facing = (aim - scavengeRun.Player).normalized;
                if (mouse.leftButton.wasPressedThisFrame && !SurvivalPanelOpen &&
                    scavengeRun.SurvivalAttack(aim, AimSurvivalHead(mouse.position.ReadValue())))
                    PlayTownShot(aim - scavengeRun.Player);
            }
            SyncScavenge();
        }

        private int AimSurvivalHead(Vector2 cursor)
        {
            int target = -1;
            float best = float.MaxValue;
            for (int i = 0; i < scavengeRun.Patrols.Count; i++)
            {
                var patrol = scavengeRun.Patrols[i];
                if (!patrol.Alive) continue;
                var head = gameCamera.WorldToScreenPoint(At(patrol.Position, 1.34f));
                if (head.z <= 0 || Vector2.Distance(cursor, head) > 12 * HudScale || head.z >= best) continue;
                target = i;
                best = head.z;
            }
            return target;
        }

        private bool SurvivalPanelOpen => survivalPackOpen || survivalBuildOpen || survivalContainer != null ||
            scavengeRun.OpenCache >= 0;

        private void CloseSurvivalPanels()
        {
            survivalPackOpen = survivalBuildOpen = false;
            survivalContainer = null;
            survivalScroll = Vector2.zero;
            scavengeRun.CloseSearch();
        }

        private void OpenSurvivalPack()
        {
            bool open = !survivalPackOpen || survivalContainer != null || scavengeRun.OpenCache >= 0;
            CloseSurvivalPanels();
            survivalPackOpen = open;
        }

        private void OpenSurvivalBuild()
        {
            bool open = !survivalBuildOpen && scavengeRun.NearbySurvivalDoor >= 0;
            CloseSurvivalPanels();
            survivalBuildOpen = open;
        }

        private void StartSurvivalAction(SurvivalAction action, int index = -1)
        {
            if (scavengeRun.BeginSurvivalAction(action, index)) CloseSurvivalPanels();
        }

        private string SurvivalInteraction()
        {
            if (scavengeRun.Survival.sleeping) return "起身";
            if (SurvivalPanelOpen) return "关闭";
            if (scavengeRun.AtExit) return scavengeRun.Survival.Ready ? "撤离" : "提前撤离";
            int door = scavengeRun.NearbySurvivalDoor;
            if (door >= 0 && scavengeRun.Survival.rooms[door].durability > 0)
                return scavengeRun.Survival.rooms[door].open ? "关门" : "开门";
            if (scavengeRun.NearbySurvivalBed >= 0) return "睡觉";
            if (scavengeRun.NearbySurvivalCabinet >= 0) return "打开储物柜";
            if (NearbySurvivalDrop() != null) return "查看地面物品";
            int cache = scavengeRun.NearbyCache;
            return cache < 0 ? null : "搜索" + scavengeRun.CacheNames[cache];
        }

        private SurvivalContainer NearbySurvivalDrop()
        {
            foreach (var drop in scavengeRun.Survival.drops)
                if (drop.items.Count > 0 && Vector2.Distance(drop.point, scavengeRun.Player) <= 1.4f) return drop;
            return null;
        }

        private void InteractSurvival()
        {
            if (scavengeRun.Survival.sleeping) { scavengeRun.WakeSurvival("已起身"); return; }
            if (SurvivalPanelOpen) { CloseSurvivalPanels(); return; }
            if (scavengeRun.AtExit)
            {
                if (!scavengeRun.Survival.Ready) { survivalConfirmExit = true; return; }
                if (scavengeRun.SurvivalExit()) CloseSurvivalSave();
                return;
            }
            int door = scavengeRun.NearbySurvivalDoor;
            if (door >= 0 && scavengeRun.Survival.rooms[door].durability > 0) { scavengeRun.ToggleSurvivalDoor(); return; }
            if (scavengeRun.NearbySurvivalBed >= 0) { scavengeRun.SleepSurvival(); return; }
            int cabinet = scavengeRun.NearbySurvivalCabinet;
            if (cabinet >= 0)
            {
                survivalContainer = scavengeRun.Survival.rooms[cabinet].storage;
                survivalContainer.point = SurvivalRooms.Cabinet(cabinet);
                return;
            }
            survivalContainer = NearbySurvivalDrop();
            if (survivalContainer == null) scavengeRun.Interact();
        }

        private static Rect SurvivalInfo => new Rect(18, 18, 374, 141);
        private static Rect SurvivalInventory(float width, float height) => new Rect(18, 172, Mathf.Min(600, width - 36), Mathf.Min(395, height - 248));
        private static Rect SurvivalContext(float width, float height) => new Rect(width / 2 - 175, height - 61, 350, 40);

        private bool SurvivalHudContains(Vector2 cursor)
        {
            float scale = HudScale;
            float width = Screen.width / scale, height = Screen.height / scale;
            var point = new Vector2(cursor.x, Screen.height - cursor.y) / scale;
            return SurvivalInfo.Contains(point) || SurvivalContext(width, height).Contains(point) ||
                SurvivalJourneyContains(point, width, height) ||
                ScavengeMapRect(width).Contains(point) || (SurvivalPanelOpen && SurvivalInventory(width, height).Contains(point)) ||
                (scavengeRun.InsideSurvivalRoom >= 0 && new Rect(width - 205, height - 61, 184, 40).Contains(point)) ||
                (scavengeRun.NearbySurvivalDoor >= 0 && new Rect(width - 205, height - 108, 184, 38).Contains(point));
        }

        private void DrawSurvival(float width, float height)
        {
            var state = scavengeRun.Survival;
            if (DrawSurvivalJourneyModal(width, height)) return;
            if (survivalConfirmNew || scavengePaused || scavengeRun.Outcome != ScavengeOutcome.Exploring)
            {
                DrawSurvivalModal(width, height);
                return;
            }

            GUI.Box(SurvivalInfo, "");
            int day = (int)(state.elapsed / ScavengeSurvival.Day) + 1;
            int minutes = Mathf.FloorToInt(state.Hour * 60);
            string timer = state.Ready ? $"撤离窗口 {SurvivalTime(ScavengeSurvival.Deadline - state.elapsed)}" :
                $"生存还需 {SurvivalTime(ScavengeSurvival.Goal - state.elapsed)}";
            GUI.Label(new Rect(30, 27, 350, 27), $"第 {day} 天  {minutes / 60:00}:{minutes % 60:00}   {timer}");
            GUI.Label(new Rect(30, 56, 350, 28), $"生命 {Mathf.CeilToInt(Mathf.Max(0, state.health))}   饮水 {state.water:0}   饱腹 {state.food:0}   精力 {state.energy:0}");
            string warning = state.bleeding ? "正在出血" : state.water < 25 ? "需要饮水" :
                state.energy < 25 ? "需要休息" : state.food < 25 ? "需要进食" : "";
            GUI.Label(new Rect(30, 85, 350, 27), warning.Length > 0 ? warning :
                state.weapon == null ? "未装备武器" : DescribeSurvivalItem(state.weapon));
            if (GUI.Button(new Rect(30, 114, 148, 34), $"背包 {state.Used}/6  Tab")) OpenSurvivalPack();
            GUI.Label(new Rect(194, 114, 188, 32), state.torch == null ? "无手电" : $"手电 {state.torch.charge / 3.6f:0}%  F");

            string notice = state.sleeping ? "正在睡觉 · 移动键起身" : state.action != SurvivalAction.None ?
                $"{SurvivalActionName(state.action)} · {state.actionTime:0.0} 秒" : scavengeRun.SurvivalFeedback;
            if (!string.IsNullOrEmpty(notice)) GUI.Box(new Rect(width / 2 - 180, height - 103, 360, 34), notice);
            if (state.Warning)
                GUI.Box(new Rect(width / 2 - 155, 20, 310, 35), state.Ready ? "前往撤离点" : "即将达标 · 请向撤离点靠拢");
            string interaction = SurvivalInteraction();
            if (interaction != null && GUI.Button(SurvivalContext(width, height), interaction + "  E"))
                InteractSurvival();

            int inside = scavengeRun.InsideSurvivalRoom;
            if (inside >= 0 && state.baseRoom != inside &&
                GUI.Button(new Rect(width - 205, height - 61, 184, 40), "设为基地"))
                scavengeRun.ClaimSurvivalRoom();
            if (scavengeRun.NearbySurvivalDoor >= 0 &&
                GUI.Button(new Rect(width - 205, height - 108, 184, 38), "入口操作  V"))
                OpenSurvivalBuild();

            if (SurvivalPanelOpen) DrawSurvivalPanels(SurvivalInventory(width, height));
            DrawSurvivalJourney(width, height);
            if (scavengeRun.ExitKnown || state.Warning) DrawScavengeExitLabel();
            DrawScavengeMap(width);
        }

        private void DrawSurvivalPanels(Rect panel)
        {
            var state = scavengeRun.Survival;
            GUI.Box(panel, "");
            if (survivalBuildOpen)
            {
                DrawSurvivalBuild(panel);
                return;
            }
            int opened = scavengeRun.OpenCache;
            if (opened >= 0 && scavengeRun.SearchRemaining > 0)
            {
                GUI.Label(new Rect(panel.x + 13, panel.y + 12, panel.width - 26, 30),
                    $"搜索{scavengeRun.CacheNames[opened]} · {scavengeRun.SearchRemaining:0.0} 秒");
                return;
            }
            var container = opened >= 0 ? state.caches[opened] : survivalContainer;
            float half = panel.width / 2;
            GUI.Label(new Rect(panel.x + 12, panel.y + 8, half - 160, 31), $"随身 {state.Used}/6");
            if (container != null && GUI.Button(new Rect(panel.x + half - 145, panel.y + 8, 133, 30), "使用 / 装备"))
            {
                CloseSurvivalPanels();
                survivalPackOpen = true;
                return;
            }
            GUI.Label(new Rect(panel.x + half + 10, panel.y + 8, half - 20, 31),
                opened >= 0 ? scavengeRun.CacheNames[opened] : container != null ? "容器" : "已装备");
            var scroll = new Rect(panel.x + 9, panel.y + 44, panel.width - 18, panel.height - 52);
            float rows = Mathf.Max(state.pack.Count, container?.items.Count ?? 3);
            survivalScroll = GUI.BeginScrollView(scroll, survivalScroll,
                new Rect(0, 0, scroll.width - 18, Mathf.Max(rows * 45, scroll.height - 1)));
            try
            {
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && state.action == SurvivalAction.None && !state.sleeping;
                DrawSurvivalPackRows(half - 24, container);
                if (container != null) DrawSurvivalContainerRows(half, half - 44, container);
                else
                {
                    if (state.weapon != null && GUI.Button(new Rect(half, 0, half - 44, 37),
                        "卸下 · " + DescribeSurvivalItem(state.weapon)) && !state.Stow(false))
                        scavengeRun.ReportSurvival("背包没有空格");
                    if (state.torch != null)
                    {
                        if (GUI.Button(new Rect(half, 45, half - 44, 37), "卸下 · " + DescribeSurvivalItem(state.torch)) &&
                            !state.Stow(true)) scavengeRun.ReportSurvival("背包没有空格");
                        if (GUI.Button(new Rect(half, 90, half - 44, 37), "更换手电电池")) StartSurvivalAction(SurvivalAction.Battery);
                    }
                }
                GUI.enabled = enabled;
            }
            finally { GUI.EndScrollView(); }
        }

        private void DrawSurvivalPackRows(float width, SurvivalContainer container)
        {
            var state = scavengeRun.Survival;
            for (int i = 0; i < state.pack.Count; i++)
            {
                var item = state.pack[i];
                float y = i * 45;
                GUI.Label(new Rect(3, y, width - 110, 39), DescribeSurvivalItem(item));
                if (container != null)
                {
                    if (GUI.Button(new Rect(width - 83, y, 81, 37), "放入"))
                    {
                        if (!ScavengeSurvival.Transfer(state.pack, i, container.items, 12))
                            scavengeRun.ReportSurvival("容器已满");
                        return;
                    }
                    continue;
                }

                var action = item.item switch
                {
                    SurvivalItem.Water => SurvivalAction.Drink,
                    SurvivalItem.Food => SurvivalAction.Eat,
                    SurvivalItem.Bandage => SurvivalAction.Bandage,
                    SurvivalItem.Aid => SurvivalAction.Aid,
                    _ => SurvivalAction.None
                };
                bool equip = item.item is SurvivalItem.Pistol or SurvivalItem.Crowbar or SurvivalItem.Torch;
                if ((equip || action != SurvivalAction.None) &&
                    GUI.Button(new Rect(width - 115, y, 55, 37), equip ? "装备" : "使用"))
                {
                    if (equip)
                    {
                        if (!state.Equip(i)) scavengeRun.ReportSurvival("换下装备需要背包空格");
                    }
                    else StartSurvivalAction(action, i);
                    return;
                }
                if (GUI.Button(new Rect(width - 58, y, 56, 37), "放下"))
                {
                    var drop = NearbySurvivalDrop();
                    if (drop == null)
                    {
                        drop = new SurvivalContainer { point = scavengeRun.Player };
                        state.drops.Add(drop);
                    }
                    ScavengeSurvival.Transfer(state.pack, i, drop.items, int.MaxValue);
                    return;
                }
            }
        }

        private void DrawSurvivalContainerRows(float x, float width, SurvivalContainer container)
        {
            for (int i = 0; i < container.items.Count; i++)
                if (GUI.Button(new Rect(x, i * 45, width, 37), "拾取 · " + DescribeSurvivalItem(container.items[i])))
                {
                    if (!ScavengeSurvival.Transfer(container.items, i, scavengeRun.Survival.pack, ScavengeSurvival.Capacity))
                        scavengeRun.ReportSurvival("背包已满");
                    return;
                }
            if (container.items.Count == 0) GUI.Label(new Rect(x, 0, width, 34), "空");
        }

        private void DrawSurvivalBuild(Rect panel)
        {
            var state = scavengeRun.Survival;
            int index = scavengeRun.NearbySurvivalDoor;
            if (index < 0) return;
            var room = state.rooms[index];
            GUI.Label(new Rect(panel.x + 13, panel.y + 10, panel.width - 26, 29),
                $"{SurvivalRooms.Names[index]}入口 · {room.durability}/{room.MaxDurability}");
            float width = panel.width - 26;
            if (GUI.Button(new Rect(panel.x + 13, panel.y + 49, width, 39), "加固入口 · 1 建材"))
                StartSurvivalAction(SurvivalAction.Fortify);
            if (GUI.Button(new Rect(panel.x + 13, panel.y + 97, width, 39), "维修 · 1 维修零件"))
                StartSurvivalAction(SurvivalAction.Repair);
            if (!room.alarm && GUI.Button(new Rect(panel.x + 13, panel.y + 145, width, 39), "安装警报器 · 1 警报器和 1 电池"))
                StartSurvivalAction(SurvivalAction.Alarm);
            if (room.alarm && GUI.Button(new Rect(panel.x + 13, panel.y + 145, width, 39),
                room.battery <= 0 ? "更换警报器电池" : room.armed ? "关闭警报器" : "启用警报器"))
            {
                if (room.battery <= 0) StartSurvivalAction(SurvivalAction.AlarmBattery);
                else scavengeRun.ToggleSurvivalAlarm();
            }
        }

        private void DrawSurvivalModal(float width, float height)
        {
            var state = scavengeRun.Survival;
            ScavengeFill(new Rect(0, 0, width, height), new Color(.045f, .065f, .075f, .88f));
            float x = (width - 560) / 2, y = (height - 410) / 2;
            GUI.Box(new Rect(x, y, 560, 410), "");
            if (survivalConfirmNew)
            {
                GUI.Label(new Rect(x + 20, y + 25, 425, 35), "开始新行动会放弃原来的续玩档");
                if (GUI.Button(new Rect(x + 20, y + 75, 420, 42), "放弃旧行动并开始") && CloseSurvivalSave())
                    survivalConfirmNew = false;
                if (GUI.Button(new Rect(x + 20, y + 130, 420, 42), "返回开始页")) LeaveScavenge();
            }
            else if (scavengePaused)
            {
                GUI.Label(new Rect(x + 20, y + 25, 425, 35), "行动暂停");
                if (GUI.Button(new Rect(x + 20, y + 75, 420, 42), "继续行动")) scavengePaused = false;
                if (GUI.Button(new Rect(x + 20, y + 130, 420, 42), "保存并返回开始页")) SaveAndLeaveSurvival();
                if (GUI.Button(new Rect(x + 20, y + 185, 420, 42), "操作与目标")) survivalHelpOpen = true;
                GUI.Label(new Rect(x + 20, y + 246, 520, 64),
                    "暂停期间时间停止。保存后可从开始页继续本趟。\n搜索和背包界面不会暂停世界，请先找安全位置。",
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
            }
            else
            {
                string outcome = scavengeRun.Outcome switch
                {
                    ScavengeOutcome.Died => "阵亡",
                    ScavengeOutcome.WindowClosed => "撤离窗口已关闭",
                    _ => state.Ready ? "达成生存目标 · 已回归" : "已撤离 · 生存目标未达成"
                };
                GUI.Label(new Rect(x + 20, y + 18, 420, 38), outcome);
                GUI.Label(new Rect(x + 20, y + 57, 420, 28), $"存活 {SurvivalTime(state.elapsed)}  ·  {(state.Ready ? "已活满三天" : "未活满三天")}");
                GUI.Label(new Rect(x + 20, y + 94, 520, 86), SurvivalResultDetails(),
                    new GUIStyle(GUI.skin.label) { wordWrap = true });
                if (GUI.Button(new Rect(x + 20, y + 190, 520, 39), "用相同种子重试 · 空手") && CloseSurvivalSave())
                    BeginScavenge(scavengeRun.Seed);
                if (GUI.Button(new Rect(x + 20, y + 239, 520, 39), "换种子开始 · 空手") && CloseSurvivalSave())
                    RestartScavenge();
                if (GUI.Button(new Rect(x + 20, y + 288, 520, 39), "返回开始页") && CloseSurvivalSave())
                    LeaveScavenge();
            }
            if (!string.IsNullOrEmpty(survivalSaveError))
                GUI.Label(new Rect(x + 20, y + 354, 520, 42), survivalSaveError);
        }

        private static string SurvivalTime(double seconds)
        {
            int time = Mathf.CeilToInt((float)Math.Max(0, seconds));
            return $"{time / 60:00}:{time % 60:00}";
        }

        private static string DescribeSurvivalItem(SurvivalStack stack)
        {
            string name = stack.item switch
            {
                SurvivalItem.Water => "密封饮水", SurvivalItem.Food => "包装食品", SurvivalItem.Bandage => "绷带",
                SurvivalItem.Aid => "急救包", SurvivalItem.Crowbar => "撬棍", SurvivalItem.Pistol => "手枪",
                SurvivalItem.Ammo => "子弹", SurvivalItem.Torch => "手电", SurvivalItem.Battery => "电池",
                SurvivalItem.Boards => "建材", SurvivalItem.Parts => "维修零件", _ => "警报器"
            };
            if (stack.item == SurvivalItem.Pistol) return $"{name} {stack.rounds}/6";
            if (stack.item == SurvivalItem.Torch) return $"{name} {stack.charge / 3.6f:0}%";
            return stack.count > 1 ? $"{name} ×{stack.count}" : name;
        }

        private static string SurvivalActionName(SurvivalAction action) => action switch
        {
            SurvivalAction.Drink => "饮水", SurvivalAction.Eat => "进食", SurvivalAction.Aid => "使用急救包",
            SurvivalAction.Bandage => "包扎", SurvivalAction.Reload => "装填", SurvivalAction.Fortify => "加固入口",
            SurvivalAction.Repair => "维修入口", SurvivalAction.Alarm => "安装警报器", _ => "更换电池"
        };
    }
}
