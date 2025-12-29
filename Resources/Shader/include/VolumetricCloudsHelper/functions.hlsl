#pragma once

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "inputs.hlsl"

#ifdef ATMOSPHERIC_HEIGHT_FOG
#ifdef ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD
#include "Assets/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"
#elifdef ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD 
#include "Assets/09.ThirdParty/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"
#endif
#endif

float remap(float v, float minOld, float maxOld, float minNew, float maxNew)
{
    // return minNew + (v - minOld) * (maxNew - minNew) / max(maxOld - minOld, 1e-5);
    return minNew + (((v - minOld) / (maxOld - minOld)) * (maxNew - minNew));
}

float2 SwirlUV(float2 uv, float2 center, float strength)
{
    float2 delta = uv - center;
    float angle = strength * length(delta);
    float x = cos(angle) * delta.x - sin(angle) * delta.y;
    float y = sin(angle) * delta.x + cos(angle) * delta.y;
    return float2(x + center.x, y + center.y);
}

float2 PolarCoordinate(float2 uv, float2 center)
{
    float2 delta = uv - center;
    float radius = length(delta) * 2;
    float angle = atan2(delta.x, delta.y) * 1.0 / 6.28;
    return float2(angle, radius);
}

// ====================================
// Henyey-Greenstein Phase Function
// ====================================
float HG(float cosTheta, float g)
{
    g = min(g, 0.9999);
    float g2 = g*g;
    return (1 - g2) / (4 * PI * pow(1 + g2 - 2 * g * cosTheta, 1.5));
}

float phase(float cosTheta, float4 phaseParams, float4 silverParams)
{
    float g1 = phaseParams.x;
    float hgBlend = max(HG(cosTheta, g1), saturate(silverParams.x * 5 * HG(cosTheta, 0.99 - silverParams.y)));
    return phaseParams.y + hgBlend * phaseParams.z;
}

float CalculateLODLevel(float index, float distance)
{
    float lodNear = _LODNear[index];
    float lodFar = _LODFar[index];
    
    if (distance < lodNear) return 0.0;
    else if (distance < lodFar) return 1.0 + (distance - lodNear) / (lodFar - lodNear);
    return 2.0 + (distance - lodFar) / lodFar;
}

float CalculateLODMultiplier(float lodLevel)
{
    if (lodLevel < 1.0) return 1.0;
    else if (lodLevel < 2.0) return lerp(1.0, 1.5, lodLevel - 1.0);
    else if (lodLevel < 3.0) return lerp(1.5, 2.0, lodLevel - 2.0);
    else return lerp(2.0, 3.0, saturate(lodLevel - 3.0));
}

float GetTextureLOD(float index, float distance, float baseLOD)
{
    float distanceLOD = saturate(distance / _LODFar[index]) * 2.0;
    return baseLOD + distanceLOD;
}

bool ShouldSampleDetail(float index, float distance, float transmittance)
{
    if (transmittance < 0.3) return false;
    
    float detailFadeStart = _LODNear[index];
    float detailFadeEnd = _LODFar[index];
    float detailFactor = 1.0 - saturate((distance - detailFadeStart) / (detailFadeEnd - detailFadeStart));
    
    float transmittanceFactor = smoothstep(0.1, 0.3, transmittance);
    return (detailFactor * transmittanceFactor) > 0.1;
}

int CalculateLightMarchingSteps(float index, float lodLevel, float transmittance, float density)
{
    if (density < 0.01) return 0;
    
    int baseSteps = _LightSteps[index];
    int halfSteps = ceil(baseSteps * 0.5);
    int quatSteps = ceil(baseSteps * 0.25);
    
    if (lodLevel >= 2.0) baseSteps = quatSteps;
    else if (lodLevel >= 1.0) baseSteps = halfSteps;
    
    if (transmittance < 0.2) baseSteps = min(quatSteps, 2);
    else if (transmittance < 0.4) baseSteps = min(halfSteps, 4);
    
    return baseSteps;
}

float3 CalculateWindOffset(int index, float height, out float3 timeOffset)
{
    float4x4 worldToLocal = _VolumeWorldToLocal[index]; 
    float3 windDirection = mul(worldToLocal, float4(_WindDirection[index].xyz, 0.0)).xyz;;
    windDirection = -windDirection;
    timeOffset = (windDirection + float3(0, -0.1, 0)) * _Time.y;
    float3 heightOffset = smoothstep(-0.2, 1.2, height) * windDirection * TOPWINDOFFSET;
    
    float3 windOffset = timeOffset + heightOffset;
    return windOffset;
}

float4 SampleWeatherMap(int index, float3 rayPosOS, float3 size, float height, float2 windDirection, float lod)
{
    float3 invSize = rcp(size);
    float2 uv = rayPosOS.xz * invSize.xz;
    uv = uv * 0.5 + 0.5;
    
    uv = SwirlUV(uv, float2(0.0, 0.0), _SwirlStrength);
    float2 weatherUVPolar = PolarCoordinate(uv, float2(0.0, 0.0));
    uv = lerp(uv, weatherUVPolar, _PolarStrength) * _WeatherMapTilingOffset[index].xy + _WeatherMapTilingOffset[index].zw + windDirection;
    
    float wrapMode = _WeatherMapWrapMode[index];
    float4 weatherData;
    switch (wrapMode)
    {
        case 0:
            weatherData = SAMPLE_TEXTURE2D_ARRAY_LOD(_WeatherMapArray, sampler_LinearRepeat, uv, index, lod);
        break;
        
        case 1:
        case 2:
        case 3:
        default:
            weatherData = SAMPLE_TEXTURE2D_ARRAY_LOD(_WeatherMapArray, sampler_LinearClamp, uv, index, lod);
        break;
    }
    
    
    float stormEyeRadius = min(size.x, size.z) * _HoleRatio;
    stormEyeRadius *= _MakeAHole;
    float stormEye = saturate(length(rayPosOS.xz) / stormEyeRadius);
        
    float coverage = weatherData.x;
    float anvilExponent = remap(height, 0.7, 0.8, lerp(1.0, 0.5, _AnvilBias[index]), 1.0);
    anvilExponent = lerp(anvilExponent, 1.0, smoothstep(0.7, 0.5, height));
    coverage = pow(coverage, anvilExponent);
    
    coverage = saturate(coverage * _Coverage[index]);
        
    coverage *= stormEye;
        
    float cloudsType = weatherData.y;
    cloudsType = lerp(0, cloudsType, _FogPhase);
    cloudsType = saturate(cloudsType);
        
    float2 curlNoise = weatherData.zw;
    
    return half4(coverage, cloudsType, curlNoise.x, curlNoise.y);
}

float SampleShapeNoise(float3 uvw, float coverage, float heightGradient, float lod)
{
    float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_ShapeNoise, sampler_LinearRepeat, uvw, lod);
    float lowFreqFBM = shapeNoise.g * 0.625 + shapeNoise.b * 0.25 + shapeNoise.a * 0.125;
    float baseCloud = remap(shapeNoise.r, -(1.0 - lowFreqFBM), 1.0, 0.0, 1.0);
    baseCloud *= heightGradient;
    baseCloud = saturate(pow(baseCloud, 2.0));
    float shapeWithCoverage = remap(baseCloud, 1.0 - coverage, 1.0, 0.0, 1.0);
    shapeWithCoverage *= coverage;
    
    float shapeDensity = saturate(shapeWithCoverage);
    return shapeDensity;
}

float DistanceAttenuation(float distanceSqr, float invRangeSqr)
{
    float rangeSqr = rcp(invRangeSqr);
    float distance = sqrt(distanceSqr);
    float range = sqrt(rangeSqr);
    float normalizedDist = saturate(distance / range);
    
    // float nearIntensity = 1.0 / max(distance * 0.5, 0.5);
    
    float t = 1.0 - normalizedDist;
    float smoothFalloff = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    float distFalloff = 1.0 / (1.0 + distance * 0.1);
    return distFalloff * smoothFalloff;
}

float SpotAttenuation(float3 spotDir, float3 lightDir, float outerCos, float innerCos)
{
    float cosAngle = dot(spotDir, lightDir);
    float angleAtten = saturate((cosAngle - outerCos) / max(innerCos - outerCos, 0.001));
    return angleAtten * angleAtten;
}

float SampleDensity(float3 rayPosOS, int index, bool sampleDetail, float lod, float height, float2 uv, out float cloudsType)
{
    float3 boundsMin = _BoundsMin[index];
    float3 boundsMax = _BoundsMax[index];
    float3 size = boundsMax - boundsMin;
    float3 localMin = -size * 0.5;
    float3 localMax = size * 0.5;
    
    float3 distToMin = rayPosOS - localMin;
    float3 distToMax = localMax - rayPosOS;
    float3 minDistToEdge = min(distToMin, distToMax);
    float edgeFadeDist = lerp(0.0, length(size) * 0.5, _EdgeFadeDistance[index]);
    float minDist = min(minDistToEdge.y, min(minDistToEdge.x, minDistToEdge.z));
    float edgeWeight = smoothstep(0.0, 2.0, minDist / edgeFadeDist);
    edgeWeight = saturate(edgeWeight);
    
    if (edgeWeight <= 0) return 0;
    
    float3 invSize = rcp(size);
    
    float3 timeOffset;
    float3 windOffset = CalculateWindOffset(index, height, timeOffset);
    
    float3 shapeOffsetPos = (rayPosOS + windOffset * _ShapeWindMultiplier[index]) * 0.001;
    float3 shapeUVW = shapeOffsetPos * _ShapeTiling[index] + _ShapeOffset[index];

    float coverage = 1.0;
    cloudsType = 1.0;
    float2 curlNoise = 0;
    if (_UseWeatherMap[index] > 0.5)
    {
        float2 weatherMapOffset = timeOffset.xz * _WeatherMapFlowMultiplier[index] * invSize.xz;
        float4 weatherData = SampleWeatherMap(index, rayPosOS, size, height, weatherMapOffset, lod);
        
        coverage = weatherData.x;
        cloudsType = weatherData.y;
        curlNoise = weatherData.zw;
    }
    else
    {
        coverage = _Coverage[index];
    }
    
    coverage = lerp(0, coverage, edgeWeight);
    
    float localHeight = (rayPosOS.y - localMin.y) / size.y;
    float2 curveUV = float2(localHeight, (index + 0.5) / VOLUME_LIMITS);
    float curveFactor = SAMPLE_TEXTURE2D_LOD(_CoverageOverHeightCurve, sampler_LinearClamp, curveUV, 0).r;
    coverage *= curveFactor;
    
    if (coverage < 0.01) return 0;
    
    // ========================
    // Height Gradient
    // Cirrus/Alto use 2D approach
    // ========================
    float stratusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * 
        remap(height, 0.2, 0.3, 1.0, 0.0);
    float cumulusGradient = remap(height, 0.0, 0.2, 0.0, 1.0) * 
        remap(height, 0.4, 0.6, 1.0, 0.0);
    float cumulonimbusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * 
        remap(height, 0.6, 0.8, 1.0, 0.0);
    
    float t1 = saturate(cloudsType * 2.0);
    float t2 = saturate((cloudsType - 0.5) * 2.0);
    float lowBlend = lerp(stratusGradient, cumulusGradient, t1);
    float highBlend = lerp(cumulusGradient, cumulonimbusGradient, t2);
    float heightGradient = lerp(lowBlend, highBlend, step(0.5, cloudsType));
    
    heightGradient = saturate(heightGradient);
    
    float shapeDensity = SampleShapeNoise(shapeUVW, coverage, heightGradient, lod);
    
    if (!sampleDetail || shapeDensity <= 0)
    {
        return shapeDensity * _Density[index];
    }
    
    float3 cn;
    cn = SAMPLE_TEXTURE3D_LOD(_ShapeNoise, sampler_LinearRepeat, shapeUVW + float3(0.5,0.5,0.5), 0);
    float3 curlOffset = float3(cn.x, cn.y, cn.z) * 2.0 - 1.0;
    
    float3 detailSamplePos = rayPosOS + curlOffset * (1.0 - height) * _DetailTurbulence[index];
    float3 detailOffsetPos = (detailSamplePos + windOffset * _DetailWindMultiplier[index]) * 0.001;
    float3 detailUVW = detailOffsetPos * _DetailTiling[index] + _DetailOffset[index];
    
    float3 detailNoise = SAMPLE_TEXTURE3D_LOD(_DetailNoise, sampler_LinearRepeat, detailUVW, lod).yzw;
    
    float lowFreqFactor = 1.0 - shapeDensity * shapeDensity;
    float highFreqFBM = detailNoise.r * 0.625 + detailNoise.g * 0.25 + detailNoise.b * 0.125;
    float highFreqModifier = lerp(highFreqFBM, 1.0 - highFreqFBM, saturate(height * 10.0));
    
    float erosionAmount = highFreqModifier * _DetailNoiseWeights[index] * lowFreqFactor;
    float cloudDensity = remap(shapeDensity, erosionAmount, 1.0, 0.0, 1.0);
    cloudDensity = saturate(cloudDensity);
    
    return cloudDensity * _Density[index];
}

float SampleDensity(float3 rayPosOS, int index, float dist, float transmittance, bool sampleDetail, float height, float2 uv, out float cloudsType)
{
    float lodLevel = CalculateLODLevel(index, dist);
    float textureLOD = GetTextureLOD(index, dist, lodLevel);
    bool useDetail = sampleDetail && ShouldSampleDetail(index, dist, transmittance);
    
    return SampleDensity(rayPosOS, index, useDetail, textureLOD, height, uv, cloudsType);
}

float SampleDensity(float3 rayPosOS, int index, float dist, bool sampleDetail, float height, float2 uv, out float cloudsType)
{
    float lodLevel = CalculateLODLevel(index, dist);
    float textureLOD = GetTextureLOD(index, dist, lodLevel);
    bool useDetail = sampleDetail;
    
    return SampleDensity(rayPosOS, index, useDetail, textureLOD, height, uv, cloudsType);
}

float SampleDensity(float3 rayPosOS, int index, float lod, float height, float2 uv, out float cloudsType)
{
    return SampleDensity(rayPosOS, index, false, lod, height, uv, cloudsType);
}

// =========================
// Obsolete
// =========================
float SampleDensityLightRayMarch(float3 rayPosOS, float2 uv, int index, float textureLOD)
{
    float3 size = _BoundsMax[index] - _BoundsMin[index];
    float3 invSize = rcp(size);
    float3 center = (_BoundsMin[index] + _BoundsMax[index]) * 0.5;
    float height = (rayPosOS.y - _BoundsMin[index].y) / size.y;
    
    float3 timeOffset;
    float3 windOffset = CalculateWindOffset(index, height, timeOffset);
    
    float3 offsetPos = rayPosOS + windOffset;
        
    float3 uvw = offsetPos * 0.001;
    float3 shapeUVW = uvw * _ShapeTiling[index] + _ShapeOffset[index];
    
    float edgeFadeDist = _EdgeFadeDistance[index];
    float dist2X = min(edgeFadeDist, min(rayPosOS.x - _BoundsMin[index].x, _BoundsMax[index].x - rayPosOS.x));
    float dist2Z = min(edgeFadeDist, min(rayPosOS.z - _BoundsMin[index].z, _BoundsMax[index].z - rayPosOS.z));
    float edgeWeight = min(dist2X, dist2Z) / edgeFadeDist;
    
    float coverage = 1.0;
    float cloudsType = 1.0;
    if (_UseWeatherMap[index] > 0.5)
    {
        float2 weatherMapOffset = timeOffset.xz * _WeatherMapFlowMultiplier[index] * invSize.xz;
        float4 weatherData = SampleWeatherMap(index, rayPosOS, size, height, weatherMapOffset, textureLOD);
        
        coverage = weatherData.x;
        cloudsType = weatherData.y;
    }
    else
    {
        coverage = _Coverage[index];    
    }
    
    if (edgeWeight <= 0 || coverage < 0.01) return 0;
    
    float stratusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * 
        remap(height, 0.2, 0.3, 1.0, 0.0);
    
    float cumulusGradient = remap(height, 0.0, 0.2, 0.0, 1.0) * 
        remap(height, 0.4, 0.6, 1.0, 0.0);
    
    float cumulonimbusGradient = remap(height, 0.0, 0.1, 0.0, 1.0) * 
        remap(height, 0.6, 0.8, 1.0, 0.0);
    
    float t1 = saturate(cloudsType * 2.0);
    float t2 = saturate((cloudsType - 0.5) * 2.0);
    float lowBlend = lerp(stratusGradient, cumulusGradient, t1);
    float highBlend = lerp(cumulusGradient, cumulonimbusGradient, t2);
    float heightGradient = lerp(lowBlend, highBlend, step(0.5, cloudsType));
    
    heightGradient = saturate(heightGradient);
    
    heightGradient *= edgeWeight;
    
    float shapeDensity = SampleShapeNoise(shapeUVW, coverage, heightGradient, textureLOD);
    
    if (uv.x > _Comparison)
    {
        return shapeDensity * (_Density[index] + 2.5);
    }
    else
    {
        return shapeDensity * _Density[index];
    }
}

float LightRayMarching(float3 rayPosOS, float3 lightDirOS, float3 boundsSize, float index, float cosTheta, float cloudsType, float2 uv, float t, float height, float dist, float density, out float sunBoost)
{
    lightDirOS = -lightDirOS;
    float lodLevel = CalculateLODLevel(index, dist);
    int maxSteps = CalculateLightMarchingSteps(index, lodLevel, t, density);
    
    float totalDensity = 0;
    float stepSize = length(boundsSize.y) * 0.01;
    
    float3 currentPos = rayPosOS;
    
    float3 perpendicular = abs(lightDirOS.y) < 0.99? float3(0,1,0) : float3(1,0,0);
    float3 coneRight = normalize(cross(lightDirOS, perpendicular));
    float3 coneUp = cross(coneRight, lightDirOS);
    
    float2 blueNoiseUV = uv + float2(_TemporalFrameIndex * 0.4356, _TemporalFrameIndex * 0.9786);
    float blueNoise = SAMPLE_TEXTURE2D(_BlueNoise, sampler_LinearRepeat, blueNoiseUV * _ScaledScreenParams.xy).x;
    
    float coneAngle = blueNoise * TWO_PI;
    float2 coneRotation = float2(cos(coneAngle), sin(coneAngle));
    
    float lodStepMultiplier = CalculateLODMultiplier(lodLevel);
    
    // float jitter = frac(blueNoise * 7.3 + height * 3.7);
    
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float stepMultiplier = pow(2.0, float(i)) * lodStepMultiplier;

        if (i == maxSteps - 1) stepMultiplier *= 6.0;
        float currentStep = stepSize * stepMultiplier;
        
        // float stepJitter = frac(jitter + i * 0.618034);
        // float jitteredStep = currentStep * (0.2 + stepJitter * 0.5);
        // jitteredStep = currentStep;
        
        currentPos += lightDirOS * currentStep;
        
        float coneRadius = stepSize * lerp(0.2, 2.0, float(i) / (maxSteps - 1));
        float3 coneOffset = (coneRight * coneRotation.x + coneUp * coneRotation.y) * coneRadius;

        float3 samplePos = currentPos + coneOffset;
        float sampleDist = length(samplePos - _WorldSpaceCameraPos);
        float sampleLOD = GetTextureLOD(index, sampleDist, lodLevel + float(i) * 0.5);
        
        float unusedCloudsType;
        float sampleDensity = SampleDensity(samplePos, (int)index, sampleLOD, height, uv, unusedCloudsType);
        
        if (sampleDensity > 0)
        {
            totalDensity += sampleDensity * currentStep;
        }
    }
    
    float rainAbsorptionMultiplier = lerp(1.0, 3.0, smoothstep(0.5, 1.0, cloudsType));
    // rainAbsorptionMultiplier = 1;
    float depth = totalDensity * _LightAbsorptionTowardsSun[index] * rainAbsorptionMultiplier;
    
    float beer = max(exp(-depth), exp(-depth * 0.25) * 0.7);
    
    float powder = 1.0 - exp(-depth * 2.0);
    float powderScale = saturate(cosTheta * 0.5 + 0.5);
    powder = lerp(powder, 1.0, powderScale);
    
    sunBoost = exp(-rcp(depth * depth * 0.01)) * 0.5;
    
    float depthProbability = lerp(0.05 + pow(density, remap(height, 0.3, 0.85, 0.5, 2.0)), 4, saturate(totalDensity / stepSize));
    float verticalProbability = pow(smoothstep(0.05, 0.2, height), 0.8);
    float inScatterProbability = depthProbability * verticalProbability; 
    
    float transmittance = beer * max(0.25, powder) * inScatterProbability;

    return _DarknessThreshold[index] + transmittance * (1 - _DarknessThreshold[index]);
}

float ApproximateLightTransmittance(float index, float cosTheta, float3 boundsSize, float height, float density, float cloudsType)
{
    float stepSize = boundsSize.y * 0.05;
    
    float rainAbsorptionMultiplier = lerp(1.0, 3.0, smoothstep(0.5, 1.0, cloudsType));
    float depth = density * _LightAbsorptionTowardsSun[index] * rainAbsorptionMultiplier;
    
    float beer = max(exp(-depth), exp(-depth * 0.25) * 0.7);
    
    float powder = 1.0 - exp(-depth * 2.0);
    float powderScale = saturate(cosTheta * 0.5 + 0.5);
    powder = lerp(powder, 1.0, powderScale);
    
    float depthProbability = lerp(0.05 + pow(density, remap(height, 0.0, 0.85, 0.5, 2.0)), 1.0, saturate(density / stepSize));
    float verticalProbability = pow(remap(height, 0.0, 0.14, 0.1, 1.0), 0.8);
    float inScatterProbability = depthProbability * verticalProbability; 
    
    float transmittance = beer * max(0.25, powder) * inScatterProbability;

    return _DarknessThreshold[index] + transmittance * (1 - _DarknessThreshold[index]);
}

float3 ApproximateAdditionalLights(float index, float3 rayPosWorld, float3 rayDir, float density, float height,float transmittance)
{
    float3 additionalLightContrib = 0;
    
    [loop]
    for (uint lightIndex = 0; lightIndex < _AddLightsCount; lightIndex++)
    {
        AdditionalLightData light = _AdditionalLightsBuffer[lightIndex];
        
        float3 lightPos = light.positionWS.xyz;
        float range = light.positionWS.w;
        float3 lightColor = light.color.rgb;
        float lightType = light.color.a;
        
        float3 toLight = lightPos - rayPosWorld;
        float distanceSqr = dot(toLight, toLight);
        float3 lightDir = toLight * rsqrt(distanceSqr);
        
        float distAtten = DistanceAttenuation(distanceSqr, light.attenuation.x);
        
        float spotAtten = 1.0;
        if (lightType > 0.5)
        {
            float3 spotDir = light.directionWS.xyz;
            spotAtten = SpotAttenuation(spotDir, -lightDir, light.attenuation.y, light.attenuation.z);
        }
        
        float attenuation = distAtten * spotAtten;
        
        if (attenuation < 0.01) continue;
        
        float cosTheta = dot(rayDir, lightDir);
        float phaseVal = HG(cosTheta, 0.8);
        
        float beer = saturate(exp(-density * 5.0));
        float3 contrib = lightColor * attenuation * phaseVal * beer;
        additionalLightContrib += contrib;
    }
    
    return additionalLightContrib;
}

float3 ComputeBoxNormal(float3 pos, float3 halfExtents, float sharpness)
{
    float3 normalized = pos / halfExtents;
    float3 absNorm = abs(normalized);
    
    float3 weights = pow(absNorm, sharpness);
    float totalWeight = weights.x + weights.y + weights.z + 1e-5;
    weights /= totalWeight;
    
    float3 normal = float3(
        sign(normalized.x) * weights.x,
        sign(normalized.y) * weights.y,
        sign(normalized.z) * weights.z
        );
    
    return normalize(normal);
}

float4 CloudsRayMarching(float index, float3 rayStart, float3 rayDir, float viewLength, float2 uv, out float depth)
{
    float4x4 worldToLocal = _VolumeWorldToLocal[index];
    float4x4 localToWorld = _VolumeLocalToWorld[index];
    
    float3 rayStartLocal = mul(worldToLocal, float4(rayStart, 1.0)).xyz;
    float3 rayDirLocal = mul(worldToLocal, float4(rayDir, 0.0)).xyz;
    float rayDirLocalLength = length(rayDirLocal);
    rayDirLocal = normalize(rayDirLocal);
    
    float3 boundsMin = _BoundsMin[index];
    float3 boundsMax = _BoundsMax[index];
    float3 boundsSize = boundsMax - boundsMin;
    
    float3 localMin = -boundsSize * 0.5;
    float3 localMax = boundsSize * 0.5;
    
    float3 realLocalMin = min(localMin, localMax);
    float3 realLocalMax = max(localMin, localMax);
    
    // rayStart + t0 * rayDir = 0.0
    // rayStart + t1 * rayDir = 1.0
    // float3 invRayDir = rcp(rayDir);
    // float3 t0 = (_BoundsMin[index] - rayStart) * invRayDir;
    // float3 t1 = (_BoundsMax[index] - rayStart) * invRayDir;
    // float3 tmin = min(t0, t1);
    // float3 tmax = max(t0, t1);
    //
    // float tNear = max(max(tmin.x, tmin.y), tmin.z);
    // tNear = max(0, tNear);
    // float tFar = min(min(tmax.x, tmax.y), tmax.z);
    
    float3 invRayDirLocal = rcp(rayDirLocal);
    float3 t0 = (realLocalMin - rayStartLocal) * invRayDirLocal;
    float3 t1 = (realLocalMax - rayStartLocal) * invRayDirLocal;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    
    float tNear = max(max(tmin.x, tmin.y), tmin.z);
    tNear = max(0, tNear);
    float tFar = min(min(tmax.x, tmax.y), tmax.z);
    
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams) * viewLength;
    float tNearWorld = tNear * rayDirLocalLength;
    float tFarWorld = tFar * rayDirLocalLength;
    
    if (eyeDepth < tNearWorld) return 0;
    
    if (tNear >= tFar || tFar < 0) return 0;
    
    float rayLengthWorld = min(eyeDepth - tNearWorld, tFarWorld - tNearWorld);
    float rayLengthLocal = rayLengthWorld / rayDirLocalLength;
    
    // 256x256 BlueNoise Texture + Interleaved Gradient Noise
    float2 blueNoiseUV = uv + float2(_TemporalFrameIndex * 0.75487, _TemporalFrameIndex * 0.56783);
    float4 blueNoiseTex = SAMPLE_TEXTURE2D(_BlueNoise, sampler_LinearRepeat, blueNoiseUV * _ScaledScreenParams.xy); 
    float blueNoise = blueNoiseTex.x; 
    float interleavedGradient = InterleavedGradientNoise(uv * _ScaledScreenParams.xy, _TemporalFrameIndex);
    float combinedNoise = frac(blueNoise + interleavedGradient * 0.5);
    
    // float rayLength = min(eyeDepth - tNear, tFar - tNear);
    
    float maxSteps = _Steps[index];
    float minSteps = maxSteps * 0.5;
    
    float viewAngleCos = abs(dot(rayDirLocal, float3(0,1,0)));
    float angleBasedSteps = lerp(maxSteps, minSteps, viewAngleCos);
    float maxBoundsDist = length(boundsSize);
    float rayLengthFactor = saturate(rayLengthLocal / maxBoundsDist);
    float adaptiveSteps = lerp(minSteps, angleBasedSteps, rayLengthFactor);
    adaptiveSteps = clamp(adaptiveSteps, minSteps, maxSteps);
    
    float avgLOD = CalculateLODLevel(index, tNear + rayLengthLocal * 0.5);

    float lodFactor = saturate(1.0 - (avgLOD - 0.5) * 0.4);
    lodFactor = lerp(0.25, 1.0, lodFactor);
    adaptiveSteps = adaptiveSteps * lodFactor;
    adaptiveSteps = max(minSteps * 0.5,  adaptiveSteps);
    
    float stepSize = rayLengthLocal / adaptiveSteps;
    
    float noiseOffset = tNear > 0? combinedNoise * stepSize * 2.0 : combinedNoise * stepSize * 0.5;
    
    float cheapStepSize = stepSize * 2.0;
    
    // float3 rayPos = rayStart + rayDir * (tNear + noiseOffset);
    float3 rayPosLocal = rayStartLocal + rayDirLocal * (tNear + noiseOffset);
    float3 rayOrigin = rayPosLocal;
    
    Light mainLight = GetMainLight();
    float3 lightDirLocal = mul(worldToLocal, float4(mainLight.direction, 0.0)).xyz;
    lightDirLocal = normalize(lightDirLocal);
    
    float cosTheta = dot(rayDirLocal, lightDirLocal);
    float phaseVal = phase(cosTheta, _PhaseParams[index], _SilverParams[index]);
    
    float transmittance = 1;
    float lightEnergy = 0;
    float sunEnergy = 0;
    float4 fog = float4(0,0,0,0);
    float3 ambientLight = 0;
    
    bool isHit = false;
    float3 firstHit;
    
    bool cheapMode = true;
    int zeroDensityCount = 0;
    float currentStep = cheapStepSize;
    float dstTraveled = noiseOffset;
    float3 additionalLightAcc = 0;
    
    [loop]
    for (int i = 0; i < int(adaptiveSteps) * 4; i++)
    {
        if (dstTraveled >= rayLengthLocal) break;
        if (dstTraveled + currentStep > rayLengthLocal) currentStep = rayLengthLocal - dstTraveled;
        
        float sampleDistance = tNear + dstTraveled;
        
        float lodLevel = CalculateLODLevel(index, sampleDistance);
        
        float lodStepMultiplier = CalculateLODMultiplier(lodLevel);
        float lodAdjustedStep = currentStep * lodStepMultiplier;
        
        float sampleJitter = frac(combinedNoise + i * 0.618034 + i * i * 0.0073);
        float3 jitteredPos = rayPosLocal + rayDirLocal * (sampleJitter - 0.5) * currentStep * 0.5;
        
        float3 rayPosWorld = mul(localToWorld, float4(rayPosLocal, 1.0)).xyz;
        float3 worldMin = _WorldBoundsMin[index];
        float3 worldBoundsSize = _WorldBoundsMax[index] - worldMin;
        float height = (rayPosWorld.y - worldMin.y) / worldBoundsSize.y;
        height = saturate(height);
        
        float cloudsType;
        float density = SampleDensity(jitteredPos, index, sampleDistance, !cheapMode, height, uv, cloudsType);
        
        if (density > 0)
        {
            if (cheapMode)
            {
                float stepBack = min(currentStep, dstTraveled);
                rayPosLocal -= rayDirLocal * stepBack;
                dstTraveled -= stepBack;
                
                cheapMode = false;
                currentStep = stepSize;
                zeroDensityCount = 0;
                
                continue;
            }
            else
            {
                zeroDensityCount = 0;
                if (transmittance >= 0.5 && !isHit)
                {
                    firstHit = mul(_VolumeLocalToWorld[index], float4(rayPosLocal, 1.0));
                    depth = -TransformWorldToView(firstHit).z;
                    isHit = true;
                }
                
                // Main Light
                float lightTransmittance = 1.0;
                float sunBoost;
                lightTransmittance = LightRayMarching(jitteredPos, lightDirLocal, boundsSize, index, cosTheta, cloudsType, uv, transmittance, height, sampleDistance, density, sunBoost);
                
                float4 shadowCoord = TransformWorldToShadowCoord(rayPosWorld);
                float shadowFade = GetMainLightShadowFade(rayPosWorld);
                float shadowAtt = MainLightRealtimeShadow(shadowCoord); 
                shadowAtt = lerp(shadowAtt, 1.0, shadowFade);
                shadowAtt = saturate(shadowAtt + 0.2);
                
                float extinction = density * currentStep * _LightAbsorptionThroughCloud[index];
                float stepTransmittance = exp(-extinction);
                float stepOpacity = 1.0 - stepTransmittance;
                
                lightEnergy += stepOpacity * transmittance * lightTransmittance * phaseVal * shadowAtt;
                
                // Additional Lights
                float3 additionalLightsEnergy = ApproximateAdditionalLights(index, rayPosWorld, rayDir, density, height, transmittance);
                additionalLightsEnergy *= stepOpacity * transmittance;
                
                // Ambient Lights
                float heightContrib = lerp(1.0, height, _HeightInfluence[index]);
                float ambientContribution = heightContrib * heightContrib;
                
                float3 centerToRayPos = normalize(rayPosLocal);
                centerToRayPos = mul(localToWorld, float4(centerToRayPos, 0.0));
                centerToRayPos = normalize(centerToRayPos);
                float3 skyAmbient = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, centerToRayPos, (1 - lodFactor) * 4 + 2), unity_SpecCube0_HDR);
                skyAmbient = saturate(skyAmbient);
                skyAmbient = max(0.3, length(skyAmbient)) * normalize(skyAmbient + 0.001);
                
                ambientLight += stepOpacity * transmittance * skyAmbient * ambientContribution;
                
                // Sun Boost
                float3 N = ComputeBoxNormal(rayPosLocal, boundsSize * 0.5, 1.0);
                float3 L = lightDirLocal;
                float3 V = -rayDirLocal;
                float3 H = normalize(L + V);
                float NoL = pow(saturate(dot(N, lightDirLocal)), 2.0);
                float NoH = saturate(dot(N, H));
                float specular = pow(NoH, 2.0) * 0.5;
                // float diffuse = saturate((NoL + 0.5) / 1.5);
                
                sunEnergy += stepOpacity * transmittance * (sunBoost + specular) * shadowAtt;
                
                // Fog
                float4 fogParams = 0;
                #ifdef ATMOSPHERIC_HEIGHT_FOG
                float4 fogParams = GetAtmosphericHeightFog(rayPosWorld);
                #endif
                fog.rgb += stepOpacity * transmittance * fogParams.xyz;
                fog.a += stepOpacity * transmittance * fogParams.w;
                
                transmittance *= stepTransmittance;
                if (transmittance < 0.01)
                    break;
                
                rayPosLocal += rayDirLocal * currentStep;
                dstTraveled += currentStep;
                
                additionalLightAcc += additionalLightsEnergy;
            }
        }
        else
        {
            if (!cheapMode)
            {
                zeroDensityCount++;
                if (zeroDensityCount > 11)
                {
                    cheapMode = true;
                    currentStep = cheapStepSize * lodStepMultiplier;
                    zeroDensityCount = 0;
                    
                    rayPosLocal -= rayDirLocal * currentStep;
                }
            }
            
            rayPosLocal += rayDirLocal * lodAdjustedStep * 2.0;
            dstTraveled += lodAdjustedStep * 2.0;
        }
        
        if (any(rayPosLocal < localMin) || any(rayPosLocal > localMax)) break;
    }
    
    float3 directLight = lightEnergy * _MainLightColor.rgb * _ScatterTint[index];
    float3 additionalLights = additionalLightAcc * _ScatterTint[index];
    float3 sunLight = sunEnergy * _ScatterTint[index] * _MainLightColor.rgb;
    
    ambientLight *= _ScatterTint[index];
    
    float3 cloudsColor = directLight + additionalLights + ambientLight + sunLight;
    float cloudsAlpha = 1.0 - transmittance;
    
    cloudsColor = lerp(cloudsColor, fog.rgb, fog.a);
    
    return half4(cloudsColor, cloudsAlpha);
}
