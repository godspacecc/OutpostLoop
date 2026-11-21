Shader "OutpostLoop/River Valley Fog"
{
    Properties
    {
        _MainTex ("Blit source", 2D) = "black" {}
        _Distances ("Sight distance by direction", 2D) = "black" {}
        _FogAtlas ("Explored pages", 2D) = "black" {}
        _FogPresence ("Page presence", 2D) = "black" {}
        _FogPage ("Writing page origin", Vector) = (0,0,0,0)
        _FogAtlasOrigin ("Atlas origin", Vector) = (0,0,0,0)
        _FogCamera ("Camera world position", Vector) = (0,0,0,0)
        _FogRight ("Camera right", Vector) = (1,0,0,0)
        _FogUp ("Camera up", Vector) = (0,1,0,0)
        _FogForward ("Camera forward", Vector) = (0,0,1,0)
        _FogProjection ("View scale and foot height", Vector) = (1,1,0,0)
        _FogViewer ("Observer XZ", Vector) = (0,0,0,0)
        _PageSize ("World page size", Float) = 24
        _AtlasPages ("Atlas width in pages", Float) = 5
        _KnownCount ("Known object count", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Distances, _FogAtlas, _FogPresence;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _FogPage, _FogAtlasOrigin, _FogCamera, _FogRight, _FogUp, _FogForward, _FogProjection, _FogViewer;
        float4 _FogAtlas_TexelSize;
        float _PageSize, _AtlasPages;
        float _KnownCount;
        float4 _KnownCenters[24], _KnownExtents[24];

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

        // 玩家始终可见。探索纹理只存已知范围，不存任何对象或画面颜色。
        float ViewerPixel(float3 worldSample)
        {
            float elevation = worldSample.y - _FogProjection.w;
            float distance = length(worldSample.xz - _FogViewer.xy);
            return step(.09, elevation) * step(elevation, 1.95) * step(distance, .46);
        }

        fixed4 Stamp(v2f_img input) : SV_Target
        {
            float explored = tex2D(_MainTex, input.uv).a;
            float2 pointXZ = _FogPage.xy + input.uv * _PageSize;
            return fixed4(0, 0, 0, max(explored, smoothstep(.56, .91, Visible(pointXZ))));
        }

        fixed4 Resolve(v2f_img input) : SV_Target
        {
            fixed3 current = tex2D(_MainTex, input.uv).rgb;
            float3 ray = ViewRay(input.uv);
            float3 worldSample = ScenePoint(input.uv, ray);
            float2 where = (worldSample.xz - _FogAtlasOrigin.xy) / _PageSize;
            float2 page = floor(where);
            float explored = 0;
            if (all(page >= 0) && all(page < _AtlasPages))
            {
                float inset = _FogAtlas_TexelSize.x * _AtlasPages * .5;
                float2 within = clamp(frac(where), inset, 1 - inset);
                explored = tex2D(_FogAtlas, (page + within) / _AtlasPages).a;
                explored *= tex2D(_FogPresence, (page + .5) / _AtlasPages).r;
            }
            // 有记录的门板和物件按整个三维轮廓呈现，地面迷雾继续使用原地块。
            [loop]
            for (int index = 0; index < _KnownCount; index++)
            {
                float4 center = _KnownCenters[index];
                float4 extent = _KnownExtents[index];
                float3 delta = worldSample - center.xyz;
                float2 local = float2(extent.z * delta.x - extent.w * delta.z,
                    extent.w * delta.x + extent.z * delta.z);
                if (abs(delta.y) <= center.w + .04 && all(abs(local) <= extent.xy + .04))
                {
                    explored = 1;
                    break;
                }
            }
            float gray = dot(current, float3(.213, .715, .072));
            fixed3 remembered = lerp(current, gray.xxx, .25) * .86;
            fixed3 unknown = fixed3(.36, .40, .39);
            fixed3 covered = lerp(unknown, remembered, explored);
            return fixed4(lerp(covered, current, max(Visible(worldSample.xz), ViewerPixel(worldSample))), 1);
        }

        // 旧档原来存彩色画面，导入时只继承曾看过的范围。
        fixed4 ImportExplored(v2f_img input) : SV_Target
        {
            return fixed4(0, 0, 0, tex2D(_MainTex, input.uv).a);
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
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment ImportExplored
            ENDCG
        }
    }
    Fallback Off
}
