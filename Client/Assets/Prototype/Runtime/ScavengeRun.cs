using System;
using UnityEngine;

namespace OutpostLoop
{
    [Flags]
    public enum ScavengeItem
    {
        None = 0, Supplies = 1, Crowbar = 2, Signal = 4, Materials = 8,
        DefenseParts = 16, RepairMaterials = 32, RepairKit = 64, Pistol = 128, Ammo = 256, ReserveMedicine = 512
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
            new Vector2(-6, -4), new Vector2(-.8f, -3), new Vector2(6.3f, 2), new Vector2(8.62f, -4.02f),
            new Vector2(.02f, -4.05f), new Vector2(-9.35f, -1.1f), new Vector2(-7f, 2.65f)
        };
        public static readonly string[] CacheNames =
        {
            "临街物资箱", "仓门工具箱", "仓内办公室", "维修间", "装卸架", "药房柜台", "路边废车"
        };
        public const int RepairRoom = 3, LoadingRack = 4, Pharmacy = 5, AbandonedCar = 6;
        public static readonly Rect PharmacyFacade = new Rect(-11f, -2.3f, .8f, 2.45f);
        public static readonly Rect CarBody = new Rect(-8.54f, 2.06f, 1.02f, 1.86f);
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
            new Rect(-2.6f, .3f, 1.6f, 3.4f),
            PharmacyFacade, CarBody
        };

        /// <summary>逐轴移动，保证斜着撞墙时可以沿墙移动。</summary>
        public static Vector2 Move(Vector2 point, Vector2 step) => Move(point, step, new Rect(-11, -9, 22, 19), Walls);

        internal static Vector2 Move(Vector2 point, Vector2 step, Rect bounds, Rect[] walls)
        {
            var next = new Vector2(Mathf.Clamp(point.x + step.x, bounds.xMin, bounds.xMax), point.y);
            if (Blocked(next, .34f, walls)) next.x = point.x;
            next.y = Mathf.Clamp(point.y + step.y, bounds.yMin, bounds.yMax);
            if (Blocked(next, .34f, walls)) next.y = point.y;
            return next;
        }

        /// <summary>低墙和货架遮挡敌人观察、近战及拾取。</summary>
        public static bool ClearLine(Vector2 from, Vector2 to, float clearance = 0) => ClearLine(from, to, clearance, Walls);

        internal static bool ClearLine(Vector2 from, Vector2 to, float clearance, Rect[] walls)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 5);
            for (int i = 1; i <= steps; i++)
                if (Blocked(Vector2.Lerp(from, to, (float)i / steps), clearance, walls)) return false;
            return true;
        }

        private static bool Blocked(Vector2 point, float padding, Rect[] walls)
        {
            foreach (var wall in walls)
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
        public const int MaxHealth = 3;
        public const float SearchSeconds = .6f;
        public const float ShelterWidth = 1.6f;
        public const float ShelterBuildSeconds = 3f;
        private readonly ScavengeItem[] contents;
        // 走近才在地图上登记；成功搜索才公开存货。
        private readonly bool[] discovered;
        // 每个容器只在首次成功搜索后显示内容。
        private readonly bool[] known;
        private float attackCooldown;

        public Vector2 Player { get; private set; }
        public Vector2 Enemy => patrols[0].Position;
        public Vector2 EnemyFacing => patrols[0].Facing;
        public bool EnemyAlive => patrols[0].Alive;
        public bool Alerted
        {
            get { foreach (var patrol in patrols) if (patrol.Engaged) return true; return false; }
        }
        public int Seed { get; }
        public int Health { get; private set; } = MaxHealth;
        public ScavengeItem Inventory { get; private set; }
        // 仅用于本趟结算的撤离快照，实际背包在终局清空。
        public ScavengeItem CarriedOut { get; private set; }
        public int Count => CountItems(Inventory);
        public int OpenCache { get; private set; } = -1;
        public float SearchRemaining { get; private set; }
        public bool ExitKnown { get; private set; }
        public bool AtExit => ExitKnown && Vector2.Distance(Player, ExitPoint) <= 1.65f;
        public ScavengeOutcome Outcome { get; private set; }
        // 工地和休整机会仅属于当前行动。
        public Vector2? Shelter { get; private set; }
        public float ShelterRemaining { get; private set; }
        public bool Rested { get; private set; }
        public bool NearShelter => Shelter.HasValue && Vector2.Distance(Player, Shelter.Value) <= 1.45f;

        /// <summary>种子决定两个相互独立的物资位置选择，固定地标维持相同的路线。</summary>
        public ScavengeRun(int seed = 0, bool town = false)
        {
            Seed = seed;
            IsTown = town;
            Player = town ? ScavengeTown.Start : ScavengeLayout.Start;
            contents = new ScavengeItem[CachePoints.Length];
            discovered = new bool[contents.Length];
            known = new bool[contents.Length];
            if (town)
            {
                var random = new System.Random(seed);
                ScavengeTown.FillLoot(contents, random, out medicineCache);
                patrols = ScavengeTown.CreatePatrols(random);
                return;
            }
            patrols = CreateTrialPatrols();
            contents[0] = ScavengeItem.Materials;
            contents[1] = ScavengeItem.Crowbar;
            contents[2] = ScavengeItem.Signal;
            bool swapParts = (seed & 2) != 0;
            contents[ScavengeLayout.RepairRoom] = swapParts ? ScavengeItem.DefenseParts : ScavengeItem.RepairMaterials;
            contents[ScavengeLayout.LoadingRack] = swapParts ? ScavengeItem.RepairMaterials : ScavengeItem.DefenseParts;
            contents[(seed & 1) == 0 ? ScavengeLayout.Pharmacy : ScavengeLayout.AbandonedCar] = ScavengeItem.Supplies;
        }

        public int NearbyCache
        {
            get
            {
                int closest = -1;
                float closestDistance = 1.65f * 1.65f;
                for (int i = 0; i < CachePoints.Length; i++)
                {
                    var position = CachePoints[i];
                    float distance = (Player - position).sqrMagnitude;
                    if (distance > closestDistance || !ClearRunLine(Player, position)) continue;
                    closest = i;
                    closestDistance = distance;
                }
                return closest;
            }
        }

        public bool IsDiscovered(int index) => discovered[index];
        public bool IsSearched(int index) => known[index];
        public ScavengeItem VisibleContents(int index) => known[index] ? contents[index] : ScavengeItem.None;

        /// <summary>推进一帧。移动离开容器或受到伤害会打断搜索。</summary>
        public void Tick(float dt, Vector2 input)
        {
            if (Outcome != ScavengeOutcome.Exploring || dt <= 0) return;
            Player = MoveInRun(Player, Vector2.ClampMagnitude(input, 1) * (5 * dt));
            for (int i = 0; i < discovered.Length; i++)
                if (!discovered[i] && CanReachCache(i)) discovered[i] = true;
            if (Vector2.Distance(Player, ExitPoint) < 4 &&
                ClearRunLine(Player, ExitPoint)) ExitKnown = true;

            if (OpenCache >= 0)
            {
                if (!CanReachCache(OpenCache)) CloseSearch();
                else
                {
                    SearchRemaining = Mathf.Max(0, SearchRemaining - dt);
                    if (SearchRemaining == 0)
                    {
                        known[OpenCache] = true;
                        if (IsTown && OpenCache == ScavengeTown.Manifest) RevealMedicineLead();
                    }
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
            if (CanTalk)
            {
                SurvivorSpoken = true;
                RevealMedicineLead();
                return true;
            }
            if (cache < 0) return NearShelter && Rest();
            discovered[cache] = true;
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
                if ((contents[OpenCache] & item) == 0 || (Inventory & item) != 0 || Count >= Capacity) return false;
                Inventory |= item;
                contents[OpenCache] &= ~item;
                if (item == ScavengeItem.ReserveMedicine) MedicineFound = true;
                if (item == ScavengeItem.Pistol && (Inventory & ScavengeItem.Crowbar) == 0) FirearmSelected = true;
            }
            else
            {
                if ((Inventory & item) == 0 || (contents[OpenCache] & item) != 0) return false;
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
            foreach (var ground in BuildGrounds)
                if (point.x - half >= ground.xMin && point.x + half <= ground.xMax &&
                    point.y - half >= ground.yMin && point.y + half <= ground.yMax &&
                    ClearRunLine(Player, point)) return ShelterPlacement.Available;
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
            if (Rested || ShelterRemaining > 0 || Health >= MaxHealth) return false;
            Health++;
            Rested = true;
            return true;
        }

        /// <summary>受伤且携带急救包才消耗一份，背包腾出一格。</summary>
        public bool UseFirstAid()
        {
            if (Outcome != ScavengeOutcome.Exploring || Health >= MaxHealth ||
                (Inventory & ScavengeItem.Supplies) == 0) return false;
            Inventory &= ~ScavengeItem.Supplies;
            Health++;
            return true;
        }

        private bool CanReachCache(int index) => Vector2.Distance(Player, CachePoints[index]) <= 1.65f &&
            ClearRunLine(Player, CachePoints[index]);

        /// <summary>装备撬棍后才可近战。攻击有间隔、距离、方向和障碍约束。</summary>
        public bool Attack(Vector2 direction)
        {
            if (Outcome != ScavengeOutcome.Exploring || OpenCache >= 0 || attackCooldown > 0) return false;
            if (UsingFirearm) return Fire(direction);
            if ((Inventory & ScavengeItem.Crowbar) == 0) return false;
            attackCooldown = .55f;
            ScavengePatrol target = null;
            float best = 1.75f * 1.75f;
            foreach (var patrol in patrols)
            {
                var delta = patrol.Position - Player;
                if (!patrol.Alive || delta.sqrMagnitude > best ||
                    (delta.sqrMagnitude > .001f && Vector2.Dot(direction.normalized, delta.normalized) < .45f) ||
                    !ClearRunLine(Player, patrol.Position)) continue;
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

        private const ScavengeItem AllItems = ScavengeItem.Supplies | ScavengeItem.Crowbar | ScavengeItem.Signal |
            ScavengeItem.Materials | ScavengeItem.DefenseParts | ScavengeItem.RepairMaterials | ScavengeItem.RepairKit |
            ScavengeItem.Pistol | ScavengeItem.Ammo | ScavengeItem.ReserveMedicine;

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
}
