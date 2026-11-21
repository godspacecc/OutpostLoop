Shader "OutpostLoop/River Valley Peek Roof"
{
    Properties
    {
        _MainTex ("Surface", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PeekMask ("Visible distance by direction", 2D) = "black" {}
        _PeekPlayer ("Viewer position", Vector) = (0,0,0,0)
        _PeekRoom ("Room origin and size", Vector) = (0,0,1,1)
        _PeekFloor ("Floor height", Float) = 0
        _PeekActive ("Peek active", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0
        sampler2D _MainTex, _PeekMask;
        fixed4 _Color;
        float4 _PeekRoom;
        float4 _PeekPlayer;
        float _PeekFloor, _PeekActive;
        struct Input { float2 uv_MainTex; float3 worldPos; };

        void surf(Input input, inout SurfaceOutput output)
        {
            // 投影到地面后沿角色视线做连续距离比较，保留真正的门框及遮挡折线。
            if (_PeekActive > .5)
            {
                float3 ray = input.worldPos - _WorldSpaceCameraPos.xyz;
                float factor = (_PeekFloor - input.worldPos.y) / min(-.001, ray.y);
                float2 floor = input.worldPos.xz + ray.xz * factor;
                float2 uv = (floor - _PeekRoom.xy) / _PeekRoom.zw;
                if (all(uv >= 0) && all(uv <= 1))
                {
                    float2 delta = floor - _PeekPlayer.xy;
                    if (delta.x > 0)
                    {
                        float u = atan2(delta.y, delta.x) / UNITY_PI + .5;
                        float reach = tex2D(_PeekMask, float2(u, .5)).r;
                        clip(length(delta) - reach);
                    }
                }
            }
            output.Albedo = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback Off
}
