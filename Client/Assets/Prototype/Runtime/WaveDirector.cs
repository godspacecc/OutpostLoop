using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>敌种只影响运行规则和临时表现，进场前不公开来向。</summary>
    public enum EnemyKind { Standard, Fast, Siege }

    /// <summary>单个敌人的战斗状态。路线复用同一版地图距离场。</summary>
    public sealed class EnemyState
    {
        public Vector2 Position;
        public float Health;
        public EnemyKind Kind { get; }
        public int Wave { get; }
        // 用于合并同一路的首次发现，同时让侧路独立报告。
        internal int ArrivalGroup { get; }
        public float LastAttackTime { get; internal set; } = float.NegativeInfinity;

        public EnemyState(EnemyKind kind = EnemyKind.Standard, int wave = 0, int arrivalGroup = -1)
        {
            Kind = kind;
            Wave = wave;
            ArrivalGroup = arrivalGroup;
        }
        internal readonly RouteWalker Walker = new RouteWalker();
        internal int AttackSite = -2;
        internal float AttackClock;
    }

    /// <summary>按已确认的编成安排进场，容量不足的敌人留在等待列表。</summary>
    internal sealed class WaveDirector
    {
        private static readonly int[][] MainGroups =
        {
            new[] { 10, 0, 0 }, new[] { 10, 4, 0 }, new[] { 12, 4, 2 },
            new[] { 9, 4, 1 }, new[] { 16, 6, 4 }, new[] { 11, 5, 2 }
        };
        private static readonly Vector2[] Directions = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        private readonly List<Group> groups = new List<Group>();
        private readonly System.Random random;
        private int lastDirection = -1;
        internal int Wave { get; private set; }
        internal int Waiting { get; private set; }

        internal WaveDirector(System.Random random) => this.random = random;

        /// <summary>各路按独立时刻入场，同一时间先发较早安排的队伍。</summary>
        internal void Step(float time, IReadOnlyList<float> times, List<EnemyState> enemies, OutpostLoop.Config.PrototypeConfig rules)
        {
            while (Wave < times.Count && time >= times[Wave])
            {
                float start = times[Wave++];
                int direction = random.Next(lastDirection < 0 ? 4 : 3);
                if (lastDirection >= 0 && direction >= lastDirection) direction++;
                lastDirection = direction;
                var main = MainGroups[Mathf.Min(Wave, MainGroups.Length) - 1];
                AddGroup(start, direction, main[0], main[1], main[2]);
                if (Wave == 4) AddGroup(start + 5, direction ^ 1, 5, 2, 1, true);
                if (Wave == 6) AddGroup(start + 5, direction ^ 1, 7, 3, 2, true);
            }

            for (int i = 0; i < groups.Count && enemies.Count < rules.MaxEnemies;)
            {
                var group = groups[i];
                if (group.Start > time) { i++; continue; }
                while (group.Remaining > 0 && enemies.Count < rules.MaxEnemies)
                {
                    var kind = group.Take();
                    var direction = Directions[group.Direction];
                    float angle = Mathf.Atan2(direction.y, direction.x) + ((float)random.NextDouble() - .5f) * rules.SpawnSpread;
                    float radius = Mathf.Min(rules.MapRadius - 2.5f, rules.SpawnRadius + (float)random.NextDouble() * rules.SpawnRadiusRange);
                    enemies.Add(new EnemyState(kind, group.Wave, group.Id)
                    {
                        Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                        Health = kind switch
                        {
                            EnemyKind.Fast => 10 + 2 * group.Wave,
                            EnemyKind.Siege => 36 + 4 * group.Wave,
                            _ => rules.EnemyHealthBase + rules.EnemyHealthStep * group.Wave
                        }
                    });
                    Waiting--;
                }
                if (group.Remaining == 0) groups.RemoveAt(i);
                else i++;
            }
        }

        private void AddGroup(float start, int direction, int standard, int fast, int siege, bool secondary = false)
        {
            groups.Add(new Group(start, direction, Wave, (Wave - 1) * 2 + (secondary ? 1 : 0), standard, fast, siege));
            Waiting += standard + fast + siege;
        }

        /// <summary>保存尚未进场的单路编成，容量释放后继续派出。</summary>
        private sealed class Group
        {
            internal readonly float Start;
            internal readonly int Direction, Wave, Id;
            private int standard, fast, siege;
            internal int Remaining => standard + fast + siege;

            internal Group(float start, int direction, int wave, int id, int standard, int fast, int siege)
            {
                Start = start;
                Direction = direction;
                Wave = wave;
                Id = id;
                this.standard = standard;
                this.fast = fast;
                this.siege = siege;
            }

            internal EnemyKind Take()
            {
                if (standard > 0) { standard--; return EnemyKind.Standard; }
                if (fast > 0) { fast--; return EnemyKind.Fast; }
                siege--;
                return EnemyKind.Siege;
            }
        }
    }
}
