Shader "Custom/TranslucentHempCurtain"
{
    Properties
    {
        _BaseMap ("Hemp Weave (RGBA)", 2D) = "white" {}
        _BaseColor ("Tint Color", Color) = (0.96, 0.84, 0.64, 0.92)
        _Opacity ("Opacity (투명도)", Range(0.0, 1.0)) = 1.0
        _BacklightColor ("Backlight Glow Color", Color) = (1.0, 0.76, 0.40, 1.0)
        _Translucency ("Backlight Translucency", Range(0.0, 4.0)) = 2.0
        _WrapLighting ("Soft Wrap Lighting", Range(0.0, 1.0)) = 0.65
        _AmbientWarmth ("Subtle Warm Fill", Range(0.0, 0.3)) = 0.04
        _RimStrength ("Edge Rim Glow", Range(0.0, 2.0)) = 0.40
        _WindSpeed ("Wind Speed", Range(0.0, 3.0)) = 0.55
        _WindAmplitude ("Wind Sway Amplitude", Range(0.0, 0.08)) = 0.012
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Opacity;
                half4 _BacklightColor;
                half _Translucency;
                half _WrapLighting;
                half _AmbientWarmth;
                half _RimStrength;
                half _WindSpeed;
                half _WindAmplitude;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = input.positionOS.xyz;

                // Subtle organic breeze stronger near bottom hem
                float bottomWeight = (1.0 - saturate(input.uv.y));
                bottomWeight *= bottomWeight;
                float t = _Time.y * _WindSpeed;
                float sway = sin(t + posOS.x * 2.2 + posOS.y * 0.8) * 0.65
                           + sin(t * 1.7 - posOS.x * 3.7) * 0.35;
                posOS.z += sway * _WindAmplitude * bottomWeight;

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.screenPos = ComputeScreenPos(posInputs.positionCS);
                return output;
            }

            half3 EvaluateClothLight(Light light, float3 normalWS, float3 viewDirWS, half3 albedo)
            {
                float3 L = light.direction;
                float atten = light.distanceAttenuation * light.shadowAttenuation;

                // Soft double-sided wrapped diffuse
                float frontDiff = saturate((abs(dot(normalWS, L)) + _WrapLighting) / (1.0 + _WrapLighting));

                // Subsurface warm backlight transmission through open-weave hemp
                float backTransmit = saturate(dot(-L, viewDirWS) * 0.5 + 0.5);
                backTransmit = pow(backTransmit, 1.8) * 0.65 + 0.35;
                half3 transColor = _BacklightColor.rgb * _Translucency * backTransmit;

                return light.color * atten * (albedo * frontDiff * 0.75 + albedo * transColor * 0.65);
            }

            half4 frag(Varyings input, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS) * (facing >= 0 ? 1.0 : -1.0);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Prepare InputData for URP Forward+ / Forward light loop
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half3 lighting = albedo * _BacklightColor.rgb * _AmbientWarmth;

                Light mainLight = GetMainLight();
                lighting += EvaluateClothLight(mainLight, normalWS, viewDirWS, albedo);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS);
                    lighting += EvaluateClothLight(addLight, normalWS, viewDirWS, albedo);
                LIGHT_LOOP_END
                #endif

                // Warm fiber rim sheen along folds
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewDirWS))), 2.6);
                lighting += lighting * fresnel * _RimStrength;

                // Boost cloth density so Opacity=1 feels rich & mostly opaque while keeping subtle weave透
                half weaveAlpha = lerp(0.58, 1.0, tex.a);
                half alpha = saturate(weaveAlpha * _BaseColor.a * _Opacity);
                return half4(lighting, alpha);
            }
            ENDHLSL
        }
    }
}
