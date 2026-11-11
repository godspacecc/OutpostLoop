using System;
using System.IO;
using Game;
using UnityEngine;

namespace OutpostLoop
{
    [Flags]
    public enum ScavengeItem
    {
        None = 0, Supplies = 1, Crowbar = 2, Signal = 4, Materials = 8,
        DefenseParts = 16, RepairMaterials = 32, RepairKit = 64
    }
    public enum ScavengeOutcome { Exploring, Extracted, Died }
    public enum ShelterPlacement { Available, MissingMaterials, AlreadyPlaced, Obstructed, TooFar, NeedsShelter }

    /// <summary>首张临时探索图，障碍范围同时用于渲染与地面通行。</summary>
    public static class ScavengeLayout
    {
        public static readonly Vector2 Start = new Vector2(-8, -7);
        public static readonly Vector2 Exit = new Vector2(-5, 8);
        public static readonly Vector2[] Caches =
        {
            new Vector2(-6, -4), new Vector2(-.8f, -3), new Vector2(6.3f, 2), new Vector2(8.62f, -4.02f)
        };
        // 两块空地包含多个落位，避开两条主通路和侧门。
        public static readonly Rect[] ShelterGrounds =
        {
            new Rect(-4.35f, -8, 3.3f, 3.25f),
            new Rect(6.9f, 7.7f, 4.1f, 2.45f)
        };
        public static readonly Rect[] Walls =
        {
            new Rect(.5f, -1.6f, .7f, 9),
            new Rect(.5f, -5.3f, 9.8f, .7f),
            new Rect(9.6f, -5.3f, .7f, 12.7f),
            new Rect(.5f, 6.7f, 3.6f, .7f),
            new Rect(6.7f, 6.7f, 3.6f, .7f),
            new Rect(2.4f, -.7f, 1.15f, 4.5f),
            new Rect(-2.6f, .3f, 1.6f, 3.4f)
        };

        /// <summary>逐轴移动，保证斜着撞墙时可以沿墙移动。</summary>
        public static Vector2 Move(Vector2 point, Vector2 step)
        {
            var next = new Vector2(Mathf.Clamp(point.x + step.x, -11, 11), point.y);
            if (Blocked(next, .34f)) next.x = point.x;
            next.y = Mathf.Clamp(point.y + step.y, -9, 10);
            if (Blocked(next, .34f)) next.y = point.y;
            return next;
        }

        /// <summary>低墙和货架遮挡敌人观察、近战及拾取。</summary>
        public static bool ClearLine(Vector2 from, Vector2 to, float clearance = 0)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 5);
            for (int i = 1; i <= steps; i++)
                if (Blocked(Vector2.Lerp(from, to, (float)i / steps), clearance)) return false;
            return true;
        }

        private static bool Blocked(Vector2 point, float padding)
        {
            foreach (var wall in Walls)
                if (point.x > wall.xMin - padding && point.x < wall.xMax + padding &&
                    point.y > wall.yMin - padding && point.y < wall.yMax + padding)
                    return true;
            return false;
        }
    }

    /// <summary>一次探索的状态。负责移动、搜取、敌人和终局，独立于镜头、输入与界面。</summary>
    public sealed partial class ScavengeRun
    {
        public const int Capacity = 2;
        public const float SearchSeconds = .6f;
        public const float ShelterWidth = 1.6f;
        public const float ShelterBuildSeconds = 3f;
        private readonly ScavengeItem[] contents =
        {
            ScavengeItem.Supplies | ScavengeItem.Materials, ScavengeItem.Crowbar | ScavengeItem.DefenseParts,
            ScavengeItem.Signal, ScavengeItem.RepairMaterials
        };
        // 每个容器只在首次成功搜索后显示内容。
        private readonly bool[] known = new bool[ScavengeLayout.Caches.Length];
        private float attackCooldown;

        public Vector2 Player { get; private set; } = ScavengeLayout.Start;
        public Vector2 Enemy => patrols[0].Position;
        public Vector2 EnemyFacing => patrols[0].Facing;
        public bool EnemyAlive => patrols[0].Alive;
        public bool Alerted => patrols[0].Engaged || patrols[1].Engaged;
        public int Health { get; private set; } = 3;
        public ScavengeItem Inventory { get; private set; }
        public ScavengeItem CarriedOut { get; private set; }
        public int Count => CountItems(Inventory);
        public int OpenCache { get; private set; } = -1;
        public float SearchRemaining { get; private set; }
        public bool ExitKnown { get; private set; }
        public bool AtExit => ExitKnown && Vector2.Distance(Player, ScavengeLayout.Exit) <= 1.65f;
        public ScavengeOutcome Outcome { get; private set; }
        // 工地和休整机会仅属于当前行动。
        public Vector2? Shelter { get; private set; }
        public float ShelterRemaining { get; private set; }
        public bool Rested { get; private set; }
        public bool NearShelter => Shelter.HasValue && Vector2.Distance(Player, Shelter.Value) <= 1.45f;

        public int NearbyCache
        {
            get
            {
                for (int i = 0; i < ScavengeLayout.Caches.Length; i++)
                    if (Vector2.Distance(Player, ScavengeLayout.Caches[i]) <= 1.65f &&
                        ScavengeLayout.ClearLine(Player, ScavengeLayout.Caches[i])) return i;
                return -1;
            }
        }

        public bool IsSearched(int index) => known[index];
        public ScavengeItem VisibleContents(int index) => known[index] ? contents[index] : ScavengeItem.None;

        /// <summary>推进一帧。移动离开容器或受到伤害会打断搜索。</summary>
        public void Tick(float dt, Vector2 input)
        {
            if (Outcome != ScavengeOutcome.Exploring || dt <= 0) return;
            Player = ScavengeLayout.Move(Player, Vector2.ClampMagnitude(input, 1) * (5 * dt));
            if (Vector2.Distance(Player, ScavengeLayout.Exit) < 4 &&
                ScavengeLayout.ClearLine(Player, ScavengeLayout.Exit)) ExitKnown = true;

            if (OpenCache >= 0)
            {
                if (NearbyCache != OpenCache) CloseSearch();
                else
                {
                    SearchRemaining = Mathf.Max(0, SearchRemaining - dt);
                    if (SearchRemaining == 0) known[OpenCache] = true;
                }
            }

            attackCooldown = Mathf.Max(0, attackCooldown - dt);
            TickDefense(dt);
            TickWorkshop(dt);
        }

        /// <summary>同一交互键依次处理撤离、开箱或关箱。</summary>
        public bool Interact()
        {
            if (Outcome != ScavengeOutcome.Exploring) return false;
            if (AtExit)
            {
                CarriedOut = Inventory;
                End(ScavengeOutcome.Extracted);
                return true;
            }
            if (OpenCache >= 0)
            {
                CloseSearch();
                return true;
            }
            int cache = NearbyCache;
            if (cache < 0) return NearShelter && Rest();
            OpenCache = cache;
            SearchRemaining = known[cache] ? 0 : SearchSeconds;
            return true;
        }

        /// <summary>在已打开的容器与背包之间转移一件物品。</summary>
        public bool Transfer(ScavengeItem item, bool take)
        {
            if (Outcome != ScavengeOutcome.Exploring || OpenCache < 0 ||
                SearchRemaining > 0 || !IsSingleItem(item)) return false;
            if (take)
            {
                if ((contents[OpenCache] & item) == 0 || Count >= Capacity) return false;
                Inventory |= item;
                contents[OpenCache] &= ~item;
            }
            else
            {
                if ((Inventory & item) == 0) return false;
                Inventory &= ~item;
                contents[OpenCache] |= item;
            }
            return true;
        }

        /// <summary>验证整块占地、施工距离和玩家当前位置，无效预览不会消耗材料。</summary>
        public ShelterPlacement CheckShelter(Vector2 point)
        {
            if (Outcome != ScavengeOutcome.Exploring || Shelter.HasValue || ShelterLost) return ShelterPlacement.AlreadyPlaced;
            if ((Inventory & ScavengeItem.Materials) == 0) return ShelterPlacement.MissingMaterials;
            if ((Player - point).sqrMagnitude > 4.2f * 4.2f) return ShelterPlacement.TooFar;
            float half = ShelterWidth * .5f;
            if (Mathf.Abs(Player.x - point.x) < half + .35f && Mathf.Abs(Player.y - point.y) < half + .35f)
                return ShelterPlacement.Obstructed;
            foreach (var ground in ScavengeLayout.ShelterGrounds)
                if (point.x - half >= ground.xMin && point.x + half <= ground.xMax &&
                    point.y - half >= ground.yMin && point.y + half <= ground.yMax &&
                    ScavengeLayout.ClearLine(Player, point)) return ShelterPlacement.Available;
            return ShelterPlacement.Obstructed;
        }

        /// <summary>落位时扣除一份建材，完成前无法休整。</summary>
        public bool BuildShelter(Vector2 point)
        {
            if (CheckShelter(point) != ShelterPlacement.Available) return false;
            Inventory &= ~ScavengeItem.Materials;
            CloseSearch();
            Shelter = point;
            ShelterRemaining = ShelterBuildSeconds;
            ShelterHealth = ShelterMaxHealth;
            return true;
        }

        /// <summary>每趟只可恢复一格生命，满血时保留机会。</summary>
        private bool Rest()
        {
            if (Rested || ShelterRemaining > 0 || Health >= 3) return false;
            Health++;
            Rested = true;
            return true;
        }

        /// <summary>装备撬棍后才可近战。攻击有间隔、距离、方向和障碍约束。</summary>
        public bool Attack(Vector2 direction)
        {
            if (Outcome != ScavengeOutcome.Exploring || OpenCache >= 0 ||
                (Inventory & ScavengeItem.Crowbar) == 0 || attackCooldown > 0) return false;
            attackCooldown = .55f;
            ScavengePatrol target = null;
            float best = 1.75f * 1.75f;
            foreach (var patrol in patrols)
            {
                var delta = patrol.Position - Player;
                if (!patrol.Alive || delta.sqrMagnitude > best ||
                    (delta.sqrMagnitude > .001f && Vector2.Dot(direction.normalized, delta.normalized) < .45f) ||
                    !ScavengeLayout.ClearLine(Player, patrol.Position)) continue;
                target = patrol;
                best = delta.sqrMagnitude;
            }
            if (target != null) target.Health--;
            return true;
        }

        public void CloseSearch()
        {
            OpenCache = -1;
            SearchRemaining = 0;
        }

        private void End(ScavengeOutcome outcome)
        {
            Outcome = outcome;
            Inventory = ScavengeItem.None;
            Shelter = null;
            ShelterRemaining = 0;
            Defense = null;
            DefenseRemaining = 0;
            ClearShelterStorage();
            CloseSearch();
        }

        internal const ScavengeItem AllItems = ScavengeItem.Supplies | ScavengeItem.Crowbar | ScavengeItem.Signal |
            ScavengeItem.Materials | ScavengeItem.DefenseParts | ScavengeItem.RepairMaterials | ScavengeItem.RepairKit;

        private static bool IsSingleItem(ScavengeItem item) =>
            item != ScavengeItem.None && (item & AllItems) == item && (((int)item & ((int)item - 1)) == 0);

        private static int CountItems(ScavengeItem items)
        {
            int bits = (int)(items & AllItems);
            int count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }
            return count;
        }
    }

    /// <summary>空间保留带回过的物品种类。同一种物品重复撤离只登记一次。</summary>
    public sealed class ScavengeCollection
    {
        private const string FileName = "scavenge-collection.json";
        [Serializable] private sealed class Data { public int items; }
        private readonly JsonFileStore<Data> store;
        public ScavengeItem Items { get; private set; }

        public ScavengeCollection(string directory = null)
        {
            store = new JsonFileStore<Data>(directory);
            var read = store.Read(FileName);
            if (read.Status == SaveFileStatus.Valid) Items = (ScavengeItem)read.Data.items & ScavengeRun.AllItems;
        }

        /// <summary>只提交成功撤离的结果，保存失败可以原地重试。</summary>
        public bool SaveReturn(ScavengeRun run)
        {
            if (run.Outcome != ScavengeOutcome.Extracted) return false;
            var next = Items | run.CarriedOut;
            if (next == Items) return true;
            try
            {
                store.Save(FileName, new Data { items = (int)next });
                Items = next;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warning("空间物品登记失败：" + error.GetType().Name);
                return false;
            }
        }
    }
}
