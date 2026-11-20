using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private bool survivalMode, survivalSaveChecked, survivalHasSave;
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
        // 单次操作提示限时显示，避免离开房间后仍长时间显示开门反馈。
        private int survivalFeedbackVersion = -1;
        private float survivalFeedbackUntil;

        /// <summary>在进行中的行动发放一件测试武器，沿用普通背包的容量和装备规则。</summary>
        internal (bool success, string message) GiveSurvivalTestWeapon(string weaponId)
        {
            if (!CanGrantTestItems)
                return (false, "请先进入进行中的河谷镇行动。");

            var weapon = weaponId.ToLowerInvariant() switch
            {
                "crowbar" => new SurvivalStack(SurvivalItem.Crowbar),
                "pistol" => new SurvivalStack(SurvivalItem.Pistol) { rounds = ScavengeSurvival.PistolMagazineCapacity },
                _ => null
            };
            if (weapon == null) return (false, "可用武器：crowbar（撬棍）、pistol（手枪）。");

            var state = scavengeRun.Survival;
            if (!ScavengeSurvival.Transfer(new List<SurvivalStack> { weapon }, 0, state.pack, ScavengeSurvival.Capacity))
                return (false, "背包已满，腾出一格后再试。");

            return (true, $"已将{DescribeSurvivalItem(weapon)}放入背包。打开背包后可装备。");
        }

        // 暂停中也能发放，过场和结算期间拒绝修改背包。
        private bool CanGrantTestItems => survivalMode && !crossing && scavengeRun?.Survival != null &&
            scavengeRun.Outcome == ScavengeOutcome.Exploring;

        /// <summary>列出河谷镇现有物品的指令 ID 和背包名称。</summary>
        internal static string ListSurvivalTestItems() => string.Join("\n",
            Enum.GetValues(typeof(SurvivalItem)).Cast<SurvivalItem>().Select(item => $"{item.ToString().ToLowerInvariant()} · {SurvivalItemName(item)}"));

        /// <summary>按 ID 或名称发放，沿用普通拾取的堆叠、容量和存档规则。</summary>
        internal (bool success, string message) GiveSurvivalTestItems(string query, int amount)
        {
            if (!CanGrantTestItems) return (false, "请先进入进行中的河谷镇行动。");
            if (amount < 1) return (false, "数量必须是正整数。");

            var items = Enum.GetValues(typeof(SurvivalItem)).Cast<SurvivalItem>();
            var matches = items.Where(item => string.Equals(item.ToString(), query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SurvivalItemName(item), query, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
                matches = items.Where(item => SurvivalItemName(item).Contains(query) ||
                    item.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (matches.Length == 0) return (false, "未找到物品，可用 #item.list 查看目录。");
            if (matches.Length > 1) return (false, "匹配到多个物品，请输入完整 ID：" +
                string.Join("、", matches.Select(item => item.ToString().ToLowerInvariant())));

            var match = matches[0];
            var pack = scavengeRun.Survival.pack;
            var source = new List<SurvivalStack>(1);
            int accepted = 0;
            while (accepted < amount)
            {
                source.Add(new SurvivalStack(match));
                if (!ScavengeSurvival.Transfer(source, 0, pack, ScavengeSurvival.Capacity)) break;
                accepted++;
            }
            if (accepted == 0) return (false, "背包容量不足。");
            return (true, $"已放入背包：{SurvivalItemName(match)} ×{accepted}。" +
                (accepted == amount ? "" : $" 容量不足，剩余 {amount - accepted} 件未发放。"));
        }

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
                save.run.caches?.Length == ScavengeTown.Caches.Length && save.run.layoutVersion >= 0 && save.run.layoutVersion <= OutpostLoop.RiverValley.RiverValleyShopInterior.Version ? save : null;
        }

        /// <summary>新开前保护有效续玩档。新开和续玩共用地图加载流程。</summary>
        internal void CrossToSurvival(bool resume = false)
        {
            if (!resume && HasSurvivalSave) return;
            BeginCrossing(EnterSurvival(resume), resume);
        }

        /// <summary>继续档与新行动从相同的地图和角色入口进入。</summary>
        private IEnumerable<(float value, string label)> EnterSurvival(bool resume)
        {
            if (!initialized || scavengeRoot != null || Overlay != PrototypeOverlay.Start || UtilityWindowOpen) yield break;
            survivalResume = resume ? ReadSurvivalSave() : null;
            if (resume && survivalResume == null)
            {
                survivalHasSave = false;
                RefreshOverlay();
                yield break;
            }
            survivalLayoutVersion = survivalResume?.run?.layoutVersion ?? OutpostLoop.RiverValley.RiverValleyShopInterior.Version;
            survivalMode = true;
            foreach (var step in LoadScavenge(true)) yield return step;
        }

        private bool WriteSurvivalSave(bool available)
        {
            try
            {
                // 结束标记与续玩档使用相同的原子替换，避免读取已结算的旧行动。
                var progress = JourneyProgress;
                bool record = !available && scavengeRun?.Survival != null &&
                    scavengeRun.Outcome != ScavengeOutcome.Exploring && !ReferenceEquals(journeyRecordedRun, scavengeRun);
                if (record) progress = SurvivalJourneyProgress.Record(progress, scavengeRun, CaptureSurvivalCarriedSummary());
                var data = available ? scavengeRun.CaptureSurvival() : new SurvivalSave();
                data.journey = progress;
                SurvivalStore.Save(SurvivalSaveName, data);
                survivalJourney = progress;
                journeyStoreOwner = SurvivalStore;
                if (record) journeyRecordedRun = scavengeRun;
                survivalHasSave = available;
                survivalSaveChecked = true;
                survivalSaveError = null;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException)
            {
                survivalSaveError = available ? "保存失败，仍在本趟行动中。可以重试。" : "结果或旧续玩档未能保存，请重试。";
                return false;
            }
        }

        private void SaveAndLeaveSurvival()
        {
            if (WriteSurvivalSave(true)) LeaveScavenge();
        }

        private bool CloseSurvivalSave()
        {
            if (scavengeRun?.Survival != null && scavengeRun.Outcome != ScavengeOutcome.Exploring &&
                !ReferenceEquals(journeyRecordedRun, scavengeRun)) return WriteSurvivalSave(false);
            return !HasSurvivalSave || WriteSurvivalSave(false);
        }

        private void ResetSurvivalPresentation()
        {
            ResetSurvivalJourney();
            ResetRiverValleyPortal();
            survivalFeedbackVersion = -1;
            survivalContainer = null;
            survivalPackOpen = survivalBuildOpen = false;
            survivalSaveError = null;
            for (int i = 0; i < survivalDoorDurability.Length; i++)
                survivalDoorDurability[i] = scavengeRun.Survival.rooms[i].durability;
        }

        private void CreateSurvivalRoom(int room, Material walls, Material floor, Material wood, Material roof)
        {
            var rect = CurrentSurvivalRooms.Footprint(room);
            var interior = RegisterSurvivalInterior(room, rect);
            ScavengeBox("房间地面", SurvivalPosition(rect.center, .035f), new Vector3(rect.width, .065f, rect.height), floor, interior);
            foreach (var wall in CurrentSurvivalRooms.Walls(room))
            {
                var section = ScavengeBox("现有外墙", SurvivalPosition(wall.center, 1.42f), new Vector3(wall.width, 2.84f, wall.height), walls);
                RegisterSurvivalShell(room, section.GetComponentsInChildren<Renderer>());
            }
            var cover = ScavengeBox("屋顶", SurvivalPosition(rect.center, 2.92f), new Vector3(rect.width + .2f, .16f, rect.height + .2f), roof);
            RegisterSurvivalShell(room, cover.GetComponentsInChildren<Renderer>(), roof: true);
            var door = CurrentSurvivalRooms.Door(room);
            survivalDoors[room] = ScavengeBox("现有房门", SurvivalPosition(door.center, 1.02f), new Vector3(.21f, 2.04f, door.height), wood);
            survivalDoorBraces[room] = Shape("加固横板", survivalDoors[room], PrimitiveType.Cube,
                new Vector3(.5f, .13f, 0), new Vector3(.35f, .13f, 1.08f), floor);
            RegisterSurvivalShell(room, survivalDoors[room].GetComponentsInChildren<Renderer>());
            var bed = CurrentSurvivalRooms.Bed(room);
            ScavengeBox("床架", SurvivalPosition(bed, .20f), new Vector3(1.2f, .4f, 2), wood, interior);
            ScavengeBox("床垫", SurvivalPosition(bed, .48f), new Vector3(1.1f, .22f, 1.9f), floor, interior);
            var cabinet = ScavengeBox("储物柜", SurvivalPosition(CurrentSurvivalRooms.Cabinet(room), .52f), new Vector3(1.1f, 1.04f, .55f), wood, interior);
            if (survivalLayoutVersion >= 1 && survivalCityArt != null)
            {
                cabinet.rotation=Quaternion.Euler(0,180,0);
                OutpostLoop.RiverValley.RiverValleyShelfDressing.Fill(cabinet,survivalCityArt,false);
            }
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
            survivalTorch.transform.SetPositionAndRotation(SurvivalPosition(scavengeRun.Player, 1.05f),
                Quaternion.LookRotation(At(state.facing, -.12f)));
            for (int i = 0; i < survivalDoors.Length; i++)
            {
                var room = state.rooms[i];
                var hinge = CurrentSurvivalRooms.Door(i);
                var door = survivalDoors[i];
                var render = door.GetComponent<Renderer>();
                render.enabled = room.durability > 0;
                survivalDoorBraces[i].gameObject.SetActive(room.fortified && room.durability > 0);
                door.position = SurvivalPosition(room.open ? new Vector2(hinge.center.x + .8f, hinge.yMin) : hinge.center, 1.02f);
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
                    survivalDropMarkers.Add(ScavengeBox("地面物品", SurvivalPosition(drop.point, .13f),
                        new Vector3(.43f, .26f, .37f), survivalDropMaterial));
                survivalDropMarkers[i].position = SurvivalPosition(drop.point, .13f);
                survivalDropMarkers[i].gameObject.SetActive(drop.items.Count > 0 && CanSeeSurvivalInteriorAt(drop.point));
            }
            for (int i = state.drops.Count; i < survivalDropMarkers.Count; i++)
                survivalDropMarkers[i].gameObject.SetActive(false);
        }

        private void ReleaseSurvivalPresentation()
        {
            journeyRecordedRun = null;
            if (survivalTorch)
            {
                if (survivalSun != null) survivalSun.intensity = survivalSunBefore;
                RenderSettings.ambientLight = survivalAmbientBefore;
            }
            Release(survivalAlarmClip);
            Release(survivalDoorImpact);
            survivalAlarmClip = null;
            survivalDoorImpact = null;
            survivalDropMarkers.Clear();
            survivalMode = false;
        }

        /// <summary>键鼠事件由房间、容器和随身道具界面按当前上下文接收。</summary>
        private void PollSurvival()
        {
            if (PresentRiverValleyPortal()) return;
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring) return;
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

            bool driving = scavengeRun.Driving;
            bool interactPressed = keyboard?.eKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.E);
            Vector2 movement = driving ? ReadDrivingInput(keyboard) : keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            scavengeRun.SurvivalSprint = movement.sqrMagnitude > 0 &&
                (keyboard?.leftShiftKey.isPressed == true || keyboard?.rightShiftKey.isPressed == true);
            var direction = driving ? movement : ScavengeWorldMovement(movement);
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
            if (interactPressed && (driving ||
                (!SurvivalPanelOpen && scavengeRun.CanEnterVehicle)))
            {
                InteractSurvival();
                return;
            }
            if (driving) { SyncScavenge(); return; }
            if (keyboard?.tabKey.wasPressedThisFrame == true) OpenSurvivalPack();
            if (keyboard?.vKey.wasPressedThisFrame == true) OpenSurvivalBuild();
            if (interactPressed) InteractSurvival();
            if (keyboard?.fKey.wasPressedThisFrame == true) scavengeRun.ToggleSurvivalTorch();
            if (keyboard?.rKey.wasPressedThisFrame == true) StartSurvivalAction(SurvivalAction.Reload);
            if (mouse != null && TryProjectBattleGround(mouse.position.ReadValue(), out var aim) &&
                !TestCommandLauncherContains(mouse.position.ReadValue()) &&
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
                var head = gameCamera.WorldToScreenPoint(SurvivalPosition(patrol.Position, 1.34f));
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

        /// <summary>底部只显示当前 E 键能成功完成的主操作。失败反馈仍由旧输入处理。</summary>
        private string SurvivalInteraction()
        {
            if (scavengeRun.Driving) return scavengeRun.CanLeaveVehicle ? Loc.T("ContextLeaveVehicle") : null;
            if (scavengeRun.Survival.sleeping) return Loc.T("ContextWake");
            if (SurvivalPanelOpen) return SurvivalInventoryOpen ? null : Loc.T("ContextClose");
            if (scavengeRun.CanEnterVehicle) return Loc.T("ContextEnterVehicle");
            if (scavengeRun.AtExit) return Loc.T(scavengeRun.Survival.Ready ? "ContextExtract" : "ContextEarlyExtract");
            int door = scavengeRun.NearbySurvivalDoor;
            if (door >= 0 && scavengeRun.Survival.rooms[door].durability > 0)
                return scavengeRun.SurvivalDoorBlocked(door) != null ? null :
                    Loc.T(scavengeRun.Survival.rooms[door].open ? "ContextCloseDoor" : "ContextOpenDoor");
            int bed = scavengeRun.NearbySurvivalBed;
            if (bed >= 0) return scavengeRun.SurvivalSleepBlocked(bed) == null ? Loc.T("ContextSleep") : null;
            if (scavengeRun.NearbySurvivalCabinet >= 0) return Loc.T("ContextStorage");
            if (NearbySurvivalDrop() != null) return Loc.T("ContextGround");
            if (scavengeRun.CanTalkToClinicSurvivor) return Loc.T("ContextTalk");
            if (scavengeRun.NearbyCache >= 0) return Loc.T("ContextSearch");
            return null;
        }

        private SurvivalContainer NearbySurvivalDrop()
        {
            foreach (var drop in scavengeRun.Survival.drops)
                if (drop.items.Count > 0 && Vector2.Distance(drop.point, scavengeRun.Player) <= 1.4f) return drop;
            return null;
        }

        private void InteractSurvival()
        {
            if (scavengeRun.Driving || (!SurvivalPanelOpen && scavengeRun.CanEnterVehicle))
            {
                scavengeRun.InteractVehicle();
                SyncScavenge();
                return;
            }
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
                survivalContainer.point = CurrentSurvivalRooms.Cabinet(cabinet);
                return;
            }
            survivalContainer = NearbySurvivalDrop();
            if (survivalContainer != null) return;
            if (scavengeRun.CanTalkToClinicSurvivor)
                scavengeRun.ReportSurvival("幸存者：前几天，药都转到医院去了。");
            else
                scavengeRun.Interact();
        }

        private static Rect SurvivalInfo => new Rect(18, 18, 374, 141);
        private Rect SurvivalInventory(float width, float height)
        {
            float panelHeight = SurvivalInventoryOpen ? SurvivalInventoryHeight() : 395;
            return new Rect(18, 172, Mathf.Min(600, width - 36), Mathf.Min(panelHeight, height - 248));
        }
        private static Rect SurvivalClaimRect(float width, float height) => new Rect(width - 205, height - 61, 184, 40);

        private static Rect SurvivalBuildRect(float width, float height) => new Rect(width - 205, height - 108, 184, 38);

        private bool ShowSurvivalClaim => !scavengeRun.Driving && scavengeRun.InsideSurvivalRoom >= 0 &&
            scavengeRun.Survival.baseRoom != scavengeRun.InsideSurvivalRoom;

        private bool ShowSurvivalBuild => !scavengeRun.Driving && scavengeRun.NearbySurvivalDoor >= 0;

        private bool SurvivalHudContains(Vector2 cursor)
        {
            float scale = HudScale;
            float width = Screen.width / scale, height = Screen.height / scale;
            var point = new Vector2(cursor.x, Screen.height - cursor.y) / scale;
            return SurvivalInfo.Contains(point) || SurvivalContextContains(point, width, height) ||
                SurvivalJourneyContains(point, width, height) ||
                ScavengeMapRect(width).Contains(point) || InventoryContains(cursor, point, width, height) ||
                (ShowSurvivalClaim && SurvivalClaimRect(width, height).Contains(point)) ||
                (ShowSurvivalBuild && SurvivalBuildRect(width, height).Contains(point));
        }

        private void DrawSurvival(float width, float height)
        {
            if (RiverValleyPortalPlaying) return;
            var state = scavengeRun.Survival;
            if (DrawSurvivalJourneyModal(width, height)) return;
            if (scavengePaused || scavengeRun.Outcome != ScavengeOutcome.Exploring)
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
            if (scavengeRun.Driving)
                GUI.Label(new Rect(30, 114, 350, 34), $"车速 {Mathf.Abs(scavengeRun.VehicleSpeed) * 3.6f:0} km/h   WASD 驾驶");
            else
            {
                string packLabel = SurvivalInventoryOpen ? "背包  Tab" : $"背包 {state.Used}/6  Tab";
                if (GUI.Button(new Rect(30, 114, 148, 34), packLabel)) OpenSurvivalPack();
                GUI.Label(new Rect(194, 114, 188, 32), state.torch == null ? "无手电" : $"手电 {state.torch.charge / 3.6f:0}%  F");
            }

            if (survivalFeedbackVersion != scavengeRun.SurvivalFeedbackVersion)
            {
                survivalFeedbackVersion = scavengeRun.SurvivalFeedbackVersion;
                survivalFeedbackUntil = Time.unscaledTime + 4;
            }
            string notice = state.action == SurvivalAction.None && scavengeRun.SearchRemaining <= 0 &&
                Time.unscaledTime <= survivalFeedbackUntil ? scavengeRun.SurvivalFeedback : "";
            if (!string.IsNullOrEmpty(notice)) GUI.Box(new Rect(width / 2 - 180, height - 103, 360, 34), notice);
            if (state.Warning)
                GUI.Box(new Rect(width / 2 - 155, 20, 310, 35), state.Ready ? "前往撤离点" : "即将达标 · 请向撤离点靠拢");

            if (ShowSurvivalClaim && GUI.Button(SurvivalClaimRect(width, height), "设为基地"))
                scavengeRun.ClaimSurvivalRoom();
            if (ShowSurvivalBuild && GUI.Button(SurvivalBuildRect(width, height), "入口操作  V"))
                OpenSurvivalBuild();

            if (survivalBuildOpen)
            {
                Rect panel = SurvivalInventory(width, height);
                GUI.Box(panel, "");
                DrawSurvivalBuild(panel);
            }
            DrawSurvivalJourney(width, height);
            DrawScavengeExitLabel();
            DrawScavengeMap(width);
            DrawFullRiverValleyEntrances();
        }

        /// <summary>返回随身物品已有的使用动作。</summary>
        private static SurvivalAction ActionForSurvivalItem(SurvivalItem item) => item switch
        {
            SurvivalItem.Water => SurvivalAction.Drink,
            SurvivalItem.Food => SurvivalAction.Eat,
            SurvivalItem.Bandage => SurvivalAction.Bandage,
            SurvivalItem.Aid => SurvivalAction.Aid,
            _ => SurvivalAction.None
        };

        private static bool IsSurvivalEquipment(SurvivalItem item) =>
            item is SurvivalItem.Pistol or SurvivalItem.Crowbar or SurvivalItem.Torch;

        /// <summary>在背包中执行装备或使用，沿用行动与容量校验。</summary>
        private void UseSurvivalPackItem(int index, bool equip, SurvivalAction action)
        {
            if (!equip) StartSurvivalAction(action, index);
            else if (!scavengeRun.Survival.Equip(index)) scavengeRun.ReportSurvival("换下装备需要背包空格");
        }

        /// <summary>放下物品时合用附近掉落，或在当前脚下创建容器。</summary>
        private void DropSurvivalPackItem(int index)
        {
            var state = scavengeRun.Survival;
            var drop = NearbySurvivalDrop();
            if (drop == null)
            {
                drop = new SurvivalContainer { point = scavengeRun.Player };
                state.drops.Add(drop);
            }
            ScavengeSurvival.Transfer(state.pack, index, drop.items, int.MaxValue);
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
            if (scavengePaused)
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
                if (GUI.Button(new Rect(x + 20, y + 190, 520, 39), survivalLayoutVersion == 0 ?
                    "用相同种子重试 · 旧版街区" : "用相同种子重试 · 空手") && CloseSurvivalSave())
                    BeginScavenge(scavengeRun.Seed);
                if (GUI.Button(new Rect(x + 20, y + 239, 520, 39), "换种子开始 · 河谷镇整图"))
                    StartFreshSurvivalFromResult();
                if (GUI.Button(new Rect(x + 20, y + 288, 520, 39),
                    scavengeRun.Outcome == ScavengeOutcome.Extracted ? "回到空间 · 查看行动记录" : "返回开始页 · 保留结果"))
                    ReturnFromSurvival();
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
            string name = SurvivalItemName(stack.item);
            if (stack.item == SurvivalItem.Pistol) return $"{name} {stack.rounds}/{ScavengeSurvival.PistolMagazineCapacity}";
            if (stack.item == SurvivalItem.Torch) return $"{name} {stack.charge / 3.6f:0}%";
            return stack.count > 1 ? $"{name} ×{stack.count}" : name;
        }

        /// <summary>背包与测试指令使用同一套物品名称。</summary>
        private static string SurvivalItemName(SurvivalItem item) => item switch
        {
            SurvivalItem.Water => "密封饮水", SurvivalItem.Food => "包装食品", SurvivalItem.Bandage => "绷带",
            SurvivalItem.Aid => "急救包", SurvivalItem.Crowbar => "撬棍", SurvivalItem.Pistol => "手枪",
            SurvivalItem.Ammo => "子弹", SurvivalItem.Torch => "手电", SurvivalItem.Battery => "电池",
            SurvivalItem.Boards => "建材", SurvivalItem.Parts => "维修零件", _ => "警报器"
        };
    }
}
