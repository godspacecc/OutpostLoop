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
        _PeekOpen ("Show visible room", Float) = 0
        _PeekKeepPlayerClear ("Show avatar in camera view", Float) = 0
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
        float _PeekFloor, _PeekOpen, _PeekKeepPlayerClear;
        struct Input { float2 uv_MainTex; float3 worldPos; };

        void surf(Input input, inout SurfaceOutput output)
        {
            // 投影到地面后沿角色视线做连续距离比较，保留真正的门框及遮挡折线。
            float3 ray = input.worldPos - _WorldSpaceCameraPos.xyz;
            float factor = (_PeekFloor - input.worldPos.y) / min(-.001, ray.y);
            float2 floor = input.worldPos.xz + ray.xz * factor;
            float2 uv = (floor - _PeekRoom.xy) / _PeekRoom.zw;
            if (_PeekOpen > .5 && all(uv >= 0) && all(uv <= 1))
            {
                float2 delta = floor - _PeekPlayer.xy;
                float u = atan2(delta.y, delta.x) / (2 * UNITY_PI) + .5;
                float reach = tex2D(_PeekMask, float2(u, .5)).r;
                clip(length(delta) - reach);
            }
            // 只在覆盖物挡住镜头时给角色轮廓留出空间，不改变角色看到哪一段室内。
            if (_PeekKeepPlayerClear > .5)
            {
                float3 camera = _WorldSpaceCameraPos.xyz;
                float feetHeight = _PeekPlayer.z;
                float2 foot = _PeekPlayer.xy;
                float2 ground = camera.xz + ray.xz * ((feetHeight - camera.y) / min(-.001, ray.y));
                float2 head = camera.xz + (foot - camera.xz) *
                    ((feetHeight - camera.y) / min(-.001, feetHeight + 1.64 - camera.y));
                float2 axis = head - foot;
                float t = saturate(dot(ground - foot, axis) / max(.001, dot(axis, axis)));
                clip(length(ground - foot - t * axis) - .60);
            }
            output.Albedo = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback Off
}
