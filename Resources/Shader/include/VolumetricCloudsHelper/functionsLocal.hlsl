#pragma once

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "inputsLocal.hlsl"

// #if defined(ATMOSPHERIC_HEIGHT_FOG)
#include "Assets/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"
// #endif

float remap(float v, float minOld, float maxOld, float minNew, float maxNew)
{
    return minNew + (v - minOld) * (maxNew - minNew) / max(maxOld - minOld, 1e-5);
}

float remap01(float v, float left, float right)
{
    return (v - left) / (right - left);
}

// ====================================
// Henyey-Greenstein Phase Function
// ====================================
float hg(float cosTheta, float g)
{
    float g2 = g*g;
    return (1 - g2) / (4 * PI * pow(1 + g2 - 2 * g * cosTheta, 1.5));
}

float phase(float cosTheta, float4 phaseParam)
{
    float blend = .5;
    float hgBlend = hg(cosTheta, phaseParam.x) * (1 - blend) + hg(cosTheta, phaseParam.y) * blend;
    return phaseParam.z + hgBlend * phaseParam.w;
}

float SampleDensityLocal(float3 rayPos)
{
    float3 size = _BoundsMax - _BoundsMin;
    float3 invSize = rcp(size);
    float3 center = (_BoundsMin + _BoundsMax) * 0.5;
    float height = (rayPos.y - _BoundsMin.y) / size.y;
    float3 uvw = (rayPos - center) * invSize;
    float3 shapeUVW = uvw * _ShapeTiling + _ShapeOffset * _Time.y * 0.1;
    float3 detailUVW = uvw * _DetailTiling + _DetailOffset * _Time.y * 0.1;
    
    float coverage = 1.0;
    float cloudType = 0.5;
    float2 curlNoise = 0;
    if (_UseWeatherMap > 0.5)
    {
        float2 weatherUV = rayPos.xz * invSize.xz * _WeatherMapTilingOffset.xy + _WeatherMapTilingOffset.zw * _Time.y * 0.1;
        float4 weatherData = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_LinearRepeat, weatherUV, 0);
        coverage = saturate(weatherData.x * _Coverage);
        // Form the Anvil Shape
        coverage = pow(coverage, remap(height, 0.7, 0.8, 1.0, lerp(1.0, 0.1, _AnvilBias)));
        
        cloudType = weatherData.y;
        
        curlNoise = weatherData.zw;
    }
    else
    {
        coverage = _Coverage;
    }
    if (coverage < 0.01) return 0;
    
    float edgeFadeDist = _EdgeFadeDistance;
    float dist2X = min(edgeFadeDist, min(rayPos.x - _BoundsMin.x, _BoundsMax.x - rayPos.x));
    float dist2Z = min(edgeFadeDist, min(rayPos.z - _BoundsMin.z, _BoundsMax.z - rayPos.z));
    float edgeWeight = min(dist2X, dist2Z) / edgeFadeDist;
    
    // ========================
    // Height Gradient
    // ========================
    float stratusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * remap(height, 0.2, 0.3, 1.0, 0.0);
    float cumulusGradient = remap(height, 0.0, 0.2, 0.0, 1.0) * remap(height, 0.4, 0.6, 1.0, 0.0);
    float cumulonimbusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * remap(height, 0.6, 0.8, 1.0, 0.0);
    
    float heightGradient;
    if (cloudType < 0.5)
    {
        heightGradient = lerp(stratusGradient, cumulusGradient, cloudType * 2.0);    
    }
    else
    {
        heightGradient = lerp(cumulusGradient, cumulonimbusGradient, (cloudType - 0.5) * 2.0);
    }
    heightGradient = saturate(heightGradient);
    
    heightGradient *= edgeWeight;
    
    float4 shapeNoise = tex3D(_ShapeNoise, shapeUVW);
    float4 shapeWeightNormalized = _ShapeWeights / dot(_ShapeWeights, 1);
    float shapeFBM = dot(max(0, shapeNoise), shapeWeightNormalized);
    
    float shapeWithCoverage = remap(shapeFBM, 1 - coverage, 1.0, 0.0, 1.0);
    shapeWithCoverage = saturate(shapeWithCoverage);
    
    float shapeDensity = saturate(shapeWithCoverage * heightGradient);
    
    if (shapeDensity > 0)
    {
        detailUVW = detailUVW + float3(curlNoise.x, 0, curlNoise.y) * 2;
        float3 detailNoise = tex3D(_DetailNoise, detailUVW).yzw;
        float3 detailWeightNormalized = _DetailWeights/(dot(_DetailWeights, 1));
        float detailFBM = dot(detailNoise, detailWeightNormalized);
        
        float oneMinusShape = 1.0 - shapeFBM;
        float detailErodeWeight = oneMinusShape * oneMinusShape * oneMinusShape;
        float detailWeight = _DetailNoiseWeights;
        float cloudDensity = shapeDensity - saturate((1 - detailFBM) * detailWeight);
        return cloudDensity * _Density * 0.1;
    }
    
    return 0;
}

float LightRayMarchingLocal(float3 rayPos, float tNear, float tFar)
{
    float3 lightDirWS = _MainLightPosition; // towards mainLight
    
    float dstInsideBox = max(0, tFar - tNear);
    
    float stepSize = dstInsideBox / _LightSteps;
    float totalDensity = 0;
    
    [loop]
    for (int i = 0; i < _LightSteps; i++)
    {
        rayPos += lightDirWS * stepSize;
        float density = max(0, SampleDensityLocal(rayPos) * stepSize);
        totalDensity += density;
    }
    
    float transmittance = exp(-totalDensity * _LightAbsorptionTowardsSun);
    return _DarknessThreshold + transmittance * (1 - _DarknessThreshold);
    
}

float4 CloudsRayMarchingLocal(float3 rayStart, float3 rayDir, float3 viewDirWS, float2 uv)
{
    // rayStart + t0 * rayDir = 0.0
    // rayStart + t1 * rayDir = 1.0
    float3 invRayDir = rcp(rayDir);
    float3 t0 = (_BoundsMin - rayStart) * invRayDir;
    float3 t1 = (_BoundsMax - rayStart) * invRayDir;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    
    float tNear = max(max(tmin.x, tmin.y), tmin.z);
    tNear = max(0, tNear);
    float tFar = min(min(tmax.x, tmax.y), tmax.z);
    
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * length(viewDirWS);
    
    if (eyeDepth < tNear) return 0;
    
    if (tNear >= tFar || tFar < 0) return 0;
    
    float2 blueNoiseUV = uv + float2(_FrameIndex * 0.75487, _FrameIndex * 0.56783);
    float blueNoise = tex2D(_BlueNoise, blueNoiseUV * _ScreenParams.xy * 0.004).x;
    
    float rayLength = min(eyeDepth - tNear, tFar - tNear);
    float stepSize = rayLength / _Steps;
    float3 rayPos = rayStart + rayDir * (tNear + blueNoise * stepSize * 2);
    float3 rayOrigin = rayPos;
    
    float cosAngle = dot(rayDir, GetMainLight().direction);
    float phaseVal = phase(cosAngle, _PhaseParams);
    
    float transmittance = 1;
    float lightEnergy = 0;
    
    [loop]
    for (int i = 0; i < int(_Steps); i++)
    {
        float density = SampleDensityLocal(rayPos);
        
        if (density > 0)
        {
            float lightTransmittance = LightRayMarchingLocal(rayPos, tNear, tFar);
            lightEnergy += density * stepSize * transmittance * lightTransmittance * phaseVal;
            
            transmittance *= exp(-density * stepSize * _LightAbsorptionThroughCloud);
            if (transmittance < 0.01)
                break;
        }
        
        rayPos += rayDir * stepSize;
        
        if (any(rayPos < _BoundsMin) || any(rayPos > _BoundsMax)) break;
    }
    
    float3 cloudsColor = lightEnergy * _MainLightColor;
    
    // #if defined(ATMOSPHERIC_HEIGHT_FOG)
    float4 fogParams = GetAtmosphericHeightFog(rayOrigin + (rayPos - rayOrigin) * _FogPhase);
    cloudsColor = ApplyAtmosphericHeightFog(cloudsColor, fogParams);
    // #endif
    
    return half4(cloudsColor, 1 - transmittance);
}