using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    /// <summary>车辆续玩记录。缺少版本字段的旧档使用默认停车位。</summary>
    [System.Serializable]
    internal struct RiverValleyVehicleSave
    {
        public int version;
        public Vector2 position;
        public float heading;
        public bool occupied;
    }

    public sealed partial class ScavengeRun
    {
        // City 民用车的平面占地与步行避让半径。
        private static readonly Vector2 VehicleSize = new Vector2(1.8f, 4.2f);
        private const float FootClearance = .34f;
        private const float DoorReach = 1.5f;
        private RiverValleyVehicleSave vehicle;
        // 速度不写入存档，续玩从静止开始。
        internal float VehicleSpeed { get; private set; }
        internal bool HasVehicle => vehicle.version > 0;
        internal bool Driving => vehicle.occupied;
        internal Vector2 VehiclePosition => vehicle.position;
        internal float VehicleHeading => vehicle.heading;
        private static Vector2 VehicleForward(float heading) =>
            new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        private static Vector2 VehicleRight(Vector2 forward) => new Vector2(forward.y, -forward.x);

        private RiverValleyWorldGeometry.Solid VehicleBody =>
            new RiverValleyWorldGeometry.Solid(vehicle.position, VehicleSize, vehicle.heading * Mathf.Deg2Rad);

        /// <summary>用既有道路朝向选择镇口附近可泊车的路肩，并给两侧预留步行空间。</summary>
        private void PlaceSurvivalVehicle()
        {
            if (Survival == null || FullTown == null) return;
            float best = float.PositiveInfinity;
            foreach (var road in FullTown.Roads)
            {
                Vector2 axis = road.B - road.A;
                float length = axis.magnitude;
                if (length < 2) continue;
                Vector2 forward = axis / length;
                float heading = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
                float along = Mathf.Clamp(Vector2.Dot(FullTown.Start - road.A, forward), 0, length);
                for (int offset = -24; offset <= 24; offset += 4)
                {
                    float distance = along + offset;
                    if (distance < 0 || distance > length) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var point = road.A + forward * distance +
                            VehicleRight(forward) * (side * (road.Width * .5f + 1.2f));
                        float score = (point - FullTown.Start).sqrMagnitude;
                        if (score >= best || (point - FullTown.Exit).sqrMagnitude < 25 ||
                            (point - FullTown.Start).sqrMagnitude < 25 || !VehiclePoseClear(point, heading) ||
                            !TryVehicleLanding(point, heading, -1, out _) ||
                            !TryVehicleLanding(point, heading, 1, out _)) continue;
                        best = score;
                        vehicle = new RiverValleyVehicleSave { version = 1, position = point, heading = heading };
                    }
                }
            }
        }

        /// <summary>车身用旋转矩形检查实体和室内，整块占地再采样地图与水面。</summary>
        private bool VehiclePoseClear(Vector2 point, float heading)
        {
            var body = new RiverValleyWorldGeometry.Solid(point, VehicleSize, heading * Mathf.Deg2Rad);
            foreach (var solid in FullTown.Solids)
                if (body.Overlaps(solid)) return false;
            foreach (var area in FullTown.Interiors)
                if (area.width > 0 && body.Overlaps(new RiverValleyWorldGeometry.Solid(area.center, area.size, 0))) return false;
            foreach (var wall in RunWalls)
                if (body.Overlaps(new RiverValleyWorldGeometry.Solid(wall.center, wall.size, 0))) return false;

            var forward = VehicleForward(heading);
            var right = VehicleRight(forward);
            for (int x = -1; x <= 1; x++)
                for (int z = -2; z <= 2; z++)
                {
                    var sample = point + right * (x * VehicleSize.x * .5f) +
                        forward * (z * VehicleSize.y * .25f);
                    if (!FullTown.Bounds.Contains(sample) || !FullTown.TerrainWalkable(sample, .15f)) return false;
                }
            return true;
        }

        /// <summary>安全落点必须可站立，且从车门到落点的短路径无阻挡。</summary>
        private bool TryVehicleLanding(Vector2 point, float heading, int side, out Vector2 landing)
        {
            var outward = VehicleRight(VehicleForward(heading)) * side;
            var door = point + outward * (VehicleSize.x * .5f);
            landing = door + outward * .62f;
            return FullTown.Walkable(landing, FootClearance) &&
                ClearVehicleDoorPath(door, landing, FootClearance);
        }

        private bool ClearVehicleDoorPath(Vector2 from, Vector2 to, float clearance)
        {
            if (!FullTown.ClearSegment(from, to, clearance)) return false;
            foreach (var wall in RunWalls)
                if (new RiverValleyWorldGeometry.Solid(wall.center, wall.size, 0).Intersects(from, to, clearance)) return false;
            return true;
        }

        /// <summary>只有驾驶门一侧、视线畅通且未在执行动作时可上车。</summary>
        internal bool CanEnterVehicle
        {
            get
            {
                if (!PlayerOnGround || !HasVehicle || Driving || Outcome != ScavengeOutcome.Exploring || OpenCache >= 0 ||
                    Survival.sleeping || Survival.action != SurvivalAction.None) return false;
                var outward = -VehicleRight(VehicleForward(vehicle.heading));
                var door = vehicle.position + outward * (VehicleSize.x * .5f);
                return Vector2.Dot(Player - door, outward) >= 0 &&
                    (Player - door).sqrMagnitude <= DoorReach * DoorReach &&
                    ClearVehicleDoorPath(Player, door, .05f);
            }
        }

        /// <summary>提示与下车执行共用相同的落点检测。</summary>
        internal bool CanLeaveVehicle => Driving && TryLeaveVehicle(out _);

        private bool TryLeaveVehicle(out Vector2 landing) =>
            TryVehicleLanding(vehicle.position, vehicle.heading, -1, out landing) ||
            TryVehicleLanding(vehicle.position, vehicle.heading, 1, out landing);

        /// <summary>按驾驶侧、另一侧依次找落点，成功下车时立即停住。</summary>
        internal bool InteractVehicle()
        {
            if (Outcome != ScavengeOutcome.Exploring) return false;
            if (!Driving)
            {
                if (!CanEnterVehicle) return false;
                vehicle.occupied = true;
                Player = vehicle.position;
                Survival.facing = VehicleForward(vehicle.heading);
                return true;
            }
            if (!TryLeaveVehicle(out var landing))
                return ReportSurvival("两侧无法下车，请挪车");
            vehicle.occupied = false;
            VehicleSpeed = 0;
            Player = landing;
            return true;
        }

        /// <summary>按实际车头解释输入，先制动再换向，停着时车身不旋转。</summary>
        private void TickVehicle(float dt, Vector2 input)
        {
            for (float remaining = dt; remaining > 0;)
            {
                float step = Mathf.Min(remaining, .025f);
                remaining -= step;
                bool braking = input.y * VehicleSpeed < 0;
                float target = braking ? 0 : input.y > 0 ? 12 : input.y < 0 ? -4 : 0;
                float acceleration = braking ? 16 : input.y == 0 ? 4 : 7;
                VehicleSpeed = Mathf.MoveTowards(VehicleSpeed, target, acceleration * step);
                if (VehicleSpeed == 0) continue;
                float turn = Mathf.Clamp(input.x, -1, 1) * Mathf.Clamp(VehicleSpeed / 4, -1, 1) * 85 * step;
                float heading = Mathf.Repeat(vehicle.heading + turn, 360);
                var point = vehicle.position + VehicleForward(vehicle.heading + turn * .5f) * (VehicleSpeed * step);
                if (!VehiclePoseClear(point, heading)) { VehicleSpeed = 0; break; }
                vehicle.position = point;
                vehicle.heading = heading;
            }
            Player = vehicle.position;
            Survival.facing = VehicleForward(vehicle.heading);
        }

        /// <summary>只限制步行角色，避免旧档恰好落在新车占地里时被困住。</summary>
        private void MovePlayerOnFoot(Vector2 step)
        {
            var residence = FullTown?.Residence;
            if (!HasVehicle && residence == null) { Player = MoveInRun(Player, step); return; }
            var body = VehicleBody;
            var point = Player;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / .12f));
            for (int i = 0; i < pieces; i++)
            {
                var next = PlayerOnGround ? MoveInRunStep(point, step / pieces) : point + step / pieces;
                if (HasVehicle && PlayerOnGround && !body.Contains(point, FootClearance) &&
                    body.Intersects(point, next, FootClearance)) continue;
                point = residence == null ? next : residence.Move(point, next, ref playerSurface);
            }
            Player = point;
        }
    }
}
