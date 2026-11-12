using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("OutpostLoop.Tests")]
#endif

namespace OutpostLoop
{
    internal enum SurvivalItem { Water, Food, Bandage, Aid, Crowbar, Pistol, Ammo, Torch, Battery, Boards, Parts, Alarm }
    internal enum SurvivalAction { None, Drink, Eat, Bandage, Aid, Reload, Battery, Fortify, Repair, Alarm, AlarmBattery }

    /// <summary>物品的数量及装入其中的电池或弹药，随物品在容器之间移动。</summary>
    [Serializable]
    internal sealed class SurvivalStack
    {
        public SurvivalItem item;
        public int count;
        public float charge;
        public int rounds;

        public SurvivalStack(SurvivalItem item, int count = 1)
        {
            this.item = item;
            this.count = count;
            charge = item == SurvivalItem.Torch ? 360 : 0;
        }

        public int Slots => item == SurvivalItem.Boards ? 2 : 1;
        public int StackLimit => item switch
        {
            SurvivalItem.Ammo => 6,
            SurvivalItem.Water or SurvivalItem.Food or SurvivalItem.Bandage or SurvivalItem.Parts => 2,
            _ => 1
        };
    }

    /// <summary>搜点、储物柜或地面物品的持久状态。</summary>
    [Serializable]
    internal sealed class SurvivalContainer
    {
        public List<SurvivalStack> items = new List<SurvivalStack>();
        public Vector2 point;
        public bool searched;
        public bool known;
        public bool active;
    }

    /// <summary>现成房间的门、加固与本地报警状态。</summary>
    [Serializable]
    internal sealed class SurvivalRoom
    {
        public bool open;
        public bool fortified;
        public int durability = 60;
        public bool alarm;
        public bool armed;
        public float battery;
        public float ringing;
        public float impact;
        public SurvivalContainer storage = new SurvivalContainer();
        public int MaxDurability => fortified ? 100 : 60;
        public bool Blocks => !open && durability > 0;
    }

    /// <summary>室内设施的位置与移动碰撞共用同一平面布局。</summary>
    internal static class SurvivalRooms
    {
        public static readonly int[] BuildingIds = { 2, 3 };
        public static readonly string[] Names = { "商铺后屋", "住宅" };
        public static Rect Footprint(int index) => ScavengeTown.Buildings[BuildingIds[index]];
        public static Vector2 Bed(int index)
        {
            var rect = Footprint(index);
            return new Vector2(rect.xMax - 1.25f, rect.yMax - 1.5f);
        }

        public static Vector2 Cabinet(int index)
        {
            var rect = Footprint(index);
            return new Vector2(rect.xMax - 1.2f, rect.yMin + 1.2f);
        }

        public static Rect Door(int index)
        {
            var rect = Footprint(index);
            return new Rect(rect.xMin, rect.center.y - .8f, .28f, 1.6f);
        }

        public static Rect[] Walls(int index)
        {
            var r = Footprint(index);
            var door = Door(index);
            return new[]
            {
                new Rect(r.xMin, r.yMin, .28f, door.yMin - r.yMin),
                new Rect(r.xMin, door.yMax, .28f, r.yMax - door.yMax),
                new Rect(r.xMax - .28f, r.yMin, .28f, r.height),
                new Rect(r.xMin, r.yMin, r.width, .28f),
                new Rect(r.xMin, r.yMax - .28f, r.width, .28f)
            };
        }

        public static Rect[] Collision(SurvivalRoom[] rooms)
        {
            var walls = new List<Rect>(ScavengeTown.Walls.Length + 12);
            for (int i = 0; i < ScavengeTown.Buildings.Length; i++)
                if (Array.IndexOf(BuildingIds, i) < 0) walls.Add(ScavengeTown.Buildings[i]);
            walls.AddRange(ScavengeTown.WarehouseWalls);
            for (int i = 0; i < rooms.Length; i++)
            {
                walls.AddRange(Walls(i));
                if (rooms[i].Blocks) walls.Add(Door(i));
            }
            return walls.ToArray();
        }
    }

    /// <summary>完整记录生存行动的身体、装备、设施与时间。</summary>
    [Serializable]
    internal sealed class ScavengeSurvival
    {
        public const int Day = 24 * 60, Goal = 3 * Day, Deadline = Goal + 120, Capacity = 6;
        public static readonly int[] ActiveCaches = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14 };

        public double elapsed;
        public float health = 100, water = 80, food = 80, energy = 80;
        public float dehydration, starvation, bleedClock, lastHit = -11, attackCooldown;
        public bool bleeding, sleeping, torchOn;
        public int baseRoom = -1, sleepRoom = -1;
        public Vector2 facing = Vector2.up;
        public SurvivalAction action;
        public float actionTime;
        public int actionIndex, actionRoom;
        public List<SurvivalStack> pack = new List<SurvivalStack>();
        [SerializeReference] public SurvivalStack weapon;
        [SerializeReference] public SurvivalStack torch;
        public SurvivalContainer[] caches;
        public SurvivalRoom[] rooms = { new SurvivalRoom(), new SurvivalRoom() };
        public List<SurvivalContainer> drops = new List<SurvivalContainer>();

        public bool Ready => elapsed >= Goal;
        public bool Warning => elapsed >= Goal - 300;
        public float Hour => (float)((8 + elapsed / 60) % 24);
        public bool Night => Hour < 6 || Hour >= 19;
        public float Light => torchOn && torch != null ? Mathf.Clamp01(torch.charge / 60) : 0;
        public int Used => Slots(pack);
        public bool Weak => water < 25 || energy < 25;

        /// <summary>固定起点补给，其余在可绕行路线和高风险区域内重排。</summary>
        public static ScavengeSurvival Create(int seed)
        {
            var result = new ScavengeSurvival { caches = new SurvivalContainer[ScavengeTown.Caches.Length] };
            for (int i = 0; i < result.caches.Length; i++)
                result.caches[i] = new SurvivalContainer
                    { point = ScavengeTown.Caches[i], active = Array.IndexOf(ActiveCaches, i) >= 0 };
            var random = new System.Random(seed);
            int[] nearby = { 1, 2, 3, 4, 5, 6, 7 };
            int[] distant = { 8, 10, 12, 14 };
            void Add(int index, SurvivalItem item, int count = 1) =>
                result.caches[index].items.Add(new SurvivalStack(item, count));
            void Distribute(SurvivalItem item, int count, int[] locations)
            {
                var order = new List<int>(locations);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int index = random.Next(i + 1);
                    (order[i], order[index]) = (order[index], order[i]);
                }
                for (int i = 0; i < count; i++) Add(order[i % order.Count], item);
            }
            Add(0, SurvivalItem.Water);
            Add(0, SurvivalItem.Food);
            Distribute(SurvivalItem.Water, 6, nearby);
            Distribute(SurvivalItem.Water, 5, distant);
            Distribute(SurvivalItem.Food, 4, nearby);
            Distribute(SurvivalItem.Food, 3, distant);
            Distribute(SurvivalItem.Bandage, 3, nearby);
            Distribute(SurvivalItem.Aid, 2, distant);
            Add(3, SurvivalItem.Crowbar);
            Add(5, SurvivalItem.Torch);
            Add(10, SurvivalItem.Battery);
            Add(12, SurvivalItem.Alarm);
            Add(8, SurvivalItem.Pistol);
            Add(14, SurvivalItem.Ammo, 6);
            Add(4, SurvivalItem.Boards);
            Add(10, SurvivalItem.Boards);
            Distribute(SurvivalItem.Parts, 3, distant);
            return result;
        }

        /// <summary>按实际活动连续推进身体状态。退出和暂停时由会话停止调用。</summary>
        public void Advance(float dt, bool running)
        {
            elapsed = Math.Min(Deadline, elapsed + dt);
            float hours = dt / 60;
            water = Mathf.Max(0, water - hours * (sleeping ? 2 : running ? 6 : 4));
            food = Mathf.Max(0, food - hours * (sleeping ? 1 : running ? 3 : 2));
            energy = Mathf.Clamp(energy + hours * (sleeping ? 18 : (running ? -4 : -2) - (food < 25 ? 1 : 0)), 0, 100);
            dehydration = water > 0 ? 0 : dehydration + dt;
            starvation = food > 0 ? 0 : starvation + dt;
            bleedClock = bleeding ? bleedClock + dt : 0;
            if (dehydration >= 120) { health -= 10; dehydration -= 120; }
            if (starvation >= 240) { health -= 5; starvation -= 240; }
            if (bleedClock >= 30) { health -= 5; bleedClock -= 30; }
            if (torchOn && torch != null)
            {
                torch.charge = Mathf.Max(0, torch.charge - dt);
                if (torch.charge == 0) torchOn = false;
            }
            attackCooldown = Mathf.Max(0, attackCooldown - dt);
            foreach (var room in rooms)
            {
                room.impact = Mathf.Max(0, room.impact - dt);
                if (room.ringing <= 0) continue;
                room.ringing = Mathf.Max(0, room.ringing - dt);
                room.battery = Mathf.Max(0, room.battery - dt);
                if (room.battery == 0) room.ringing = 0;
            }
        }

        /// <summary>先校验格数，再转移一件或一组子弹，保持原道具状态。</summary>
        public static bool Transfer(List<SurvivalStack> source, int index, List<SurvivalStack> target, int capacity)
        {
            if (index < 0 || index >= source.Count || ReferenceEquals(source, target)) return false;
            var item = source[index];
            int amount = item.item == SurvivalItem.Ammo ? item.count : 1;
            var merge = target.Find(s => s.item == item.item && s.count + amount <= s.StackLimit);
            if (merge == null && Slots(target) + item.Slots > capacity) return false;
            if (merge != null) merge.count += amount;
            else if (amount == item.count) target.Add(item);
            else target.Add(new SurvivalStack(item.item, amount));
            item.count -= amount;
            if (item.count == 0)
            {
                if (merge == null) item.count = amount;
                source.RemoveAt(index);
            }
            return true;
        }

        /// <summary>换下的装备使用被换上装备释放的格位。</summary>
        public bool Equip(int index)
        {
            var item = pack[index];
            bool isTorch = item.item == SurvivalItem.Torch;
            if (!isTorch && item.item != SurvivalItem.Pistol && item.item != SurvivalItem.Crowbar) return false;
            var old = isTorch ? torch : weapon;
            pack.RemoveAt(index);
            if (old != null) pack.Insert(index, old);
            if (isTorch) { torch = item; torchOn = false; }
            else weapon = item;
            return true;
        }

        public bool Stow(bool light)
        {
            var item = light ? torch : weapon;
            if (item == null || Used + item.Slots > Capacity) return false;
            pack.Add(item);
            if (light) { torch = null; torchOn = false; }
            else weapon = null;
            return true;
        }

        public static int Slots(List<SurvivalStack> items)
        {
            int used = 0;
            foreach (var stack in items) used += stack.Slots;
            return used;
        }

        public int Find(SurvivalItem item) => pack.FindIndex(stack => stack.item == item);

        public void Consume(int index, int count = 1)
        {
            pack[index].count -= count;
            if (pack[index].count == 0) pack.RemoveAt(index);
        }
    }
}
