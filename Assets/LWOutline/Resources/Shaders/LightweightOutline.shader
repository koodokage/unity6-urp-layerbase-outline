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
            ZTest Always
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
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // x = bu layer'a ait maksimum outline mesafesi (world units).
            // 0 = sinirsiz.
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

                // Rendering layer 0 -> ID 1, layer 1 -> ID 2, ... 0 = outline yok.
                [unroll]
                for (uint i = 0u; i < 32u; i++)
                {
                    uint bit = 1u << i;

                    if ((renderingLayers & bit) != 0u)
                        return i + 1u;
                }

                return 0u;
            }

            half4 MaskFragment(MaskVaryings input) : SV_Target
            {
                uint layerID = GetOutlineLayerID();

                if (layerID != 0u)
                {
                    uint index = min(layerID - 1u, 31u);
                    float maxDistance = _LayerRenderDistance[index].x;

                    if (maxDistance > 0.0)
                    {
                        float camDistance = distance(input.positionWS, GetCameraPositionWS());

                        if (camDistance > maxDistance)
                            layerID = 0u;
                    }
                }

                // NOT: Burada occlusion/derinlik testi KASITLI olarak
                // uygulanmiyor. Mask, objenin TAM/kesintisiz silueti
                // olarak uretilmeli ki JFA'nin uzerinde calistigi mesafe
                // alani temiz kalsin (occluded bolgeyi burada "delersek"
                // JFA o delik kenarini da ayri bir siluet sanip cift
                // outline / dugumlenme artifact'i olusturur).
                //
                // Occlusion kontrolu Composite pass'te, her outline
                // pikseli icin ayri ayri "bu segmenti ciz mi cizme mi"
                // seklinde, mask SEKLINI degistirmeden uygulanir.
                //
                // R kanali: layerID (0=yok, 1..32=layer 0..31)
                // G kanali: bu fragment'in kendi device-space derinligi
                //           (yalnizca layerID!=0 iken anlamli; composite
                //           pass bunu seed UV'sinden geri okuyup sahne
                //           derinligiyle karsilastiracak).
                return half4(layerID / 255.0, input.positionCS.z, 0.0, 1.0);
            }

            ENDHLSL
        }


        // =========================================================
        // FULLSCREEN BLIT PASSES (JFA Init / JFA Step / Composite)
        // =========================================================

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D_X(_BlitTexture);
        float4 _BlitTexture_TexelSize;

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
        // PASS 1
        // JFA INIT
        // Mask texture'daki her piksel icin: doluysa kendi UV'sini,
        // bosSa (-1,-1) "seed yok" degerini yazar.
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

            float4 JFAInitFragment(BlitVaryings input) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.uv).r;

                if (m > 0.001h)
                    return float4(input.uv, 0.0, 0.0);

                return float4(-1.0, -1.0, 0.0, 0.0);
            }

            ENDHLSL
        }


        // =========================================================
        // PASS 2
        // JFA STEP
        // 3x3 komsuluk (step ile olceklenmis) icinde en yakin seed'i
        // bulur. log2(maxWidth) civarinda pass ile calisir, boylece
        // outline genisligi ne olursa olsun maliyet sabit kalir.
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

            float4 JFAStepFragment(BlitVaryings input) : SV_Target
            {
                float2 uv = input.uv;

                float2 bestSeed = float2(-1.0, -1.0);
                float bestDistSq = 1e20;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offsetUV = uv +
                            float2(x, y) * _JFAStep * _BlitTexture_TexelSize.xy;

                        float2 seed = SAMPLE_TEXTURE2D_X(
                            _BlitTexture, sampler_PointClamp, offsetUV).xy;

                        if (seed.x < 0.0)
                            continue;

                        float2 diff = (uv - seed) / _BlitTexture_TexelSize.xy;
                        float distSq = dot(diff, diff);

                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestSeed = seed;
                        }
                    }
                }

                return float4(bestSeed, 0.0, 0.0);
            }

            ENDHLSL
        }


        // =========================================================
        // PASS 3
        // OUTLINE COMPOSITE
        // =========================================================

        Pass
        {
            Name "Outline Composite"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM

            #pragma vertex BlitVert
            #pragma fragment Frag

            TEXTURE2D_X(_OutlineMask);
            TEXTURE2D_X(_JFASeedTex);

            /*
             * x = bu layer'a ait outline mesafesi (full-res piksel)
             *
             * Vector4 array kullanmamizin sebebi: HLSL constant buffer'da
             * scalar float array elemanlari 16 byte'a hizalanir ama
             * Unity'nin SetFloatArray'i veriyi sikisik gonderir; bu da
             * ilk eleman disindaki degerlerin bozuk okunmasina yol acar.
             */
            float4 _LayerWidths[32];
            float4 _LayerColors[32];

            // x = depth test acik mi (1 = normal occlusion, 0 = X-Ray/her
            // zaman gorunur), y = depth bias (world units, LinearEyeDepth
            // uzayinda kullanilir).
            float4 _LayerDepthTest[32];

            uint SampleLayerID(float2 uv)
            {
                half encoded = SAMPLE_TEXTURE2D_X(_OutlineMask, sampler_PointClamp, uv).r;
                return (uint) round(encoded * 255.0);
            }

            // Mask pass'te yazilan, o siluet noktasinin KENDI device-space
            // derinligi (G kanali). Sadece mask'ta layerID!=0 olan
            // pikseller icin anlamlidir.
            float SampleLayerDeviceDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_OutlineMask, sampler_PointClamp, uv).g;
            }

            float4 GetLayerColor(uint layerID)
            {
                if (layerID == 0u) return 0.0;
                uint index = min(layerID - 1u, 31u);
                return _LayerColors[index];
            }

            float GetLayerWidth(uint layerID)
            {
                if (layerID == 0u) return 0.0;
                uint index = min(layerID - 1u, 31u);
                return _LayerWidths[index].x;
            }

            // Bu outline segmentinin KAYNAGI olan siluet noktasi (seedUV),
            // o ekran konumunda sahnenin opak derinligine gore gizli mi?
            // Mask'in SEKLINI degil, sadece "bu segmenti ciz/cizme"
            // kararini etkiler - boylece JFA alani hep temiz/deliksiz
            // silueti kullanir, cift-outline/dugum artifact'i olusmaz.
            bool IsSeedOccluded(float2 seedUV, uint layerID)
            {
                uint index = min(layerID - 1u, 31u);

                float depthTestEnabled = _LayerDepthTest[index].x;
                if (depthTestEnabled < 0.5)
                    return false; // X-Ray: hep gorunur

                float bias = _LayerDepthTest[index].y;

                float myDeviceDepth = SampleLayerDeviceDepth(seedUV);
                float sceneRawDepth = SampleSceneDepth(seedUV);

                float myEyeDepth = LinearEyeDepth(myDeviceDepth, _ZBufferParams);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

                // Sahnede bu noktadan daha yakin (daha kucuk eye depth)
                // bir sey varsa, bu siluet noktasi occluded'dir.
                return sceneEyeDepth + bias < myEyeDepth;
            }

            half4 Frag(BlitVaryings input) : SV_Target
            {
                float2 uv = input.uv;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, uv);

                // Bu piksel zaten outline'li objenin ustundeyse, ustune
                // outline cizme (outline sadece cevresine cizilir).
                uint centerLayerID = SampleLayerID(uv);
                if (centerLayerID != 0u)
                    return sceneColor;

                float2 seed = SAMPLE_TEXTURE2D_X(_JFASeedTex, sampler_PointClamp, uv).xy;
                if (seed.x < 0.0)
                    return sceneColor;

                uint layerID = SampleLayerID(seed);
                if (layerID == 0u)
                    return sceneColor;

                float width = GetLayerWidth(layerID);

                // Mesafe full-res ekran pikseli cinsinden hesaplanir;
                // JFA'nin calistigi (dusuk) cozunurlukten bagimsizdir.
                float distPixels = length((uv - seed) * _ScreenParams.xy);

                if (distPixels > width + 1.0)
                    return sceneColor;

                float coverage = 1.0 - smoothstep(max(width - 1.0, 0.0), width, distPixels);
                if (coverage <= 0.0)
                    return sceneColor;

                // Per-layer occlusion: mask'in SEKLINI degil, sadece bu
                // segmentin cizilip cizilmeyecegini belirler.
                if (IsSeedOccluded(seed, layerID))
                    return sceneColor;

                float4 layerColor = GetLayerColor(layerID);

                half3 result = lerp(sceneColor.rgb, layerColor.rgb * layerColor.a, coverage);

                return half4(result, sceneColor.a);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
