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
        // OUTLINE MASK
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

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct MaskAttributes
            {
                float4 positionOS : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            MaskVaryings MaskVertex(
                MaskAttributes input)
            {
                MaskVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz
                    );

                return output;
            }

            uint GetOutlineLayerID()
            {
                uint renderingLayers =
                    GetMeshRenderingLayer();

                /*
                 * Rendering layer 0 -> ID 1
                 * Rendering layer 1 -> ID 2
                 * Rendering layer 2 -> ID 3
                 * ...
                 *
                 * 0 means no outline.
                 *
                 * We select the first matching bit.
                 */

                [unroll]
                for (uint i = 0u; i < 32u; i++)
                {
                    uint bit =
                        1u << i;

                    if ((renderingLayers & bit) != 0u)
                    {
                        return i + 1u;
                    }
                }

                return 0u;
            }

            half4 MaskFragment(
                MaskVaryings input
            ) : SV_Target
            {
                uint layerID =
                    GetOutlineLayerID();

                /*
                 * R8 texture.
                 *
                 * 0 = no outline
                 * 1 = rendering layer 0
                 * 2 = rendering layer 1
                 * ...
                 */

                return half4(
                    layerID / 255.0,
                    0.0,
                    0.0,
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

            ZWrite Off
            ZTest Always
            Cull Back

            Blend One Zero

            HLSLPROGRAM

            #pragma vertex SelectedDepthVertex
            #pragma fragment SelectedDepthFragment

            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings SelectedDepthVertex(
                DepthAttributes input)
            {
                DepthVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

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
                float rawDepth =
                    input.positionCS.z /
                    input.positionCS.w;

                return LinearEyeDepth(
                    rawDepth,
                    _ZBufferParams
                );
            }

            ENDHLSL
        }


        // =========================================================
        // PASS 2
        // COMPOSITE
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
            // BLIT SOURCE
            // =====================================================

            TEXTURE2D_X(_BlitTexture);

            // =====================================================
            // OUTLINE MASK
            // =====================================================

            TEXTURE2D_X(_OutlineMask);

            // =====================================================
            // SELECTED DEPTH
            // =====================================================

            TEXTURE2D_X(_SelectedDepth);

            // =====================================================
            // CAMERA DEPTH
            // =====================================================

            TEXTURE2D_X(_CameraDepthTexture);

            // =====================================================
            // PARAMETERS
            // =====================================================

            float _MaxOutlineWidth;

            // =====================================================
            // LAYER DATA
            // =====================================================

            /*
             * 32 renk (HDR).
             *
             * Index 0 = Rendering Layer 0
             * Index 1 = Rendering Layer 1
             * ...
             */

            float4 _LayerColors[32];

            /*
             * x = occlusion mode (0 = VisibleOnly, 1 = AlwaysVisible)
             * y = bu layer'a ait outline kalinligi (piksel)
             */

            float4 _LayerOcclusion[32];

            // =====================================================
            // FULLSCREEN
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

            half SampleMask(
                float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _OutlineMask,
                    sampler_PointClamp,
                    uv
                ).r;
            }

            uint SampleLayerID(
                float2 uv)
            {
                half encoded =
                    SampleMask(uv);

                /*
                 * R8 normalized:
                 *
                 * 1 / 255
                 * 2 / 255
                 * ...
                 */

                return (uint)
                    round(
                        encoded * 255.0
                    );
            }

            // =====================================================
            // DEPTH
            // =====================================================

            float SampleSelectedDepth(
                float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _SelectedDepth,
                    sampler_PointClamp,
                    uv
                ).r;
            }

            float SampleCameraDepth(
                float2 uv)
            {
                float rawDepth =
                    SAMPLE_TEXTURE2D_X(
                        _CameraDepthTexture,
                        sampler_PointClamp,
                        uv
                    ).r;

                return LinearEyeDepth(
                    rawDepth,
                    _ZBufferParams
                );
            }

            // =====================================================
            // LAYER
            // =====================================================

            float4 GetLayerColor(
                uint layerID)
            {
                if (layerID == 0u)
                    return 0.0;

                uint index =
                    layerID - 1u;

                index =
                    min(index, 31u);

                return _LayerColors[index];
            }

            float GetLayerOcclusion(
                uint layerID)
            {
                if (layerID == 0u)
                    return 0.0;

                uint index =
                    layerID - 1u;

                index =
                    min(index, 31u);

                return _LayerOcclusion[index].x;
            }

            float GetLayerWidth(
                uint layerID)
            {
                if (layerID == 0u)
                    return 0.0;

                uint index =
                    layerID - 1u;

                index =
                    min(index, 31u);

                return _LayerOcclusion[index].y;
            }

            // =====================================================
            // OCCLUSION
            // =====================================================

            float GetOcclusionVisibility(
                float2 uv,
                uint layerID)
            {
                if (layerID == 0u)
                    return 0.0;

                float alwaysVisible =
                    GetLayerOcclusion(
                        layerID
                    );

                /*
                 * AlwaysVisible:
                 *
                 * Derinlik testi yapilmaz, her zaman gorunur.
                 */

                if (alwaysVisible > 0.5)
                    return 1.0;

                float selectedDepth =
                    SampleSelectedDepth(uv);

                if (selectedDepth <= 0.0001)
                    return 0.0;

                float cameraDepth =
                    SampleCameraDepth(uv);

                /*
                 * Obje sahnede gorunur durumda.
                 */

                if (selectedDepth <=
                    cameraDepth + 0.001)
                {
                    return 1.0;
                }

                /*
                 * Obje baska bir yuzeyin arkasinda kaliyor.
                 */

                return 0.0;
            }

            // =====================================================
            // NEAREST OBJECT SEARCH
            //
            // Arka plan pikselinden 4 yonde disari dogru yuruyerek
            // en yakin objeyi ve mesafeyi bulur. Her objenin kendi
            // width degeri ile karsilastirilir.
            // =====================================================

            void FindNearestOutline(
                float2 uv,
                float2 pixelSize,
                int maxSteps,
                out uint outLayerID,
                out float2 outUV)
            {
                outLayerID = 0u;
                outUV = uv;

                float bestDistance =
                    1e6;

                /*
                 * 8 yon (kardinal + capraz): sadece 4 kardinal
                 * yon kullanilirsa outline siluetli/kose kose
                 * (diamond shape) gorunur. Capraz yonler eklenince
                 * cok daha yuvarlak ve pürüzsüz bir kenar elde
                 * edilir.
                 */

                float2 directions[8] =
                {
                    float2(0.0, 1.0),
                    float2(0.0, -1.0),
                    float2(1.0, 0.0),
                    float2(-1.0, 0.0),
                    float2(0.70710678, 0.70710678),
                    float2(-0.70710678, 0.70710678),
                    float2(0.70710678, -0.70710678),
                    float2(-0.70710678, -0.70710678)
                };

                [loop]
                for (int d = 0; d < 8; d++)
                {
                    [loop]
                    for (int s = 1; s <= maxSteps; s++)
                    {
                        float2 sampleUV =
                            uv +
                            directions[d] *
                            pixelSize *
                            (float) s;

                        uint lid =
                            SampleLayerID(sampleUV);

                        if (lid != 0u)
                        {
                            float layerWidth =
                                GetLayerWidth(lid);

                            if ((float) s <= layerWidth &&
                                (float) s < bestDistance)
                            {
                                bestDistance = (float) s;
                                outLayerID = lid;
                                outUV = sampleUV;
                            }

                            break;
                        }
                    }
                }
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

                half4 sceneColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        uv
                    );

                // -------------------------------------------------
                // Outline objenin ustune degil, sadece etrafina
                // cizilir.
                // -------------------------------------------------

                uint centerLayerID =
                    SampleLayerID(uv);

                if (centerLayerID != 0u)
                    return sceneColor;

                float2 pixelSize =
                    1.0 /
                    _ScreenParams.xy;

                int maxSteps =
                    max(
                        1,
                        (int) ceil(_MaxOutlineWidth)
                    );

                // -------------------------------------------------
                // SMOOTH / AA
                //
                // Tek merkez ornegi yerine piksel icinde 4 alt
                // nokta (rotated grid) ornekleniyor. Kenar
                // bolgesinde bazi alt noktalar isabet ediyor,
                // bazilari etmiyor; bu da yumusak (anti-alias)
                // bir gecis (kismi kapsama / coverage) sagliyor.
                // -------------------------------------------------

                float2 subOffsets[4] =
                {
                    float2(0.25, 0.25),
                    float2(-0.25, 0.25),
                    float2(0.25, -0.25),
                    float2(-0.25, -0.25)
                };

                float coverage =
                    0.0;

                half3 colorAccum =
                    0.0;

                [loop]
                for (int ss = 0; ss < 4; ss++)
                {
                    float2 subUV =
                        uv +
                        subOffsets[ss] *
                        pixelSize;

                    uint hitLayerID;
                    float2 hitUV;

                    FindNearestOutline(
                        subUV,
                        pixelSize,
                        maxSteps,
                        hitLayerID,
                        hitUV
                    );

                    if (hitLayerID == 0u)
                        continue;

                    float visibility =
                        GetOcclusionVisibility(
                            hitUV,
                            hitLayerID
                        );

                    if (visibility <= 0.0)
                        continue;

                    float4 layerColor =
                        GetLayerColor(
                            hitLayerID
                        );

                    coverage +=
                        0.25;

                    colorAccum +=
                        layerColor.rgb *
                        layerColor.a *
                        0.25;
                }

                if (coverage <= 0.0)
                    return sceneColor;

                half3 outlineColor =
                    colorAccum /
                    max(coverage, 0.0001);

                half3 result =
                    lerp(
                        sceneColor.rgb,
                        outlineColor,
                        coverage
                    );

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
