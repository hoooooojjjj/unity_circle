Shader "Custom/DreamyDustMote"
{
    Properties
    {
        _BaseColor ("Glow Color", Color) = (0.92, 0.95, 1.0, 0.65)
        _Softness ("Softness", Range(1.5, 8.0)) = 4.2
        _Intensity ("Glow Intensity", Range(0.2, 3.0)) = 1.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

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
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Softness;
                half _Intensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centered = input.uv * 2.0 - 1.0;
                float r2 = dot(centered, centered);
                if (r2 >= 1.0) discard;

                // Soft dreamy radial halation orb
                half glow = exp(-r2 * _Softness) * saturate(1.0 - r2);
                half4 col = _BaseColor * input.color;
                half alpha = col.a * glow;
                return half4(col.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
}
