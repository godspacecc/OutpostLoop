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
        public bool CanTalk => IsTown && !SurvivorSpoken && Outcome == ScavengeOutcome.Exploring &&
            Vector2.Distance(Player, ScavengeTown.Survivor) <= 1.8f && ClearRunLine(Player, ScavengeTown.Survivor);
        internal int MedicineCache => MedicineLeadKnown ? medicineCache : -1;
        internal Vector2[] CachePoints => IsTown ? ScavengeTown.Caches : ScavengeLayout.Caches;
        internal string[] CacheNames => IsTown ? ScavengeTown.Names : ScavengeLayout.CacheNames;
        internal Rect[] BuildGrounds => IsTown ? ScavengeTown.ShelterGrounds : ScavengeLayout.ShelterGrounds;
        internal Rect[] RunWalls
        {
            get
            {
                if (!IsTown) return ScavengeLayout.Walls;
#if UNITY_EDITOR
                if (townPreviewWalls != null) return townPreviewWalls;
#endif
                return ScavengeTown.Walls;
            }
        }

        private static Vector2[] TownRouteCorners
        {
            get
            {
#if UNITY_EDITOR
                if (townPreviewCorners != null) return townPreviewCorners;
#endif
                return ScavengeTown.Corners;
            }
        }
        internal Vector2 ExitPoint => IsTown ? ScavengeTown.Exit : ScavengeLayout.Exit;

        private Vector2 MoveInRun(Vector2 point, Vector2 step) => IsTown
            ? ScavengeLayout.Move(point, step, ScavengeTown.Bounds, RunWalls)
            : ScavengeLayout.Move(point, step);

        private bool ClearRunLine(Vector2 from, Vector2 to, float clearance = 0) =>
            ScavengeLayout.ClearLine(from, to, clearance, RunWalls);

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
