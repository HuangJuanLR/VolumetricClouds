using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WeatherMapSettings", menuName = "NoiseGenerator/Weather Map")]
public class WeatherMapSettings : NoiseSettings
{
    [Header("Weather Map")]
    [Range(1, 8)] public int octaves = 1;
    [Range(1.0f, 4.0f)] public float lacunarity = 2.0f;
    [Range(0.0f, 1.0f)] public float gain = 0.5f;
    [Range(0.1f, 10f)] public float coverageScale = 1.0f;
    [Range(-0.5f, 0.5f)] public float coverageOffset = 0.0f;
    [Range(0.1f, 10f)] public float typeScale = 2.0f;
    [Range(0.1f, 10f)] public float precipitationScale = 3.0f;
    public int seed = 0;
    
    public ComputeShader weatherMapCS;
    
    public override string GetNoiseTypeName()=>"WeatherMap";
    
    
    public override ComputeShader GetComputeShader() => weatherMapCS;
    
    public override void SetComputeShaderParameters(ComputeShader compute, int kernelIndex)
    {
        compute.SetInt("_Octaves", octaves);
        compute.SetFloat("_Lacunarity", lacunarity);
        compute.SetFloat("_Gain", gain);
        compute.SetFloat("_CoverageScale", coverageScale);
        compute.SetFloat("_CoverageOffset", coverageOffset);
        compute.SetFloat("_TypeScale", typeScale);
        compute.SetFloat("_PrecipitationScale", precipitationScale);
        compute.SetInt("_Seed", seed);
        compute.SetVector("_Resolution", new Vector3((int)resolution, (int)resolution, (int)resolution));
    }
}
