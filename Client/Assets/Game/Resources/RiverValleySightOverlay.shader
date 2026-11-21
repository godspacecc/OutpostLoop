Shader "OutpostLoop/River Valley Sight Overlay"
{
    Properties
    {
        _PeekMask ("Visible distance by direction", 2D) = "black" {}
        _SightViewer ("Viewer XZ, ground height, opacity", Vector) = (0,0,0,.16)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZTest Always ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _PeekMask;
            float4 _SightViewer;
            struct VertexOutput
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
            };

            VertexOutput vert(float4 vertex : POSITION)
            {
                VertexOutput output;
                output.clip = UnityObjectToClipPos(vertex);
                output.world = mul(unity_ObjectToWorld, vertex).xyz;
                return output;
            }

            fixed4 frag(VertexOutput input) : SV_Target
            {
                float3 ray = input.world - _WorldSpaceCameraPos.xyz;
                float t = (_SightViewer.z - _WorldSpaceCameraPos.y) / min(-.001, ray.y);
                float2 delta = _WorldSpaceCameraPos.xz + ray.xz * t - _SightViewer.xy;
                float range = length(delta);
                float u = atan2(delta.y, delta.x) / (2 * UNITY_PI) + .5;
                float reach = tex2D(_PeekMask, float2(u, .5)).r;
                float alpha = smoothstep(.05, .85, range - reach) * smoothstep(.25, 1.75, range) * _SightViewer.w;
                return fixed4(.035, .055, .07, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
