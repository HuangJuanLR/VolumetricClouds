using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace VolumetricClouds
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VolumetricCloudsLocalVolume : MonoBehaviour
    {
        public Texture3D shapeNoise;
        public Texture3D detailNoise;
        public Texture2D weatherMap;
        public Texture2D blueNoise;
        
        [Min(1)]public int steps = 32;
        [Min(0.0f)]public float density = 1.0f;
        [Range(0.0f, 5.0f)] public float coverage = 1.0f;
        [Range(0.1f, 100.0f)] public float edgeFadeDistance = 5.0f;

        [Header("Weather Map")] 
        public bool useWeatherMap = true;
        public Vector2 weatherMapTiling = Vector2.one;
        public Vector2 weatherMapOffset = Vector2.zero;
        [Range(0.0f, 1.0f)] public float anvilBias = 0.5f;

        [Header("Wind")]
        public Vector3 windDirection = Vector3.zero;
        
        [Header("Shape")]
        public float shapeTiling = 1.0f;
        public Vector3 shapeOffset = Vector3.zero;
        public Vector4 shapeWeights = Vector4.one;
        
        [Header("Detail")] 
        public float detailTiling = 1.0f;
        public Vector3 detailOffset = Vector3.zero;
        public Vector3 detailWeights = Vector3.one;
        [Range(0.0f, 1.0f)]public float detailNoiseWeight = 1.0f;
        
        [Header("Lighting")] 
        [Min(1)] public int lightSteps = 32;
        [Min(0.1f)]public float lightAbsorptionThroughCloud = 1.0f;
        [Min(0.1f)]public float lightAbsorptionTowardsSun = 1.0f;
        public Color scatterTint = Color.white;
        [Range(0.0f, 1.0f)] public float darknessThreshold = 0.2f;
        [Range(0.0f, 1.0f)] public float forwardScattering = 0.5f;
        [Range(0.0f, 1.0f)] public float backScattering = 0.3f;
        [Range(0.0f, 1.0f)] public float baseBrightness = 0.8f;
        [Range(0.0f, 1.0f)] public float phaseFactor = 0.15f;

        private MeshFilter m_MeshFilter;
        private MeshRenderer m_MeshRenderer;
        private Material m_CloudsMat;
        private Bounds m_Bounds;

        private RenderTexture m_HistoryBuffer;
        
        private Vector4 WeatherMapTilingOffset
        {
            get
            {
                return new Vector4(weatherMapTiling.x, weatherMapTiling.y, weatherMapOffset.x, weatherMapOffset.y);
            }
        }

        private Vector4 PhaseParams
        {
            get
            {
                return new Vector4(forwardScattering, backScattering, 
                    baseBrightness, phaseFactor);
            }
        }

        [MenuItem("GameObject/Volume/Volumetric Clouds Local", false)]
        static void CreateVolumetricCloudsVolume()
        {
            GameObject cloudsVolume = new GameObject();
            cloudsVolume.name = "VolumetricCloudsLocalVolume";
            cloudsVolume.AddComponent<VolumetricCloudsLocalVolume>();

            Selection.activeGameObject = cloudsVolume;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        
        private void OnEnable()
        {
            m_MeshFilter = GetComponent<MeshFilter>();
            m_MeshRenderer = GetComponent<MeshRenderer>();
            if (m_MeshRenderer != null)
            {
                m_Bounds = m_MeshRenderer.bounds;
            }
            
            CreateMeshIfNeeded();
            CreateMaterialIfNeeded();
        }

        private void OnDisable()
        {
            if (m_HistoryBuffer != null)
            {
                m_HistoryBuffer.Release();
                m_HistoryBuffer = null;
            }
        }

        private void CreateMeshIfNeeded()
        {
            if (m_MeshFilter.sharedMesh == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_MeshFilter.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(cube);
            }
        }

        private void CreateMaterialIfNeeded()
        {
            if (m_CloudsMat == null)
            {
                Shader cloudsShader = Shader.Find("Hidden/Juan/VolumetricCloudsLocal");
                if (cloudsShader == null)
                {
                    throw new NullReferenceException("VolumetricCloudsLocal shader is null");
                }

                m_CloudsMat = new Material(cloudsShader);
            }
            
            m_MeshRenderer.sharedMaterial = m_CloudsMat;
        }

        private void OnDrawGizmos()
        {
            Gizmos.DrawWireCube(transform.position + m_Bounds.center, m_Bounds.size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + m_Bounds.center, m_Bounds.size);
            Gizmos.color = Color.white;
        }

        void Update()
        {
            CreateMeshIfNeeded();
            CreateMaterialIfNeeded();

            if (m_HistoryBuffer == null || m_HistoryBuffer.width != Screen.width ||
                m_HistoryBuffer.height != Screen.height)
            {
                if(m_HistoryBuffer != null) m_HistoryBuffer.Release();
                
                m_HistoryBuffer = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
                m_HistoryBuffer.filterMode = FilterMode.Bilinear;
                m_HistoryBuffer.wrapMode = TextureWrapMode.Clamp;
                m_HistoryBuffer.Create();

                RenderTexture rt = RenderTexture.active;
                RenderTexture.active = m_HistoryBuffer;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = rt;
            }
            
            m_CloudsMat.SetTexture("_HistoryTex", m_HistoryBuffer);
            m_CloudsMat.SetVector("_HistorySize", 
                new Vector4(
                    m_HistoryBuffer.width,
                    m_HistoryBuffer.height,
                    1.0f / m_HistoryBuffer.width,
                    1.0f / m_HistoryBuffer.height)
                );
            m_CloudsMat.SetInt("_FrameIndex", Time.frameCount % 16); // 4x4 pattern
            
            m_CloudsMat.SetTexture("_BlueNoise", blueNoise);
            
            m_CloudsMat.SetFloat("_Steps", steps);
            m_CloudsMat.SetVector("_BoundsMin", transform.position - m_Bounds.extents);
            m_CloudsMat.SetVector("_BoundsMax", transform.position + m_Bounds.extents);
            
            m_CloudsMat.SetFloat("_Density", density);
            m_CloudsMat.SetFloat("_Coverage", coverage);
            m_CloudsMat.SetFloat("_EdgeFadeDistance", edgeFadeDistance);
            
            m_CloudsMat.SetTexture("_WeatherMap", weatherMap);
            m_CloudsMat.SetVector("_WeatherMapTilingOffset", WeatherMapTilingOffset);
            m_CloudsMat.SetFloat("_UseWeatherMap", useWeatherMap? 1.0f : 0.0f);
            m_CloudsMat.SetFloat("_AnvilBias", anvilBias);
            
            m_CloudsMat.SetVector("_WindDirection", windDirection);
            
            m_CloudsMat.SetTexture("_ShapeNoise", shapeNoise);
            m_CloudsMat.SetVector("_ShapeWeights", shapeWeights);
            m_CloudsMat.SetFloat("_ShapeTiling", shapeTiling);
            m_CloudsMat.SetVector("_ShapeOffset", shapeOffset);
            
            m_CloudsMat.SetTexture("_DetailNoise", detailNoise);
            m_CloudsMat.SetVector("_DetailWeights", detailWeights);
            m_CloudsMat.SetFloat("_DetailTiling", detailTiling);
            m_CloudsMat.SetVector("_DetailOffset", detailOffset);
            m_CloudsMat.SetFloat("_DetailNoiseWeights", detailNoiseWeight);
            
            m_CloudsMat.SetFloat("_LightSteps", lightSteps);
            m_CloudsMat.SetFloat("_LightAbsorptionThroughCloud", lightAbsorptionThroughCloud);
            m_CloudsMat.SetFloat("_LightAbsorptionTowardsSun", lightAbsorptionTowardsSun);
            m_CloudsMat.SetColor("_ScatterTint", scatterTint);
            m_CloudsMat.SetFloat("_DarknessThreshold", darknessThreshold);
            m_CloudsMat.SetVector("_PhaseParams", PhaseParams);
        }
    }
}
