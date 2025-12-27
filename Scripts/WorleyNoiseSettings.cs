using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WorleyNoiseSettings", menuName = "NoiseGenerator/Worley Noise")]
public class WorleyNoiseSettings : NoiseSettings
{
    [Header("Worley Noise")] 
    [Min(1)]public int cellCount = 4;
    [Range(1, 8)] public int octaves = 1;
    [Range(1.0f, 4.0f)] public float lacunarity = 2.0f;
    [Range(0.0f, 1.0f)] public float gain = 0.5f;
    [Range(0.0f, 1.0f)] public float zSlice = 0.5f;
    public WorleyDistanceType distanceType = WorleyDistanceType.Euclidean;
    public bool invert = false;
    public int seed = 0;
    
    public ComputeShader worleyNoiseCS;

    public override ComputeShader GetComputeShader()=> worleyNoiseCS;

    public override void SetComputeShaderParameters(ComputeShader compute, int kernelIndex)
    {
        compute.SetInt("_CellCount", cellCount);
        compute.SetInt("_Octaves", octaves);
        compute.SetFloat("_Lacunarity", lacunarity);
        compute.SetFloat("_Gain", gain);
        compute.SetFloat("_ZSlice", zSlice);
        compute.SetInt("_DistanceType", (int)distanceType);
        compute.SetBool("_Invert", invert);
        compute.SetInt("_Seed", seed);
        compute.SetVector("_Resolution", new Vector3((int)resolution, (int)resolution, (int)resolution));
    }
    
    public override string GetNoiseTypeName() => "WorleyNoise";

    private float CalculateDistance(Vector3 a, Vector3 b)
    {
        switch (distanceType)
        {
            default:
            case WorleyDistanceType.Euclidean:
                return Vector3.Distance(a, b);
            case WorleyDistanceType.Manhattan:
                return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
            case WorleyDistanceType.Chebyshev:
                return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z));
        }
    }
}
