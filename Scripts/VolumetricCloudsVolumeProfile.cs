using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds
{
    [CreateAssetMenu(menuName = "VolumetricClouds/VolumetricClouds Profile")]
    public class VolumetricCloudsVolumeProfile : ScriptableObject
    {
        [Header("Basic")]
        [NameTag("步进次数")][Range(1, 1024)]public int steps = 64;
        [NameTag("步进偏移")][Range(0.1f, 10.0f)] public float noiseOffset = 2.0f;
        [NameTag("密度")][Range(0.0f, 10.0f)]public float density = 1.0f;
        [NameTag("覆盖范围")][Range(0.0f, 5.0f)] public float coverage = 1.0f;
        [NameTag("边缘衰减(世界)")][Min(0.0f)] public float edgeFadeDistance = 0.0f;
        [NameTag("LOD近距离")][Range(0.1f, 1000.0f)] public float lodNear = 500.0f;
        [NameTag("LOD远距离")][Range(100.0f, 2000.0f)] public float lodFar = 2000.0f;
        [NameTag("高度衰减")]public AnimationCurve coverageOverHeight = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Header("Wind")]
        [NameTag("风速(世界)")]public Vector3 windDirection = Vector3.zero;
        
        [Header("Weather Map")]
        [NameTag("使用天气图")]public bool useWeatherMap = true;
        [NameTag("天气图(R:范围 G:类型 BA:扰动)")]public Texture2D weatherMap;
        [Tooltip("0: Repeat, 1: Clamp, (后面两个也是Clamp)2: Mirror, 3: MirrorOnce")]
        [NameTag("UV模式")]public TextureWrapMode weatherMapWrapMode = TextureWrapMode.Repeat;
        [NameTag("平铺")]public Vector2 weatherMapTiling = Vector2.one;
        [NameTag("偏移")]public Vector2 weatherMapOffset = Vector2.zero;
        [NameTag("速度强度")][Range(0.0f, 5.0f)] public float weatherMapWindMultiplier = 0.0f;
        [NameTag("顶部厚度增强")][Range(0.0f, 1.0f)] public float anvilBias = 0.5f;
        
        [Header("Shape")] 
        [NameTag("固定形状")]public bool useFixedShape = false;
        [NameTag("形状平铺")]public float shapeTiling = 1.0f;
        [NameTag("形状偏移")]public Vector3 shapeOffset = Vector3.zero;
        [NameTag("速度")][Range(0.0f, 5.0f)]public float shapeWindMultiplier = 0.0f;
        
        [Header("Detail")] 
        [NameTag("固定细节")]public bool useFixedDetail = false;
        [NameTag("噪波平铺")]public float detailTiling = 10.0f;
        [NameTag("噪波偏移")]public Vector3 detailOffset = Vector3.zero;
        [NameTag("噪波强度")][Range(0.0f, 1.0f)]public float detailNoiseWeight = 1.0f;
        [NameTag("扰动强度")]public float turbulence = 50.0f;
        [NameTag("速度")][Range(0.0f, 5.0f)]public float detailWindMultiplier = 0.0f;

        [Header("Lighting")]
        [NameTag("额外光源数")][Min(0)]public int maxAdditionalLights = 4;
        [NameTag("光照步进")][Range(4,12)] public int lightSteps = 6;
        [NameTag("高度遮蔽")][Range(0.0f, 1.0f)] public float heightInfluence = 1.0f;
        [NameTag("密度吸光率")][Min(0.1f)]public float lightAbsorptionThroughCloud = 1.0f;
        [NameTag("向阳吸光率")][Min(0.1f)]public float lightAbsorptionTowardsSun = 0.5f;
        [NameTag("颜色")][ColorUsage(true, true)]public Color scatterTint = Color.white;
        [Range(0.0f, 1.0f)] public float darknessThreshold = 0.2f;
        
        [Header("Sun Scattering")]
        [NameTag("透射扩散")][Range(0.0f, 1.0f)] public float forwardScattering = 0.5f;
        [NameTag("基础明度")][Range(0.0f, 1.0f)] public float baseBrightness = 0.8f;
        [NameTag("透射强度")][Range(0.0f, 1.0f)] public float phaseFactor = 0.15f;
        
        [NameTag("边缘扩散强度")][Range(0.0f, 1.0f)] public float silverIntensity = 0.5f;
        [NameTag("边缘扩散范围")][Range(0.0f, 1.0f)] public float silverSpread = 0.1f;
    }
}
