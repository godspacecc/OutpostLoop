using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using OutpostLoop.RiverValley;
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
        // 门板当前的表现角度，续玩时从存档姿态起步。
        private readonly float[] survivalDoorAngles = new float[2];
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
                save.run.caches?.Length == ScavengeTown.Caches.Length && save.run.layoutVersion >= 0 && save.run.layoutVersion <= RiverValleyResidence.Version ? save : null;
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
            survivalLayoutVersion = survivalResume?.run?.layoutVersion ?? RiverValleyResidence.Version;
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
                if (available && survivalFog) data.fog = survivalFog.Capture();
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
            {
                var room = scavengeRun.Survival.rooms[i];
                survivalDoorDurability[i] = room.durability;
                survivalDoorAngles[i] = SurvivalDoorTargetAngle(room);
            }
        }

        private void CreateSurvivalRoom(int room, Material walls, Material floor, Material wood, Material roof)
        {
            var rect = CurrentSurvivalRooms.Footprint(room);
            var interior = RegisterSurvivalInterior(room, rect);
            ScavengeBox("房间地面", SurvivalPosition(rect.center, .035f), new Vector3(rect.width, .065f, rect.height), floor, interior);
            var door = CurrentSurvivalRooms.Door(room);
            foreach (var wall in CurrentSurvivalRooms.Walls(room))
            {
                if (TryCreateShopWindow(room, wall, door, walls, wood)) continue;
                var section = ScavengeBox("现有外墙", SurvivalPosition(wall.center, 1.42f), new Vector3(wall.width, 2.84f, wall.height), walls, sightBox: true);
                RegisterSurvivalShell(room, section.GetComponentsInChildren<Renderer>());
            }
            bool twoLevels = room == RiverValleyResidence.RoomIndex && survivalWorldGeometry?.Residence != null;
            float roofHeight = twoLevels ? 2.92f + RiverValleyResidence.StoreyHeight : 2.92f;
            if (!twoLevels || !survivalWorldGeometry.Residence.HasRoof)
            {
                var cover = ScavengeBox("屋顶", SurvivalPosition(rect.center, roofHeight), new Vector3(rect.width + .2f, .16f, rect.height + .2f), roof, sightBox: true);
                RegisterSurvivalShell(room, cover.GetComponentsInChildren<Renderer>(), roof: true);
            }
            if (twoLevels) CreateResidenceUpperFloor(interior, walls, floor, wood, roof);
            survivalDoors[room] = ScavengeBox("现有房门", SurvivalPosition(door.center, 1.02f), new Vector3(.21f, 2.04f, door.height), wood);
            survivalSight.AddDoor(survivalDoors[room]);
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

        private void SyncSurvivalPresentation(bool snapDoor = false)
        {
            var state = scavengeRun.Survival;
            float hour = state.Hour;
            float daylight = Mathf.Clamp01(Mathf.Min(hour - 5.5f, 19.5f - hour) / 1.5f);
            if (survivalSun != null) survivalSun.intensity = Mathf.Lerp(.18f, survivalSunBefore, daylight);
            RenderSettings.ambientLight = Color.Lerp(new Color(.075f, .095f, .145f), survivalAmbientBefore, daylight);
            survivalTorch.enabled = state.Light > 0;
            survivalTorch.range = Mathf.Lerp(3, 12, state.Light);
            survivalTorch.intensity = Mathf.Lerp(.4f, 2.6f, state.Light);
            survivalTorch.transform.SetPositionAndRotation(ScavengePlayerPosition(1.05f),
                Quaternion.LookRotation(At(state.facing, -.12f)));
            for (int i = 0; i < survivalDoors.Length; i++)
            {
                var room = state.rooms[i];
                var hinge = CurrentSurvivalRooms.Door(i);
                var door = survivalDoors[i];
                var render = door.GetComponent<Renderer>();
                render.enabled = room.durability > 0;
                survivalDoorBraces[i].gameObject.SetActive(room.fortified && room.durability > 0);
                if (snapDoor) survivalDoorAngles[i] = SurvivalDoorTargetAngle(room);
                SyncSurvivalDoorPose(i, hinge, room, scavengePaused ? 0 : Time.unscaledDeltaTime);
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
        }

        private void SyncSurvivalDrops()
        {
            var state = scavengeRun.Survival;
            for (int i = 0; i < state.drops.Count; i++)
            {
                var drop = state.drops[i];
                float height = survivalWorldGeometry?.Residence?.HeightOffset(drop.point, drop.surface) ?? 0f;
                var position = SurvivalPosition(drop.point, .13f + height);
                if (i == survivalDropMarkers.Count)
                    survivalDropMarkers.Add(ScavengeBox("地面物品", position,
                        new Vector3(.43f, .26f, .37f), survivalDropMaterial));
                survivalDropMarkers[i].position = position;
                survivalDropMarkers[i].gameObject.SetActive(drop.items.Count > 0 && survivalSight.Sees(position + Vector3.up * .13f));
            }
            for (int i = state.drops.Count; i < survivalDropMarkers.Count; i++)
                survivalDropMarkers[i].gameObject.SetActive(false);
        }

        /// <summary>开启时向玩家所在侧的对面摆动，关闭时回到门框。</summary>
        private static float SurvivalDoorTargetAngle(SurvivalRoom room) =>
            room.open ? (room.opensOutward ? -90f : 90f) : 0f;

        /// <summary>由门轴反算门板中心，使门板和附着的横板一起绕轴旋转。</summary>
        private void SyncSurvivalDoorPose(int index, Rect doorway, SurvivalRoom room, float deltaTime)
        {
            const float swingDuration = .28f;
            float angle = survivalDoorAngles[index] = Mathf.MoveTowards(survivalDoorAngles[index],
                SurvivalDoorTargetAngle(room), 90f / swingDuration * deltaTime);
            var rotation = Quaternion.Euler(0, angle, 0);
            var pivot = SurvivalPosition(new Vector2(doorway.center.x, doorway.yMin), 1.02f);
            survivalDoors[index].SetPositionAndRotation(pivot + rotation * (Vector3.forward * (doorway.height * .5f)), rotation);
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
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Escape);
            bool pausePressed = !CurrentSurvivalPauseBlocked() &&
                (Game.Game.Interface.GetSystem<Game.HotKeySystem>().WasPressedThisFrame(Game.HotKeyActionIds.TogglePause) ||
                 (escape && RiverValleyPauseKey == Game.HotKeyDefaults.Escape));
            if (PollSurvivalJourney(keyboard, pausePressed)) return;

            // Esc 始终先关闭当前物品页或中止动作。开启暂停使用设置中的绑定。
            if (escape && survivalPackOpen)
            {
                CloseSurvivalPanels();
                return;
            }
            if (escape && scavengeRun.Survival.action != SurvivalAction.None && !scavengePaused)
                scavengeRun.CancelSurvivalAction();
            else if (escape && SurvivalPanelOpen)
                CloseSurvivalPanels();
            else if (pausePressed || (escape && scavengePaused))
            {
                scavengePaused = !scavengePaused;
                SyncSurvivalPresentation();
                return;
            }
            if (scavengePaused) { SyncSurvivalPresentation(); return; }

            bool driving = scavengeRun.Driving;
            bool interactPressed = keyboard?.eKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.E);
            Vector2 movement = driving ? ReadDrivingInput(keyboard) : survivalPackOpen || keyboard == null ? Vector2.zero : new Vector2(
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

            if (survivalContainer != null && (survivalContainer.surface != scavengeRun.PlayerSurface ||
                Vector2.Distance(survivalContainer.point, scavengeRun.Player) > 1.8f))
                CloseSurvivalPanels();
            if (survivalBuildOpen && scavengeRun.NearbySurvivalDoor < 0) CloseSurvivalPanels();
            if (interactPressed && (driving ||
                (!SurvivalPanelOpen && scavengeRun.CanEnterVehicle)))
            {
                InteractSurvival();
                return;
            }
            if (driving) { SyncScavenge(); return; }
            if (keyboard?.tabKey.wasPressedThisFrame == true || Input.GetKeyDown(KeyCode.Tab))
            {
                OpenSurvivalPack();
                SyncScavenge();
                return;
            }
            if (survivalPackOpen) { SyncScavenge(); return; }
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
            SetSurvivalSightView();
            int target = -1;
            float best = float.MaxValue;
            for (int i = 0; i < scavengeRun.Patrols.Count; i++)
            {
                var patrol = scavengeRun.Patrols[i];
                if (!patrol.Alive) continue;
                var headPosition = SurvivalPosition(patrol.Position, 1.34f);
                if (!survivalSight.Sees(headPosition)) continue;
                var head = gameCamera.WorldToScreenPoint(headPosition);
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
            managementPage = ManagementPage.Pack;
            managementSelection = null;
            managementRevision = null;
            survivalContainer = null;
            scavengeRun.CloseSearch();
        }

        private void OpenSurvivalPack()
        {
            if (scavengeRun.Driving || scavengeRun.Survival.sleeping) return;
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

        /// <summary>按现有 E 键优先级选出当前可操作物件，只显示一个现场标签。</summary>
        private (string Label, Vector3 Position) SurvivalFocusTarget()
        {
            var run = scavengeRun;
            if (run.Driving || run.Survival.sleeping || SurvivalPanelOpen) return default;
            if (run.CanEnterVehicle)
                return (Loc.T("FocusVehicle"), SurvivalPosition(run.VehiclePosition, 1.2f));
            if (run.AtExit)
                return (Loc.T(run.Survival.Ready ? "FocusExit" : "ContextEarlyExtract"),
                    scavengeExit.position + Vector3.up * 1.4f);
            int door = run.NearbySurvivalDoor;
            if (door >= 0 && run.Survival.rooms[door].durability > 0)
                return (Loc.T(run.SurvivalDoorBlocked(door) == null ? "FocusDoor" : "FocusDoorBlocked"),
                    SurvivalPosition(CurrentSurvivalRooms.Door(door).center, 1.25f));
            int bed = run.NearbySurvivalBed;
            if (bed >= 0)
                return run.SurvivalSleepBlocked(bed) == null
                    ? (Loc.T("FocusBed"), SurvivalPosition(CurrentSurvivalRooms.Bed(bed), .61f)) : default;
            int cabinet = run.NearbySurvivalCabinet;
            if (cabinet >= 0)
                return (Loc.T("InventoryStorage"), SurvivalPosition(CurrentSurvivalRooms.Cabinet(cabinet), 1.06f));
            var drop = NearbySurvivalDrop();
            if (drop != null)
                return (Loc.T("InventoryGround"), SurvivalPosition(drop.point, .25f) + Vector3.up *
                    (survivalWorldGeometry?.Residence?.HeightOffset(drop.point, drop.surface) ?? 0));
            if (run.CanTalkToClinicSurvivor)
                return (Loc.T("FocusSurvivor"), SurvivalPosition(survivalWorldGeometry.ClinicSurvivor, 1.4f));
            int cache = run.NearbyCache;
            return cache >= 0 && scavengeCrates[cache].gameObject.activeInHierarchy
                ? (Loc.T("FocusSupply"), scavengeCrates[cache].position + Vector3.up * .55f) : default;
        }

        /// <summary>只在指针指向当前近处目标时标出名称，文本不参与点击命中。</summary>
        private bool TrySurvivalFocusLabel(Vector2 cursor, out string label, out Vector2 pin)
        {
            label = null;
            pin = default;
            if (!CanShowSurvivalHud() || SurvivalContextHasProgress || SurvivalHudContains(cursor)) return false;
            var target = SurvivalFocusTarget();
            int door = scavengeRun.NearbySurvivalDoor;
            var doorSurface = door >= 0 && scavengeRun.Survival.rooms[door].durability > 0 ? survivalDoors[door] : null;
            if (target.Label == null || !survivalSight.Sees(target.Position, doorSurface)) return false;
            var screen = gameCamera.WorldToScreenPoint(target.Position);
            if (!IsCameraPoint(screen) || screen.z <= gameCamera.nearClipPlane ||
                (cursor - (Vector2)screen).sqrMagnitude > Mathf.Pow(52 * HudScale, 2)) return false;
            label = target.Label;
            pin = new Vector2(screen.x, Screen.height - screen.y) / HudScale;
            return true;
        }

        private void DrawSurvivalFocusLabel(float width)
        {
            if (Event.current.type != EventType.Repaint ||
                !TrySurvivalFocusLabel(Input.mousePosition, out string label, out var pin)) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            float w = Mathf.Clamp(style.CalcSize(new GUIContent(label)).x + 18, 56, width);
            var bounds = new Rect(Mathf.Clamp(pin.x - w / 2, 0, width - w), Mathf.Max(0, pin.y - 46), w, 27);
            ScavengeFill(bounds, new Color(.04f, .055f, .05f, .72f));
            GUI.Label(bounds, label, style);
        }

        private SurvivalContainer NearbySurvivalDrop()
        {
            foreach (var drop in scavengeRun.Survival.drops)
                if (drop.items.Count > 0 && drop.surface == scavengeRun.PlayerSurface &&
                    Vector2.Distance(drop.point, scavengeRun.Player) <= 1.4f) return drop;
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

        private Rect SurvivalInventory(float width, float height)
        {
            float panelHeight = SurvivalInventoryOpen ? SurvivalInventoryHeight() : 395;
            return new Rect(18, 172, Mathf.Min(600, width - 36), Mathf.Min(panelHeight, height - 248));
        }
        private bool SurvivalHudContains(Vector2 cursor)
        {
            float scale = HudScale;
            float width = Screen.width / scale, height = Screen.height / scale;
            var point = new Vector2(cursor.x, Screen.height - cursor.y) / scale;
            return SurvivalStatusContains(point) ||
                (ShowPerceptionMap() && PerceptionRect(PerceptionGroup.Map).Contains(point)) || InventoryContains(cursor, point, width, height) ||
                (survivalBuildOpen && SurvivalInventory(width, height).Contains(point));
        }

        private void DrawSurvival(float width, float height)
        {
            if (RiverValleyPortalPlaying) return;
            var state = scavengeRun.Survival;
            if (DrawSurvivalJourneyModal(width, height)) return;
            if (scavengeRun.Outcome != ScavengeOutcome.Exploring)
            {
                DrawSurvivalModal(width, height);
                return;
            }

            if (survivalPackOpen && inventoryRoot) return;
            if (!survivalStatusView) DrawSurvivalStatusFallback(state);

            if (survivalFeedbackVersion != scavengeRun.SurvivalFeedbackVersion)
            {
                survivalFeedbackVersion = scavengeRun.SurvivalFeedbackVersion;
                survivalFeedbackUntil = Time.unscaledTime + 4;
            }
            string notice = state.action == SurvivalAction.None && scavengeRun.SearchRemaining <= 0 &&
                Time.unscaledTime <= survivalFeedbackUntil ? scavengeRun.SurvivalFeedback : "";
            if (!string.IsNullOrEmpty(notice))
            {
                var interaction = PerceptionRect(PerceptionGroup.Interaction);
                float y = interaction.y - 38;
                if (y < lastPerceptionArea.y) y = interaction.yMax + 4;
                GUI.Box(new Rect(interaction.x, y, interaction.width, 34), notice);
            }

            if (survivalBuildOpen)
            {
                Rect panel = SurvivalInventory(width, height);
                GUI.Box(panel, "");
                DrawSurvivalBuild(panel);
            }
            DrawSurvivalFocusLabel(width);
            DrawScavengeMap(width, opensRegion: true);
        }

        /// <summary>资源加载失败时保留旧状态读数和背包入口。</summary>
        private void DrawSurvivalStatusFallback(ScavengeSurvival state)
        {
            var action = PerceptionRect(PerceptionGroup.Action);
            if (ShowPerceptionGroup(PerceptionGroup.Action, action) || state.Warning || state.Ready)
            {
                string timer = state.Ready ? $"撤离窗口 {SurvivalTime(ScavengeSurvival.Deadline - state.elapsed)}" :
                    $"生存还需 {SurvivalTime(ScavengeSurvival.Goal - state.elapsed)}";
                GUI.Box(action, timer);
            }
            var body = PerceptionRect(PerceptionGroup.Body);
            string warning = state.bleeding ? Loc.T("SurvivalStatusBleeding") :
                state.water < 25 ? Loc.T("SurvivalStatusWaterLow") :
                state.energy < 25 ? Loc.T("SurvivalStatusEnergyLow") :
                state.food < 25 ? Loc.T("SurvivalStatusFoodLow") : "";
            if (ShowPerceptionGroup(PerceptionGroup.Body, body))
                GUI.Box(body, $"生命 {state.health:0}   饮水 {state.water:0}   饱腹 {state.food:0}   精力 {state.energy:0}\n{warning}");
            else if (warning.Length > 0) GUI.Box(body, warning);

            var equipment = PerceptionRect(PerceptionGroup.Equipment);
            if (!scavengeRun.Driving && ShowPerceptionGroup(PerceptionGroup.Equipment, equipment))
            {
                string weapon = state.weapon == null ? "" : DescribeSurvivalItem(state.weapon);
                string light = state.torch == null ? "" : Loc.T("SurvivalStatusLight", state.torch.charge / 3.6f);
                if (weapon.Length > 0 || light.Length > 0) GUI.Box(equipment, $"{weapon}\n{light}");
            }
            if (scavengeRun.Driving)
            {
                var context = PerceptionRect(PerceptionGroup.Interaction);
                GUI.Label(new Rect(context.x, context.y, 150, 25),
                    Loc.T("SurvivalStatusSpeed", Mathf.Abs(scavengeRun.VehicleSpeed) * 3.6f));
            }
            else if (!SurvivalInventoryOpen && GUI.Button(PerceptionGeometry.Pack(lastPerceptionArea),
                Loc.T("SurvivalStatusPack", state.Used, ScavengeSurvival.Capacity))) OpenSurvivalPack();
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
                drop = new SurvivalContainer { point = scavengeRun.Player, surface = scavengeRun.PlayerSurface };
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
