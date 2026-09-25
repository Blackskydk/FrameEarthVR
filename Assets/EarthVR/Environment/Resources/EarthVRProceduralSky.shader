Shader "EarthVR/ProceduralSky"
{
    Properties
    {
        _SunDirection ("Sun Direction", Vector) = (0,1,0,0)
        _MoonDirection ("Moon Direction", Vector) = (0,-1,0,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.8,1)
        _DayAmount ("Day Amount", Range(0,1)) = 1
        _NightAmount ("Night Amount", Range(0,1)) = 0
        _HorizonWarmth ("Horizon Warmth", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _SunDirection;
            float4 _MoonDirection;
            float4 _SunColor;
            float _DayAmount;
            float _NightAmount;
            float _HorizonWarmth;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = UnityObjectToWorldDir(input.vertex.xyz);
                return output;
            }

            float hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float starLayer(float3 direction, float density, float threshold)
            {
                float3 cell = floor(direction * density);
                float value = hash31(cell);
                float star = smoothstep(threshold, 1.0, value);
                return star * star;
            }

            half4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.direction);
                float up = direction.y;
                float upperSky = saturate(up * 0.5 + 0.5);
                float horizon = exp(-abs(up) * 7.0);

                float3 dayHorizon = float3(0.48, 0.70, 0.93);
                float3 dayZenith = float3(0.055, 0.22, 0.52);
                float3 daySky = lerp(dayHorizon, dayZenith, pow(saturate(up), 0.42));
                daySky += float3(1.35, 0.19, 0.025) * horizon * _HorizonWarmth * 1.15;

                float3 nightHorizon = float3(0.018, 0.026, 0.075);
                float3 nightZenith = float3(0.0015, 0.003, 0.014);
                float3 nightSky = lerp(nightHorizon, nightZenith, pow(saturate(up), 0.55));

                float stars = starLayer(direction, 420.0, 0.9935) * 0.9;
                stars += starLayer(direction + 7.13, 790.0, 0.9972) * 1.35;
                float3 galaxyAxis = normalize(float3(0.22, 0.52, 0.825));
                float milkyBand = pow(saturate(1.0 - abs(dot(direction, galaxyAxis))), 13.0);
                float milkyNoise = 0.3 + hash31(floor(direction * 180.0)) * 0.7;
                float nightVisibility = (1.0 - _DayAmount) * smoothstep(-0.14, 0.08, up);
                nightSky += (stars * float3(3.0, 3.35, 4.0) +
                             milkyBand * milkyNoise * float3(0.34, 0.43, 0.72)) * nightVisibility;

                float3 sky = lerp(nightSky, daySky, _DayAmount);
                if (up < 0.0)
                {
                    float3 groundNight = float3(0.001, 0.002, 0.006);
                    float3 groundDay = float3(0.075, 0.095, 0.11);
                    sky = lerp(sky, lerp(groundNight, groundDay, _DayAmount), saturate(-up * 5.0));
                }

                float sunDot = dot(direction, normalize(_SunDirection.xyz));
                float sunDisk = smoothstep(0.99988, 0.99996, sunDot);
                float sunGlow = pow(saturate(sunDot), 700.0) * 0.35;
                float sunVisible = smoothstep(-0.03, 0.02, _SunDirection.y);
                sky += _SunColor.rgb * (sunDisk * 4.0 + sunGlow) * sunVisible;

                float moonDot = dot(direction, normalize(_MoonDirection.xyz));
                float moonDisk = smoothstep(0.99989, 0.99996, moonDot);
                float moonGlow = pow(saturate(moonDot), 520.0) * 0.14;
                float moonVisible = smoothstep(-0.03, 0.02, _MoonDirection.y) * _NightAmount;
                sky += float3(0.62, 0.72, 1.0) * (moonDisk * 1.8 + moonGlow) * moonVisible;

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
