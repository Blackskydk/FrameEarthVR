Shader "EarthVR/Moon"
{
    Properties
    {
        [MainColor] _BaseColor ("Moon Color", Color) = (0.82, 0.85, 0.9, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "MoonForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 normalWorld : TEXCOORD0;
                float3 viewWorld : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.viewWorld = _WorldSpaceCameraPos.xyz - worldPosition;
                output.uv = input.uv;
                return output;
            }

            float hash21(float2 point)
            {
                point = frac(point * float2(123.34, 456.21));
                point += dot(point, point + 45.32);
                return frac(point.x * point.y);
            }

            float valueNoise(float2 point)
            {
                float2 cell = floor(point);
                float2 fraction = frac(point);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                return lerp(
                    lerp(hash21(cell), hash21(cell + float2(1, 0)), fraction.x),
                    lerp(hash21(cell + float2(0, 1)), hash21(cell + 1.0), fraction.x),
                    fraction.y);
            }

            float crater(float2 uv, float2 center, float radius)
            {
                float2 delta = uv - center;
                delta.x = min(abs(delta.x), 1.0 - abs(delta.x));
                float distanceToCenter = length(delta) / radius;
                float basin = 1.0 - smoothstep(0.0, 0.82, distanceToCenter);
                float rim = smoothstep(0.72, 0.88, distanceToCenter) *
                            (1.0 - smoothstep(0.88, 1.08, distanceToCenter));
                return rim * 0.22 - basin * 0.30;
            }

            half4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWorld = normalize(input.normalWorld);
                float3 viewWorld = normalize(input.viewWorld);
                float limb = pow(saturate(dot(normalWorld, viewWorld)), 0.32);

                float broad = valueNoise(input.uv * float2(5.0, 3.0));
                float fine = valueNoise(input.uv * 31.0);
                float surface = 0.72 + broad * 0.22 + fine * 0.08;
                surface += crater(input.uv, float2(0.28, 0.61), 0.085);
                surface += crater(input.uv, float2(0.64, 0.72), 0.055);
                surface += crater(input.uv, float2(0.73, 0.39), 0.095);
                surface += crater(input.uv, float2(0.46, 0.31), 0.045);
                surface += crater(input.uv, float2(0.17, 0.42), 0.035);
                surface += crater(input.uv, float2(0.86, 0.58), 0.028);

                float maria = smoothstep(0.46, 0.68, valueNoise(input.uv * float2(3.0, 4.0) + 8.7));
                surface *= lerp(1.0, 0.68, maria * 0.55);
                float3 moon = _BaseColor.rgb * max(0.24, surface) * lerp(0.62, 1.08, limb);

                // The scene uses strong nighttime exposure reduction. A modest HDR
                // output keeps the Moon readable without turning it into a flat disk.
                return half4(moon * 5.2, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
