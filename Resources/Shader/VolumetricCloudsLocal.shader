Shader "Hidden/Juan/VolumetricCloudsLocal"
{
    Properties
    {
        
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        
        Pass
        {
            Name "Volumetric Clouds Local"
            
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
            #include "include/VolumetricCloudsHelper/inputsLocal.hlsl"
            #include "include/VolumetricCloudsHelper/functionsLocal.hlsl"
            
            TEXTURE2D(_HistoryTex);
            SAMPLER(sampler_HistoryTex);
            float4 _HistorySize;
  
            struct appData
            {
                float3 positionOS: POSITION;
            };
            
            struct v2f
            {
                float4 positionCS: SV_POSITION;
            };
            
            bool ShouldUpdatePixel(uint2 pixelPos, uint frameIndex)
            {
                uint tileX = (frameIndex & 3); // 0 ~ 3
                uint tileY = ((frameIndex >> 2) & 3); // 0 ~ 3
                
                uint localX = pixelPos.x & 3;
                uint localY = pixelPos.y & 3;
                return (localX == tileX) && (localY == tileY);
            }
            
            float4 SampleHistory(float2 uv, float2 pixelPos)
            {
                return SAMPLE_TEXTURE2D(_HistoryTex, sampler_HistoryTex, uv);
            }
            
            float4 SpatialReconstruct(float2 uv, uint2 pixelPos, uint frameIndex)
            {
                float4 result = 0;
                float totalWeight = 0;
                
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        uint2 samplePos = pixelPos + int2(x, y);
                        
                        for (uint f = 0; f < 4; f++)
                        {
                            uint testFrame = (frameIndex + 16 - f) & 15;
                            if (ShouldUpdatePixel(samplePos, testFrame))
                            {
                                float2 sampleUV = uv + float2(x, y) * _HistorySize.zw;
                                float4 sample = SAMPLE_TEXTURE2D(_HistoryTex, sampler_HistoryTex, sampleUV);
                                
                                float dist = length(float2(x, y));
                                float weight = 1.0 / (1.0 + dist);
                                
                                result += sample * weight;
                                totalWeight += weight;
                                break;
                            }
                        }
                    }
                }
                
                return totalWeight > 0? result / totalWeight : 0;
            }
            
            v2f vert(appData v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                o.positionCS = TransformObjectToHClip(v.positionOS);
                
                return o;
            }
            
            half4 frag(v2f input) :  SV_Target
            {
                uint2 pixelPos = uint2(input.positionCS.xy);
                float2 uv = input.positionCS.xy / _ScreenSize.xy;
                
                if (ShouldUpdatePixel(pixelPos, _FrameIndex))
                {
                    float3 viewDirVS = mul(unity_CameraInvProjection, float4(uv * 2.0 - 1, 0, -1));
                    float3 viewDirWS = mul(unity_CameraToWorld, float4(viewDirVS, 0));
                    
                    float3 rayStart = _WorldSpaceCameraPos;
                    float3 rayDir = normalize(viewDirWS);
                    
                    float4 accColor = 0;
                    float4 rayResult = CloudsRayMarchingLocal(rayStart, rayDir, viewDirWS, uv);
                    accColor += rayResult;
                    
                    accColor = saturate(accColor);
                    return float4(accColor);   
                }
                else
                {
                    float4 history = SampleHistory(uv, pixelPos);
                    
                    if (history.a < 0.001)
                    {
                        history = SpatialReconstruct(uv, pixelPos, _FrameIndex);
                    }
                    
                    return history;
                }
                
                
            }
            
            ENDHLSL
        }
    }
}
