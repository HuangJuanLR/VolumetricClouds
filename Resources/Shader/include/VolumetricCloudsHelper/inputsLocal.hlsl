#pragma once
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// =============================
// Global
// =============================
sampler3D _ShapeNoise;
sampler3D _DetailNoise;
sampler2D _BlueNoise;
Texture2D<float4> _WeatherMap;
int _FrameIndex;

// =============================
// Shape
// =============================
float _Steps;
float4 _BoundsMin;
float4 _BoundsMax;

float _Density;
float _Coverage;
float _EdgeFadeDistance;

float4 _ShapeWeights;
float _ShapeTiling;
float3 _ShapeOffset;

float3 _DetailWeights;
float _DetailTiling;
float3 _DetailOffset;
float _DetailNoiseWeights;

// =============================
// Wind
// =============================
float3 _WindDirection;

// =============================
// Weather Map
// =============================
float4 _WeatherMapTilingOffset;
float _UseWeatherMap;
float _AnvilBias;

// =============================
// Lighting
// =============================
float _LightSteps;
float _LightAbsorptionThroughCloud;
float _LightAbsorptionTowardsSun;
float4 _ScatterTint;
float _DarknessThreshold;
float4 _PhaseParams;

// Test
float _FogPhase;