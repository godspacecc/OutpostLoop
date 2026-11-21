using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class ScavengeRun
    {
        private const int MagazineCapacity = 6;
        private readonly int medicineCache;
        private bool FirearmSelected { get; set; }
        private Vector2 gunshot;
        private float gunshotRemaining;
#if UNITY_EDITOR
        // 编辑器美术样片可临时提供一套墙与绕行点，退出样片后回到地图配置。
        private static Rect[] townPreviewWalls;
        private static Vector2[] townPreviewCorners;

        private static void SetTownPreviewGeometry(Rect[] walls, Vector2[] corners)
        {
            townPreviewWalls = walls;
            townPreviewCorners = corners;
        }
#endif

        public bool IsTown { get; }
        public bool IsRain => IsTown && (Seed & 1) != 0;
        public int Rounds { get; private set; }
        internal Vector2 GunshotEnd { get; private set; }
        public bool UsingFirearm => (Inventory & ScavengeItem.Pistol) != 0 &&
            (FirearmSelected || (Inventory & ScavengeItem.Crowbar) == 0);
        public bool SurvivorSpoken { get; private set; }
        public bool MedicineFound { get; private set; }
        public bool MedicineLeadKnown { get; private set; }
        public bool CanTalk => Survival == null && IsTown && !SurvivorSpoken && Outcome == ScavengeOutcome.Exploring &&
            Vector2.Distance(Player, ScavengeTown.Survivor) <= 1.8f && ClearRunLine(Player, ScavengeTown.Survivor);
        internal int MedicineCache => MedicineLeadKnown ? medicineCache : -1;
        // 本趟整图几何；旧地图为空，不使用全局可变布局切换。
        internal RiverValley.RiverValleyWorldGeometry FullTown { get; }
        // 动态房门由当前房间集合生成。
        internal SurvivalRoomSet RoomSet { get; }
        // 小地图与实际移动共用边界。
        internal Rect MapBounds => FullTown?.Bounds ?? (IsTown ? ScavengeTown.Bounds : new Rect(-12,-10,24,21));
        internal Vector2[] CachePoints => FullTown?.Caches ?? (IsTown ? ScavengeTown.Caches : ScavengeLayout.Caches);
        internal string[] CacheNames => FullTown?.CacheNames ?? (IsTown ? ScavengeTown.Names : ScavengeLayout.CacheNames);
        internal Rect[] BuildGrounds => IsTown ? ScavengeTown.ShelterGrounds : ScavengeLayout.ShelterGrounds;
        internal Rect[] RunWalls
        {
            get
            {
                if (Survival != null) return survivalWalls;
                if (!IsTown) return ScavengeLayout.Walls;
#if UNITY_EDITOR
                if (townPreviewWalls != null) return townPreviewWalls;
#endif
                return ScavengeTown.Walls;
            }
        }

        private Vector2[] TownRouteCorners
        {
            get
            {
                if (FullTown != null) return FullTown.Corners;
#if UNITY_EDITOR
                if (townPreviewCorners != null) return townPreviewCorners;
#endif
                return ScavengeTown.Corners;
            }
        }
        internal Vector2 ExitPoint => FullTown?.Exit ?? (IsTown ? ScavengeTown.Exit : ScavengeLayout.Exit);

        /// <summary>逐轴滑动并检查实体与河道，保持旧图原有移动规则。</summary>
        private Vector2 MoveInRun(Vector2 point, Vector2 step)
        {
            // 新室内有薄隔墙；分步碰撞防止低帧或大 dt 从家具一侧跳到另一侧。
            int pieces=FullTown?.Shop!=null?Mathf.Max(1,Mathf.CeilToInt(step.magnitude/.12f)):1;
            var delta=step/pieces;
            for(int i=0;i<pieces;i++)point=MoveInRunStep(point,delta);
            return point;
        }

        /// <summary>执行单步滑动，分别检查动态房门、静态家具和河道。</summary>
        private Vector2 MoveInRunStep(Vector2 point, Vector2 step)
        {
            if (FullTown == null) return IsTown ? ScavengeLayout.Move(point, step, ScavengeTown.Bounds, RunWalls) : ScavengeLayout.Move(point, step);
            var next = ScavengeLayout.Move(point, step, FullTown.Bounds, RunWalls);
            if (!FullTown.Walkable(new Vector2(next.x, point.y))) next.x = point.x;
            if (!FullTown.Walkable(new Vector2(next.x, next.y))) next.y = point.y;
            return next;
        }

        /// <summary>房门、墙体和可见实体遮挡视线，水面本身不遮挡。</summary>
        internal bool ClearRunLine(Vector2 from, Vector2 to, float clearance = 0)
        {
            if (!ScavengeLayout.ClearLine(from, to, clearance, RunWalls)) return false;
            if (FullTown == null) return true;
            if(FullTown.Shop!=null)return FullTown.ClearSegment(from,to,clearance);
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 2);
            for (int i = 1; i <= steps; i++) if (!FullTown.ClearBody(Vector2.Lerp(from, to, (float)i / steps), clearance)) return false;
            return true;
        }

        /// <summary>寻路同时检查河道和桥面；战斗视线仍只被实体阻挡。</summary>
        private bool ClearRunRoute(Vector2 from, Vector2 to, float clearance = .34f)
        {
            if (!ClearRunLine(from, to, clearance)) return false;
            if (FullTown == null) return true;
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 2);
            for (int i = 1; i <= steps; i++)
                if (!FullTown.TerrainWalkable(Vector2.Lerp(from, to, (float)i / steps), clearance)) return false;
            return true;
        }

        private void RevealMedicineLead() => MedicineLeadKnown = true;

        /// <summary>使用数字键切换已经搜到的武器。</summary>
        public void SelectWeapon(bool firearm)
        {
            if (Outcome != ScavengeOutcome.Exploring) return;
            var weapon = firearm ? ScavengeItem.Pistol : ScavengeItem.Crowbar;
            if ((Inventory & weapon) != 0) FirearmSelected = firearm;
        }

        /// <summary>一个弹药盒补满空弹匣，未打完时保留现有弹药。</summary>
        public bool Reload()
        {
            const ScavengeItem required = ScavengeItem.Pistol | ScavengeItem.Ammo;
            if (Outcome != ScavengeOutcome.Exploring || OpenCache >= 0 || Rounds > 0 ||
                (Inventory & required) != required) return false;
            Inventory &= ~ScavengeItem.Ammo;
            Rounds = MagazineCapacity;
            return true;
        }

        /// <summary>一次射击消耗一发子弹，并沿实际可通行视线命中最近目标。</summary>
        private bool Fire(Vector2 direction)
        {
            if (Rounds <= 0 || direction.sqrMagnitude < .0001f) return false;
            Rounds--;
            attackCooldown = .32f;
            gunshot = Player;
            gunshotRemaining = .24f;
            var aim = direction.normalized;
            float nearest = 14f;
            ScavengePatrol target = null;
            foreach (var patrol in patrols)
            {
                if (!patrol.Alive) continue;
                var offset = patrol.Position - Player;
                float along = Vector2.Dot(offset, aim);
                if (along < 0 || along >= nearest || (offset - aim * along).sqrMagnitude > .46f * .46f ||
                    !ClearRunLine(Player, patrol.Position)) continue;
                nearest = along;
                target = patrol;
            }
            if (target != null)
            {
                target.Health -= 2;
                GunshotEnd = target.Position;
            }
            else
            {
                GunshotEnd = Player;
                for (float distance = .25f; distance <= 14f; distance += .25f)
                {
                    var point = Player + aim * distance;
                    if (!ClearRunLine(GunshotEnd, point)) break;
                    GunshotEnd = point;
                }
            }
            return true;
        }
    }
}
