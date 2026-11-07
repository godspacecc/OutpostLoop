using UnityEngine;
using UnityEngine.UI;

namespace Boot.UI
{
    /// <summary>用 UGUI 网格绘制信标，宿主可以在资源包加载前显示它。</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BootBeaconGraphic : MaskableGraphic
    {
        protected override void Awake()
        {
            base.Awake();
            useLegacyMeshGeneration = false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float scale = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 184f;
            Vector2 center = rectTransform.rect.center;

            Arc(mesh, center, scale, Vector2.zero, 72, 0, 360, 1, .22f);
            for (int angle = 0; angle < 360; angle += 8)
                Arc(mesh, center, scale, Vector2.zero, 62, angle, 2, 1, .38f);

            Arc(mesh, center, scale, Vector2.zero, 72, 0, 90, 1.6f, .7f);
            Arc(mesh, center, scale, Vector2.zero, 72, 180, 90, 1.6f, .7f);
            for (int axis = 0; axis < 4; axis++)
            {
                Vector2 direction = Polar(axis * 90, 1);
                Stroke(mesh, center, scale, direction * 66, direction * 82, 1, .48f);
            }

            // 无线电波从塔尖向外展开。每条弧线和塔身都使用当前状态色。
            Vector2 emitter = new Vector2(0, -4);
            Arc(mesh, center, scale, emitter, 28, 45, 90, 2, 1);
            Arc(mesh, center, scale, emitter, 19, 45, 90, 2, 1);
            Arc(mesh, center, scale, emitter, 10, 45, 90, 2, 1);
            Arc(mesh, center, scale, emitter, 2, 0, 360, 4, 1);
            Arc(mesh, center, scale, Polar(45, 72), 2, 0, 360, 4, 1);
            var tip = new Vector2(0, -11);
            var left = new Vector2(-12, -36);
            var right = new Vector2(12, -36);
            Stroke(mesh, center, scale, tip, left, 2, 1);
            Stroke(mesh, center, scale, left, right, 2, 1);
            Stroke(mesh, center, scale, right, tip, 2, 1);
        }

        /// <summary>按固定角度间隔近似圆弧，网格仅在尺寸或颜色变化时更新。</summary>
        private void Arc(VertexHelper mesh, Vector2 center, float scale, Vector2 origin, float radius,
            float start, float sweep, float width, float opacity)
        {
            int steps = Mathf.CeilToInt(sweep / 6);
            Vector2 previous = origin + Polar(start, radius);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 point = origin + Polar(start + sweep * i / steps, radius);
                Stroke(mesh, center, scale, previous, point, width, opacity);
                previous = point;
            }
        }

        private static Vector2 Polar(float degrees, float radius)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>以四边形画一条线段，使用 Canvas 的透明混合。</summary>
        private void Stroke(VertexHelper mesh, Vector2 center, float scale, Vector2 from, Vector2 to, float width, float opacity)
        {
            Vector2 delta = to - from;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
            Color tint = color;
            tint.a *= opacity;
            int first = mesh.currentVertCount;
            mesh.AddVert(center + (from + normal) * scale, tint, Vector2.zero);
            mesh.AddVert(center + (to + normal) * scale, tint, Vector2.zero);
            mesh.AddVert(center + (to - normal) * scale, tint, Vector2.zero);
            mesh.AddVert(center + (from - normal) * scale, tint, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
