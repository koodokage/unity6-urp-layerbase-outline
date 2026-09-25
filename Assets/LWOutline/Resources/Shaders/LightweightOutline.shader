Shader "Hidden/Lightweight Outline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        // =========================================================
        // PASS 0
        // MASK
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
                return half4(
                    1.0,
                    1.0,
                    1.0,
                    1.0
                );
            }

            ENDHLSL
        }

        // =========================================================
        // PASS 1
        // OUTLINE
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

            // =====================================================
            // TEXTURES
            // =====================================================

            TEXTURE2D_X(_BlitTexture);

            TEXTURE2D_X(_OutlineMask);
            SAMPLER(sampler_OutlineMask);

            // =====================================================
            // PARAMETERS
            // =====================================================

            float4 _OutlineColor;

            float _OutlineWidth;

            float _GlowRadius;

            float _GlowIntensity;

            float _SmoothCorners;

            // =====================================================
            // FULLSCREEN TRIANGLE
            // =====================================================

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

            // =====================================================
            // MASK SAMPLE
            // =====================================================

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _OutlineMask,
                    sampler_OutlineMask,
                    uv
                ).r;
            }

            // =====================================================
            // OUTLINE SAMPLE
            // =====================================================

            half GetOutline(
                float2 uv,
                float2 pixelSize
            )
            {
                half center =
                    SampleMask(uv);

                // -------------------------------------------------
                // BASIC 4 DIRECTIONS
                // -------------------------------------------------

                half north =
                    SampleMask(
                        uv +
                        float2(
                            0.0,
                            pixelSize.y
                        )
                    );

                half south =
                    SampleMask(
                        uv +
                        float2(
                            0.0,
                            -pixelSize.y
                        )
                    );

                half east =
                    SampleMask(
                        uv +
                        float2(
                            pixelSize.x,
                            0.0
                        )
                    );

                half west =
                    SampleMask(
                        uv +
                        float2(
                            -pixelSize.x,
                            0.0
                        )
                    );

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

                // -------------------------------------------------
                // DIAGONALS
                // -------------------------------------------------

                half northEast =
                    SampleMask(
                        uv +
                        float2(
                            pixelSize.x,
                            pixelSize.y
                        )
                    );

                half northWest =
                    SampleMask(
                        uv +
                        float2(
                            -pixelSize.x,
                            pixelSize.y
                        )
                    );

                half southEast =
                    SampleMask(
                        uv +
                        float2(
                            pixelSize.x,
                            -pixelSize.y
                        )
                    );

                half southWest =
                    SampleMask(
                        uv +
                        float2(
                            -pixelSize.x,
                            -pixelSize.y
                        )
                    );

                half diagonal =
                    max(
                        max(
                            northEast,
                            northWest
                        ),
                        max(
                            southEast,
                            southWest
                        )
                    );

                // -------------------------------------------------
                // SMOOTH CORNER MODE
                // -------------------------------------------------

                half diagonalWeight =
                    lerp(
                        0.0,
                        1.0,
                        _SmoothCorners
                    );

                surrounding =
                    max(
                        surrounding,
                        diagonal * diagonalWeight
                    );

                // -------------------------------------------------
                // OUTLINE ONLY OUTSIDE
                // -------------------------------------------------

                return saturate(
                    surrounding - center
                );
            }

            // =====================================================
            // GLOW
            // =====================================================

            half GetGlow(
                float2 uv,
                float2 pixelSize
            )
            {
                if (_GlowRadius <= 0.0 ||
                    _GlowIntensity <= 0.0)
                {
                    return 0.0;
                }

                float2 stepSize =
                    pixelSize * _GlowRadius;

                half glow = 0.0;

                // -------------------------------------------------
                // 8 SAMPLE GLOW
                // -------------------------------------------------

                glow += SampleMask(
                    uv +
                    float2(
                        stepSize.x,
                        0.0
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        -stepSize.x,
                        0.0
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        0.0,
                        stepSize.y
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        0.0,
                        -stepSize.y
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        stepSize.x,
                        stepSize.y
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        -stepSize.x,
                        stepSize.y
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        stepSize.x,
                        -stepSize.y
                    )
                );

                glow += SampleMask(
                    uv +
                    float2(
                        -stepSize.x,
                        -stepSize.y
                    )
                );

                glow *= 0.125;

                return glow *
                    _GlowIntensity;
            }

            // =====================================================
            // FRAGMENT
            // =====================================================

            half4 Frag(
                Varyings input
            ) : SV_Target
            {
                float2 uv =
                    input.uv;

                // -------------------------------------------------
                // SCENE
                // -------------------------------------------------

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv
                    );

                // -------------------------------------------------
                // PIXEL SIZE
                // -------------------------------------------------

                float2 pixelSize =
                    1.0 /
                    _ScreenParams.xy;

                // -------------------------------------------------
                // OUTLINE
                // -------------------------------------------------

                float2 outlinePixelSize =
                    pixelSize *
                    _OutlineWidth;

                half outline =
                    GetOutline(
                        uv,
                        outlinePixelSize
                    );

                // -------------------------------------------------
                // GLOW
                // -------------------------------------------------

                half glow =
                    GetGlow(
                        uv,
                        pixelSize
                    );

                // Glow should not overpower the actual outline.
                glow *=
                    (1.0 - outline);

                // -------------------------------------------------
                // HDR OUTLINE
                // -------------------------------------------------

                half3 outlineColor =
                    _OutlineColor.rgb;

                // -------------------------------------------------
                // COMPOSITE
                // -------------------------------------------------

                half3 result =
                    sceneColor.rgb;

                // Main outline.
                result =
                    lerp(
                        result,
                        outlineColor,
                        outline *
                        _OutlineColor.a
                    );

                // HDR glow.
                //
                // Additive instead of lerp.
                // This allows values > 1.0 to reach Bloom.
                result +=
                    outlineColor *
                    glow;

                return half4(
                    result,
                    sceneColor.a
                );
            }

            ENDHLSL
        }
    }

    FallBack Off
}