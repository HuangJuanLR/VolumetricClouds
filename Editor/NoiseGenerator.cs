using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

public class NoiseGenerator : EditorWindow
{
    private enum PreviewMode
    {
        Volume,
        Slice
    }
    
    private const int NUMTHREADSX = 8;
    private const int NUMTHREADSY = 8;
    
    private static NoiseGenerator m_Window;
    
    private NoiseSettings m_Settings;
    
    private Texture2D m_PreviewTexture2D;
    private Texture3D m_PreviewTexture3D;
    private Material m_PreviewMaterial;
    
    private Vector2 m_ScrollPosition;
    
    private bool m_IsGenerating = false;
    private float m_Progress = 0f;

    private Editor m_SettingsEditor;

    private RenderTexture m_RenderTexture2D;
    private RenderTexture m_RenderTexture3D;
    private ComputeBuffer m_SampleBuffer;
    
    // Preview State
    private bool m_IsDragging; 
    private PreviewRenderUtility m_PreviewRenderUtility;
    private PreviewMode m_PreviewMode = PreviewMode.Volume;
    private float m_PreviewTiling = 1.0f;
    private Vector3 m_SliceDepths = Vector3.zero;
    private Vector2 m_PreviewRotation = Vector2.zero;
    private Color m_VolumeFilterColor = Color.white;
    private float m_VolumeOpacity = 1.0f;
    private float m_VolumeDensity = 1.0f;
    private int m_VolumeSteps = 32;

    private float m_PreviewDistance = 3.0f;
    private Mesh m_PreviewCubeMesh;
    private Mesh m_PreviewQuadMesh;
    private PreviewMode m_LastPreviewMode;
    
    [MenuItem("Tools/VolumetricClouds/Noise Generator")]
    public static void OpenWindow()
    {
        m_Window = GetWindow<NoiseGenerator>();
        
        GUIContent title = new GUIContent("Noise Generator");

        m_Window.titleContent = title;
        m_Window.minSize = new Vector2(300, 500);
        m_Window.maxSize = new Vector2(500, 800);
    }

    private void OnEnable()
    {
        CreatePreviewMaterial();
        InitPreviewRenderUtility();
    }

    private void OnDisable()
    {
        CleanupTextures();
        CleanupPreviewRenderUtility();
        if(m_PreviewMaterial != null) DestroyImmediate(m_PreviewMaterial);
        if(m_SettingsEditor != null) DestroyImmediate(m_SettingsEditor);
        m_SampleBuffer?.Release();
        m_PreviewQuadMesh = null;
        m_PreviewCubeMesh = null;
    }

    private void InitPreviewRenderUtility()
    {
        if (m_PreviewRenderUtility is null)
        {
            m_PreviewRenderUtility = new PreviewRenderUtility();
            m_PreviewRenderUtility.camera.transform.position = new Vector3(0, 0, -5);
            m_PreviewRenderUtility.camera.transform.rotation = Quaternion.identity;
            m_PreviewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            m_PreviewRenderUtility.camera.backgroundColor = new Color(0.2f,0.2f,0.2f,1f);
            m_PreviewRenderUtility.camera.fieldOfView = 45;
            m_PreviewRenderUtility.camera.nearClipPlane = 0.3f;
            m_PreviewRenderUtility.camera.farClipPlane = 500f;
        }
        
        if(m_PreviewCubeMesh == null)
        {
            m_PreviewCubeMesh = CreateCubeMesh();
            m_PreviewCubeMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        if (m_PreviewQuadMesh == null)
        {
            m_PreviewQuadMesh = CreateQuadMesh();
            m_PreviewQuadMesh.hideFlags = HideFlags.HideAndDontSave;
        }
    }
    
    private void CleanupTextures()
    {
        if(m_PreviewTexture2D != null) DestroyImmediate(m_PreviewTexture2D);
        if(m_PreviewTexture3D != null) DestroyImmediate(m_PreviewTexture3D);
        // Seems the next line is ok
        if(m_RenderTexture2D != null) m_RenderTexture2D.Release();
        if(m_RenderTexture3D != null) m_RenderTexture3D.Release();
    }

    private void CleanupPreviewRenderUtility()
    {
        if (m_PreviewRenderUtility is not null)
        {
            m_PreviewRenderUtility.Cleanup();
            m_PreviewRenderUtility = null;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Noise Settings", EditorStyles.boldLabel);
        
        DrawNoiseSettings();

        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = !m_IsGenerating && m_Settings != null;
        if (GUILayout.Button("Generate Noise", GUILayout.Height(30)))
        {
            GenerateNoise();
        }
        GUI.enabled = true;

        GUI.enabled = m_Settings != null;
        if (GUILayout.Button("Save Settings", GUILayout.Height(30)))
        {
            SaveSettings();
        }
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
        
        // Progress Bar
        if (m_IsGenerating)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(20)), 
                m_Progress, $"Generating... {(int)(m_Progress * 100)}%");
        }
        
        EditorGUILayout.Space(10);
        
        EditorGUILayout.EndVertical(); // End Noise Settings
        
        // Preview
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        GUI.enabled = (m_PreviewTexture2D != null || m_PreviewTexture3D != null) && !m_IsGenerating;
        if (GUILayout.Button("Save Texture", GUILayout.Height(30)))
        {
            SaveTexture();
        }
        GUI.enabled = true;

        EditorGUILayout.Space(5);
        
        DrawPreview();
        
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawNoiseSettings()
    {
        NoiseSettings newSettings = (NoiseSettings)EditorGUILayout.ObjectField(
            "Settings Asset",
            m_Settings,
            typeof(NoiseSettings),
            false
            );

        if (newSettings != m_Settings)
        {
            m_Settings = newSettings;
            if (m_SettingsEditor != null)
            {
                DestroyImmediate(m_SettingsEditor);
                m_SettingsEditor = null;
            }
        }

        if (m_Settings == null)
        {
            EditorGUILayout.HelpBox(
                "No NoiseSettings loaded",
                MessageType.Info
            );
            return;
        }
        
        EditorGUILayout.LabelField("Noise Type", m_Settings.GetNoiseTypeName(), EditorStyles.boldLabel);

        if (m_SettingsEditor == null || m_SettingsEditor.target != m_Settings)
        {
            if(m_SettingsEditor != null) DestroyImmediate(m_SettingsEditor);
            m_SettingsEditor = Editor.CreateEditor(m_Settings);
        }

        if (m_SettingsEditor != null)
        {
            EditorGUI.BeginChangeCheck();
            
            m_SettingsEditor.serializedObject.Update();

            SerializedProperty prop = m_SettingsEditor.serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "m_Script") continue;
                    EditorGUILayout.PropertyField(prop, true);
                } while (prop.NextVisible(false));
            }
            
            m_SettingsEditor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(m_Settings);
                if (m_Settings.realtime && !m_IsGenerating)
                {
                    GenerateNoise();
                }
            }
        }
    }

    private void DrawPreview()
    {
        if (m_Settings == null)
        {
            EditorGUILayout.HelpBox("Load or create noise settings first", MessageType.Info);
            return;
        }

        float rectSize = EditorGUIUtility.currentViewWidth * 0.8f;
        if (m_Settings.dimension == Dimension._2D && m_PreviewTexture2D != null)
        {
            Rect previewRect = GUILayoutUtility.GetRect(rectSize, rectSize, GUILayout.ExpandWidth(true));
            
            float size = Mathf.Min(previewRect.width, previewRect.height);
            Rect centeredRect = new Rect(
                previewRect.x + (previewRect.width - size) / 2,
                previewRect.y,
                size,
                size
            );
            
            EditorGUI.DrawPreviewTexture(centeredRect, m_PreviewTexture2D);
        }
        else if (m_Settings.dimension == Dimension._3D && m_PreviewTexture3D != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            m_PreviewMode = (PreviewMode)EditorGUILayout.EnumPopup("Preview Mode", m_PreviewMode);
            m_PreviewTiling = EditorGUILayout.FloatField("Tiling", m_PreviewTiling);

            switch (m_PreviewMode)
            {
                case PreviewMode.Slice:
                    m_SliceDepths.x = EditorGUILayout.Slider("X", m_SliceDepths.x, 0.0f, 1.0f);
                    m_SliceDepths.y = EditorGUILayout.Slider("Y", m_SliceDepths.y, 0.0f, 1.0f);
                    m_SliceDepths.z = EditorGUILayout.Slider("Z", m_SliceDepths.z, 0.0f, 1.0f);
                    break;
                
                case PreviewMode.Volume:
                    m_VolumeFilterColor = EditorGUILayout.ColorField("Filter Color", m_VolumeFilterColor);
                    m_VolumeDensity = EditorGUILayout.Slider("Density", m_VolumeDensity, 0f, 1f);
                    m_VolumeOpacity = EditorGUILayout.Slider("Opacity", m_VolumeOpacity, 0f, 1f);
                    m_VolumeSteps = EditorGUILayout.IntSlider("Steps", m_VolumeSteps, 1, 100);
                    break;
            }
            
            Rect previewRect = GUILayoutUtility.GetRect(rectSize, rectSize, GUILayout.ExpandWidth(true));
            float size = Mathf.Min(previewRect.width, previewRect.height);
            Rect centeredRect = new Rect(
                previewRect.x + (previewRect.width - size) / 2,
                previewRect.y,
                size,
                size
            );

            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (centeredRect.Contains(e.mousePosition) && e.button == 0)
                    {
                        GUIUtility.hotControl = controlID;
                        m_IsDragging = true;
                        e.Use();
                    }
                    break;
                
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID && e.button == 0)
                    {
                        m_PreviewRotation.x += e.delta.y * 0.5f;
                        m_PreviewRotation.y += -e.delta.x * 0.5f;
                        e.Use();
                        Repaint();
                    }
                    break;
                
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        m_IsDragging = false;
                        e.Use();
                    }
                    break;
                
                case EventType.ScrollWheel:
                    if (centeredRect.Contains(e.mousePosition))
                    {
                        m_PreviewDistance += e.delta.y * 0.1f;
                        m_PreviewDistance = Mathf.Clamp(m_PreviewDistance, 1.5f, 10f);
                        e.Use();
                        Repaint();
                    }
                    break;
            }
            
            Render3DTexturePreview(centeredRect);
            
            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.HelpBox("Generate noise to preview", MessageType.Info);
        }
    }

    private void Render3DTexturePreview(Rect rect)
    {
        if (m_PreviewRenderUtility is null || m_PreviewTexture3D is null)
        {
            return;
        }

        if (m_PreviewMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/PreviewTexture3D");
            if (shader != null)
            {
                m_PreviewMaterial = new Material(shader);
                m_PreviewMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                Debug.LogError("Cannot find any shader!");
                return;
            }
        }

        float yaw = -m_PreviewRotation.y * Mathf.Deg2Rad;
        float pitch = m_PreviewRotation.x * Mathf.Deg2Rad;
        pitch = Mathf.Clamp(pitch, -89f * Mathf.Deg2Rad, 89f * Mathf.Deg2Rad);

        Vector3 cameraPosition = new Vector3(
            Mathf.Cos(pitch) * Mathf.Sin(yaw) * m_PreviewDistance,
            Mathf.Sin(pitch) * m_PreviewDistance,
            Mathf.Cos(pitch) * Mathf.Cos(yaw) * m_PreviewDistance
            );

        m_PreviewRenderUtility.camera.transform.position = cameraPosition;
        m_PreviewRenderUtility.camera.transform.LookAt(Vector3.zero);
        
        m_PreviewRenderUtility.BeginPreview(rect, GUIStyle.none);
        
        m_PreviewMaterial.SetTexture("_Volume", m_PreviewTexture3D);
        m_PreviewMaterial.SetColor("_FilterColor", m_VolumeFilterColor);
        m_PreviewMaterial.SetFloat("_Mode", (float)m_PreviewMode);
        m_PreviewMaterial.SetFloat("_Tiling", m_PreviewTiling);

        if (m_PreviewMode == PreviewMode.Volume)
        {
            m_PreviewMaterial.SetInt("_Steps", m_VolumeSteps);
            m_PreviewMaterial.SetFloat("_Density", m_VolumeDensity);
            m_PreviewMaterial.SetFloat("_Opacity", m_VolumeOpacity);
            
            // Cull Back ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            m_PreviewMaterial.SetFloat("_Cull", 2.0f);
            m_PreviewMaterial.SetFloat("_ZWrite", 0.0f);
            m_PreviewMaterial.SetFloat("_BlendSrc", 5.0f);
            m_PreviewMaterial.SetFloat("_BlendDst", 10.0f);
            
            if (m_PreviewCubeMesh == null)
            {
                m_PreviewCubeMesh = CreateCubeMesh();
                m_PreviewCubeMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            
            Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            m_PreviewRenderUtility.DrawMesh(m_PreviewCubeMesh, matrix, m_PreviewMaterial, 0);   
        }
        else
        {
            m_PreviewMaterial.SetVector("_SliceDepths", m_SliceDepths);
            
            // Cull Off ZWrite On Blend Off
            m_PreviewMaterial.SetFloat("_Cull", 0.0f);
            m_PreviewMaterial.SetFloat("_ZWrite", 1.0f);
            m_PreviewMaterial.SetFloat("_BlendSrc", 1.0f);
            m_PreviewMaterial.SetFloat("_BlendDst", 0.0f);

            if (m_PreviewQuadMesh == null)
            {
                m_PreviewQuadMesh = CreateQuadMesh();
                m_PreviewQuadMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            
            DrawSlicePlanes();
        }
        
        
        m_PreviewRenderUtility.Render(false, false);
        Texture resultTexture = m_PreviewRenderUtility.EndPreview();
        GUI.DrawTexture(rect, resultTexture, ScaleMode.StretchToFill, false);
    }

    private void DrawSlicePlanes()
    {
        Matrix4x4 xPlane = Matrix4x4.TRS(
            new Vector3(m_SliceDepths.x - 0.5f, 0, 0),
            Quaternion.Euler(0, 90, 0),
            Vector3.one
        );
        
        Matrix4x4 yPlane = Matrix4x4.TRS(
            new Vector3(0, m_SliceDepths.y - 0.5f, 0),
            Quaternion.Euler(90, 0, 0),
            Vector3.one
        );

        Matrix4x4 zPlane = Matrix4x4.TRS(
            new Vector3(0, 0, m_SliceDepths.z - 0.5f),
            Quaternion.identity,
            Vector3.one
        );
        
        m_PreviewRenderUtility.DrawMesh(m_PreviewQuadMesh, xPlane, m_PreviewMaterial, 0);
        m_PreviewRenderUtility.DrawMesh(m_PreviewQuadMesh, yPlane, m_PreviewMaterial, 0);
        m_PreviewRenderUtility.DrawMesh(m_PreviewQuadMesh, zPlane, m_PreviewMaterial, 0);
    }
    
    private Mesh CreateCubeMesh()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.hideFlags = HideFlags.HideAndDontSave;
        Mesh mesh = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        mesh.hideFlags = HideFlags.HideAndDontSave;
        DestroyImmediate(cube);
        return mesh;
    }
    
    private Mesh CreateQuadMesh()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.hideFlags = HideFlags.HideAndDontSave;
        Mesh mesh = Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
        mesh.hideFlags = HideFlags.HideAndDontSave;
        DestroyImmediate(quad);
        return mesh;
    }

    private void GenerateNoise()
    {
        if (m_Settings == null) return;

        m_IsGenerating = true;
        m_Progress = 0f;

        try
        {
            if (m_Settings.GetComputeShader() != null)
            {
                GenerateNoiseWithComputeShader();    
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error generating noise: {e.Message}");
        }
        finally
        {
            m_IsGenerating = false;
            m_Progress = 0f;
            Repaint();
        }
    }

    private void GenerateNoiseWithComputeShader()
    {
        ComputeShader compute = m_Settings.GetComputeShader();
        
        if(compute == null)
        {
            Debug.LogError("Compute shader not assigned");
            return;
        }
        
        string kernelName = m_Settings.dimension == Dimension._2D? 
            $"Generate{m_Settings.GetNoiseTypeName()}2D" : $"Generate{m_Settings.GetNoiseTypeName()}3D";
        
        int kernelIndex = compute.FindKernel(kernelName);
        if (m_Settings.dimension == Dimension._2D)
        {
            GenerateNoise2DWithComputeShader(compute, kernelIndex);
        }
        else
        {
            GenerateNoise3DWithComputeShader(compute, kernelIndex);
        }
    }

    private void GenerateNoise2DWithComputeShader(ComputeShader compute, int kernelIndex)
    {
        CleanupTextures();

        int width = (int)m_Settings.resolution;
        int height = (int)m_Settings.resolution;

        m_RenderTexture2D = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        m_RenderTexture2D.enableRandomWrite = true;
        m_RenderTexture2D.Create();
        
        compute.SetTexture(kernelIndex, "Result2D", m_RenderTexture2D);
        m_Settings.SetComputeShaderParameters(compute, kernelIndex);

        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        
        compute.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

        m_PreviewTexture2D = new Texture2D(width, height, TextureFormat.RGBA32, false);
        RenderTexture.active = m_RenderTexture2D;
        m_PreviewTexture2D.ReadPixels(new Rect(0,0, width, height), 0, 0);
        m_PreviewTexture2D.Apply();
        RenderTexture.active = null;

        m_Progress = 1.0f;
    }
    
    private void GenerateNoise3DWithComputeShader(ComputeShader compute, int kernelIndex)
    {
        if(m_PreviewTexture2D != null) DestroyImmediate(m_PreviewTexture2D);
        if(m_RenderTexture2D != null) m_RenderTexture2D.Release();
        if(m_RenderTexture3D != null) m_RenderTexture3D.Release();
        
        int width = (int)m_Settings.resolution;
        int height = (int)m_Settings.resolution;
        int depth = (int)m_Settings.resolution;

        m_RenderTexture3D = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        m_RenderTexture3D.dimension = TextureDimension.Tex3D;
        m_RenderTexture3D.filterMode = FilterMode.Bilinear;
        m_RenderTexture3D.wrapMode = TextureWrapMode.Repeat;
        m_RenderTexture3D.volumeDepth = depth;
        m_RenderTexture3D.enableRandomWrite = true;
        m_RenderTexture3D.Create();
        
        compute.SetTexture(kernelIndex, "Result3D", m_RenderTexture3D);
        m_Settings.SetComputeShaderParameters(compute, kernelIndex);
        
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(depth / 8.0f);
        
        compute.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);


        var pixelData = new NativeArray<byte>(width * height * depth * 4, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);

        AsyncGPUReadback.RequestIntoNativeArray(ref pixelData, m_RenderTexture3D, 0, (_) =>
        {
            m_PreviewTexture3D = new Texture3D(width, height, depth, TextureFormat.RGBA32, false);
            m_PreviewTexture3D.filterMode = m_RenderTexture3D.filterMode;
            m_PreviewTexture3D.wrapMode = m_RenderTexture3D.wrapMode;
            m_PreviewTexture3D.SetPixelData(pixelData, 0);
            m_PreviewTexture3D.Apply();
            pixelData.Dispose();
            
            Repaint();
        });
        
        m_Progress = 1f;
    }
    
    private void CreatePreviewMaterial()
    {
        Shader shader = Shader.Find("Hidden/PreviewTexture3D");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }
        m_PreviewMaterial = new Material(shader);
        m_PreviewMaterial.hideFlags = HideFlags.HideAndDontSave;
    }

    private void SaveSettings()
    {
        if (m_Settings == null)
        {
            return;
        }
        
        string existingPath = AssetDatabase.GetAssetPath(m_Settings);

        if (!string.IsNullOrEmpty(existingPath))
        {
            EditorUtility.SetDirty(m_Settings);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", "Settings saved", "OK");
        }
        else
        {
            string defaultName = $"{m_Settings.GetNoiseTypeName()}Settings";
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Noise Settings",
                defaultName,
                "asset",
                "Select location to save settings"
            );

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(m_Settings, path);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Success", "Settings saved", "OK");
            }
        }
    }

    private void SaveTexture()
    {
        if (m_Settings == null) return;

        string extension = "png";
        string defaultName = $"{m_Settings.GetNoiseTypeName()}Texture";

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Noise Texture",
            defaultName,
            extension,
            "Select location to save texture"
            );

        if (!string.IsNullOrEmpty(path))
        {
            if (m_Settings.dimension == Dimension._2D)
            {
                byte[] bytes = m_PreviewTexture2D.EncodeToPNG();
                
                File.WriteAllBytes(path, bytes);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success", "2D Texture saved successfully!", "OK");
            }
            else
            {
                SaveTexture3DAsSlices(path);
            }
        }
    }

    private void SaveTexture3DAsSlices(string path)
    {
        int width = m_PreviewTexture3D.width;
        int height = m_PreviewTexture3D.height;
        int depth = m_PreviewTexture3D.depth;

        int dstWidth = width;
        int dstHeight = height * depth;

        Color[] srcPixels = m_PreviewTexture3D.GetPixels();

        Texture2D dst = new Texture2D(dstWidth, dstHeight, TextureFormat.RGBA32, false);
        Color[] dstPixels = new Color[dstWidth * dstHeight];

        for (int z = 0; z < depth; z++)
        {
            int offsetY = height * z;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIndex = x + y * width + z * width * height;
                    int dstIndex = x + (offsetY + y) * width;
                    
                    Color pixel = srcPixels[srcIndex];

                    if (IsSingleChannelFormat(m_Settings.outputFormat))
                    {
                        float r = pixel.r;
                        dstPixels[dstIndex] = new Color(r, r, r, 1.0f);
                    }
                    else
                    {
                        dstPixels[dstIndex] = pixel;
                    }
                }
            }
        }
        
        dst.SetPixels(dstPixels);
        dst.Apply();
        
        // Encode to PNG
        byte[] bytes = dst.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        DestroyImmediate(dst);
        
        AssetDatabase.Refresh();
        
        // Import Config for 3D texture
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        TextureImporterSettings settings = new TextureImporterSettings();
        if (importer != null)
        {
            settings.textureShape = TextureImporterShape.Texture3D;
            settings.textureType = TextureImporterType.Default;
            settings.sRGBTexture = false;
            settings.mipmapEnabled = false;
            settings.wrapMode = TextureWrapMode.Repeat;
            settings.filterMode = FilterMode.Bilinear;
            settings.flipbookColumns = 1;
            settings.flipbookRows = depth;
            importer.SetTextureSettings(settings);
            
            TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings();
            platformSettings.name = "Standalone";
            platformSettings.overridden = true;
            platformSettings.maxTextureSize = 4096;

            switch (m_Settings.outputFormat)
            {
                case TextureOutputFormat.RGBA32:
                    platformSettings.format = TextureImporterFormat.RGBA32;
                    platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    break;
                
                case TextureOutputFormat.R8:
                    platformSettings.format = TextureImporterFormat.R8;
                    platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    break;
                
                case TextureOutputFormat.R16:
                    platformSettings.format = TextureImporterFormat.R16;
                    platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    break;
                
                case TextureOutputFormat.RFloat:
                    platformSettings.format = TextureImporterFormat.RFloat;
                    platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    break;
                
                case TextureOutputFormat.BC4:
                    platformSettings.format = TextureImporterFormat.BC4;
                    platformSettings.textureCompression = TextureImporterCompression.Compressed;
                    break;
                
                case TextureOutputFormat.BC5:
                    platformSettings.format = TextureImporterFormat.BC5;
                    platformSettings.textureCompression = TextureImporterCompression.Compressed;
                    break;
                
                case TextureOutputFormat.BC7:
                    platformSettings.format = TextureImporterFormat.BC7;
                    platformSettings.textureCompression = TextureImporterCompression.Compressed;
                    break;
            }
            
            importer.SetPlatformTextureSettings(platformSettings);
            
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Failed to configure texture importer", "OK");
        }
    }

    private bool IsSingleChannelFormat(TextureOutputFormat format)
    {
        return format == TextureOutputFormat.R8 || 
               format == TextureOutputFormat.R16 || 
               format == TextureOutputFormat.RFloat || 
               format == TextureOutputFormat.BC4;
    }
}
