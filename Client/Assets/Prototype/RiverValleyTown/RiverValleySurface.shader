Shader "OutpostLoop/River Valley Surface"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow
        #pragma target 3.0

        struct Input
        {
            float4 tint;
            float3 worldPos;
        };

        void vert(inout appdata_full vertex, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.tint = vertex.color;
        }

        float grain(float3 position)
        {
            return frac(sin(dot(floor(position * 13), float3(13.72, 81.18, 42.37))) * 43758.54);
        }

        void surf(Input data, inout SurfaceOutput output)
        {
            float variation = sin(data.worldPos.x * 0.86 + data.worldPos.z * 0.34);
            float pixelWidth = max(length(ddx(data.worldPos)), length(ddy(data.worldPos)));
            float detail = 1 - smoothstep(0.06, 0.24, pixelWidth);
            float pattern = 0.97 + 0.08 * (grain(data.worldPos) - 0.5) * detail + variation * 0.018;
            output.Albedo = data.tint.rgb * pattern;
            output.Emission = data.tint.rgb * 0.14;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
