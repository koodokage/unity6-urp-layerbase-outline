Shader "Hidden/Lightweight Outline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        // =========================================================
        // PASS 0 - OUTLINE MASK
        // =========================================================

        Pass
        {
            Name "Outline Mask"

            ZWrite Off
            ZTest LEqual
            Cull Back

            Blend One Zero

            HLSLPROGRAM

            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            MaskVaryings MaskVertex(
                MaskAttributes input
            )
            {
                MaskVaryings output;

                output.positionCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz
                    );

                return output;
            }

            half4 MaskFragment(
                MaskVaryings input
            ) : SV_Target
            {
                return half4(1.0, 1.0, 1.0, 1.0);
            }

            ENDHLSL
        }

        // =========================================================
        // PASS 1 - OUTLINE COMPOSITE
        // =========================================================

        Pass
        {
            Name "Outline Composite"

            ZWrite Off
            ZTest Always
            Cull Off

            Blend One Zero

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // -----------------------------------------------------
            // CAMERA COLOR
            // -----------------------------------------------------

            TEXTURE2D_X(_BlitTexture);

            // -----------------------------------------------------
            // OUTLINE MASK
            // -----------------------------------------------------

            TEXTURE2D_X(_OutlineMask);
            SAMPLER(sampler_OutlineMask);

            // -----------------------------------------------------
            // PARAMETERS
            // -----------------------------------------------------

            float4 _OutlineColor;
            float _OutlineWidth;

            // -----------------------------------------------------
            // FULLSCREEN TRIANGLE
            // -----------------------------------------------------

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(
                Attributes input
            )
            {
                Varyings output;

                output.positionCS =
                    GetFullScreenTriangleVertexPosition(
                        input.vertexID
                    );

                output.uv =
                    GetFullScreenTriangleTexCoord(
                        input.vertexID
                    );

                return output;
            }

            // -----------------------------------------------------
            // MASK SAMPLE
            // -----------------------------------------------------

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _OutlineMask,
                    sampler_OutlineMask,
                    uv
                ).r;
            }

            // -----------------------------------------------------
            // FRAGMENT
            // -----------------------------------------------------

            half4 Frag(
                Varyings input
            ) : SV_Target
            {
                float2 uv = input.uv;

                // -------------------------------------------------
                // ORIGINAL CAMERA COLOR
                // -------------------------------------------------

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv
                    );

                // -------------------------------------------------
                // TEXEL SIZE
                // -------------------------------------------------

                float2 texelSize =
                    1.0 / _ScreenParams.xy;

                float2 offset =
                    texelSize * _OutlineWidth;

                // -------------------------------------------------
                // CENTER
                // -------------------------------------------------

                half center =
                    SampleMask(uv);

                // -------------------------------------------------
                // 8 DIRECTIONS
                // -------------------------------------------------

                half north =
                    SampleMask(
                        uv + float2(
                            0.0,
                            offset.y
                        )
                    );

                half south =
                    SampleMask(
                        uv + float2(
                            0.0,
                            -offset.y
                        )
                    );

                half east =
                    SampleMask(
                        uv + float2(
                            offset.x,
                            0.0
                        )
                    );

                half west =
                    SampleMask(
                        uv + float2(
                            -offset.x,
                            0.0
                        )
                    );

                half northEast =
                    SampleMask(
                        uv + float2(
                            offset.x,
                            offset.y
                        )
                    );

                half northWest =
                    SampleMask(
                        uv + float2(
                            -offset.x,
                            offset.y
                        )
                    );

                half southEast =
                    SampleMask(
                        uv + float2(
                            offset.x,
                            -offset.y
                        )
                    );

                half southWest =
                    SampleMask(
                        uv + float2(
                            -offset.x,
                            -offset.y
                        )
                    );

                // -------------------------------------------------
                // FIND SURROUNDING MASK
                // -------------------------------------------------

                half surrounding =
                    max(
                        max(
                            north,
                            south
                        ),
                        max(
                            east,
                            west
                        )
                    );

                surrounding =
                    max(
                        surrounding,
                        max(
                            max(
                                northEast,
                                northWest
                            ),
                            max(
                                southEast,
                                southWest
                            )
                        )
                    );

                // -------------------------------------------------
                // OUTLINE
                // -------------------------------------------------

                half outline =
                    saturate(
                        surrounding - center
                    );

                // -------------------------------------------------
                // COMPOSITE
                // -------------------------------------------------

                half3 finalColor =
                    lerp(
                        sceneColor.rgb,
                        _OutlineColor.rgb,
                        outline * _OutlineColor.a
                    );

                return half4(
                    finalColor,
                    sceneColor.a
                );
            }

            ENDHLSL
        }
    }

    FallBack Off
}