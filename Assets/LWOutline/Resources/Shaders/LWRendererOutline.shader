Shader "LWOutline/RendererOutline"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Scale ("Scale", Float) = 1.01
        
        _MinFadeDistance ("Min Fade Distance", Float) = 0.0
        _MaxFadeDistance ("Max Fade Distance", Float) = 10.0

        // Sadece Depth Test kontrolü bırakıldı
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4 // Varsayılan: LEqual
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
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            
            // Depth Write tamamen kapalı, Depth Test materyalden dinamik okunuyor
            ZWrite Off
            ZTest [_ZTest]
            
            // Grafikteki Render Face: Back ayarına karşılık gelir
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0; 
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Scale;
                float _MinFadeDistance;
                float _MaxFadeDistance;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float3 scaledPosOS = input.positionOS.xyz * _Scale;
                output.positionCS = TransformObjectToHClip(scaledPosOS);
                output.positionWS = TransformObjectToWorld(scaledPosOS);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float dist = distance(GetCameraPositionWS(), input.positionWS);
                float distanceFade = smoothstep(_MaxFadeDistance, _MinFadeDistance, dist);
                
                half4 finalColor = _Color;
                finalColor.a *= distanceFade;
                
                return finalColor; 
            }
            ENDHLSL
        }
    }
}