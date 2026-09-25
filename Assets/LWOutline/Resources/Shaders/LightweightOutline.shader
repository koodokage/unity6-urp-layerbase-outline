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
                MaskAttributes input)
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
        // SELECTED DEPTH
        // =========================================================

        Pass
        {
            Name "Selected Depth"

            /*
             * IMPORTANT
             *
             * This depth pass is NOT testing against the
             * camera depth.
             *
             * We need the actual depth of the selected object
             * even when another object is in front of it.
             */

            ZWrite On
            ZTest LEqual

            Cull Back

            Blend One Zero


            HLSLPROGRAM

            #pragma vertex SelectedDepthVertex
            #pragma fragment SelectedDepthFragment


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };


            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };


            DepthVaryings SelectedDepthVertex(
                DepthAttributes input)
            {
                DepthVaryings output;


                output.positionCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz
                    );


                return output;
            }


            float SelectedDepthFragment(
                DepthVaryings input
            ) : SV_Target
            {
                /*
                 * Convert the selected object's depth
                 * into linear eye depth.
                 *
                 * This gives us a world-like distance
                 * along the camera view direction.
                 */
                float rawDepth =
                    input.positionCS.z /
                    input.positionCS.w;


                float linearDepth =
                    LinearEyeDepth(
                        rawDepth,
                        _ZBufferParams
                    );


                return linearDepth;
            }


            ENDHLSL
        }


        // =========================================================
        // PASS 2
        // FULLSCREEN COMPOSITE
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


            TEXTURE2D_X(_SelectedDepth);
            SAMPLER(sampler_SelectedDepth);


            TEXTURE2D_X(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);


            // =====================================================
            // PARAMETERS
            // =====================================================

            float4 _OutlineColor;

            float _OutlineWidth;

            float _GlowRadius;

            float _GlowIntensity;

            float _SmoothCorners;


            float4 _IntersectionColor;

            float _IntersectionThreshold;

            float _IntersectionWidth;

            float _IntersectionEnabled;


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
                Attributes input)
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
            // MASK
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
            // SELECTED DEPTH
            // =====================================================

            float SampleSelectedDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _SelectedDepth,
                    sampler_SelectedDepth,
                    uv
                ).r;
            }


            // =====================================================
            // CAMERA DEPTH
            // =====================================================

            float SampleCameraDepth(float2 uv)
            {
                float rawDepth =
                    SAMPLE_TEXTURE2D_X(
                        _CameraDepthTexture,
                        sampler_CameraDepthTexture,
                        uv
                    ).r;


                return LinearEyeDepth(
                    rawDepth,
                    _ZBufferParams
                );
            }


            // =====================================================
            // NORMAL OUTLINE
            // =====================================================

            half GetOutline(
                float2 uv,
                float2 pixelSize)
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
                // SMOOTH CORNERS
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
                        diagonal *
                        diagonalWeight
                    );


                // -------------------------------------------------
                // OUTSIDE ONLY
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
                float2 pixelSize)
            {
                if (_GlowRadius <= 0.0 ||
                    _GlowIntensity <= 0.0)
                {
                    return 0.0;
                }


                float2 stepSize =
                    pixelSize *
                    _GlowRadius;


                half glow = 0.0;


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
            // INTERSECTION MASK
            // =====================================================

            half GetIntersection(
                float2 uv)
            {
                if (_IntersectionEnabled <= 0.0)
                    return 0.0;


                float selectedDepth =
                    SampleSelectedDepth(uv);


                /*
                 * No selected object at this pixel.
                 */
                if (selectedDepth <= 0.0001)
                    return 0.0;


                float sceneDepth =
                    SampleCameraDepth(uv);


                /*
                 * Another object must be in front
                 * of the selected object.
                 *
                 * Example:
                 *
                 * sceneDepth    = 9.98
                 * selectedDepth = 10.00
                 *
                 * Difference    = 0.02
                 */
                float depthDifference =
                    selectedDepth -
                    sceneDepth;


                /*
                 * If the selected object is not behind
                 * the camera-visible surface, this isn't
                 * an intersection.
                 */
                if (depthDifference <= 0.0)
                    return 0.0;


                /*
                 * Only detect surfaces that are close
                 * enough to each other.
                 *
                 * This prevents a completely hidden object
                 * from getting an outline across its entire
                 * surface.
                 */
                float intersection =
                    1.0 -
                    smoothstep(
                        0.0,
                        _IntersectionThreshold,
                        depthDifference
                    );


                return intersection;
            }


            // =====================================================
            // INTERSECTION EDGE
            // =====================================================

            half GetIntersectionEdge(
                float2 uv,
                float2 pixelSize)
            {
                if (_IntersectionEnabled <= 0.0)
                    return 0.0;


                half center =
                    GetIntersection(uv);


                /*
                 * If there is no intersection at the center,
                 * still check surrounding pixels.
                 */
                half north =
                    GetIntersection(
                        uv +
                        float2(
                            0.0,
                            pixelSize.y *
                            _IntersectionWidth
                        )
                    );


                half south =
                    GetIntersection(
                        uv +
                        float2(
                            0.0,
                            -pixelSize.y *
                            _IntersectionWidth
                        )
                    );


                half east =
                    GetIntersection(
                        uv +
                        float2(
                            pixelSize.x *
                            _IntersectionWidth,
                            0.0
                        )
                    );


                half west =
                    GetIntersection(
                        uv +
                        float2(
                            -pixelSize.x *
                            _IntersectionWidth,
                            0.0
                        )
                    );


                half northEast =
                    GetIntersection(
                        uv +
                        float2(
                            pixelSize.x *
                            _IntersectionWidth,
                            pixelSize.y *
                            _IntersectionWidth
                        )
                    );


                half northWest =
                    GetIntersection(
                        uv +
                        float2(
                            -pixelSize.x *
                            _IntersectionWidth,
                            pixelSize.y *
                            _IntersectionWidth
                        )
                    );


                half southEast =
                    GetIntersection(
                        uv +
                        float2(
                            pixelSize.x *
                            _IntersectionWidth,
                            -pixelSize.y *
                            _IntersectionWidth
                        )
                    );


                half southWest =
                    GetIntersection(
                        uv +
                        float2(
                            -pixelSize.x *
                            _IntersectionWidth,
                            -pixelSize.y *
                            _IntersectionWidth
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


                /*
                 * Only the border of the intersection region.
                 */
                half edge =
                    saturate(
                        surrounding -
                        center
                    );


                /*
                 * If the intersection itself is very thin,
                 * keep the center visible too.
                 */
                edge =
                    max(
                        edge,
                        center * 0.5
                    );


                return edge;
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
                // NORMAL OUTLINE
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


                /*
                 * Glow should not overpower the
                 * actual outline.
                 */
                glow *=
                    (1.0 - outline);


                // -------------------------------------------------
                // INTERSECTION
                // -------------------------------------------------

                half intersection =
                    GetIntersectionEdge(
                        uv,
                        pixelSize
                    );


                // -------------------------------------------------
                // RESULT
                // -------------------------------------------------

                half3 result =
                    sceneColor.rgb;


                // -------------------------------------------------
                // NORMAL OUTLINE
                // -------------------------------------------------

                result =
                    lerp(
                        result,
                        _OutlineColor.rgb,
                        outline *
                        _OutlineColor.a
                    );


                // -------------------------------------------------
                // INTERSECTION OUTLINE
                // -------------------------------------------------

                result =
                    lerp(
                        result,
                        _IntersectionColor.rgb,
                        intersection *
                        _IntersectionColor.a
                    );


                // -------------------------------------------------
                // GLOW
                // -------------------------------------------------

                result +=
                    _OutlineColor.rgb *
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