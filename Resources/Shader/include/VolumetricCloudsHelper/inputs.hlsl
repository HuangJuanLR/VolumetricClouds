#pragma once

#define VOLUME_LIMITS 8

struct AdditionalLightData
{
    float4 positionWS;
    float4 directionWS; // spot only
    float4 color;
    float4 attenuation; // x = 1/range^2, y = spot outer angle cos, z = spot inner angle cos, w = unused
};

// =============================
// Global
// =============================
Texture3D<float4> _ShapeNoise;
Texture3D<float4> _DetailNoise;
Texture2D<float4> _BlueNoise;
Texture2DArray<float4> _WeatherMapArray;
int _FrameIndex;
int _TemporalFrameIndex;

float _VolumeCount;

// =============================
// Shape
// =============================
float _Steps[VOLUME_LIMITS];
float _NoiseOffsets[VOLUME_LIMITS];
float4 _BoundsMin[VOLUME_LIMITS];
float4 _BoundsMax[VOLUME_LIMITS];
float4 _WorldBoundsMin[VOLUME_LIMITS];
float4 _WorldBoundsMax[VOLUME_LIMITS];
float4x4 _VolumeWorldToLocal[VOLUME_LIMITS];
float4x4 _VolumeLocalToWorld[VOLUME_LIMITS];

float _Density[VOLUME_LIMITS];
float _Coverage[VOLUME_LIMITS];
float _EdgeFadeDistance[VOLUME_LIMITS];
float _LODNear[VOLUME_LIMITS];
float _LODFar[VOLUME_LIMITS];
Texture2D<float> _CoverageOverHeightCurve;

float _UseFixedShape[VOLUME_LIMITS];
float _ShapeTiling[VOLUME_LIMITS];
float3 _ShapeOffset[VOLUME_LIMITS];

float _UseFixedDetail[VOLUME_LIMITS];
float _DetailTiling[VOLUME_LIMITS];
float3 _DetailOffset[VOLUME_LIMITS];
float _DetailNoiseWeights[VOLUME_LIMITS];
float _DetailTurbulence[VOLUME_LIMITS];

// =============================
// Wind
// =============================
float3 _WindDirection[VOLUME_LIMITS];
float _WeatherMapFlowMultiplier[VOLUME_LIMITS];
float _ShapeWindMultiplier[VOLUME_LIMITS];
float _DetailWindMultiplier[VOLUME_LIMITS];

// =============================
// Weather Map
// =============================
float _WeatherMapWrapMode[VOLUME_LIMITS];
float4 _WeatherMapTilingOffset[VOLUME_LIMITS];
float _UseWeatherMap[VOLUME_LIMITS];
float _AnvilBias[VOLUME_LIMITS];
float _HeightInfluence[VOLUME_LIMITS];

// =============================
// Lighting
// =============================
float _LightSteps[VOLUME_LIMITS];
float _LightAbsorptionThroughCloud[VOLUME_LIMITS];
float _LightAbsorptionTowardsSun[VOLUME_LIMITS];
float4 _ScatterTint[VOLUME_LIMITS];
float _DarknessThreshold[VOLUME_LIMITS];
float4 _PhaseParams[VOLUME_LIMITS];
float4 _SilverParams[VOLUME_LIMITS];

// Additional Lights
StructuredBuffer<AdditionalLightData> _AdditionalLightsBuffer;
int _AddLightsCount[VOLUME_LIMITS];

// Test
float _Comparison;
float _FogPhase;
float _DistortStrength;

// Storm
float _MakeAHole;
float _HoleRatio;
float _SwirlStrength;
float _PolarStrength;

static const float TOPWINDOFFSET = 5;