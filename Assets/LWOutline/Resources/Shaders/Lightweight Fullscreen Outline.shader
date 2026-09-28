Shader "Hidden/Lightweight Fullscreen Outline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        // =========================================================
        // SHARED
        // =========================================================

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D_X(_BlitTexture);

        // xy = 1 / size, zw = size  (scaled mask / JFA texture'lari)
        float4 _OutlineMaskTexel;

        // JFA texture'inda "seed yok" isareti (half'ta tam temsil edilir).
        #define JFA_INVALID     30000.0
        #define JFA_VALID_LIMIT 20000.0

        struct BlitAttributes
        {
            uint vertexID : SV_VertexID;
        };

        struct BlitVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        BlitVaryings BlitVert(BlitAttributes input)
        {
            BlitVaryings output;

            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);

            return output;
        }

        ENDHLSL

        // =========================================================
        // PASS 0 - OUTLINE MASK
        // Kendi depth buffer'i ile: ayni pikselde birden fazla outline'li
        // obje varsa en yakin olan kalir.
        // R = layerID/255, G = device-space derinlik.
        // =========================================================

        Pass
        {
            Name "Outline Mask"

            ZWrite On
            ZTest LEqual
            Cull Back
            Blend One Zero

            HLSLPROGRAM

            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            #pragma multi_compile_instancing

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // x = layer basina maksimum outline mesafesi (world units), 0 = sinirsiz
            float4 _LayerRenderDistance[32];

            MaskVaryings MaskVertex(MaskAttributes input)
            {
                MaskVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);

                return output;
            }

            uint GetOutlineLayerID()
            {
                uint renderingLayers = GetMeshRenderingLayer();

                [unroll]
                for (uint i = 0u; i < 32u; i++)
                {
                    if ((renderingLayers & (1u << i)) != 0u)
                        return i + 1u;
                }

                return 0u;
            }

            half4 MaskFragment(MaskVaryings input) : SV_Target
            {
                uint layerID = GetOutlineLayerID();

                if (layerID == 0u)
                    discard;

                uint index = min(layerID - 1u, 31u);
                float maxDistance = _LayerRenderDistance[index].x;

                if (maxDistance > 0.0)
                {
                    // discard: depth yazip arkadaki outline'li objeleri gizlemesin.
                    if (distance(input.positionWS, GetCameraPositionWS()) > maxDistance)
                        discard;
                }

                // Mask sekli occlusion'dan bagimsiz (tam siluet); occlusion
                // Composite'te segment bazinda uygulanir.
                return half4(layerID / 255.0, input.positionCS.z, 0.0, 1.0);
            }

            ENDHLSL
        }

        // =========================================================
        // PASS 1 - JFA INIT
        // Iki seed alani (RGBAHalf, deger = kendi pikselinden texel ofseti):
        //   xy = boslukla komsu kenar pikselleri  (bos piksellere yayar)
        //   zw = daha uzaktaki BASKA layer ile komsu kenar pikselleri
        //        (baska objenin ICINDEKI piksellere yayar)
        // =========================================================

        Pass
        {
            Name "JFA Init"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM

            #pragma vertex BlitVert
            #pragma fragment JFAInitFragment

            uint InitLayerID(float2 uv)
            {
                return (uint) round(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).r * 255.0);
            }

            float InitEyeDepth(float2 uv)
            {
                float d = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).g;
                return LinearEyeDepth(d, _ZBufferParams);
            }

            float2 InitNeighborFlags(uint myLayer, float myEye, float2 nuv)
            {
                uint nLayer = InitLayerID(nuv);

                float emptyNeighbor = (nLayer == 0u) ? 1.0 : 0.0;

                float fartherOtherLayer =
                    ((nLayer != 0u) && (nLayer != myLayer) && (InitEyeDepth(nuv) > myEye))
                    ? 1.0 : 0.0;

                return float2(emptyNeighbor, fartherOtherLayer);
            }

            float4 JFAInitFragment(BlitVaryings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 texel = _OutlineMaskTexel.xy;

                uint myLayer = InitLayerID(uv);
                float myEye = InitEyeDepth(uv);

                float2 flags = InitNeighborFlags(myLayer, myEye, uv + float2( texel.x, 0.0));
                flags = max(flags, InitNeighborFlags(myLayer, myEye, uv + float2(-texel.x, 0.0)));
                flags = max(flags, InitNeighborFlags(myLayer, myEye, uv + float2(0.0,  texel.y)));
                flags = max(flags, InitNeighborFlags(myLayer, myEye, uv + float2(0.0, -texel.y)));

                bool inMask = (myLayer != 0u);

                // Seed ise ofset (0,0) = kendisi; degilse JFA_INVALID.
                float2 seedEmpty = (inMask && flags.x > 0.5) ? float2(0.0, 0.0) : float2(JFA_INVALID, JFA_INVALID);
                float2 seedOver  = (inMask && flags.y > 0.5) ? float2(0.0, 0.0) : float2(JFA_INVALID, JFA_INVALID);

                return float4(seedEmpty, seedOver);
            }

            ENDHLSL
        }

        // =========================================================
        // PASS 2 - JFA STEP
        // =========================================================

        Pass
        {
            Name "JFA Step"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM

            #pragma vertex BlitVert
            #pragma fragment JFAStepFragment

            float _JFAStep;

            // Outline'in ulasabilecegi maksimum mesafe (mask texel cinsinden).
            float _JFAReach;

            TEXTURE2D_X(_OutlineMask);

            void SampleSeedInfo(float2 seedUV, out uint layer, out float eyeDepth)
            {
                float2 m = SAMPLE_TEXTURE2D_X(_OutlineMask, sampler_PointClamp, seedUV).rg;
                layer = (uint) round(m.x * 255.0);
                eyeDepth = LinearEyeDepth(m.y, _ZBufferParams);
            }

            // Ayni layer -> ekranda yakin olan. Farkli layer'lar ve ikisi de
            // menzil icinde -> kameraya yakin olan layer kazanir.
            bool IsBetterSeed(
                float candDistSq, uint candLayer, float candEye,
                float bestDistSq, uint bestLayer, float bestEye)
            {
                float reachSq = _JFAReach * _JFAReach;

                bool differentLayers = (candLayer != bestLayer);
                bool bothInReach = (candDistSq <= reachSq) && (bestDistSq <= reachSq);

                return (differentLayers && bothInReach)
                    ? (candEye < bestEye)
                    : (candDistSq < bestDistSq);
            }

            float4 JFAStepFragment(BlitVaryings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 texel = _OutlineMaskTexel.xy;

                float2 bestEmpty = float2(JFA_INVALID, JFA_INVALID);
                float bestEmptyDistSq = 1e20;
                uint bestEmptyLayer = 0u;
                float bestEmptyEye = 1e20;

                float2 bestOver = float2(JFA_INVALID, JFA_INVALID);
                float bestOverDistSq = 1e20;
                uint bestOverLayer = 0u;
                float bestOverEye = 1e20;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 d = float2(x, y) * _JFAStep;
                        float2 candUV = uv + d * texel;

                        // Ofset tabanli oldugu icin ekran disi (clamp'lenen)
                        // adaylar atlanmali; aksi halde seed kayar.
                        bool inBounds =
                            (candUV.x >= 0.0) && (candUV.x <= 1.0) &&
                            (candUV.y >= 0.0) && (candUV.y <= 1.0);

                        float4 cand = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, candUV);

                        // Ekran disi aday -> gecersiz say (erken cikis / continue yok).
                        cand = inBounds ? cand : float4(JFA_INVALID, JFA_INVALID, JFA_INVALID, JFA_INVALID);

                        if (cand.x < JFA_VALID_LIMIT)
                        {
                            float2 seedOffset = d + cand.xy;
                            float distSq = dot(seedOffset, seedOffset);

                            uint candLayer;
                            float candEye;
                            SampleSeedInfo(uv + seedOffset * texel, candLayer, candEye);

                            if (IsBetterSeed(distSq, candLayer, candEye,
                                             bestEmptyDistSq, bestEmptyLayer, bestEmptyEye))
                            {
                                bestEmptyDistSq = distSq;
                                bestEmptyLayer = candLayer;
                                bestEmptyEye = candEye;
                                bestEmpty = seedOffset;
                            }
                        }

                        if (cand.z < JFA_VALID_LIMIT)
                        {
                            float2 seedOffset = d + cand.zw;
                            float distSq = dot(seedOffset, seedOffset);

                            uint candLayer;
                            float candEye;
                            SampleSeedInfo(uv + seedOffset * texel, candLayer, candEye);

                            if (IsBetterSeed(distSq, candLayer, candEye,
                                             bestOverDistSq, bestOverLayer, bestOverEye))
                            {
                                bestOverDistSq = distSq;
                                bestOverLayer = candLayer;
                                bestOverEye = candEye;
                                bestOver = seedOffset;
                            }
                        }
                    }
                }

                return float4(bestEmpty, bestOver);
            }

            ENDHLSL
        }

        // =========================================================
        // PASS 3 - OUTLINE COMPOSITE
        // Kamera rengine dogrudan alpha-blend eder. Sahne rengi
        // okunmaz; outline olmayan pikseller discard edilir.
        // =========================================================

        Pass
        {
            Name "Outline Composite"

            ZWrite Off
            ZTest Always
            Cull Off

            // rgb: src.rgb * coverage + dst.rgb * (1 - coverage)
            // a  : hedef alpha korunur.
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM

            #pragma vertex BlitVert
            #pragma fragment Frag

            TEXTURE2D_X(_OutlineMask);
            TEXTURE2D_X(_JFASeedTex);

            // Vector4 array: scalar float array 16 byte hizalama sorununu onler.
            float4 _LayerWidths[32];
            float4 _LayerColors[32];

            // x = depth test acik mi, y = bias
            float4 _LayerDepthTest[32];

            uint SampleLayerID(float2 uv)
            {
                half encoded = SAMPLE_TEXTURE2D_X(_OutlineMask, sampler_PointClamp, uv).r;
                return (uint) round(encoded * 255.0);
            }

            float SampleLayerDeviceDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_OutlineMask, sampler_PointClamp, uv).g;
            }

            bool IsSeedOccluded(float2 seedUV, uint layerID)
            {
                uint index = min(layerID - 1u, 31u);

                if (_LayerDepthTest[index].x < 0.5)
                    return false; // X-Ray

                float bias = _LayerDepthTest[index].y;

                float myEyeDepth = LinearEyeDepth(SampleLayerDeviceDepth(seedUV), _ZBufferParams);
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(seedUV), _ZBufferParams);

                return sceneEyeDepth + bias < myEyeDepth;
            }

            half4 Frag(BlitVaryings input) : SV_Target
            {
                float2 uv = input.uv;

                uint centerLayerID = SampleLayerID(uv);

                // Bos piksel -> xy (bosluga bakan seed'ler);
                // baska objenin icindeki piksel -> zw (uzaktaki layer'a bakan seed'ler).
                float4 jfa = SAMPLE_TEXTURE2D_X(_JFASeedTex, sampler_PointClamp, uv);
                float2 offset = (centerLayerID == 0u) ? jfa.xy : jfa.zw;

                if (offset.x > JFA_VALID_LIMIT)
                    discard;

                // Bu full-res pikselin denk geldigi JFA texel'inin merkezi +
                // texel ofseti = seed'in UV'si.
                float2 texelCenterUV =
                    (floor(uv * _OutlineMaskTexel.zw) + 0.5) * _OutlineMaskTexel.xy;

                float2 seed = texelCenterUV + offset * _OutlineMaskTexel.xy;

                uint layerID = SampleLayerID(seed);
                if (layerID == 0u)
                    discard;

                // Kendi silueti icinde cizme.
                if (centerLayerID == layerID)
                    discard;

                // Baska outline'li objenin ustundeysek sadece seed daha
                // yakinsa ciz (onde olanin outline'i tam, arkadakinin kesik).
                if (centerLayerID != 0u)
                {
                    float seedEye = LinearEyeDepth(SampleLayerDeviceDepth(seed), _ZBufferParams);
                    float centerEye = LinearEyeDepth(SampleLayerDeviceDepth(uv), _ZBufferParams);

                    if (seedEye > centerEye)
                        discard;
                }

                uint index = min(layerID - 1u, 31u);
                float width = _LayerWidths[index].x;

                float distPixels = length((uv - seed) * _ScreenParams.xy);

                if (distPixels > width + 1.0)
                    discard;

                float coverage = 1.0 - smoothstep(max(width - 1.0, 0.0), width, distPixels);
                if (coverage <= 0.0)
                    discard;

                if (IsSeedOccluded(seed, layerID))
                    discard;

                float4 layerColor = _LayerColors[index];

                return half4(layerColor.rgb * layerColor.a, coverage);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
