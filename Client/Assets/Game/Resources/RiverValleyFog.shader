Shader "OutpostLoop/River Valley Fog"
{
    Properties
    {
        _MainTex ("Blit source", 2D) = "black" {}
        _SceneTex ("Camera scene", 2D) = "black" {}
        _Distances ("Sight distance by direction", 2D) = "black" {}
        _FogAtlas ("Last seen pages", 2D) = "black" {}
        _FogPresence ("Page presence", 2D) = "black" {}
        _FogPage ("Writing page origin", Vector) = (0,0,0,0)
        _FogAtlasOrigin ("Atlas origin", Vector) = (0,0,0,0)
        _FogCamera ("Camera world position", Vector) = (0,0,0,0)
        _FogRight ("Camera right", Vector) = (1,0,0,0)
        _FogUp ("Camera up", Vector) = (0,1,0,0)
        _FogForward ("Camera forward", Vector) = (0,0,1,0)
        _FogProjection ("View scale, map plane, foot height", Vector) = (1,1,0,0)
        _FogViewer ("Observer XZ", Vector) = (0,0,0,0)
        _PageSize ("World page size", Float) = 24
        _AtlasPages ("Atlas width in pages", Float) = 5
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _SceneTex, _Distances, _FogAtlas, _FogPresence;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _FogPage, _FogAtlasOrigin, _FogCamera, _FogRight, _FogUp, _FogForward, _FogProjection, _FogViewer;
        float4 _FogAtlas_TexelSize;
        float _PageSize, _AtlasPages;

        float3 ViewRay(float2 uv)
        {
            float2 offset = (uv - .5) * _FogProjection.xy;
            return _FogForward.xyz + _FogRight.xyz * offset.x + _FogUp.xyz * offset.y;
        }

        float2 PlanePoint(float3 ray, float height)
        {
            float t = (height - _FogCamera.y) / min(-.001, ray.y);
            return _FogCamera.xz + t * ray.xz;
        }

        float3 ScenePoint(float2 uv, float3 ray)
        {
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
            if (Linear01Depth(raw) >= .999)
            {
                float2 ground = PlanePoint(ray, _FogProjection.w);
                return float3(ground.x, _FogProjection.w, ground.y);
            }
            return _FogCamera.xyz + LinearEyeDepth(raw) * ray;
        }

        float Visible(float2 pointXZ)
        {
            float2 delta = pointXZ - _FogViewer.xy;
            float angle = atan2(delta.y, delta.x) / (2 * UNITY_PI) + .5;
            float reach = tex2D(_Distances, float2(angle, .5)).r;
            return step(.02, reach) * smoothstep(-.13, .16, reach + .15 - length(delta));
        }

        // 将玩家当作始终可见的现场对象，并从最后所见里排除其像素。
        float ViewerPixel(float3 worldSample)
        {
            float elevation = worldSample.y - _FogProjection.w;
            float distance = length(worldSample.xz - _FogViewer.xy);
            return step(.09, elevation) * step(elevation, 1.95) * step(distance, .46);
        }

        fixed4 Stamp(v2f_img input) : SV_Target
        {
            fixed4 last = tex2D(_MainTex, input.uv);
            float2 pointXZ = _FogPage.xy + input.uv * _PageSize;
            float3 offset = float3(pointXZ.x, _FogProjection.z, pointXZ.y) - _FogCamera.xyz;
            float depth = dot(offset, _FogForward.xyz);
            float2 ndc = float2(dot(offset, _FogRight.xyz), dot(offset, _FogUp.xyz));
            float2 uv = ndc / max(.01, depth) / _FogProjection.xy + .5;
            if (depth <= 0 || any(uv <= 0) || any(uv >= 1)) return last;

            float3 worldSample = ScenePoint(uv, ViewRay(uv));
            float visible = Visible(worldSample.xz);
            if (visible < .58 || ViewerPixel(worldSample) > .5) return last;
            float strength = max(last.a, smoothstep(.56, .91, visible));
            return fixed4(tex2D(_SceneTex, uv).rgb * strength, strength);
        }

        fixed4 Resolve(v2f_img input) : SV_Target
        {
            fixed3 current = tex2D(_MainTex, input.uv).rgb;
            float3 ray = ViewRay(input.uv);
            float3 worldSample = ScenePoint(input.uv, ray);
            float2 where = (PlanePoint(ray, _FogProjection.z) - _FogAtlasOrigin.xy) / _PageSize;
            float2 page = floor(where);
            fixed4 history = 0;
            if (all(page >= 0) && all(page < _AtlasPages))
            {
                float inset = _FogAtlas_TexelSize.x * _AtlasPages * .5;
                float2 within = clamp(frac(where), inset, 1 - inset);
                history = tex2D(_FogAtlas, (page + within) / _AtlasPages);
                history.a *= tex2D(_FogPresence, (page + .5) / _AtlasPages).r;
            }
            // 以透明度预乘采样颜色，地块边缘过渡时避免混入未记录的黑色。
            fixed3 lastColor = history.rgb / max(.005, history.a);
            float gray = dot(lastColor, float3(.213, .715, .072));
            fixed3 remembered = lerp(lastColor, gray.xxx, .25) * .86;
            fixed3 unknown = fixed3(.36, .40, .39);
            fixed3 covered = lerp(unknown, remembered, history.a);
            return fixed4(lerp(covered, current, max(Visible(worldSample.xz), ViewerPixel(worldSample))), 1);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Stamp
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Resolve
            ENDCG
        }
    }
    Fallback Off
}
