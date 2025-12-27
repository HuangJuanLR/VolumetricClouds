using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

[System.Serializable]
public enum Dimension
{
    _2D,
    _3D
}

[System.Serializable]
public enum WorleyDistanceType
{
    Euclidean,
    Manhattan,
    Chebyshev
}

[System.Serializable]
public enum TextureOutputFormat
{
    RGBA32,
    R8,
    R16,
    RFloat,
    BC4,
    BC5,
    BC7
}

[System.Serializable]
public enum TextureResolution
{
    _16 = 16,
    _32 = 32,
    _64 = 64,
    _128 = 128,
    _256 = 256,
    _512 = 512,
    _1024 = 1024,
    _2048 = 2048,
    
}

public abstract class NoiseSettings: ScriptableObject
{
    public bool realtime = false;
    public Dimension dimension = Dimension._2D;
    public TextureResolution resolution = TextureResolution._128;
    public TextureOutputFormat outputFormat = TextureOutputFormat.RGBA32;
    
    public abstract string GetNoiseTypeName();
    public abstract ComputeShader GetComputeShader();
    public abstract void SetComputeShaderParameters(ComputeShader compute, int kernelIndex);
}