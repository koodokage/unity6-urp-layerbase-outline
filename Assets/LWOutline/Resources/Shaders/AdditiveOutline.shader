Shader "Custom/URP/AdditiveOutline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color (HDR)", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.1)) = 0.02
        _OutlineIntensity ("Outline Intensity", Range(0.0, 10.0)) = 1.5
        [Toggle] _ConstantScreenWidth ("Constant Screen-Space Width", Float) = 1
        _SmoothAmount ("Smoothing (0=Sharp Normal, 1=Smoothed Normal)", Range(0.0, 1.0)) = 1.0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4 // LEqual
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "AdditiveOutline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite On
            ZTest [_ZTest]
            Blend One One // Additive

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                // Smoothed normals baked into UV2 (xyz) by an external tool/script (optional).
                // If your mesh has no baked smooth normals, _SmoothAmount is simply ignored (falls back to normalOS).
                float3 smoothNormalOS : TEXCOORD2;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _OutlineIntensity;
                float _ConstantScreenWidth;
                float _SmoothAmount;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Blend between hard vertex normal and smoothed normal (avoids cracked outlines on hard edges).
                float3 baseN = normalize(IN.normalOS);
                float3 smoothN = normalize(lerp(baseN, normalize(IN.smoothNormalOS), _SmoothAmount));

                float3 positionOS = IN.positionOS.xyz;
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(smoothN);

                float width = _OutlineWidth;

                if (_ConstantScreenWidth > 0.5)
                {
                    // Scale outline by distance to camera so it stays a constant pixel-ish width.
                    float dist = distance(positionWS, GetCameraPositionWS());
                    width *= dist;
                }

                // Optional: use vertex color alpha as a per-vertex width mask (paint 0 to hide outline on some verts).
                width *= IN.color.a > 0.0001 ? IN.color.a : 1.0;

                positionWS += normalWS * width;

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.color = _OutlineColor;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // HDR color * intensity -> feeds bloom nicely, additive blend does the rest.
                return IN.color * _OutlineIntensity;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
