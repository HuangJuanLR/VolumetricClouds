using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

namespace VolumetricClouds
{
    public struct AdditionalLightData
    {
        public Vector4 positionWS;
        public Vector4 directionWS; // spot only
        public Vector4 color;
        public Vector4 attenuation; // x = 1/range, y = spot outer angle cos, z = spot inner angle cos, w = unused
    }
    
    [ExecuteAlways]
    public class VolumetricCloudsVolume : MonoBehaviour
    {
        [Header("Basic")]
        [NameTag("步进次数")][Range(1, 1024)]public int steps = 64;
        [NameTag("密度")][Range(0.0f, 10.0f)]public float density = 1.0f;
        [NameTag("覆盖范围")][Range(0.0f, 5.0f)] public float coverage = 1.0f;
        [NameTag("边缘衰减")][Range(0.0f, 1.0f)] public float edgeFadeDistance = 0.0f;
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
        [NameTag("形状平铺")]public float shapeTiling = 1.0f;
        [NameTag("形状偏移")]public Vector3 shapeOffset = Vector3.zero;
        [NameTag("速度")][Range(0.0f, 5.0f)]public float shapeWindMultiplier = 0.0f;
        
        [Header("Detail")] 
        [NameTag("噪波平铺")]public float detailTiling = 10.0f;
        [NameTag("噪波偏移")]public Vector3 detailOffset = Vector3.zero;
        [NameTag("噪波强度")][Range(0.0f, 1.0f)]public float detailNoiseWeight = 1.0f;
        [NameTag("速度")][Range(0.0f, 5.0f)]public float detailWindMultiplier = 0.0f;
        
        [Header("Lighting")] 
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

        private const int CURVE_RESOLUTION = 64;
        
        private Bounds m_CloudsBounds = new Bounds();
        private Bounds m_WorldBounds = new Bounds();
        private Vector3 m_CachedPosition;
        private Vector3 m_CachedScale;
        private Quaternion m_CachedRotation;

        private Texture2D m_CoverageOverHeightTex;
        private int m_CurveVersion = 0;

        private float m_CameraDistance;
        private Camera m_Camera;
        
        private bool m_IsDirty = true;

        public Bounds CloudsBounds => m_CloudsBounds;
        public Bounds WorldBounds => m_WorldBounds;

        public bool IsDirty
        {
            get => m_IsDirty;
            set => m_IsDirty = value;
        }

        public Texture2D CoverageOverHeightTex
        {
            get
            {
                if (m_CoverageOverHeightTex == null)
                {
                    BakeCoverageHeightCurve();
                }

                return m_CoverageOverHeightTex;
            }
        }

        public int CurveVersion => m_CurveVersion;

        [MenuItem("GameObject/Volume/Volumetric Clouds", false)]
        static void CreateVolumetricCloudsVolume()
        {
            GameObject cloudsVolume = new GameObject();
            cloudsVolume.name = "VolumetricCloudsVolume";
            cloudsVolume.AddComponent<VolumetricCloudsVolume>();

            Selection.activeGameObject = cloudsVolume;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private void Awake()
        {
            BakeCoverageHeightCurve();
        }

        private void OnEnable()
        {
            m_CachedPosition = transform.position;
            m_CachedScale = transform.localScale;
            m_CachedRotation = transform.rotation;
            m_CloudsBounds.extents = transform.localScale;
            m_CloudsBounds.center = Vector3.zero;

            UpdateWorldBounds();

            m_IsDirty = true;
        }

        private void OnValidate()
        {
            BakeCoverageHeightCurve();
            m_IsDirty = true;
        }

        private void Update()
        {
            if (transform.localScale != m_CachedScale)
            {
                UpdateWorldBounds();
                
                m_CachedScale = transform.localScale;
                m_CloudsBounds.extents = m_CachedScale;
                
            }

            if (m_CachedRotation != transform.rotation 
                || transform.position != m_CachedPosition)
            {
                UpdateWorldBounds();
                
                m_CachedPosition = transform.position;
                m_CachedRotation = transform.rotation;
                m_CachedScale = transform.localScale;
            }
        }
        
        public void BakeCoverageHeightCurve()
        {
            if (m_CoverageOverHeightTex == null)
            {
                m_CoverageOverHeightTex = new Texture2D(CURVE_RESOLUTION, 1, TextureFormat.RFloat, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            Color[] pixels = new Color[CURVE_RESOLUTION];
            for (int i = 0; i < CURVE_RESOLUTION; i++)
            {
                float t = i / (float)(CURVE_RESOLUTION - 1);
                float value = Mathf.Clamp01(coverageOverHeight.Evaluate(t));
                pixels[i] = new Color(value, 0, 0, 1);
            }
    
            m_CoverageOverHeightTex.SetPixels(pixels);
            m_CoverageOverHeightTex.Apply();

            m_CurveVersion++;
        }

        private void UpdateWorldBounds()
        {
            Vector3 center = Vector3.zero;
            Vector3 extents = Vector3.one;

            Vector3[] pts = new Vector3[8];
            pts[0] = new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);
            pts[1] = new Vector3(center.x - extents.x, center.y - extents.y, center.z + extents.z);
            pts[2] = new Vector3(center.x - extents.x, center.y + extents.y, center.z - extents.z);
            pts[3] = new Vector3(center.x - extents.x, center.y + extents.y, center.z + extents.z);
            pts[4] = new Vector3(center.x + extents.x, center.y - extents.y, center.z - extents.z);
            pts[5] = new Vector3(center.x + extents.x, center.y - extents.y, center.z + extents.z);
            pts[6] = new Vector3(center.x + extents.x, center.y + extents.y, center.z - extents.z);
            pts[7] = new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);

            m_WorldBounds.size = Vector3.zero;
            for (int i = 0; i < 8; ++i)
            {
                var pt = transform.localToWorldMatrix.MultiplyPoint3x4(pts[i]);
                m_WorldBounds.Encapsulate(pt);
            }
            
        }

        private void UpdateSceneCamera()
        {
            if (m_Camera == null)
            {
                m_Camera = Application.isPlaying? Camera.main : SceneView.lastActiveSceneView.camera;
                if (m_Camera == null)
                {
                    m_CameraDistance = 0.0f;
                }
            }
            else
            {
                m_CameraDistance = Vector3.Distance(m_Camera.transform.position, transform.position);
            }
        }

        private void OnDestroy()
        {
            if (m_CoverageOverHeightTex != null)
            {
                DestroyImmediate(m_CoverageOverHeightTex);
                m_CoverageOverHeightTex = null;
            }
        }

        private void OnDrawGizmosSelected()
        {
            var ogMatrix = Gizmos.matrix;
            
            var localToWorld = Matrix4x4.TRS(
                transform.position,
                transform.rotation,
                Vector3.one
                );
            Gizmos.matrix = localToWorld;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(CloudsBounds.center, CloudsBounds.size);
            Gizmos.DrawLine(Vector3.zero, Vector3.up * CloudsBounds.extents.y);
            Gizmos.color = Color.white;

            Gizmos.matrix = ogMatrix;
            
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, m_WorldBounds.size);
            
            Handles.color = Color.yellow;
            UpdateSceneCamera();
            Handles.Label(transform.position + Vector3.down * 10.0f, $"{m_CameraDistance:F1}");
            Handles.color = Color.white;

        }
    }
}


public class NameTag : PropertyAttribute
{
    public string name = "";
    
    public NameTag(string name)
    {
        this.name = name;
    }
}

[CustomPropertyDrawer(typeof(NameTag))]
public class NameTagDrawer : PropertyDrawer
{
    private GUIContent m_Content;
    
    NameTag nameTag => (NameTag)attribute;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        m_Content ??= new GUIContent(nameTag.name);
        EditorGUI.PropertyField(position, property, m_Content, true);
    }
}