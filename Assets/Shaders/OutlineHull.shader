// 음료 호버·선택 외곽선(뒤집은 껍데기 + 가장자리 빛). 원래 메시를 법선 방향으로 _Width(m)만큼 부풀려 뒷면만 그리고,
// 시선과 거의 수직인 가장자리일수록 진하게(_RimPower) 그린다. 유리처럼 비치는 잔에서도 가운데가 칠해지지 않고 테두리만 빛난다.
// 원래 머티리얼은 건드리지 않고, 같은 메시를 쓰는 자식 렌더러에만 이 머티리얼을 쓴다(DrinkOutline).
// 반투명(깊이 쓰기 없음) + 알파로 밝기를 바꾼다(_Color, MaterialPropertyBlock).
Shader "ProjectCR/OutlineHull"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.84, 0.42, 1)
        _Width ("Width (m)", Float) = 0.003
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _Width;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewDirWS);
                // 가장자리(시선과 수직)일수록 1, 정면일수록 0.
                float rim = pow(saturate(1.0 - abs(dot(n, v))), _RimPower);
                return half4(_Color.rgb, _Color.a * rim);
            }
            ENDHLSL
        }
    }
}
