Shader "Hidden/Juan/VolumetricClouds"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        
        HLSLINCLUDE
        
        static const int temporalPattern[16] = {
            0, 9, 3, 12,
            6, 15, 1, 10,
            4, 13, 7, 8,
            2, 11, 5, 14
        };
        
        ENDHLSL
        
        // =================================
        // Shader Pass 0: Raymarching  
        // =================================
        Pass
        {
            Name "Volumetric Clouds Raymarching"
            Tags
            {
                "LightMode" = "VolumetricCloudsRaymarching"
            }
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            #include "include/VolumetricCloudsHelper/inputs.hlsl"
            #include "include/VolumetricCloudsHelper/functions.hlsl"
            
            #ifndef VOLUME_LIMITS
            #define VOLUME_LIMITS 8
            #endif
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewDirWS: TEXCOORD1;
            };
            
            struct FragOutput
            {
                half4 color: SV_Target0;
                float depth: SV_Target1;
            };
            
            bool RayBoxIntersect(int index, float3 rayStart, float3 rayDir, out float tNearWorld, out float tFarWorld)
            {
                float4x4 worldToLocal = _VolumeWorldToLocal[index];
                
                float3 rayStartLocal = mul(worldToLocal, float4(rayStart, 1.0)).xyz;
                float3 rayDirLocal = mul(worldToLocal, float4(rayDir, 0.0)).xyz;
                float rayDirLocalLength = length(rayDirLocal);
                rayDirLocal = normalize(rayDirLocal);
                
                float3 boundsMin = _BoundsMin[index];
                float3 boundsMax = _BoundsMax[index];
                float3 boundsSize = boundsMax - boundsMin;
                
                float3 localMin = -boundsSize * 0.5;
                float3 localMax = boundsSize * 0.5;
                
                float3 invRayDirLocal = rcp(rayDirLocal);
                float3 t0 = (localMin - rayStartLocal) * invRayDirLocal;
                float3 t1 = (localMax - rayStartLocal) * invRayDirLocal;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                tNear = max(0, tNear);
                float tFar = min(min(tmax.x, tmax.y), tmax.z);
                
                // Convert to world space distances
                tNearWorld = tNear / rayDirLocalLength;
                tFarWorld = tFar / rayDirLocalLength;
                
                return tNear < tFar && tFar > 0;
            }
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                float3 viewDirVS = mul(unity_CameraInvProjection, float4(o.uv * 2.0 - 1, 0, -1));
                o.viewDirWS = mul(unity_CameraToWorld, float4(viewDirVS, 0));
                
                return o;
            }
            
            FragOutput frag(v2f input)
            {
                FragOutput output;
                
                float3 rayStart = _WorldSpaceCameraPos;
                float rayLength = length(input.viewDirWS);
                float3 rayDir = normalize(input.viewDirWS);
                
                float4 accColor = float4(0,0,0,0);
                float depth = _ProjectionParams.z;
                [loop]
                for (int i = 0; i < _VolumeCount; i++)
                {
                    if (accColor.a >= 0.99) break;
                    float volumeDepth = _ProjectionParams.z;
                    float4 rayResult = CloudsRayMarching(i, rayStart, rayDir, rayLength, input.uv, volumeDepth);
                    
                    if (rayResult.a > 0.001 && volumeDepth < depth)
                    {
                        depth = volumeDepth;
                    }
                    
                    accColor.rgb = accColor.rgb + rayResult.rgb * (1 - accColor.a);
                    accColor.a += rayResult.a * (1 - accColor.a);
                    accColor.a = saturate(accColor.a);   
                }
                
                if (depth >= _ProjectionParams.z * 0.99) depth = 0.0;
                
                output.color = accColor;
                output.depth = depth;
                return output;
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 1: Reprojection 
        // =================================
        Pass
        {
            Name "Reproject Clouds"
            Tags
            {
                "LightMode" = "VolumetricCloudsReprojection"
            }
            Blend Off
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment fragBlit
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_VolumetricCloudsResult);
            TEXTURE2D(_VolumetricCloudsDepthResult);
            TEXTURE2D(_CloudsHistoryTex);
            TEXTURE2D(_CloudsHistoryDepthTex);
            
            float4x4 _InvMatrixVP;
            float4x4 _PrevMatrixVP;
            
            int _TemporalFrameIndex;
            float4 _Resolution;
            int _IsFirstFrame;
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct FragOutput
            {
                half4 color: SV_Target0;
                float depth: SV_Target1;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            float2 ReprojectToPreviousFrame(float3 worldPos)
            {
                float4 prevClipPos = mul(_PrevMatrixVP, float4(worldPos, 1.0));
                float2 prevNDC = prevClipPos.xy / prevClipPos.w;
                
                float2 prevUV = prevNDC * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                prevUV.y = 1.0 - prevUV.y;
                #endif
                
                return prevUV;
            }
            
            float4 BilateralUpsample(float2 uv, out float depth)
            {
                float sceneDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_LinearClamp, uv).r;
                float linearDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                
                float4 centerColor = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsResult, sampler_LinearClamp, uv, 0);
                float centerDepth = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsDepthResult, sampler_PointClamp, uv, 0);
                
                static const float2 offsets[4] = {
                    float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1)
                };
                
                float depthThreshold = 1.0;
                float totalWeight = 1.0;
                float4 colorSum = centerColor;
                float depthSum = centerDepth;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float2 offset = offsets[i];
                    float2 sampleUV = (uv + offset) * _Resolution.zw;
                    
                    if (any(sampleUV < 0.0 || sampleUV > 1.0))
                    {
                        continue;
                    }
                    
                    float4 cloudsSample = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsResult, sampler_LinearClamp, sampleUV, 0);
                    float cloudsDepth = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsDepthResult, sampler_PointClamp, sampleUV, 0);
                    
                    if (cloudsSample.a < 0.001) continue;
                    
                    float spatialWeight = exp(-dot(offset, offset) / 2.0);
                    
                    float depthDiff = abs(linearDepth - cloudsDepth);
                    float depthWeight = exp(-depthDiff / depthThreshold) * 0.5;
                    
                    float weight = spatialWeight * depthWeight;
                    
                    colorSum += cloudsSample * weight;
                    depthSum += cloudsDepth * weight;
                    totalWeight += weight;
                }

                depth = depthSum / totalWeight;
                return colorSum / totalWeight;
            }
            
            float GetReprojectionConfidence(float2 prevUV, float currentDepth, float historyDepth, float2 velocity)
            {
                if (any(prevUV < 0.0) || any(prevUV > 1.0))
                    return 0.0;
                
                float depthDiff = abs(currentDepth - historyDepth);
                float depthThreshold = max(1.0, currentDepth * 0.1);
                float depthConfidence = saturate(1.0 - depthDiff / depthThreshold);
                
                float velocityMagnitude = length(velocity * _ScreenParams.xy);
                float velocityConfidence = saturate(1.0 - velocityMagnitude / 20.0);
                
                return depthConfidence * velocityConfidence;
            }
            
            FragOutput fragBlit(v2f input, float4 screenPos: SV_POSITION) : SV_Target
            {
                FragOutput output;
                
                // float2 pixelCoord = input.uv * _ScreenParams.xy;
                int2 pixelCoord = int2(screenPos.xy);
                int2 gridPos = int2(pixelCoord) % 4;
                int pixelPattern = gridPos.y * 4 + gridPos.x;
                int currentPattern = temporalPattern[_TemporalFrameIndex];
                
                float sceneDpeth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_LinearClamp, input.uv).r;
                float linearDepth = LinearEyeDepth(sceneDpeth, _ZBufferParams);
                
                float depthThreshold = 1.0;
                
                float4 centerColor = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsResult, sampler_LinearClamp, input.uv, 0);
                float centerDepth = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsDepthResult, sampler_PointClamp, input.uv, 0);
                
                float4 colorSum = centerColor;
                float depthSum = centerDepth;
                float totalWeight = 1.0;
                
                float4 neighborMin = 1e10;
                float4 neighborMax = -1e10;
                float depthMin = 1e10;
                float depthMax = -1e10;
                
                
                static const float2 offsets[4] = {
                    float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1)
                };
                
                [unroll]
                for (int i = 0; i < 4; ++i)
                {
                    float2 offset = offsets[i];
                    float2 sampleUV = input.uv + offset * _Resolution.zw;
                    
                    if (any(sampleUV < 0.0 || sampleUV > 1.0))
                    {
                        continue;
                    }
                    
                    float4 cloudsSample = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsResult, sampler_LinearClamp, sampleUV, 0);
                    float cloudsDepth = SAMPLE_TEXTURE2D_LOD(_VolumetricCloudsDepthResult, sampler_PointClamp, sampleUV, 0);
                    
                    neighborMin = min(neighborMin, cloudsSample);
                    neighborMax = max(neighborMax, cloudsSample);
                    depthMin = min(depthMin, cloudsDepth);
                    depthMax = max(depthMax, cloudsDepth);
                    
                    if (cloudsSample.a < 0.001) continue;
                    
                    float spatialWeight = exp(-dot(offset, offset) / 2.0);
                    
                    float depthDiff = abs(linearDepth - cloudsDepth);
                    float depthWeight = exp(-depthDiff / depthThreshold) * 0.5;
                    
                    float weight = spatialWeight * depthWeight;
                    
                    colorSum += cloudsSample * weight;
                    depthSum += cloudsDepth * weight;
                    totalWeight += weight;
                }
                
                float upsampledDepth = depthSum / totalWeight;
                float4 upsampledColor = colorSum / totalWeight;
                
                if (_IsFirstFrame == 1)
                {
                    output.color = upsampledColor;
                    output.depth = upsampledDepth;
                    return output;
                }
                
                float4 historyColor;
                float historyDepth;
                float confidence = 1.0;
                
                if (pixelPattern == currentPattern)
                {
                    historyColor = SAMPLE_TEXTURE2D(_CloudsHistoryTex, sampler_LinearClamp, input.uv);
                    historyDepth = SAMPLE_TEXTURE2D(_CloudsHistoryDepthTex, sampler_PointClamp, input.uv);
                }
                else
                {
                    // float currentDepth = SAMPLE_TEXTURE2D(_VolumetricCloudsDepthResult, sampler_PointClamp, input.uv);
                    float currentDepth = upsampledDepth;
                    
                    bool hasValidDepth = currentDepth < _ProjectionParams.z * 0.99;
                    
                    float3 worldPos;
                    if (hasValidDepth)
                    {
                        float ndcDepth = (1.0 / currentDepth - _ZBufferParams.w) / _ZBufferParams.z;
                        ndcDepth = saturate(ndcDepth);
                        worldPos = ComputeWorldSpacePosition(input.uv, ndcDepth, _InvMatrixVP);
                    }
                    else
                    {
                        float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_PointClamp, input.uv).r;
                        worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, _InvMatrixVP);
                    }
                    
                    float2 prevUV = ReprojectToPreviousFrame(worldPos);
                    float2 velocity = input.uv - prevUV;
                    
                    historyColor = SAMPLE_TEXTURE2D(_CloudsHistoryTex, sampler_LinearClamp, prevUV);
                    historyDepth = SAMPLE_TEXTURE2D(_CloudsHistoryDepthTex, sampler_PointClamp, prevUV);
                    
                    confidence = GetReprojectionConfidence(prevUV, currentDepth, historyDepth, velocity);
                    
                    if (historyColor.a < 0.001) confidence = 0.0;
                }
                
                float4 boxCenter = (neighborMin + neighborMax) * 0.5;
                float4 boxExtents = (neighborMax - neighborMin) * 0.5;
                boxExtents = max(boxExtents, 0.003);
                
                neighborMin = boxCenter - boxExtents * 1.25;
                neighborMax = boxCenter + boxExtents * 1.25;
                
                float4 clampedHistory = clamp(historyColor, neighborMin, neighborMax);
                float clampedDepth = clamp(historyDepth, depthMin, depthMax);
                
                float4 clampDelta = abs(historyColor - clampedHistory);
                float clampAmount = saturate(dot(clampDelta.rgb, float3(0.3, 0.5, 0.2)) * 5.0);
                
                float baseBlend = 0.95;
                float adaptiveBlend = lerp(baseBlend, 0.5, clampAmount);
                adaptiveBlend *= confidence;
                
                output.color = lerp(upsampledColor, clampedHistory, adaptiveBlend);
                output.depth = lerp(upsampledDepth, clampedDepth, adaptiveBlend);
                
                return output;
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 2: Camera Blit  
        // =================================
        Pass
        {
            Name "Blend Clouds"
            Tags
            {
                "LightMode" = "VolumetricCloudsBlend"
            }
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment fragBlit
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_VolumetricCloudsTex);
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            half4 fragBlit(v2f input) : SV_Target
            {
                float4 cloudsSample = SAMPLE_TEXTURE2D(_VolumetricCloudsTex, sampler_LinearClamp, input.uv);
                return half4(cloudsSample.rgb, saturate(cloudsSample.a + 0.01));
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 3: Transparent Composition 
        // =================================
        Pass
        {
            Name "Composite Clouds After Transparent"
            Tags
            {
                "LightMode" = "VolumetricCloudsComposite"
            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_VolumetricCloudsTex);
            TEXTURE2D(_VolumetricCloudsDepthTex);
            TEXTURE2D(_TransparentDepthTex);
            TEXTURE2D(_BlueNoise);
            
            int _FrameIndex;
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
                        
            half4 frag(v2f input) : SV_Target
            {
                float2 blueNoiseUV = input.uv + float2(_FrameIndex * 0.4356, _FrameIndex * 0.9786);
                float3 blueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_PointRepeat, blueNoiseUV * _ScreenParams.xy * 1e-3);
                float2 dither = blueNoise.rg - 0.5;
                
                float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv);
                float4 cloudsColor = SAMPLE_TEXTURE2D(_VolumetricCloudsTex, sampler_LinearClamp, input.uv);
                float cloudsDepth = SAMPLE_TEXTURE2D(_VolumetricCloudsDepthTex, sampler_PointClamp, input.uv + dither * 0.025);
                float transparentDepth = SAMPLE_TEXTURE2D(_TransparentDepthTex, sampler_PointClamp, input.uv);
                
                return sceneColor;
                
                float linearTransparentDepth = LinearEyeDepth(transparentDepth, _ZBufferParams);
                
                float depthDiff = linearTransparentDepth - cloudsDepth;
                float normalizedDepthDiff = depthDiff / _ProjectionParams.z;
                float depthFadeFactor = saturate(normalizedDepthDiff * 200.0) * step(1e-5, transparentDepth);
                
                float ditherThreshold = depthFadeFactor + (blueNoise.z - 0.5) * 0.1;
                depthFadeFactor = saturate(ditherThreshold);
                
                float blendFactor = smoothstep(-0.5, 1.5, 1.0 - normalizedDepthDiff);
                blendFactor = blendFactor + (blueNoise.z - 0.5) * 0.1;
                blendFactor = saturate(blendFactor);
                
                float3 cloudsOverTransparent = lerp(cloudsColor.rgb, cloudsColor.rgb * cloudsColor.a + sceneColor * (1.0 - cloudsColor.a), blendFactor);
            
                float finalBlendAmount = depthFadeFactor * cloudsColor.a;
            
                float3 finalColor = lerp(sceneColor.rgb, cloudsOverTransparent, finalBlendAmount);
                return half4(finalColor, 1.0);
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 4: Sun Occlusion 
        // =================================
        Pass
        {
            Name "Sun Occlusion"
            Tags
            {
                "LightMode" = "VolumetricCloudsSunOcclusion"
            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            #ifdef ATMOSPHERIC_HEIGHT_FOG
            #ifdef ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD
            #include "Assets/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"
            #elifdef ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD 
            #include "Assets/09.ThirdParty/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"
            #endif
            #endif
            
            TEXTURE2D(_VolumetricCloudsTex);
            TEXTURE2D(_VolumetricCloudsDepthTex);
            
            float _LightRaysDistanceFade;
            float _SunRadius;
            float _SunIntensity;
            float _SunFalloff;
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewDirWS: TEXCOORD1;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                float2 ndc = o.uv * 2.0 - 1.0;
                float3 viewDirVS = mul(unity_CameraInvProjection, float4(ndc, 0, -1)).xyz;
                o.viewDirWS = mul(unity_CameraToWorld, float4(viewDirVS, 0)).xyz;
                
                return o;
            }
            
            half4 frag(v2f input) : SV_Target
            {
                Light mainLight = GetMainLight();
                float4 cloudsColor = SAMPLE_TEXTURE2D(_VolumetricCloudsTex, sampler_LinearClamp, input.uv);
                float cloudsOcclusion = cloudsColor.a;
                
                float cloudsDepth = SAMPLE_TEXTURE2D(_VolumetricCloudsDepthTex, sampler_PointClamp, input.uv);
                cloudsDepth = (cloudsDepth) * step(1e-5, cloudsDepth);
                
                float farFade = saturate(cloudsDepth / _LightRaysDistanceFade);
                // float distanceFade = lerp(0.2, 1.0, farFade);
                float distanceFade = smoothstep(_LightRaysDistanceFade, _LightRaysDistanceFade * 2, cloudsDepth);
                
                half3 nearColor = mainLight.color;
                half3 farColor = mainLight.color;
                #ifdef ATMOSPHERIC_HEIGHT_FOG
                farColor = lerp(mainLight.color, AHF_FogColorDuo, 0.5);
                #endif
                half3 rayColor = lerp(nearColor, farColor, farFade * 0.4);
                
                float3 viewDir = normalize(input.viewDirWS);
                
                float3 sunDir = _MainLightPosition.xyz;
                float sunAlignment = dot(viewDir, sunDir);
                
                float lowEdge = smoothstep(0.05, 0.2, cloudsOcclusion);
                float upperEdge = 1.0 - smoothstep(0.2, 1.0, cloudsOcclusion);
                float gap = lowEdge * upperEdge;
                
                float visilibity = pow(abs(sunAlignment), _SunFalloff);
                float intensity = visilibity * gap * _SunIntensity * distanceFade;
                
                half3 sunColor = intensity * rayColor;
                
                return half4(sunColor, 1.0);
            }
            ENDHLSL
        }

        // =================================
        // Shader Pass 5: Radial Blur  
        // =================================
        Pass
        {
            Name "Radial Blur"
            Tags
            {
                "LightMode" = "VolumetricCloudsRadialBlur"
            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_SunOcclusionTex);
            TEXTURE2D(_VolumetricCloudsTex);
            
            int _BlurSamples;
            float _BlurDensity;
            
            float _Comparison;
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            float2 WorldToScreenUV(float3 worldPos)
            {
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                float2 ndc = clipPos.xy / clipPos.w;
                float2 uv = ndc * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                return uv;
            }
            
            half4 frag(v2f input): SV_TARGET
            {
                float3 sunDir = -_MainLightPosition.xyz;
                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                
                float sunOCam = dot(sunDir, cameraForward);
                
                float2 targetScreenPos;
                float2 blurDir;
                
                float farDist = 10000.0;
                
                float cloudsOcclusion = 1.0;
                
                if (sign(sunDir.y) == sign(cameraForward.y))
                {
                    float4 cloudsSample = SAMPLE_TEXTURE2D(_VolumetricCloudsTex, sampler_LinearClamp, input.uv);
                    cloudsOcclusion = lerp(1.0, 0.0, cloudsSample.a);
                }
                
                if (sunOCam > 0.0)
                {
                    float3 sunWorldPos = _WorldSpaceCameraPos + sunDir * farDist;
                    targetScreenPos = WorldToScreenUV(sunWorldPos);
                    
                    blurDir = input.uv - targetScreenPos;
                }
                else
                {
                    float3 antiSolarWorldPos = _WorldSpaceCameraPos - sunDir * farDist;
                    targetScreenPos = WorldToScreenUV(antiSolarWorldPos);
                    
                    blurDir = targetScreenPos - input.uv;
                }
                
                float sunDist = length(blurDir);
                sunDir = smoothstep(0, 1.0, sunDist);
                blurDir = normalize(blurDir) * sunDir;
                
                blurDir *= _BlurDensity / float(_BlurSamples);
                
                float illuminanceDecay = 1.0;
                
                float3 result = 0;
                float2 uv = input.uv;
                [loop]
                for (int i = 0; i < _BlurSamples; ++i)
                {
                    uv += blurDir;
                    
                    float2 sampleUV = saturate(uv);
                    half3 sampleColor = SAMPLE_TEXTURE2D(_SunOcclusionTex, sampler_LinearClamp, sampleUV).rgb;
                    
                    float edgeFade = (all(uv >= 0.0) && all(uv <= 1.0))? 1.0 : 0.3;
                    
                    sampleColor *= illuminanceDecay * edgeFade * cloudsOcclusion;
                    result += sampleColor;
                    
                    illuminanceDecay *= 0.9f;
                }
                return half4(result.rgb, 1);
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 6: Light Rays Blur Horizontal 
        // =================================
        Pass
        {
            Name "Light Rays Blur Horizontal"
            Tags
            {
                "LightMode" = "LightRaysBlurHorizontal"
            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_LightRaysTex);
            float4 _LightRaysTex_TexelSize;
            float _BlurRadius;
            
            static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            half4 frag(v2f input): SV_TARGET
            {
                float2 texelSize = _LightRaysTex_TexelSize.xy * _BlurRadius;
                float4 lightRaysColor = SAMPLE_TEXTURE2D(_LightRaysTex, sampler_LinearClamp, input.uv) * weights[0];
                
                [unroll]
                for (int i = 1; i < 5; ++i)
                {
                    float2 offset = float2(texelSize.x * i, 0.0);
                    lightRaysColor += SAMPLE_TEXTURE2D(_LightRaysTex, sampler_LinearClamp, input.uv + offset) * weights[i];
                    lightRaysColor += SAMPLE_TEXTURE2D(_LightRaysTex, sampler_LinearClamp, input.uv - offset) * weights[i];
                }
                return half4(lightRaysColor.rgb, 1.0);
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 7: Light Rays Blur Vertical 
        // =================================
        Pass
        {
            Name "Light Rays Blur Vertical"
            Tags
            {
                "LightMode" = "LightRaysBlurVertical"
            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_LightRaysBlurHorizontalTex);
            float4 _LightRaysBlurHorizontalTex_TexelSize;
            float _BlurRadius;
            
            static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            half4 frag(v2f input): SV_TARGET
            {
                float2 texelSize = _LightRaysBlurHorizontalTex_TexelSize.xy * _BlurRadius;
                float4 lightRaysColor = SAMPLE_TEXTURE2D(_LightRaysBlurHorizontalTex, sampler_LinearClamp, input.uv) * weights[0];
                
                [unroll]
                for (int i = 1; i < 5; ++i)
                {
                    float2 offset = float2(0.0, texelSize.y * i);
                    lightRaysColor += SAMPLE_TEXTURE2D(_LightRaysBlurHorizontalTex, sampler_LinearClamp, input.uv + offset) * weights[i];
                    lightRaysColor += SAMPLE_TEXTURE2D(_LightRaysBlurHorizontalTex, sampler_LinearClamp, input.uv - offset) * weights[i];
                }
                return half4(lightRaysColor.rgb, 1.0);
            }
            
            ENDHLSL
        }

        // =================================
        // Shader Pass 8: Light Rays Composition 
        // =================================
        Pass
        {
            Name "Light Rays Composition"
            Tags
            {
                "LightMode" = "LightRaysComposition"
            }
            Blend One One
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            
            TEXTURE2D(_LightRaysTex);
            
            float _LightRaysIntensity;
            float _LightRaysDistanceFade;
            
            struct appData
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                
                return o;
            }
            
            half4 frag(v2f input): SV_TARGET
            {
                float4 lightRays = SAMPLE_TEXTURE2D(_LightRaysTex, sampler_LinearClamp, input.uv);
                
                lightRays.rgb *= _LightRaysIntensity;
                lightRays = min(lightRays, 2.0);
                
                return half4(lightRays.rgb, 1.0);
            }
            
            ENDHLSL
        }
    }
}