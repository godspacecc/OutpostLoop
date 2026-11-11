using System;
using System.IO;
using Game;
using UnityEngine;

namespace OutpostLoop
{
    [Flags]
    public enum ScavengeItem { None = 0, Supplies = 1, Crowbar = 2, Signal = 4 }
    public enum ScavengeOutcome { Exploring, Extracted, Died }

    /// <summary>首张临时探索图，障碍范围同时用于渲染与地面通行。</summary>
    public static class ScavengeLayout
    {
        public static readonly Vector2 Start = new Vector2(-8, -7);
        public static readonly Vector2 Exit = new Vector2(-5, 8);
        public static readonly Vector2[] Caches =
        {
            new Vector2(-6, -4), new Vector2(-.8f, -3), new Vector2(6.3f, 2)
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
        public static bool ClearLine(Vector2 from, Vector2 to)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 5);
            for (int i = 1; i <= steps; i++)
                if (Blocked(Vector2.Lerp(from, to, (float)i / steps), 0)) return false;
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
    public sealed class ScavengeRun
    {
        public const int Capacity = 2;
        public const float SearchSeconds = .6f;
        private readonly ScavengeItem[] contents = { ScavengeItem.Supplies, ScavengeItem.Crowbar, ScavengeItem.Signal };
        // 每个容器只在首次成功搜索后显示内容。
        private readonly bool[] known = new bool[ScavengeLayout.Caches.Length];
        private static readonly Vector2 PatrolSouth = new Vector2(7.8f, -2.8f);
        private static readonly Vector2 PatrolNorth = new Vector2(7.8f, 5.5f);
        private bool headingNorth = true;
        private float attackCooldown;
        private float enemyCooldown;
        private int enemyHealth = 2;

        public Vector2 Player { get; private set; } = ScavengeLayout.Start;
        public Vector2 Enemy { get; private set; } = PatrolSouth;
        public Vector2 EnemyFacing { get; private set; } = Vector2.up;
        public bool EnemyAlive => enemyHealth > 0;
        public bool Alerted { get; private set; }
        public int Health { get; private set; } = 3;
        public ScavengeItem Inventory { get; private set; }
        public ScavengeItem CarriedOut { get; private set; }
        public int Count => CountItems(Inventory);
        public int OpenCache { get; private set; } = -1;
        public float SearchRemaining { get; private set; }
        public bool ExitKnown { get; private set; }
        public bool AtExit => ExitKnown && Vector2.Distance(Player, ScavengeLayout.Exit) <= 1.65f;
        public ScavengeOutcome Outcome { get; private set; }

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
            if (EnemyAlive) TickEnemy(dt);
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
            if (cache < 0) return false;
            OpenCache = cache;
            SearchRemaining = known[cache] ? 0 : SearchSeconds;
            return true;
        }

        /// <summary>在已打开的容器与背包之间转移一件物品。</summary>
        public bool Transfer(ScavengeItem item, bool take)
        {
            if (Outcome != ScavengeOutcome.Exploring || OpenCache < 0 ||
                SearchRemaining > 0 ||
                (item != ScavengeItem.Supplies && item != ScavengeItem.Crowbar && item != ScavengeItem.Signal)) return false;
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

        /// <summary>装备撬棍后才可近战。攻击有间隔、距离、方向和障碍约束。</summary>
        public bool Attack(Vector2 direction)
        {
            if (Outcome != ScavengeOutcome.Exploring || OpenCache >= 0 ||
                (Inventory & ScavengeItem.Crowbar) == 0 || attackCooldown > 0) return false;
            attackCooldown = .55f;
            var toEnemy = Enemy - Player;
            if (EnemyAlive && toEnemy.sqrMagnitude <= 1.75f * 1.75f &&
                Vector2.Dot(direction.normalized, toEnemy.normalized) >= .45f &&
                ScavengeLayout.ClearLine(Player, Enemy))
                enemyHealth--;
            return true;
        }

        /// <summary>敌人沿仓内通道巡逻，失去视线时退回巡逻轴线。</summary>
        private void TickEnemy(float dt)
        {
            var toPlayer = Player - Enemy;
            var distance = toPlayer.magnitude;
            Alerted = distance <= (Alerted ? 7f : 5f) && Player.x > 1.25f &&
                ScavengeLayout.ClearLine(Enemy, Player) &&
                (Alerted || distance <= 1.7f || Vector2.Dot(EnemyFacing, toPlayer.normalized) > .22f);

            enemyCooldown = Mathf.Max(0, enemyCooldown - dt);
            if (Alerted && distance < 1.25f)
            {
                EnemyFacing = toPlayer.normalized;
                if (enemyCooldown == 0)
                {
                    enemyCooldown = 1.05f;
                    Health--;
                    CloseSearch();
                    if (Health <= 0) End(ScavengeOutcome.Died);
                }
                return;
            }

            var patrolEnd = headingNorth ? PatrolNorth : PatrolSouth;
            var patrolAxis = new Vector2(PatrolSouth.x, Mathf.Clamp(Enemy.y, PatrolSouth.y, PatrolNorth.y));
            var goal = patrolEnd;
            if (Alerted) goal = Player;
            else if (Vector2.Distance(Enemy, patrolAxis) > .1f &&
                ScavengeLayout.ClearLine(Enemy, patrolAxis)) goal = patrolAxis;
            var next = Vector2.MoveTowards(Enemy, goal, (Alerted ? 4.15f : 2.05f) * dt);
            var moved = ScavengeLayout.Move(Enemy, next - Enemy);
            if ((moved - Enemy).sqrMagnitude > .000001f) EnemyFacing = (moved - Enemy).normalized;
            Enemy = moved;
            if (!Alerted && Vector2.Distance(Enemy, patrolEnd) < .12f) headingNorth = !headingNorth;
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
            CloseSearch();
        }

        private static int CountItems(ScavengeItem items) =>
            ((items & ScavengeItem.Supplies) != 0 ? 1 : 0) +
            ((items & ScavengeItem.Crowbar) != 0 ? 1 : 0) +
            ((items & ScavengeItem.Signal) != 0 ? 1 : 0);
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
            if (read.Status == SaveFileStatus.Valid) Items = (ScavengeItem)(read.Data.items & 7);
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
