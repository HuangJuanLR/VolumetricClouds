using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PerlinNoiseSettings", menuName = "NoiseGenerator/Perlin Noise")]
public class PerlinNoiseSettings : NoiseSettings
{
    [Header("Perlin Noise Parameters")] 
    [Min(0)]public int scale = 5;
    [Min(0)]public float disorder = 0.5f;

    public ComputeShader perlinNoiseCS;

    public override ComputeShader GetComputeShader()=> perlinNoiseCS;

    public override void SetComputeShaderParameters(ComputeShader compute, int kernelIndex)
    {
        compute.SetFloat("_Scale", scale);
        compute.SetFloat("_Disorder", disorder);
        compute.SetVector("_Resolution", new Vector3((int)resolution, (int)resolution, (int)resolution));
    }

    public override string GetNoiseTypeName() => "PerlinNoise";
}