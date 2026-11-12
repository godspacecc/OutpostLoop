using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>探索会话的存档包含地图种子、时间、玩家与巡逻状态。</summary>
    [Serializable]
    internal sealed class SurvivalSave
    {
        public bool available;
        public int seed;
        public Vector2 player;
        public bool exitKnown;
        public ScavengeSurvival run;
        public SurvivalPatrolSave[] patrols;
        public Vector2 gunshot;
        public float gunshotRemaining;
    }

    /// <summary>恢复时保留巡逻位置、受伤状态以及正在追踪的目标。</summary>
    [Serializable]
    internal struct SurvivalPatrolSave
    {
        public Vector2 position, facing, lastSeen, routePoint, noise;
        public float cooldown, memory, routeCooldown, noiseRemaining;
        public int health;
        public bool towardEnd, hasNoise;
    }

    public sealed partial class ScavengeRun
    {
        // 生存入口沿用大镇寻路，动态房门仅影响该入口。
        internal ScavengeSurvival Survival { get; }
        internal bool SurvivalSprint { get; set; }
        internal string SurvivalFeedback { get; private set; }
        private Rect[] survivalWalls;
        internal void RefreshSurvivalWalls() => survivalWalls = SurvivalRooms.Collision(Survival.rooms);

        private float SurvivalSpeed(Vector2 input)
        {
            if (Survival.sleeping)
            {
                if (input.sqrMagnitude < .001f) return 0;
                WakeSurvival("已起身");
            }
            if (input.sqrMagnitude > .001f)
            {
                Survival.facing = input.normalized;
                CancelSurvivalAction();
            }
            return SurvivalSprint ? Survival.Weak ? 5.2f : 7 : 4;
        }

        /// <summary>推进生命与局时，并在同一步内先判阵亡再判窗口关闭。</summary>
        private void TickSurvival(float dt, Vector2 input)
        {
            Survival.Advance(dt, SurvivalSprint && input.sqrMagnitude > .001f && !Survival.sleeping);
            if (Survival.health <= 0) { End(ScavengeOutcome.Died); return; }
            if (Survival.elapsed >= ScavengeSurvival.Deadline) { End(ScavengeOutcome.WindowClosed); return; }
            if (Survival.sleeping)
            {
                for (int i = 0; i < Survival.rooms.Length; i++)
                {
                    var room = Survival.rooms[i];
                    if (room.ringing > 0 && Vector2.Distance(Player, SurvivalRooms.Door(i).center) <= 20)
                    {
                        WakeSurvival("被附近警报惊醒");
                        break;
                    }
                }
                if (Survival.energy >= 100) WakeSurvival("休息好了");
            }

            if (Survival.action == SurvivalAction.None) return;
            Survival.actionTime = Mathf.Max(0, Survival.actionTime - dt);
            if (Survival.actionTime > 0) return;
            CompleteSurvivalAction();
        }

        internal bool SurvivalExit()
        {
            if (Outcome != ScavengeOutcome.Exploring || !AtExit || Survival.sleeping) return false;
            End(ScavengeOutcome.Extracted);
            return true;
        }

        internal int NearbySurvivalDoor => NearbySurvivalPoint(i => SurvivalRooms.Door(i).center, 1.65f);
        internal int NearbySurvivalBed => NearbySurvivalInterior(SurvivalRooms.Bed);
        internal int NearbySurvivalCabinet => NearbySurvivalInterior(SurvivalRooms.Cabinet);
        internal int InsideSurvivalRoom
        {
            get
            {
                for (int i = 0; i < Survival.rooms.Length; i++)
                    if (SurvivalRooms.Footprint(i).Contains(Player)) return i;
                return -1;
            }
        }

        private int NearbySurvivalPoint(Func<int, Vector2> point, float range)
        {
            for (int i = 0; i < Survival.rooms.Length; i++)
                if (Vector2.Distance(Player, point(i)) <= range) return i;
            return -1;
        }

        private int NearbySurvivalInterior(Func<int, Vector2> point)
        {
            int room = InsideSurvivalRoom;
            return room >= 0 && Vector2.Distance(Player, point(room)) <= 1.65f ? room : -1;
        }

        /// <summary>房间已有设施随位置可用，登记基地只开放针对该房间的施工。</summary>
        internal bool ClaimSurvivalRoom()
        {
            int room = InsideSurvivalRoom;
            if (room < 0 || HasRoomThreat(room)) return ReportSurvival("先清理房内威胁");
            Survival.baseRoom = room;
            return ReportSurvival("基地已设在" + SurvivalRooms.Names[room], true);
        }

        internal bool ToggleSurvivalDoor()
        {
            int index = NearbySurvivalDoor;
            if (index < 0 || Survival.rooms[index].durability <= 0) return ReportSurvival("入口已经损坏");
            var room = Survival.rooms[index];
            if (room.open)
            {
                var area = SurvivalRooms.Door(index);
                area = Rect.MinMaxRect(area.xMin - .38f, area.yMin - .38f, area.xMax + .38f, area.yMax + .38f);
                if (area.Contains(Player)) return ReportSurvival("离开门洞后再关门");
                foreach (var patrol in patrols)
                    if (patrol.Alive && area.Contains(patrol.Position)) return ReportSurvival("门洞有感染者");
            }
            room.open = !room.open;
            RefreshSurvivalWalls();
            return ReportSurvival(room.open ? "门已打开" : "门已关上", true);
        }

        internal bool SleepSurvival()
        {
            if (Survival.energy >= 100) return ReportSurvival("现在不困");
            int room = NearbySurvivalBed;
            if (room < 0 || InsideSurvivalRoom != room || HasRoomThreat(room))
                return ReportSurvival("附近有危险，无法睡觉");
            CancelSurvivalAction();
            CloseSearch();
            Survival.sleepRoom = room;
            Survival.sleeping = true;
            return ReportSurvival("正在睡觉 · 移动键起身", true);
        }

        private bool HasRoomThreat(int room)
        {
            var footprint = SurvivalRooms.Footprint(room);
            foreach (var patrol in patrols)
                if (patrol.Alive && (footprint.Contains(patrol.Position) ||
                    (patrol.Engaged && Vector2.Distance(patrol.Position, Player) < 8))) return true;
            return false;
        }

        internal void WakeSurvival(string reason)
        {
            if (!Survival.sleeping) return;
            Survival.sleeping = false;
            Survival.sleepRoom = -1;
            ReportSurvival(reason);
        }

        private void HitSurvivalPlayer()
        {
            Survival.health -= 15;
            if (Survival.elapsed - Survival.lastHit <= 10 && !Survival.bleeding)
            {
                Survival.bleeding = true;
                Survival.bleedClock = 0;
            }
            Survival.lastHit = (float)Survival.elapsed;
            WakeSurvival("受击惊醒");
            CancelSurvivalAction();
            CloseSearch();
            ReportSurvival(Survival.bleeding ? "受伤 · 正在出血" : "受伤");
            if (Survival.health <= 0) End(ScavengeOutcome.Died);
        }

        private bool AttackSurvivalDoor(ScavengePatrol patrol)
        {
            for (int i = 0; i < Survival.rooms.Length; i++)
            {
                var room = Survival.rooms[i];
                var door = SurvivalRooms.Door(i).center;
                if (Survival.sleepRoom == i && SurvivalRooms.Footprint(i).Contains(patrol.Position))
                    WakeSurvival("感染者进屋了");
                if (!room.Blocks || !patrol.Engaged || !SurvivalRooms.Footprint(i).Contains(patrol.LastSeen) ||
                    Vector2.Distance(patrol.Position, door) > 1.35f) continue;

                if (patrol.Cooldown > 0) return true;
                patrol.Cooldown = 1.05f;
                patrol.Memory = 2;
                room.durability = Mathf.Max(0, room.durability - (patrol.Fast ? 15 : 10));
                room.impact = .55f;
                if (room.alarm && room.armed && room.battery > 0) room.ringing = 3;
                if (Vector2.Distance(Player, door) < 12)
                {
                    WakeSurvival("被撞门声惊醒");
                    ReportSurvival(room.durability == 0 ? "入口被撞开" : "附近有撞门声");
                }
                if (room.durability == 0)
                {
                    room.open = true;
                    RefreshSurvivalWalls();
                }
                return true;
            }
            return false;
        }

        private Vector2? HearSurvival(Vector2 listener)
        {
            if (Survival == null) return null;
            for (int i = 0; i < Survival.rooms.Length; i++)
                if (Survival.rooms[i].ringing > 0 && Vector2.Distance(listener, SurvivalRooms.Door(i).center) <= 20)
                    return SurvivalRooms.Door(i).center;
            float range = Survival.action is SurvivalAction.Fortify or SurvivalAction.Repair or SurvivalAction.Alarm ? 14 :
                SurvivalSprint ? 6 : 0;
            if (range > 0 && Vector2.Distance(listener, Player) <= range) return Player;
            for (int i = 0; i < Survival.rooms.Length; i++)
                if (Survival.rooms[i].impact > 0 && Vector2.Distance(listener, SurvivalRooms.Door(i).center) <= 9)
                    return SurvivalRooms.Door(i).center;
            return null;
        }

        /// <summary>以实际瞄准位置和遮挡决定攻击结果。</summary>
        internal bool SurvivalAttack(Vector2 aim, int aimedHead)
        {
            if (Outcome != ScavengeOutcome.Exploring || Survival.sleeping || Survival.action != SurvivalAction.None ||
                Survival.attackCooldown > 0 || Survival.weapon == null || OpenCache >= 0) return false;
            bool pistol = Survival.weapon.item == SurvivalItem.Pistol;
            if (pistol && Survival.weapon.rounds <= 0) return ReportSurvival("空仓 · R 装填");
            var direction = (aim - Player).normalized;
            if (direction == Vector2.zero) return false;
            Survival.facing = direction;
            Survival.attackCooldown = pistol ? .35f : .8f;
            if (pistol)
            {
                Survival.weapon.rounds--;
                gunshot = Player;
                gunshotRemaining = .4f;
            }
            else Survival.energy = Mathf.Max(0, Survival.energy - .5f);

            float range = pistol ? 24 : 1.5f;
            ScavengePatrol target = null;
            int hit = -1;
            if (pistol && aimedHead >= 0 && aimedHead < patrols.Length)
            {
                var head = patrols[aimedHead];
                if (head.Alive && Vector2.Distance(head.Position, Player) <= range && ClearRunLine(Player, head.Position))
                {
                    target = head;
                    hit = aimedHead;
                    range = Vector2.Distance(head.Position, Player);
                }
            }
            for (int i = 0; i < patrols.Length; i++)
            {
                var candidate = patrols[i];
                var offset = candidate.Position - Player;
                float distance = offset.magnitude;
                if (!candidate.Alive || distance >= range || !ClearRunLine(Player, candidate.Position)) continue;
                if (pistol && (Vector2.Dot(direction, offset) <= 0 ||
                    (offset - direction * Vector2.Dot(direction, offset)).sqrMagnitude > .24f)) continue;
                if (!pistol && Vector2.Dot(direction, offset.normalized) < .45f) continue;
                target = candidate;
                range = distance;
                hit = i;
            }
            if (target != null)
            {
                target.Health = pistol && hit == aimedHead ? 0 : target.Health - 1;
                target.Cooldown = Mathf.Max(target.Cooldown, .4f);
            }
            if (pistol)
            {
                GunshotEnd = target == null ? Player + direction * range : target.Position;
                for (float distance = .2f; distance <= range; distance += .2f)
                {
                    var point = Player + direction * distance;
                    if (ClearRunLine(Player, point)) continue;
                    GunshotEnd = point;
                    break;
                }
            }
            return pistol;
        }

        internal bool ToggleSurvivalTorch()
        {
            if (Survival.torch == null || Survival.torch.charge <= 0) return ReportSurvival("需要装上有电的手电");
            Survival.torchOn = !Survival.torchOn;
            return true;
        }

        /// <summary>道具效果与消耗在完成时一起提交，取消不会扣除道具。</summary>
        internal bool BeginSurvivalAction(SurvivalAction action, int index = -1)
        {
            if (Outcome != ScavengeOutcome.Exploring || Survival.sleeping || Survival.action != SurvivalAction.None)
                return false;
            int room = NearbySurvivalDoor;
            SurvivalItem item;
            float time;
            switch (action)
            {
                case SurvivalAction.Drink:
                    if (Survival.water >= 100) return ReportSurvival("现在不渴");
                    item = SurvivalItem.Water; time = 2; break;
                case SurvivalAction.Eat:
                    if (Survival.food >= 100) return ReportSurvival("现在不饿");
                    item = SurvivalItem.Food; time = 4; break;
                case SurvivalAction.Bandage:
                    if (!Survival.bleeding) return ReportSurvival("没有出血");
                    item = SurvivalItem.Bandage; time = 3; break;
                case SurvivalAction.Aid:
                    if (Survival.health >= 100) return ReportSurvival("生命已满");
                    item = SurvivalItem.Aid; time = 6; break;
                case SurvivalAction.Reload:
                    if (Survival.weapon?.item != SurvivalItem.Pistol || Survival.weapon.rounds >= 6)
                        return ReportSurvival("需要装备手枪且枪内有空位");
                    item = SurvivalItem.Ammo; time = 2.5f; break;
                case SurvivalAction.Battery:
                    if (Survival.torch == null) return ReportSurvival("先装备手电");
                    item = SurvivalItem.Battery; time = 3; break;
                default:
                    if (room < 0 || Survival.baseRoom != room || Survival.rooms[room].durability == 0)
                        return ReportSurvival("需要在基地完好的入口旁操作");
                    var door = Survival.rooms[room];
                    if (action == SurvivalAction.Fortify && !door.fortified) { item = SurvivalItem.Boards; time = 20; }
                    else if (action == SurvivalAction.Repair && door.durability < door.MaxDurability)
                    { item = SurvivalItem.Parts; time = 8; }
                    else if (action == SurvivalAction.Alarm && !door.alarm && Survival.Find(SurvivalItem.Battery) >= 0)
                    { item = SurvivalItem.Alarm; time = 10; }
                    else if (action == SurvivalAction.AlarmBattery && door.alarm && door.battery <= 0)
                    { item = SurvivalItem.Battery; time = 3; }
                    else return ReportSurvival("当前入口无法进行这项操作");
                    break;
            }

            if (index < 0) index = Survival.Find(item);
            if (index < 0 || index >= Survival.pack.Count || Survival.pack[index].item != item)
                return ReportSurvival("缺少所需道具");
            Survival.action = action;
            Survival.actionTime = time;
            Survival.actionIndex = index;
            Survival.actionRoom = room;
            SurvivalFeedback = "";
            CloseSearch();
            return true;
        }

        internal void CancelSurvivalAction()
        {
            if (Survival.action == SurvivalAction.None) return;
            Survival.action = SurvivalAction.None;
            Survival.actionTime = 0;
            ReportSurvival("操作已中断");
        }

        private void CompleteSurvivalAction()
        {
            var action = Survival.action;
            var index = Survival.actionIndex;
            int amount = 1;
            Survival.action = SurvivalAction.None;
            var room = Survival.actionRoom >= 0 ? Survival.rooms[Survival.actionRoom] : null;
            if ((int)action >= (int)SurvivalAction.Fortify &&
                (room == null || room.durability == 0 || Survival.actionRoom != Survival.baseRoom))
            {
                ReportSurvival("入口状态已变，操作中断");
                return;
            }

            switch (action)
            {
                case SurvivalAction.Drink: Survival.water = Mathf.Min(100, Survival.water + 35); break;
                case SurvivalAction.Eat: Survival.food = Mathf.Min(100, Survival.food + 28); break;
                case SurvivalAction.Bandage: Survival.bleeding = false; Survival.bleedClock = 0; break;
                case SurvivalAction.Aid: Survival.health = Mathf.Min(100, Survival.health + 25); break;
                case SurvivalAction.Reload:
                    int needed = 6 - Survival.weapon.rounds;
                    for (int i = Survival.pack.Count - 1; i >= 0 && needed > 0; i--)
                    {
                        if (Survival.pack[i].item != SurvivalItem.Ammo) continue;
                        int loaded = Mathf.Min(needed, Survival.pack[i].count);
                        Survival.Consume(i, loaded);
                        Survival.weapon.rounds += loaded;
                        needed -= loaded;
                    }
                    ReportSurvival("装填完成");
                    return;
                case SurvivalAction.Battery: Survival.torch.charge = 360; break;
                case SurvivalAction.Fortify: room.fortified = true; room.durability += 40; break;
                case SurvivalAction.Repair: room.durability = Mathf.Min(room.MaxDurability, room.durability + 25); break;
                case SurvivalAction.Alarm: room.alarm = room.armed = true; room.battery = 180; break;
                case SurvivalAction.AlarmBattery: room.battery = 180; break;
            }
            Survival.Consume(index, amount);
            if (action == SurvivalAction.Alarm) Survival.Consume(Survival.Find(SurvivalItem.Battery));
            ReportSurvival("操作完成");
        }

        internal bool ToggleSurvivalAlarm()
        {
            int index = NearbySurvivalDoor;
            if (index < 0 || !Survival.rooms[index].alarm) return false;
            var room = Survival.rooms[index];
            if (room.battery <= 0) return ReportSurvival("警报器需要更换电池");
            room.armed = !room.armed;
            if (!room.armed) room.ringing = 0;
            return ReportSurvival(room.armed ? "警报器已布防" : "警报器已关闭", true);
        }

        internal bool ReportSurvival(string text, bool success = false)
        {
            SurvivalFeedback = text;
            return success;
        }

        internal SurvivalSave CaptureSurvival()
        {
            var snapshot = new SurvivalSave
            {
                available = true, seed = Seed, player = Player, exitKnown = ExitKnown, run = Survival,
                gunshot = gunshot, gunshotRemaining = gunshotRemaining, patrols = new SurvivalPatrolSave[patrols.Length]
            };
            for (int i = 0; i < patrols.Length; i++)
            {
                var p = patrols[i];
                snapshot.patrols[i] = new SurvivalPatrolSave
                {
                    position = p.Position, facing = p.Facing, health = p.Health, towardEnd = p.TowardEnd,
                    lastSeen = p.LastSeen, routePoint = p.RoutePoint, cooldown = p.Cooldown, memory = p.Memory,
                    routeCooldown = p.RouteCooldown, noise = p.Noise.GetValueOrDefault(),
                    hasNoise = p.Noise.HasValue, noiseRemaining = p.NoiseRemaining
                };
            }
            return snapshot;
        }

        internal static ScavengeRun RestoreSurvival(SurvivalSave save)
        {
            var restored = new ScavengeRun(save.seed, true, save.run)
            {
                Player = save.player, ExitKnown = save.exitKnown,
                gunshot = save.gunshot, gunshotRemaining = save.gunshotRemaining
            };
            for (int i = 0; i < restored.patrols.Length; i++)
            {
                var p = restored.patrols[i];
                var state = save.patrols[i];
                p.Position = state.position; p.Facing = state.facing; p.Health = state.health;
                p.TowardEnd = state.towardEnd; p.LastSeen = state.lastSeen; p.RoutePoint = state.routePoint;
                p.Cooldown = state.cooldown; p.Memory = state.memory; p.RouteCooldown = state.routeCooldown;
                p.Noise = state.hasNoise ? state.noise : (Vector2?)null; p.NoiseRemaining = state.noiseRemaining;
            }
            return restored;
        }
    }
}
