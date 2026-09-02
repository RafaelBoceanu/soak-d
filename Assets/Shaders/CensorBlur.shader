Shader "Custom/CensorBlur"
{
    Properties
    {
        _PixelSize ("Mosaic Block Size (screen px)", Range(1, 128)) = 24
        _BlurRadius ("Blur Radius (screen px)", Range(0, 64)) = 128
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _CameraOffset ("Offset Towards Camera (m)", Range(0, 1)) = 0.128
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CensorBlur"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _BlurRadius;
                half4 _Tint;
                half _Opacity;
                float _CameraOffset;
                float _ZTest;
            CBUFFER_END

            static const float2 kBlurOffset[9] =
            {
                float2(-1, -1), float2(0, -1), float2(1, -1),
                float2(-1,  0), float2(0,  0), float2(1,  0),
                float2(-1, -1), float2(0,  1), float2(1,  1)
            };

            static const float kBlurWeights[9] =
            {
                0.0625, 0.125, 0.0625,
                0.125,  0.25,  0.125,
                0.0625, 0.125, 0.0625
            };

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));

                float3 camRight = normalize(UNITY_MATRIX_I_V._m00_m10_m20);
                float3 camUp = normalize(UNITY_MATRIX_I_V._m01_m11_m21);
                float3 camBack = normalize(UNITY_MATRIX_I_V._m02_m12_m22);

                float scaleX = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float scaleY = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));

                float3 positionWS = centerWS
                    + camRight * (IN.positionOS.x * scaleX)
                    + camUp * (IN.positionOS.y * scaleY)
                    + camBack * _CameraOffset;

                OUT.positionCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenSize = max(_ScaledScreenParams.xy, 1.0);
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                float block = max(_PixelSize, 1.0);
                float2 blockUV = (floor(screenUV * screenSize / block) + 0.5) * block / screenSize;

                float2 blurStep = _BlurRadius / screenSize;

                half3 color = half3(0.0, 0.0, 0.0);
                UNITY_UNROLL
                for (int i = 0; i < 9; i++)
                {
                    float2 uv = clamp(blockUV + kBlurOffset[i] * blurStep, 0.0, 1.0);
                    color += SampleSceneColor(uv) * kBlurWeights[i];
                }
                
                return half4(color * _Tint.rgb, _Tint.a * _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
