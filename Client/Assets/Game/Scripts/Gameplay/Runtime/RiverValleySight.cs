using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>用角色眼高和当前实体计算同一帧的视野，独立于镜头遮挡表现。</summary>
    internal sealed class RiverValleySight
    {
        // 屋顶和环境明暗共用圆周采样；角色与交互目标用精确的三维线段。
        internal const int RayCount = 512;
        private const float DayRange = 13f, NightRange = 5f, EyeHeight = 1.55f;
        private static readonly Vector2[] directions = CreateDirections();
        private readonly List<Blocker> blockers = new List<Blocker>();
        private readonly List<NearbyBlocker> nearby = new List<NearbyBlocker>();
        private readonly float[] distances = new float[RayCount];
        private Vector3 eye;
        private Vector2 facing;
        private float daylight, lamp;

        internal Vector3 Eye => eye;
        internal float FeetHeight => eye.y - EyeHeight;
        internal float[] Distances => distances;

        // 静态实体在搭建地图时注册，活动门每帧从同一门板取实际姿态和破坏状态。
        internal void AddBox(Vector3 center, Quaternion rotation, Vector3 size, Transform owner = null) =>
            blockers.Add(new Blocker(Matrix4x4.TRS(center, rotation, size), owner, null));

        internal void AddDoor(Transform door) => blockers.Add(new Blocker(Matrix4x4.identity, door, door.GetComponent<Renderer>()));

        /// <summary>取得当前帧近处实体，供所有视线查询复用。</summary>
        internal void SetView(Vector3 feet, Vector2 look, float sun, float light)
        {
            eye = feet + Vector3.up * EyeHeight;
            facing = look.sqrMagnitude > .001f ? look.normalized : Vector2.right;
            daylight = sun;
            lamp = light;
            nearby.Clear();
            foreach (var blocker in blockers)
            {
                if (blocker.Door && !blocker.Door.enabled) continue;
                var matrix = blocker.Door ? blocker.Owner.localToWorldMatrix : blocker.LocalToWorld;
                var delta = matrix.GetColumn(3) - (Vector4)eye;
                var extent = blocker.Door ? new Vector2(1.5f, 1.5f) : blocker.Extent;
                if (Mathf.Abs(delta.x) > DayRange + extent.x || Mathf.Abs(delta.z) > DayRange + extent.y) continue;
                var inverse = blocker.Door ? matrix.inverse : blocker.WorldToLocal;
                nearby.Add(new NearbyBlocker(inverse, inverse.MultiplyPoint3x4(eye), blocker.Owner));
            }
        }

        /// <summary>给地面与屋顶裁切采样。角度用世界坐标，转身时不需要重新绑定材质。</summary>
        internal void TraceContour()
        {
            for (int i = 0; i < RayCount; i++)
            {
                var direction = directions[i];
                float range = Range(direction);
                distances[i] = range * Trace(new Vector3(direction.x * range, 0, direction.y * range));
            }
        }

        /// <summary>检查一个世界点。检查物件表面时跳过该物件自身的实体。</summary>
        internal bool Sees(Vector3 target, Transform viewed = null)
        {
            var delta = target - eye;
            return InRange(new Vector2(delta.x, delta.z)) && Trace(delta, viewed) >= .999f;
        }

        /// <summary>先裁掉较远的平面位置，从而免查其地面高度和实体。</summary>
        internal bool Reaches(Vector2 point) => InRange(point - new Vector2(eye.x, eye.z));

        private bool InRange(Vector2 offset)
        {
            float square = offset.sqrMagnitude;
            if (square > DayRange * DayRange) return false;
            float range = Range(offset.normalized);
            return square <= range * range;
        }

        private float Range(Vector2 direction)
        {
            float dot = Vector2.Dot(facing, direction);
            if (dot <= .5f) return 0;
            float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .62f, dot));
            float torch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.879f, .927f, dot));
            float ambient = Mathf.Lerp(NightRange, DayRange, daylight);
            float bright = lamp > 0 ? Mathf.Lerp(3, 12, lamp) : 0;
            return Mathf.Max(ambient, Mathf.Lerp(ambient, bright, torch)) * edge;
        }

        // 把射线转进盒体局部坐标，取得第一处实际遮挡。
        private float Trace(Vector3 delta, Transform viewed = null)
        {
            float nearest = 1;
            foreach (var blocker in nearby)
            {
                if (viewed && blocker.Owner && (viewed == blocker.Owner || viewed.IsChildOf(blocker.Owner))) continue;
                var origin = blocker.Origin;
                // 动态门摆到角色身上时，允许角色从门板内部向外看。
                if (Mathf.Abs(origin.x) < .5f && Mathf.Abs(origin.y) < .5f && Mathf.Abs(origin.z) < .5f) continue;
                var direction = blocker.Inverse.MultiplyVector(delta);
                float enter = 0, exit = nearest;
                if (ClipAxis(origin.x, direction.x, .5f, ref enter, ref exit) &&
                    ClipAxis(origin.y, direction.y, .5f, ref enter, ref exit) &&
                    ClipAxis(origin.z, direction.z, .5f, ref enter, ref exit)) nearest = enter;
            }
            return nearest;
        }

        private static bool ClipAxis(float origin, float direction, float half, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < .00001f) return Mathf.Abs(origin) < half;
            float first = (-half - origin) / direction, second = (half - origin) / direction;
            enter = Mathf.Max(enter, Mathf.Min(first, second));
            exit = Mathf.Min(exit, Mathf.Max(first, second));
            return enter <= exit;
        }

        private static Vector2[] CreateDirections()
        {
            var samples = new Vector2[RayCount];
            for (int i = 0; i < samples.Length; i++)
            {
                float angle = ((i + .5f) / samples.Length - .5f) * 2 * Mathf.PI;
                samples[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return samples;
        }

        private readonly struct Blocker
        {
            internal readonly Matrix4x4 LocalToWorld, WorldToLocal;
            internal readonly Vector2 Extent;
            internal readonly Transform Owner;
            internal readonly Renderer Door;

            internal Blocker(Matrix4x4 matrix, Transform owner, Renderer door)
            {
                LocalToWorld = matrix;
                WorldToLocal = matrix.inverse;
                Extent = new Vector2(Mathf.Abs(matrix.m00) + Mathf.Abs(matrix.m01) + Mathf.Abs(matrix.m02),
                    Mathf.Abs(matrix.m20) + Mathf.Abs(matrix.m21) + Mathf.Abs(matrix.m22)) * .5f;
                Owner = owner;
                Door = door;
            }
        }

        private readonly struct NearbyBlocker
        {
            internal readonly Matrix4x4 Inverse;
            internal readonly Vector3 Origin;
            internal readonly Transform Owner;

            internal NearbyBlocker(Matrix4x4 inverse, Vector3 origin, Transform owner)
            {
                Inverse = inverse;
                Origin = origin;
                Owner = owner;
            }
        }
    }
}
