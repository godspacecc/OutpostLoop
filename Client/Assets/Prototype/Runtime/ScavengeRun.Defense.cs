using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>地图中一名巡逻者的位置、感知与当前意图。</summary>
    public sealed class ScavengePatrol
    {
        internal readonly Vector2 Start, End;
        internal bool TowardEnd = true;
        internal float Cooldown, Memory, RouteCooldown, NoiseRemaining;
        internal Vector2 LastSeen, RoutePoint;
        internal Vector2? Noise;
        public Vector2 Position { get; internal set; }
        public Vector2 Facing { get; internal set; }
        public int Health { get; internal set; } = 2;
        public bool Alive => Health > 0;
        public bool Engaged => Alive && Memory > 0;
        public bool Investigating => Alive && !Engaged && Noise.HasValue;

        public bool Fast { get; }

        internal ScavengePatrol(Vector2 start, Vector2 end, bool fast = false)
        {
            Fast = fast;
            Position = Start = start;
            End = end;
            Facing = (end - start).normalized;
        }
    }

    public sealed partial class ScavengeRun
    {
        public const int ShelterMaxHealth = 5;
        public const float DefenseWidth = .72f;
        public const float DefenseRange = 4.8f;
        private const float NoiseRange = 5f;

        // 从开局就在地图上的两个巡逻者。状态中没有刷怪时钟。
        private readonly ScavengePatrol[] patrols;
        private static ScavengePatrol[] CreateTrialPatrols() => new[]
        {
            new ScavengePatrol(new Vector2(7.8f, -2.8f), new Vector2(7.8f, 5.5f)),
            new ScavengePatrol(new Vector2(-3.32f, .12f), new Vector2(-3.32f, -2.48f))
        };
        // 墙角与门洞的转折点，既可用于追踪，也可用于返回巡逻路线。
        private static readonly Vector2[] Corners =
        {
            new Vector2(-.05f, -2.55f), new Vector2(1.72f, -2.55f), new Vector2(4.42f, -1.23f),
            new Vector2(4.42f, 4.25f), new Vector2(5.38f, 6.08f), new Vector2(5.38f, 7.98f),
            new Vector2(-.06f, 7.96f), new Vector2(-3.12f, 4.06f), new Vector2(-3.12f, -.25f),
            new Vector2(10.75f, 8.0f), new Vector2(10.75f, -5.78f), new Vector2(-.05f, -5.78f)
        };

        private float defenseCooldown, damageNotice;
        public IReadOnlyList<ScavengePatrol> Patrols => patrols;
        public bool ConstructionInvestigated
        {
            get { foreach (var patrol in patrols) if (patrol.Investigating) return true; return false; }
        }
        public int ShelterHealth { get; private set; }
        public bool ShelterLost { get; private set; }
        public bool ShelterHit => damageNotice > 0;
        public Vector2? Defense { get; private set; }
        public float DefenseRemaining { get; private set; }
        public Vector2? DefenseShotTarget { get; private set; }
        public float DefenseShotRemaining { get; private set; }

        /// <summary>检查防御装置的射击阵位，装置和避难所都需留出完整占地。</summary>
        public ShelterPlacement CheckDefense(Vector2 point)
        {
            if (Defense.HasValue || Outcome != ScavengeOutcome.Exploring) return ShelterPlacement.AlreadyPlaced;
            if (!Shelter.HasValue) return ShelterPlacement.NeedsShelter;
            if ((Inventory & ScavengeItem.DefenseParts) == 0) return ShelterPlacement.MissingMaterials;
            if (Vector2.Distance(point, Player) > 4.2f || Vector2.Distance(point, Shelter.Value) > 3.2f)
                return ShelterPlacement.TooFar;
            float half = DefenseWidth * .5f;
            var offset = point - Shelter.Value;
            if (Mathf.Abs(offset.x) < half + ShelterWidth * .5f + .06f &&
                Mathf.Abs(offset.y) < half + ShelterWidth * .5f + .06f) return ShelterPlacement.Obstructed;
            offset = point - Player;
            if (Mathf.Abs(offset.x) < half + .35f && Mathf.Abs(offset.y) < half + .35f)
                return ShelterPlacement.Obstructed;
            foreach (var ground in BuildGrounds)
                if (ground.Contains(Shelter.Value) && point.x - half >= ground.xMin && point.x + half <= ground.xMax &&
                    point.y - half >= ground.yMin && point.y + half <= ground.yMax &&
                    ClearRunLine(Player, point)) return ShelterPlacement.Available;
            return ShelterPlacement.Obstructed;
        }

        /// <summary>消耗一组防守零件，装置完工前不会射击。</summary>
        public bool BuildDefense(Vector2 point)
        {
            if (CheckDefense(point) != ShelterPlacement.Available) return false;
            Inventory &= ~ScavengeItem.DefenseParts;
            Defense = point;
            DefenseRemaining = 1.25f;
            CloseSearch();
            return true;
        }

        private void TickDefense(float dt)
        {
            // 在工期结束的最后一帧仍可听到施工。
            Vector2? shelterNoise = ShelterRemaining > 0 ? Shelter : null;
            Vector2? defenseNoise = DefenseRemaining > 0 ? Defense : null;
            ShelterRemaining = Mathf.Max(0, ShelterRemaining - dt);
            DefenseRemaining = Mathf.Max(0, DefenseRemaining - dt);
            damageNotice = Mathf.Max(0, damageNotice - dt);
            defenseCooldown = Mathf.Max(0, defenseCooldown - dt);
            DefenseShotRemaining = Mathf.Max(0, DefenseShotRemaining - dt);

            foreach (var patrol in patrols)
            {
                if (!patrol.Alive || (IsTown && Vector2.Distance(Player, patrol.Position) > 32 &&
                    (!Shelter.HasValue || Vector2.Distance(Shelter.Value, patrol.Position) > 24))) continue;
                TickPatrol(patrol, dt, shelterNoise, defenseNoise);
                if (Outcome != ScavengeOutcome.Exploring) return;
            }

            gunshotRemaining = Mathf.Max(0, gunshotRemaining - dt);

            if (!Defense.HasValue || DefenseRemaining > 0 || defenseCooldown > 0) return;
            ScavengePatrol target = null;
            float nearest = DefenseRange;
            foreach (var patrol in patrols)
            {
                if (!patrol.Engaged) continue;
                float distance = Vector2.Distance(Defense.Value, patrol.Position);
                if (distance > nearest || !ClearRunLine(Defense.Value, patrol.Position)) continue;
                nearest = distance;
                target = patrol;
            }
            if (target == null) return;
            target.Health--;
            defenseCooldown = .63f;
            DefenseShotRemaining = .14f;
            DefenseShotTarget = target.Position;
        }

        /// <summary>听到施工只会调查。确认看到人或据点才会追击或攻击。</summary>
        private void TickPatrol(ScavengePatrol patrol, float dt, Vector2? firstNoise, Vector2? secondNoise)
        {
            patrol.Cooldown = Mathf.Max(0, patrol.Cooldown - dt);
            patrol.Memory = Mathf.Max(0, patrol.Memory - dt);
            patrol.NoiseRemaining = Mathf.Max(0, patrol.NoiseRemaining - dt);
            bool warehouse = !IsTown && patrol == patrols[0];
            float sight = IsTown ? (patrol.Fast ? 7f : 5.4f) : warehouse ? 5f : 2.65f;
            if (Survival != null && Survival.Night)
                sight = sight * .52f + Survival.Light * 6f;
            bool seesPlayer = (!warehouse || Player.x > 1.25f || patrol.Engaged) &&
                CanSee(patrol, Player, IsTown || warehouse ? sight : 2f, IsTown || warehouse ? 1.65f : .9f);
            if (Survival != null && AttackSurvivalDoor(patrol)) return;
            var shelter = Shelter.GetValueOrDefault();
            bool seesShelter = Shelter.HasValue && CanSee(patrol, shelter, sight, 1.55f);
            bool attackShelter = seesShelter && (!seesPlayer ||
                (shelter - patrol.Position).sqrMagnitude <= (Player - patrol.Position).sqrMagnitude);
            if (seesPlayer || seesShelter)
            {
                patrol.LastSeen = attackShelter ? shelter : Player;
                patrol.Memory = 1.05f;
                patrol.Noise = null;
                var offset = patrol.LastSeen - patrol.Position;
                if (offset.magnitude < 1.24f)
                {
                    if (offset.sqrMagnitude > .001f) patrol.Facing = offset.normalized;
                    if (patrol.Cooldown == 0)
                    {
                        patrol.Cooldown = 1.05f;
                        if (attackShelter) HitShelter();
                        else if (Survival != null) HitSurvivalPlayer();
                        else
                        {
                            Health--;
                            CloseSearch();
                            if (Health <= 0) End(ScavengeOutcome.Died);
                        }
                    }
                    return;
                }
            }
            else if (!patrol.Engaged)
            {
                var sound = gunshotRemaining > 0 && Vector2.Distance(patrol.Position, gunshot) <= (Survival == null ? 16f : 28f) ? gunshot :
                    Heard(patrol, firstNoise) ? firstNoise :
                    Heard(patrol, secondNoise) ? secondNoise : HearSurvival(patrol.Position);
                if (sound.HasValue)
                {
                    patrol.Noise = sound;
                    patrol.NoiseRemaining = 4f;
                }
                else if (patrol.Noise.HasValue &&
                    (patrol.NoiseRemaining == 0 ||
                        Vector2.Distance(patrol.Position, patrol.Noise.Value) < .7f)) patrol.Noise = null;
            }

            var endpoint = patrol.TowardEnd ? patrol.End : patrol.Start;
            var patrolAxis = patrol.Start.x == patrol.End.x ?
                new Vector2(patrol.Start.x, Mathf.Clamp(patrol.Position.y,
                    Mathf.Min(patrol.Start.y, patrol.End.y), Mathf.Max(patrol.Start.y, patrol.End.y))) : endpoint;
            var goal = patrol.Engaged ? patrol.LastSeen : patrol.Noise ??
                (Vector2.Distance(patrol.Position, patrolAxis) > .12f &&
                    ClearRunLine(patrol.Position, patrolAxis, .34f) ? patrolAxis : endpoint);
            MovePatrol(patrol, goal, dt);
            if (!patrol.Engaged && !patrol.Noise.HasValue && Vector2.Distance(patrol.Position, endpoint) < .13f)
                patrol.TowardEnd = !patrol.TowardEnd;
        }

        private static bool Heard(ScavengePatrol patrol, Vector2? point) =>
            point.HasValue && (point.Value - patrol.Position).sqrMagnitude <= NoiseRange * NoiseRange;

        private bool CanSee(ScavengePatrol patrol, Vector2 point, float sight, float close)
        {
            var offset = point - patrol.Position;
            float limit = sight + (patrol.Engaged ? 1.75f : 0);
            return offset.sqrMagnitude <= limit * limit &&
                (patrol.Engaged || offset.sqrMagnitude <= close * close || Vector2.Dot(patrol.Facing, offset.normalized) > .22f) &&
                ClearRunLine(patrol.Position, point);
        }

        private void HitShelter()
        {
            ShelterHealth--;
            damageNotice = 1.4f;
            if (ShelterHealth > 0) return;
            ShelterLost = true;
            Shelter = null;
            Defense = null;
            ShelterRemaining = DefenseRemaining = DefenseShotRemaining = 0;
            ClearShelterStorage();
        }

        private void MovePatrol(ScavengePatrol patrol, Vector2 goal, float dt)
        {
            patrol.RouteCooldown = Mathf.Max(0, patrol.RouteCooldown - dt);
            var waypoint = goal;
            if (!ClearRunLine(patrol.Position, goal, .34f))
            {
                if (patrol.RouteCooldown == 0 || Vector2.Distance(patrol.Position, patrol.RoutePoint) < .14f)
                {
                    patrol.RoutePoint = RouteAround(patrol.Position, goal);
                    patrol.RouteCooldown = .25f;
                }
                waypoint = patrol.RoutePoint;
            }
            else patrol.RouteCooldown = 0;
            float speed = patrol.Engaged ? (patrol.Fast ? 5.35f : 4.15f) : patrol.Investigating ? 2.8f : 2.05f;
            var step = Vector2.MoveTowards(patrol.Position, waypoint, speed * dt) - patrol.Position;
            var moved = MoveInRun(patrol.Position, step);
            if ((moved - patrol.Position).sqrMagnitude > .000001f)
                patrol.Facing = (moved - patrol.Position).normalized;
            patrol.Position = moved;
        }

        /// <summary>在有限的地形节点中寻找最近的可走绕行路径。</summary>
        private Vector2 RouteAround(Vector2 start, Vector2 goal)
        {
            var corners = Corners;
            if (IsTown)
            {
                var local = new List<Vector2>();
                foreach (var point in TownRouteCorners)
                    if (Vector2.Distance(point, start) < 19f || Vector2.Distance(point, goal) < 19f) local.Add(point);
                corners = local.ToArray();
            }
            int count = corners.Length;
            var distance = new float[count];
            var first = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = ClearRunLine(start, corners[i], .34f) ?
                    Vector2.Distance(start, corners[i]) : float.PositiveInfinity;
                first[i] = i;
            }
            float best = float.PositiveInfinity;
            var result = start;
            for (int step = 0; step < count; step++)
            {
                int index = -1;
                float closest = best;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && distance[i] < closest) { index = i; closest = distance[i]; }
                if (index < 0) break;
                visited[index] = true;
                var point = corners[index];
                float total = closest + Vector2.Distance(point, goal);
                if (total < best && ClearRunLine(point, goal, .34f))
                {
                    best = total;
                    result = corners[first[index]];
                }
                for (int i = 0; i < count; i++)
                {
                    if (visited[i]) continue;
                    float next = closest + Vector2.Distance(point, corners[i]);
                    if (next >= distance[i] || !ClearRunLine(point, corners[i], .34f)) continue;
                    distance[i] = next;
                    first[i] = first[index];
                }
            }
            return result;
        }
    }
}
