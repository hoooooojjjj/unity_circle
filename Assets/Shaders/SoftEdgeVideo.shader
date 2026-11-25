Shader "Custom/SoftEdgeVideo"
{
    Properties
    {
        [MainTexture] _BaseMap ("Video Texture", 2D) = "black" {}
        _BaseColor ("Tint & Fade Alpha", Color) = (1.0, 0.96, 0.88, 1.0)
        _FadeLeft ("Fade Left", Range(0.0, 0.48)) = 0.14
        _FadeRight ("Fade Right", Range(0.0, 0.48)) = 0.14
        _FadeTop ("Fade Top", Range(0.0, 0.48)) = 0.14
        _FadeBottom ("Fade Bottom", Range(0.0, 0.48)) = 0.14
        _EdgeSoftness ("Edge Softness", Range(0.5, 4.0)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _FadeLeft;
                half _FadeRight;
                half _FadeTop;
                half _FadeBottom;
                half _EdgeSoftness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                float fadeL = _FadeLeft   > 0.0001 ? smoothstep(0.0, _FadeLeft,   input.uv.x) : 1.0;
                float fadeR = _FadeRight  > 0.0001 ? smoothstep(0.0, _FadeRight,  1.0 - input.uv.x) : 1.0;
                float fadeB = _FadeBottom > 0.0001 ? smoothstep(0.0, _FadeBottom, input.uv.y) : 1.0;
                float fadeT = _FadeTop    > 0.0001 ? smoothstep(0.0, _FadeTop,    1.0 - input.uv.y) : 1.0;

                float edgeMask = pow(saturate(fadeL * fadeR * fadeB * fadeT), _EdgeSoftness);

                half3 rgb = tex.rgb * _BaseColor.rgb;
                half alpha = saturate(_BaseColor.a * edgeMask);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
