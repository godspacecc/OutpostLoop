Shader "OutpostLoop/River Valley Peek Roof"
{
    Properties
    {
        _MainTex ("Surface", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _PeekMask ("Visible floor", 2D) = "black" {}
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
        float _PeekFloor, _PeekActive;
        struct Input { float2 uv_MainTex; float3 worldPos; };

        void surf(Input input, inout SurfaceOutput output)
        {
            // 屋顶按镜头对应的室内地面像素开口，边界服从角色视线。
            if (_PeekActive > .5)
            {
                float3 ray = input.worldPos - _WorldSpaceCameraPos.xyz;
                float factor = (_PeekFloor - input.worldPos.y) / min(-.001, ray.y);
                float2 floor = input.worldPos.xz + ray.xz * factor;
                float2 uv = (floor - _PeekRoom.xy) / _PeekRoom.zw;
                if (all(uv >= 0) && all(uv <= 1))
                    clip(.5 - tex2D(_PeekMask, uv).r);
            }
            output.Albedo = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback Off
}
