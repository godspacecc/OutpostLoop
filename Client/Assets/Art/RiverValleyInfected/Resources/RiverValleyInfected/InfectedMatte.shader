Shader "OutpostLoop/RiverValleyInfected" {
    Properties {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _GroundY ("Ground", Float) = 0
        _Contrast ("Upper body contrast", Range(0,1)) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap;
        float _GroundY, _Contrast;
        struct Input { float2 uv_MainTex; float3 worldPos; };
        void surf (Input IN, inout SurfaceOutputStandard o) {
            float height = IN.worldPos.y - _GroundY;
            float torso = smoothstep(.99,1.18,height) * (1-smoothstep(1.51,1.68,height));
            half3 color = tex2D(_MainTex, IN.uv_MainTex).rgb;
            o.Albedo = lerp(color, saturate(color * half3(1.30,1.15,1.02)+half3(.10,.077,.055)), torso * _Contrast);
            o.Normal = normalize(lerp(half3(0,0,1), UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex)), .53));
            o.Metallic = 0;
            o.Smoothness = .11;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
