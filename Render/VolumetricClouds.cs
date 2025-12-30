using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
// ReSharper disable Unity.PreferAddressByIdToGraphicsParams

namespace VolumetricClouds
{
    public enum VolumetricCloudsDownsampling
    {
        None,
        _2x,
        _4x,
        _8x,
    }
    
    [DisallowMultipleRendererFeature]
    public class VolumetricClouds : ScriptableRendererFeature
    {
        [System.Serializable]
        public class VolumetricCloudsSetting
        {
            public LayerMask transparentLayerMask = 0;
            public VolumetricCloudsDownsampling downSampling = VolumetricCloudsDownsampling._4x;
            public Texture3D shapeNoise;
            public Texture3D detailNoise;
            public Texture2D blueNoise;

            [Header("(Obsolete, will be deleted later)God's Rays")] 
            public bool enableLightRays = false;
            [Range(0.0f, 2.0f)] public float lightIntensity = 0.5f;
            [Range(0.1f, 5.0f)] public float sunFalloff = 2.0f;
            [Range(0.1f, 5.0f)] public float sunIntensity = 1.5f;
            [Range(0, 64)] public int radialBlurSamples = 16;
            [Range(0.1f, 2.0f)] public float radialBlurDensity = 0.8f;
            [Range(0.0f, 5.0f)] public float gaussianBlurRadius = 1.0f;
            [Range(10f, 1000f)] public float distanceFade = 100f;

            [Header("Test only")] 
            [Range(0.0f, 1.0f)] public float comparison = 0.0f;
            [Range(0.0f, 1.0f)] public float fogPhase = 1.0f;
            
            [Header("Storm")]
            public bool makeAHole = false;
            [Range(0.0f, 1.0f)] public float holeRatio = 0.0f; 
            [Min(0.0f)]public float swirlStrength = 0.0f;
            [Range(0.0f, 1.0f)] public float polarStrength = 0.0f;
        }

        class VolumetricCloudsPass : ScriptableRenderPass
        {
            private const int VOLUME_LIMITS = 8;
            private const int ADDITIONAL_LIGHTS_LIMITS = 8;
            private const int WEATHERMAP_RESOLUTION = 512;

            private const string ATMOSPHERIC_HEIGHT_FOG_KEYWORD = "ATMOSPHERIC_HEIGHT_FOG";
            private const string ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD = "ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTALL";
            private const string ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD = "ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTALL";

            private ProfilingSampler m_OuterScopeSampler;
            private ProfilingSampler m_RaymarchingSampler;
            private ProfilingSampler m_UpsampleSampler;
            private ProfilingSampler m_BlitSampler;
            private VolumetricCloudsSetting m_Settings;

            private Texture2D m_CoverageOverHeightTex;
            private int[] m_CachedCurveVersions = new int[VOLUME_LIMITS];
            private int m_CachedVolumeCount = 0;

            private Texture2DArray m_WeatherMapArray;
            private Texture2D[] m_CachedWeatherMaps;
            private Texture2D m_FallbackWeatherMap;

            // Volume Cache
            private bool m_NeedSorting = true;
            private bool m_Sorted = false;
            private Vector3 m_CachedCameraPosition = Vector3.negativeInfinity;
            private Vector3[] m_CachedVolumePositions = new Vector3[VOLUME_LIMITS];
            private Vector3[] m_CachedVolumeScales = new Vector3[VOLUME_LIMITS];
            private Quaternion[] m_CachedVolumeRotations = new Quaternion[VOLUME_LIMITS];
            
            private float[] m_CachedSteps = new float[VOLUME_LIMITS];
            private float[] m_CahcedNoiseOffsets = new float[VOLUME_LIMITS];
            
            private Vector4[] m_CachedBoundsMins = new Vector4[VOLUME_LIMITS];
            private Vector4[] m_CachedBoundsMaxes = new Vector4[VOLUME_LIMITS];
            private Vector4[] m_CachedWorldBoundsMins = new Vector4[VOLUME_LIMITS];
            private Vector4[] m_CachedWorldBoundsMaxes = new Vector4[VOLUME_LIMITS];
            private Matrix4x4[] m_CachedVolumeWorldToLocal = new Matrix4x4[VOLUME_LIMITS];
            private Matrix4x4[] m_CachedVolumeLocalToWorld = new Matrix4x4[VOLUME_LIMITS];
            
            private float[] m_CachedDensities = new float[VOLUME_LIMITS];
            private float[] m_CachedCoverages = new float[VOLUME_LIMITS];
            private float[] m_CachedEdgeFadeDistances = new float[VOLUME_LIMITS];
            private float[] m_CachedLODNear = new float[VOLUME_LIMITS];
            private float[] m_CachedLODFar = new float[VOLUME_LIMITS];
            
            private Vector4[] m_CachedWindDirections = new Vector4[VOLUME_LIMITS];
            private float[] m_CachedWeatherMapWindMultipliers = new float[VOLUME_LIMITS];
            private float[] m_CachedWeatherMapWrapMode = new float[VOLUME_LIMITS];
            private Vector4[] m_CachedWeatherMapTilingOffset = new Vector4[VOLUME_LIMITS];
            private float[] m_CachedUseWeatherMap = new float[VOLUME_LIMITS];
            private float[] m_CachedAnvilBiases = new float[VOLUME_LIMITS];
            
            private float[] m_CachedHeightInfluences = new float[VOLUME_LIMITS];
            
            private float[] m_CachedShapeTiling = new float[VOLUME_LIMITS];
            private Vector4[] m_CachedShapeOffsets = new Vector4[VOLUME_LIMITS];
            private float[] m_CachedShapeWindMultipliers = new float[VOLUME_LIMITS];
            
            private float[] m_CachedDetailTiling = new float[VOLUME_LIMITS];
            private Vector4[] m_CachedDetailOffsets = new Vector4[VOLUME_LIMITS];
            private float[] m_CachedDetailNoiseWeights = new float[VOLUME_LIMITS];
            private float[] m_CachedDetailTurbulence = new float[VOLUME_LIMITS];
            private float[] m_CachedDetailWindMultipliers = new float[VOLUME_LIMITS];
            
            private float[] m_CachedLightSteps = new float[VOLUME_LIMITS];
            private float[] m_CachedLightAbsorptionThroughCloud = new float[VOLUME_LIMITS];
            private float[] m_CachedLightAbsorptionTowardsSun = new float[VOLUME_LIMITS];
            private Color[] m_CachedScatterTint = new Color[VOLUME_LIMITS];
            private float[] m_CachedDarknessThreshold = new float[VOLUME_LIMITS];
            private Vector4[] m_CachedPhaseParams = new Vector4[VOLUME_LIMITS];
            private Vector4[] m_CachedSilverParams = new Vector4[VOLUME_LIMITS];
            
            // Working Quarter-Res RT
            private RTHandle m_CloudsRT;
            private RTHandle m_CloudsDepthRT;
            
            // History Ping-Pong RT
            private RTHandle[] m_CloudsHistoryRTs = new RTHandle[2];
            private RTHandle[] m_CloudsHistoryDepthRTs = new RTHandle[2];
            private int m_HistoryIndex = 0;
            private int m_TemporalFrameIndex = 0;
            private Matrix4x4 m_PrevMatrixVP;
            private bool m_IsFirstFrame = true;
            
            // Additional Lights
            private ComputeBuffer m_AdditionalLightsBuffer;
            private AdditionalLightData[] m_LightDataArray = new AdditionalLightData[ADDITIONAL_LIGHTS_LIMITS];
            
            private Material m_CloudsMat;

            private List<VolumetricCloudsVolume> m_CloudsVolumes = new List<VolumetricCloudsVolume>();
            
            // Third-party adapt
            private bool m_ThirdPartyInitialized = false;

            public VolumetricCloudsPass(string profilerTag, VolumetricCloudsSetting settings)
            {
                m_OuterScopeSampler = new ProfilingSampler(profilerTag);
                m_RaymarchingSampler = new ProfilingSampler("VolumetricClouds Raymarching");
                m_UpsampleSampler = new ProfilingSampler("Bilateral Upsample");
                m_BlitSampler = new ProfilingSampler("Blit");
                m_Settings = settings;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var volumesArray = FindObjectsOfType<VolumetricCloudsVolume>();
                if (volumesArray.Length <= 0) return;
                bool needRefresh = m_CloudsVolumes.Count != volumesArray.Length 
                                   || !m_CloudsMat.HasFloat("_VolumeCount")
                                   || m_CloudsMat.GetFloat("_VolumeCount") == 0;
                if (!needRefresh)
                {
                    foreach (var vol in m_CloudsVolumes)
                    {
                        if (vol == null)
                        {
                            needRefresh = true;
                            break;
                        }
                    }
                }
                
                if(needRefresh)
                {
                    m_CloudsVolumes.Clear();
                    m_CloudsVolumes.AddRange(FindObjectsOfType<VolumetricCloudsVolume>());
                    m_NeedSorting = true;
                    m_CachedCameraPosition = Vector3.negativeInfinity;
                }
                
                var volumeCount = Mathf.Min(m_CloudsVolumes.Count, VOLUME_LIMITS);
                if (volumeCount == 0)
                {
                    return;
                }
                
                // =============================
                // Configure Material
                // =============================
                CreateMaterialIfNeeded();
                
                InitializeThirdPartyPlugin();
                
                // Update volumes first to get data
                var camPos = renderingData.cameraData.camera.transform.position;
                SortVolume(volumeCount, camPos);

                bool forceUpdate = needRefresh || m_Sorted;
                
                UpdateVolumesData(volumeCount, forceUpdate);
                
                UpdateCoverageOverHeightTextures(volumeCount, forceUpdate);
                m_CloudsMat.SetTexture("_CoverageOverHeightCurve", m_CoverageOverHeightTex);
                
                UpdateWeatherMapArray(volumeCount, forceUpdate);
                m_CloudsMat.SetTexture("_WeatherMapArray", m_WeatherMapArray);
                
                // Test-Only parameters
                m_CloudsMat.SetFloat("_Comparison", m_Settings.comparison);
                m_CloudsMat.SetFloat("_FogPhase", m_Settings.fogPhase);
                
                // Storm parameters (Test-Only now)
                m_CloudsMat.SetFloat("_MakeAHole", m_Settings.makeAHole? 1.0f:0.0f);
                m_CloudsMat.SetFloat("_HoleRatio", m_Settings.holeRatio);
                m_CloudsMat.SetFloat("_SwirlStrength", m_Settings.swirlStrength);
                m_CloudsMat.SetFloat("_PolarStrength", m_Settings.polarStrength);
                
                // =============================
                // Temporal Reprojection
                // =============================
                var camera = renderingData.cameraData.camera;
                var matrixV = renderingData.cameraData.GetViewMatrix();
                var matrixP = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
                var matrixVP = matrixP * matrixV;
                
                m_CloudsMat.SetMatrix("_PrevMatrixVP", m_PrevMatrixVP);
                m_CloudsMat.SetMatrix("_InvMatrixVP", matrixVP.inverse);
                m_CloudsMat.SetInt("_TemporalFrameIndex", m_TemporalFrameIndex);
                m_CloudsMat.SetInt("_IsFirstFrame", m_IsFirstFrame? 1:0);

                m_PrevMatrixVP = matrixVP;
                
                // =============================
                // Configure RTHandle
                // =============================
                var camDesc = renderingData.cameraData.cameraTargetDescriptor;
                
                var desc = camDesc;
                // Always quarter-resolution
                switch (m_Settings.downSampling)
                {
                    case VolumetricCloudsDownsampling._2x:
                        desc.width >>= 1; 
                        desc.height >>= 1;
                        break;
                    case VolumetricCloudsDownsampling._4x:
                        desc.width >>= 2; 
                        desc.height >>= 2;
                        break;
                    case VolumetricCloudsDownsampling._8x:
                        desc.width >>= 3; 
                        desc.height >>= 3;
                        break;
                    case VolumetricCloudsDownsampling.None:
                        break;
                }
                desc.depthBufferBits = 0;
                desc.colorFormat = RenderTextureFormat.ARGBFloat;

                var historyDesc = desc;
                historyDesc.width = camDesc.width;
                historyDesc.height = camDesc.height;

                var depthDesc = desc;
                depthDesc.colorFormat = RenderTextureFormat.RFloat;

                var historyDepthDesc = depthDesc;
                historyDepthDesc.width = camDesc.width;
                historyDepthDesc.height = camDesc.height;

                // Malloc Working RT
                RenderingUtils.ReAllocateIfNeeded(ref m_CloudsRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "VolumetricCloudsRT");
                RenderingUtils.ReAllocateIfNeeded(ref m_CloudsDepthRT, depthDesc, FilterMode.Point, TextureWrapMode.Clamp,
                    name: "VolumetricCloudsDepthRT");
                
                if(m_CloudsHistoryRTs == null) m_CloudsHistoryRTs = new RTHandle[2];
                if(m_CloudsHistoryDepthRTs == null) m_CloudsHistoryDepthRTs = new RTHandle[2];
                
                // Malloc History RTs
                for (int i = 0; i < 2; i++)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref m_CloudsHistoryRTs[i], historyDesc, FilterMode.Bilinear,
                        TextureWrapMode.Clamp,
                        name: "VolumetricCloudsHistoryRT_" + i);
                    RenderingUtils.ReAllocateIfNeeded(ref m_CloudsHistoryDepthRTs[i], historyDepthDesc, FilterMode.Point,
                        TextureWrapMode.Clamp,
                        name: "VolumetricCloudsDepthHistoryRT_" + i);
                }
                
                m_CloudsMat.SetVector("_Resolution", new Vector4(
                        desc.width, desc.height, 1.0f / desc.width, 1.0f / desc.height
                    ));
                
                // Additional Lights
                int additionalLightsCount = CollectAdditionalLights(ref renderingData);

                if (m_AdditionalLightsBuffer == null || m_AdditionalLightsBuffer.count != additionalLightsCount)
                {
                    m_AdditionalLightsBuffer?.Release();
                    m_AdditionalLightsBuffer = new ComputeBuffer(ADDITIONAL_LIGHTS_LIMITS,
                        Marshal.SizeOf<AdditionalLightData>());
                }
                
                m_AdditionalLightsBuffer.SetData(m_LightDataArray);
                m_CloudsMat.SetBuffer("_AdditionalLightsBuffer", m_AdditionalLightsBuffer);
                m_CloudsMat.SetInt("_AddLightsCount", additionalLightsCount);

                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
            }

            private void CreateMaterialIfNeeded()
            {
                if (m_CloudsMat == null)
                {
                    var cloudsShader = Shader.Find("Hidden/Juan/VolumetricClouds");
                    if (cloudsShader == null)
                    {
                        Debug.LogError("Shader Hidden/Juan/VolumetricClouds not found");
                        return;
                    }
                    m_CloudsMat = new Material(cloudsShader);
                }
            }

            private void InitializeThirdPartyPlugin()
            {
                if (m_CloudsMat == null || m_ThirdPartyInitialized) return;
                
                // Atmospheric height fog
                if (System.IO.File.Exists(
                        "Assets/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"))
                    
                {
                    m_CloudsMat.EnableKeyword(ATMOSPHERIC_HEIGHT_FOG_KEYWORD);
                    m_CloudsMat.EnableKeyword(ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD);
                    m_CloudsMat.DisableKeyword(ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD);
                }
                else if (System.IO.File.Exists(
                             "Assets/09.ThirdParty/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"))
                {
                    m_CloudsMat.EnableKeyword(ATMOSPHERIC_HEIGHT_FOG_KEYWORD);
                    m_CloudsMat.EnableKeyword(ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD);
                    m_CloudsMat.DisableKeyword(ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD);
                }
                else
                {
                    m_CloudsMat.DisableKeyword(ATMOSPHERIC_HEIGHT_FOG_KEYWORD);
                    m_CloudsMat.DisableKeyword(ATMOSPHERIC_HEIGHT_FOG_DEFAULTINSTAlL_KEYWORD);
                    m_CloudsMat.DisableKeyword(ATMOSPHERIC_HEIGHT_FOG_CUSTOMINSTAlL_KEYWORD);
                }

                m_ThirdPartyInitialized = true;
                
            }

            private void UpdateCoverageOverHeightTextures(int volumeCount, bool force)
            {
                const int CURVE_RESOLUTION = 64;
                
                bool needsRebuild = m_CoverageOverHeightTex == null 
                                 || m_CachedVolumeCount != volumeCount 
                                 || force;
                
                if (!needsRebuild)
                {
                    for (int i = 0; i < volumeCount; i++)
                    {
                        int version = m_CloudsVolumes[i].CurveVersion;
                        if (m_CachedCurveVersions[i] != version)
                        {
                            needsRebuild = true;
                            break;
                        }
                    }
                }
                
                if (!needsRebuild) return;
                
                for (int v = 0; v < volumeCount; v++)
                {
                    m_CachedCurveVersions[v] = m_CloudsVolumes[v].CurveVersion;
                }
                
                if (m_CoverageOverHeightTex == null 
                    || m_CoverageOverHeightTex.width != CURVE_RESOLUTION 
                    || m_CoverageOverHeightTex.height != VOLUME_LIMITS)
                {
                    if (m_CoverageOverHeightTex != null)
                        CoreUtils.Destroy(m_CoverageOverHeightTex);
                    
                    m_CoverageOverHeightTex = new Texture2D(CURVE_RESOLUTION, VOLUME_LIMITS, TextureFormat.RFloat, false)
                    {
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };
                }
                
                // Pack all curves into single texture (each row = one volume)
                Color[] allPixels = new Color[CURVE_RESOLUTION * VOLUME_LIMITS];
                
                for (int v = 0; v < VOLUME_LIMITS; v++)
                {
                    var tex = (v < volumeCount) ? m_CloudsVolumes[v].CoverageOverHeightTex : null;
                    
                    for (int x = 0; x < CURVE_RESOLUTION; x++)
                    {
                        int pixelIndex = v * CURVE_RESOLUTION + x;
                        
                        if (tex != null)
                        {
                            float u = x / (float)(CURVE_RESOLUTION - 1);
                            allPixels[pixelIndex] = tex.GetPixelBilinear(u, 0.5f);
                        }
                        else
                        {
                            // Default to 1.0 (full coverage)
                            allPixels[pixelIndex] = new Color(1f, 0, 0, 1f);
                        }
                    }
                }
                
                m_CoverageOverHeightTex.SetPixels(allPixels);
                m_CoverageOverHeightTex.Apply();
                
                m_CachedVolumeCount = volumeCount;
            }

            private void UpdateWeatherMapArray(int volumeCount, bool force)
            {
                if (!m_FallbackWeatherMap)
                {
                    m_FallbackWeatherMap = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Color[] white = new Color[4];
                    for (int i = 0; i < 4; i++)
                    {
                        white[i] = Color.white;
                    }
                    m_FallbackWeatherMap.SetPixels(white);
                    m_FallbackWeatherMap.Apply();
                }

                if (m_WeatherMapArray == null)
                {
                    m_WeatherMapArray = new Texture2DArray(
                        WEATHERMAP_RESOLUTION,
                        WEATHERMAP_RESOLUTION,
                        VOLUME_LIMITS,
                        TextureFormat.RGBA32,
                        true
                    )
                    {
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear
                    };

                    if (m_CachedWeatherMaps == null)
                    {
                        m_CachedWeatherMaps = new Texture2D[VOLUME_LIMITS];
                    }
                }

                bool needRebuild = force;

                if (!needRebuild)
                {
                    for (int i = 0; i < VOLUME_LIMITS; ++i)
                    {
                        Texture2D currentWeatherMap = null;
                        if (i < volumeCount)
                        {
                            currentWeatherMap = m_CloudsVolumes[i].weatherMap;
                        }

                        bool isDirty = false;
                        if (currentWeatherMap == null && m_CachedWeatherMaps[i] != null)
                        {
                            isDirty = false;
                        }
                        else if (currentWeatherMap != m_CachedWeatherMaps[i])
                        {
                            isDirty = true;
                        }
                        
                        if (isDirty)
                        {
                            needRebuild = true;
                        }
                    }
                }

                if (!needRebuild) return;

                RenderTexture tempRT = RenderTexture.GetTemporary(
                    WEATHERMAP_RESOLUTION,
                    WEATHERMAP_RESOLUTION,
                    0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default
                    );

                for (int i = 0; i < VOLUME_LIMITS; i++)
                {
                    Texture2D srcTex = null;

                    if (i < volumeCount)
                    {
                        srcTex = m_CloudsVolumes[i].weatherMap;
                    }

                    if (srcTex == null)
                    {
                        srcTex = m_FallbackWeatherMap;
                    }
                    
                    Graphics.Blit(srcTex, tempRT);

                    RenderTexture.active = tempRT;
                    Texture2D readbackTex = new Texture2D(WEATHERMAP_RESOLUTION, WEATHERMAP_RESOLUTION,
                        TextureFormat.RGBA32, false);
                    readbackTex.ReadPixels(new Rect(0, 0, WEATHERMAP_RESOLUTION, WEATHERMAP_RESOLUTION), 0, 0);
                    readbackTex.Apply();
                    RenderTexture.active = null;
                    
                    Graphics.CopyTexture(readbackTex, 0, 0, m_WeatherMapArray, i, 0);
                    CoreUtils.Destroy(readbackTex);

                    if (i < volumeCount)
                    {
                        m_CachedWeatherMaps[i] = m_CloudsVolumes[i].weatherMap;
                    }
                    else
                    {
                        m_CachedWeatherMaps[i] = null;
                    }
                }
                
                RenderTexture.ReleaseTemporary(tempRT);
                m_WeatherMapArray.Apply();
            }

            private void SortVolume(int volumeCount, Vector3 cameraPos)
            {
                m_Sorted = false;
                
                const float SORT_THRESHOLD = 10.0f;
                
                if (Vector3.Distance(cameraPos, m_CachedCameraPosition) > SORT_THRESHOLD)
                {
                    m_NeedSorting = true;
                    m_CachedCameraPosition = cameraPos;
                }

                for (int i = 0; i < volumeCount; ++i)
                {
                    var volume = m_CloudsVolumes[i];

                    if (m_CachedVolumePositions[i] != volume.transform.position)
                    {
                        m_NeedSorting = true;
                        break;
                    }
                }

                if (!m_NeedSorting) return;

                var tempCloudsVolumes = new VolumetricCloudsVolume[volumeCount];
                for (int i = 0; i < volumeCount; i++)
                {
                    tempCloudsVolumes[i] = m_CloudsVolumes[i];
                }
                
                m_CloudsVolumes.Sort((a, b) =>
                {
                    float distA = Vector3.Distance(a.transform.position, cameraPos);
                    float distB = Vector3.Distance(b.transform.position, cameraPos);
                    return distA.CompareTo(distB);
                });
                
                for (int i = 0; i < volumeCount; ++i)
                {
                    if (m_CloudsVolumes[i] != tempCloudsVolumes[i])
                    {
                        m_Sorted = true;
                        break;
                    }
                }
                
                m_NeedSorting = false;
            }

            private void UpdateVolumesData(int volumeCount, bool force)
            {
                bool needUpdate = false;

                if (force) needUpdate = true;

                if (!needUpdate)
                {
                    for (int i = 0; i < volumeCount; i++)
                    {
                        var volume = m_CloudsVolumes[i];
                        if (volume.IsDirty)
                        {
                            needUpdate = true;
                            break;
                        }

                        if (m_CachedVolumePositions[i] != volume.transform.position
                            || m_CachedVolumeRotations[i] != volume.transform.rotation
                            || m_CachedVolumeScales[i] != volume.transform.localScale
                           )
                        {
                            needUpdate = true;
                            break;
                        }
                    }
                }

                if (!needUpdate) return;
                
                for (int i = 0; i < volumeCount; i++)
                {
                    var volume = m_CloudsVolumes[i];
                    var bounds = volume.CloudsBounds;
                    var center = volume.transform.position + bounds.center;

                    var worldBounds = volume.WorldBounds;

                    m_CachedSteps[i] = volume.steps;
                    m_CahcedNoiseOffsets[i] = volume.noiseOffset;
                    m_CachedBoundsMins[i] = center - bounds.extents;
                    m_CachedBoundsMaxes[i] = center + bounds.extents;
                    m_CachedWorldBoundsMins[i] = worldBounds.min;
                    m_CachedWorldBoundsMaxes[i] = worldBounds.max;

                    var localToWorld = Matrix4x4.TRS(
                        volume.transform.position + volume.transform.rotation * bounds.center,
                        volume.transform.rotation,
                        Vector3.one
                    );
                    var worldToLocal = localToWorld.inverse;

                    m_CachedVolumeWorldToLocal[i] = worldToLocal;
                    m_CachedVolumeLocalToWorld[i] = localToWorld;
                    
                    m_CachedDensities[i] = volume.density;
                    m_CachedCoverages[i] = volume.coverage;
                    m_CachedEdgeFadeDistances[i] = volume.edgeFadeDistance;
                    m_CachedLODNear[i] = Mathf.Min(volume.lodNear, volume.lodFar);
                    m_CachedLODFar[i] = Mathf.Max(volume.lodFar, volume.lodNear);
                    
                    m_CachedWeatherMapWrapMode[i] = (float)volume.weatherMapWrapMode;
                    m_CachedWeatherMapTilingOffset[i].x = volume.weatherMapTiling.x;
                    m_CachedWeatherMapTilingOffset[i].y = volume.weatherMapTiling.y;
                    m_CachedWeatherMapTilingOffset[i].z = volume.weatherMapOffset.x;
                    m_CachedWeatherMapTilingOffset[i].w = volume.weatherMapOffset.y;
                        
                    m_CachedUseWeatherMap[i] = volume.useWeatherMap ? 1.0f : 0.0f;
                    m_CachedAnvilBiases[i] = volume.anvilBias;
                    m_CachedHeightInfluences[i] = volume.heightInfluence;

                    m_CachedWindDirections[i] = volume.windDirection;
                    m_CachedWeatherMapWindMultipliers[i] = volume.weatherMapWindMultiplier;
                    
                    m_CachedShapeTiling[i] = volume.shapeTiling;
                    m_CachedShapeOffsets[i] = volume.shapeOffset;
                    m_CachedShapeWindMultipliers[i] = volume.shapeWindMultiplier;
                    
                    m_CachedDetailTiling[i] = volume.detailTiling;
                    m_CachedDetailOffsets[i] = volume.detailOffset;
                    m_CachedDetailNoiseWeights[i] = volume.detailNoiseWeight;
                    m_CachedDetailTurbulence[i] = volume.turbulence;
                    m_CachedDetailWindMultipliers[i] = volume.detailWindMultiplier;
                    
                    m_CachedLightSteps[i] = volume.lightSteps;
                    m_CachedLightAbsorptionThroughCloud[i] = volume.lightAbsorptionThroughCloud;
                    m_CachedLightAbsorptionTowardsSun[i] = volume.lightAbsorptionTowardsSun;
                    m_CachedScatterTint[i] = volume.scatterTint;
                    m_CachedDarknessThreshold[i] = volume.darknessThreshold;
                    m_CachedPhaseParams[i].x = volume.forwardScattering;
                    m_CachedPhaseParams[i].y = volume.baseBrightness;
                    m_CachedPhaseParams[i].z = volume.phaseFactor;

                    m_CachedSilverParams[i].x = volume.silverIntensity;
                    m_CachedSilverParams[i].y = volume.silverSpread;

                    m_CachedVolumePositions[i] = volume.transform.position;
                    m_CachedVolumeRotations[i] = volume.transform.rotation;
                    m_CachedVolumeScales[i] = volume.transform.localScale;

                    volume.IsDirty = false;
                }
                
                m_CloudsMat.SetTexture("_BlueNoise", m_Settings.blueNoise);
                m_CloudsMat.SetFloat("_VolumeCount", volumeCount);
                m_CloudsMat.SetInt("_FrameIndex", Time.frameCount % 16);
                
                m_CloudsMat.SetFloatArray("_Steps", m_CachedSteps);
                m_CloudsMat.SetFloatArray("_NoiseOffsets", m_CahcedNoiseOffsets);
                m_CloudsMat.SetVectorArray("_BoundsMin", m_CachedBoundsMins);
                m_CloudsMat.SetVectorArray("_BoundsMax", m_CachedBoundsMaxes);
                m_CloudsMat.SetVectorArray("_WorldBoundsMin", m_CachedWorldBoundsMins);
                m_CloudsMat.SetVectorArray("_WorldBoundsMax", m_CachedWorldBoundsMaxes);
                m_CloudsMat.SetMatrixArray("_VolumeWorldToLocal", m_CachedVolumeWorldToLocal);
                m_CloudsMat.SetMatrixArray("_VolumeLocalToWorld", m_CachedVolumeLocalToWorld);
                
                m_CloudsMat.SetFloatArray("_Density", m_CachedDensities);
                m_CloudsMat.SetFloatArray("_Coverage", m_CachedCoverages);
                m_CloudsMat.SetFloatArray("_EdgeFadeDistance", m_CachedEdgeFadeDistances);
                m_CloudsMat.SetFloatArray("_LODNear", m_CachedLODNear);
                m_CloudsMat.SetFloatArray("_LODFar", m_CachedLODFar);
                
                m_CloudsMat.SetFloatArray("_WeatherMapWrapMode", m_CachedWeatherMapWrapMode);
                m_CloudsMat.SetVectorArray("_WeatherMapTilingOffset", m_CachedWeatherMapTilingOffset);
                m_CloudsMat.SetFloatArray("_UseWeatherMap", m_CachedUseWeatherMap);
                m_CloudsMat.SetFloatArray("_AnvilBias", m_CachedAnvilBiases);
                m_CloudsMat.SetFloatArray("_HeightInfluence", m_CachedHeightInfluences);
                
                m_CloudsMat.SetVectorArray("_WindDirection", m_CachedWindDirections);
                m_CloudsMat.SetFloatArray("_WeatherMapFlowMultiplier", m_CachedWeatherMapWindMultipliers);
                
                m_CloudsMat.SetTexture("_ShapeNoise", m_Settings.shapeNoise);
                m_CloudsMat.SetFloatArray("_ShapeTiling", m_CachedShapeTiling);
                m_CloudsMat.SetVectorArray("_ShapeOffset", m_CachedShapeOffsets);
                m_CloudsMat.SetFloatArray("_ShapeWindMultiplier", m_CachedShapeWindMultipliers);
                
                m_CloudsMat.SetTexture("_DetailNoise", m_Settings.detailNoise);
                m_CloudsMat.SetFloatArray("_DetailTiling", m_CachedDetailTiling);
                m_CloudsMat.SetVectorArray("_DetailOffset", m_CachedDetailOffsets);
                m_CloudsMat.SetFloatArray("_DetailNoiseWeights", m_CachedDetailNoiseWeights);
                m_CloudsMat.SetFloatArray("_DetailTurbulence", m_CachedDetailTurbulence);
                m_CloudsMat.SetFloatArray("_DetailWindMultiplier", m_CachedDetailWindMultipliers);
                
                m_CloudsMat.SetFloatArray("_LightSteps", m_CachedLightSteps);
                m_CloudsMat.SetFloatArray("_LightAbsorptionThroughCloud", m_CachedLightAbsorptionThroughCloud);
                m_CloudsMat.SetFloatArray("_LightAbsorptionTowardsSun", m_CachedLightAbsorptionTowardsSun);
                m_CloudsMat.SetColorArray("_ScatterTint", m_CachedScatterTint);
                m_CloudsMat.SetFloatArray("_DarknessThreshold", m_CachedDarknessThreshold);
                m_CloudsMat.SetVectorArray("_PhaseParams", m_CachedPhaseParams);
                m_CloudsMat.SetVectorArray("_SilverParams", m_CachedSilverParams);
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_CloudsMat == null || m_CloudsVolumes.Count == 0) return;
                
                var cmd = CommandBufferPool.Get();
                
                var readHistory = m_CloudsHistoryRTs[m_HistoryIndex];
                var readHistoryDepth = m_CloudsHistoryDepthRTs[m_HistoryIndex];
                var writeHistory = m_CloudsHistoryRTs[1 - m_HistoryIndex];
                var writeHistoryDepth = m_CloudsHistoryDepthRTs[1 - m_HistoryIndex];

                using (new ProfilingScope(cmd, m_OuterScopeSampler))
                {

                    using (new ProfilingScope(cmd, m_RaymarchingSampler))
                    {
                        // =============================
                        // Pass 0: quarter-res ray marching
                        // =============================
                        cmd.SetRenderTarget(new RenderTargetIdentifier[] { m_CloudsRT.nameID, m_CloudsDepthRT.nameID },
                            m_CloudsRT.nameID);

                        cmd.DrawProcedural(Matrix4x4.identity, m_CloudsMat, 0, MeshTopology.Triangles, 3);
                    }

                    using (new ProfilingScope(cmd, m_UpsampleSampler))
                    {

                        // =============================
                        // Pass 1: bilateral upsample + temporal blend
                        // =============================
                        cmd.SetGlobalTexture("_VolumetricCloudsResult", m_CloudsRT);
                        cmd.SetGlobalTexture("_VolumetricCloudsDepthResult", m_CloudsDepthRT);
                        cmd.SetGlobalTexture("_CloudsHistoryTex", readHistory);
                        cmd.SetGlobalTexture("_CloudsHistoryDepthTex", readHistoryDepth);

                        cmd.SetRenderTarget(
                            new RenderTargetIdentifier[] { writeHistory.nameID, writeHistoryDepth.nameID },
                            writeHistory.nameID);
                        cmd.DrawProcedural(Matrix4x4.identity, m_CloudsMat, 1, MeshTopology.Triangles, 3);
                    }

                    using (new ProfilingScope(cmd, m_BlitSampler))
                    {
                        cmd.SetGlobalTexture("_VolumetricCloudsTex", writeHistory);
                        cmd.SetGlobalTexture("_VolumetricCloudsDepthTex", writeHistoryDepth);

                        var camRT = renderingData.cameraData.renderer.cameraColorTargetHandle;
                        Blitter.BlitCameraTexture(cmd, writeHistory, camRT, m_CloudsMat, 2);
                    }

                    m_HistoryIndex = 1 - m_HistoryIndex;

                    m_TemporalFrameIndex = (m_TemporalFrameIndex + 1) % 16;
                    m_IsFirstFrame = false;

                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            private int CollectAdditionalLights(ref RenderingData renderingData)
            {
                var visibleLights = renderingData.lightData.visibleLights;
                int mainLightIndex = renderingData.lightData.mainLightIndex;

                Vector3 volumeCenter = Vector3.zero;
                if (m_CloudsVolumes.Count != 0)
                {
                    Vector3 camPos = renderingData.cameraData.camera.transform.position;
                    float closestDist = float.MaxValue;

                    foreach (var vol in m_CloudsVolumes)
                    {
                        float dist = Vector3.Distance(vol.transform.position, camPos);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            volumeCenter = vol.transform.position;
                        }
                    }
                }

                var availableLights = new List<(int index, float distance)>();

                for (int i = 0; i < visibleLights.Length; i++)
                {
                    if (i == mainLightIndex) continue;
                    var visibleLight = visibleLights[i];
                    var light = visibleLight.light;

                    if (light == null) continue;
                    if (visibleLight.lightType != LightType.Point && light.type != LightType.Spot) continue;

                    Vector3 lightPos = visibleLight.localToWorldMatrix.GetColumn(3);
                    float distance = Vector3.Distance(lightPos, volumeCenter);
                    availableLights.Add((i, distance));
                }
                
                availableLights.Sort((a, b) => a.distance.CompareTo(b.distance));
                
                int lightCount = 0;
                int maxLights = Mathf.Min(ADDITIONAL_LIGHTS_LIMITS, availableLights.Count);

                for (int i = 0; i < maxLights; i++)
                {
                    int j = availableLights[i].index;
                    var visibleLight = visibleLights[j];
                    var light = visibleLight.light;

                    AdditionalLightData lightData = new AdditionalLightData();

                    lightData.positionWS = new Vector4(
                        visibleLight.localToWorldMatrix.GetColumn(3).x,
                        visibleLight.localToWorldMatrix.GetColumn(3).y,
                        visibleLight.localToWorldMatrix.GetColumn(3).z,
                        visibleLight.range
                        );

                    Vector3 forward = visibleLight.localToWorldMatrix.GetColumn(2);
                    float spotAngleCos = Mathf.Cos(visibleLight.spotAngle * 0.5f * Mathf.Deg2Rad);
                    lightData.directionWS = new Vector4(forward.x, forward.y, forward.z, spotAngleCos);

                    Color linearColor = visibleLight.finalColor;
                    float lightType = light.type == LightType.Spot ? 1.0f : 0.0f;
                    lightData.color = new Vector4(linearColor.r, linearColor.g, linearColor.b, lightType);

                    float invRangeSqr = 1.0f / Mathf.Max(visibleLight.range * visibleLight.range, 0.0001f);
                    float spotOuterAngleCos = Mathf.Cos(visibleLight.spotAngle * 0.5f * Mathf.Deg2Rad);
                    float spotInnerAngleCos = Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                    lightData.attenuation = new Vector4(invRangeSqr, spotOuterAngleCos, spotInnerAngleCos, 0f);

                    m_LightDataArray[lightCount] = lightData;
                    lightCount++;
                }

                return lightCount;
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }

            public void Dispose()
            {
                if (m_CloudsMat != null)
                {
                    CoreUtils.Destroy(m_CloudsMat);
                    m_CloudsMat = null;
                }

                m_CloudsRT?.Release();
                m_CloudsRT = null;
                m_CloudsDepthRT?.Release();
                m_CloudsDepthRT = null;

                if (m_CloudsHistoryRTs != null && m_CloudsHistoryDepthRTs != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        m_CloudsHistoryRTs[i]?.Release();
                        m_CloudsHistoryDepthRTs[i]?.Release();
                    }

                    m_CloudsHistoryRTs = null;
                    m_CloudsHistoryDepthRTs = null;
                }
                
                m_AdditionalLightsBuffer?.Release();
                m_AdditionalLightsBuffer = null;

                if (m_CoverageOverHeightTex != null)
                {
                    CoreUtils.Destroy(m_CoverageOverHeightTex);
                    m_CoverageOverHeightTex = null;
                }

                if (m_WeatherMapArray != null)
                {
                    CoreUtils.Destroy(m_WeatherMapArray);
                    m_WeatherMapArray = null;
                }

                if (m_FallbackWeatherMap != null)
                {
                    CoreUtils.Destroy(m_FallbackWeatherMap);
                    m_FallbackWeatherMap = null;
                }
            }
        }

        class VolumetricCloudsComposite : ScriptableRenderPass
        {
            private Material m_CloudsMat; // Composite Pass is at index 2
            private VolumetricCloudsSetting m_Settings;

            private RTHandle m_TempRT;
            private ProfilingSampler m_ProfilingSampler;

            public VolumetricCloudsComposite(VolumetricCloudsSetting settings)
            {
                m_ProfilingSampler = new ProfilingSampler("VolumetricCloudsComposite");
                m_Settings = settings;
            }
            
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                CreateMaterialIfNeeded();
                
                m_CloudsMat.SetTexture("_BlueNoise", m_Settings.blueNoise);
                m_CloudsMat.SetInt("_FrameIndex", Time.frameCount % 16);
                
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_VolumetricCloudsTempCamRT");
            }
            
            private void CreateMaterialIfNeeded()
            {
                if (m_CloudsMat == null)
                {
                    var cloudsShader = Shader.Find("Hidden/Juan/VolumetricClouds");
                    if (cloudsShader == null)
                    {
                        Debug.LogError("Shader Hidden/Juan/VolumetricClouds not found");
                        return;
                    }
                    m_CloudsMat = new Material(cloudsShader);
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_CloudsMat == null) return;

                var cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, m_ProfilingSampler))
                {
                    var camTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    Blitter.BlitCameraTexture(cmd, camTarget, m_TempRT);
                    Blitter.BlitCameraTexture(cmd, m_TempRT, camTarget, m_CloudsMat, 3);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                if (m_CloudsMat != null)
                {
                    CoreUtils.Destroy(m_CloudsMat);
                    m_CloudsMat = null;
                }
                
                m_TempRT?.Release();
                m_TempRT = null;
            }
        }

        private class LightRaysPass : ScriptableRenderPass
        {
            private VolumetricCloudsSetting m_Settings;
            private Material m_LightRaysMat;
            private ProfilingSampler m_ProfilingSampler;

            private RTHandle m_SunOcclusionRT;
            private RTHandle m_LightRaysRT;
            private RTHandle m_LightRaysBlurRT;

            public LightRaysPass(VolumetricCloudsSetting settings)
            {
                m_ProfilingSampler = new ProfilingSampler("Volumetric Clouds Light Rays");
                m_Settings = settings;
            }
            
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                if (!m_Settings.enableLightRays)
                {
                    return;
                }
                
                CreateMaterialIfNeeded();

                var camDesc = renderingData.cameraData.cameraTargetDescriptor;

                var desc = camDesc;
                desc.width >>= 1;
                desc.height >>= 1;
                desc.depthBufferBits = 0;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;

                RenderingUtils.ReAllocateIfNeeded(ref m_SunOcclusionRT, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "LightRaysSunOcclusionRT");
                RenderingUtils.ReAllocateIfNeeded(ref m_LightRaysRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "LightRaysRT");
                RenderingUtils.ReAllocateIfNeeded(ref m_LightRaysBlurRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "LightRaysBlurRT");
            }

            private void CreateMaterialIfNeeded()
            {
                if (m_LightRaysMat == null)
                {
                    var shader = Shader.Find("Hidden/Juan/VolumetricClouds");
                    if (shader == null)
                    {
                        Debug.LogError("Shader for Light Rays not found");
                        return;
                    }
                    m_LightRaysMat = new Material(shader);
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!m_Settings.enableLightRays || m_LightRaysMat == null) return;

                var cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, m_ProfilingSampler))
                {
                    var camera = renderingData.cameraData.camera;
                    var mainLightIndex = renderingData.lightData.mainLightIndex;

                    if (mainLightIndex < 0)
                    {
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                        CommandBufferPool.Release(cmd);
                        return;
                    }
                    
                    m_LightRaysMat.SetFloat("_Comparison", m_Settings.comparison);
                    
                    // =================================
                    // Pass 0: Sun Occlusion
                    // =================================
                    m_LightRaysMat.SetFloat("_LightRaysDistanceFade", m_Settings.distanceFade);
                    m_LightRaysMat.SetFloat("_SunIntensity", m_Settings.sunIntensity);
                    m_LightRaysMat.SetFloat("_SunFalloff", m_Settings.sunFalloff);
                    
                    cmd.SetRenderTarget(m_SunOcclusionRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_LightRaysMat, 4, MeshTopology.Triangles, 3);
                    
                    cmd.SetGlobalTexture("_SunOcclusionTex", m_SunOcclusionRT);
                    
                    // =================================
                    // Pass 1: Radial Blur
                    // =================================
                    m_LightRaysMat.SetInt("_BlurSamples", m_Settings.radialBlurSamples);
                    m_LightRaysMat.SetFloat("_BlurDensity", m_Settings.radialBlurDensity);
                    
                    cmd.SetRenderTarget(m_LightRaysRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_LightRaysMat, 5, MeshTopology.Triangles, 3);
                    cmd.SetGlobalTexture("_LightRaysTex", m_LightRaysRT);
                    
                    // =================================
                    // Pass 2: Gaussian Blur Horizontal
                    // =================================
                    m_LightRaysMat.SetFloat("_BlurRadius", m_Settings.gaussianBlurRadius);
                    cmd.SetRenderTarget(m_LightRaysBlurRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_LightRaysMat, 6, MeshTopology.Triangles, 3);
                    cmd.SetGlobalTexture("_LightRaysBlurHorizontalTex", m_LightRaysBlurRT);
                    
                    // =================================
                    // Pass 3: Gaussian Blur Vertical
                    // =================================
                    cmd.SetRenderTarget(m_LightRaysRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_LightRaysMat, 7, MeshTopology.Triangles, 3);
                    cmd.SetGlobalTexture("_LightRaysTex", m_LightRaysRT);
                    
                    // =================================
                    // Pass 4: Light Rays Composition 
                    // =================================
                    m_LightRaysMat.SetFloat("_LightRaysIntensity", m_Settings.lightIntensity);

                    var camRT = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    cmd.SetRenderTarget(camRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_LightRaysMat, 8, MeshTopology.Triangles, 3);
                }
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                if (m_LightRaysMat != null)
                {
                    CoreUtils.Destroy(m_LightRaysMat);
                    m_LightRaysMat = null;
                }
                
                m_LightRaysRT?.Release();
                m_LightRaysRT = null;
                m_SunOcclusionRT?.Release();
                m_SunOcclusionRT = null;
                m_LightRaysBlurRT?.Release();
                m_LightRaysBlurRT = null;
            }
        }

        VolumetricCloudsPass m_VolumetricCloudsPass;
        private TransparentAboveCloudsPass m_TransparentAboveCloudsPass;
        private VolumetricCloudsComposite m_VolumetricCloudsCompositePass;
        // private LightRaysPass m_LightRaysPass;
        [SerializeField] public VolumetricCloudsSetting settings = new VolumetricCloudsSetting();

        public override void Create()
        {
            // =======================================
            // Raymarching & Bilateral Upscaling
            // =======================================
            if (m_VolumetricCloudsPass != null)
            {
                m_VolumetricCloudsPass.Dispose();
                m_VolumetricCloudsPass = null;
            }

            m_VolumetricCloudsPass = new VolumetricCloudsPass("Volumetric Cloud", settings);
            m_VolumetricCloudsPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

            // =======================================
            // Transparent Depth
            // =======================================
            m_TransparentAboveCloudsPass = new TransparentAboveCloudsPass(settings);
            m_TransparentAboveCloudsPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1; // Always draw after clouds
            
            // =======================================
            // Composite After Transparent
            // =======================================
            // if (m_VolumetricCloudsCompositePass != null)
            // {
            //     m_VolumetricCloudsCompositePass.Dispose();
            //     m_VolumetricCloudsCompositePass = null;
            // }
            // m_VolumetricCloudsCompositePass = new VolumetricCloudsComposite(settings);
            // m_VolumetricCloudsCompositePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            
            // =======================================
            // LightRays as Post-Process
            // =======================================
            // if (m_LightRaysPass != null)
            // {
            //     m_LightRaysPass.Dispose();
            //     m_LightRaysPass = null;
            // }
            // m_LightRaysPass = new LightRaysPass(settings);
            // m_LightRaysPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection) return;

            if (m_TransparentAboveCloudsPass != null)
            {
                renderer.EnqueuePass(m_TransparentAboveCloudsPass);
            }

            if (m_VolumetricCloudsPass != null)
            {
                renderer.EnqueuePass(m_VolumetricCloudsPass);
            }

            // if (m_VolumetricCloudsCompositePass != null)
            // {
            //     renderer.EnqueuePass(m_VolumetricCloudsCompositePass);
            // }

            // if (m_LightRaysPass != null)
            // {
            //     renderer.EnqueuePass(m_LightRaysPass);
            // }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            
            if (m_VolumetricCloudsPass != null)
            {
                m_VolumetricCloudsPass.Dispose();
            }

            // if (m_VolumetricCloudsCompositePass != null)
            // {
            //     m_VolumetricCloudsCompositePass.Dispose();
            // }

            // if (m_LightRaysPass != null)
            // {
            //     m_LightRaysPass.Dispose();
            // }
        }
    }
}


