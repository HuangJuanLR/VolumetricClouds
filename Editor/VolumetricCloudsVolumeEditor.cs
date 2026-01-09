using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using VolumetricClouds;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
// ReSharper disable Unity.PreferAddressByIdToGraphicsParams


namespace VolumetricCLoudsEditor
{
    [CustomEditor(typeof(VolumetricCloudsVolume))]
    public class VolumetricCloudsVolumeEditor : Editor
    {
        private SerializedProperty m_Profile;
        
        private void OnEnable()
        {
            m_Profile = serializedObject.FindProperty("profile");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var volume = (VolumetricCloudsVolume)target;
            
            EditorGUILayout.PropertyField(m_Profile, GUIContent.none);
            
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(volume.profile == null);
            if (GUILayout.Button("Load", GUILayout.Width(45)))
            {
                LoadFromProfile(volume);
            }
            EditorGUI.EndDisabledGroup();
            
            // Apply button - save current settings to profile
            EditorGUI.BeginDisabledGroup(volume.profile == null);
            if (GUILayout.Button("Apply", GUILayout.Width(45)))
            {
                ApplyToProfile(volume);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Create", GUILayout.Width(60)))
            {
                CreateProfileFromCurrentSettings(volume);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            DrawPropertiesExcluding(serializedObject, "m_Script", "profile");

            serializedObject.ApplyModifiedProperties();
        }
        
        private void CreateProfileFromCurrentSettings(VolumetricCloudsVolume volume)
        {
            // Get scene path and name
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Error", "Please save the scene first before creating a profile.", "OK");
                return;
            }

            string scenePath = Path.GetDirectoryName(scene.path);
            string sceneName = Path.GetFileNameWithoutExtension(scene.path);
            string profileFolderPath = Path.Combine(scenePath, sceneName);

            // Create folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(profileFolderPath))
            {
                AssetDatabase.CreateFolder(scenePath, sceneName);
            }

            // Generate unique filename
            string profileName = $"CloudsProfile_{volume.gameObject.name}";
            string profilePath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(profileFolderPath, $"{profileName}.asset")
            );

            // Create the profile asset
            var newProfile = ScriptableObject.CreateInstance<VolumetricCloudsVolumeProfile>();

            // Copy current settings to the new profile
            CopySettingsToProfile(volume, newProfile);

            // Save the asset
            AssetDatabase.CreateAsset(newProfile, profilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Assign to volume
            Undo.RecordObject(volume, "Assign Profile");
            volume.profile = newProfile;
            EditorUtility.SetDirty(volume);

            // Ping the asset in project window
            EditorGUIUtility.PingObject(newProfile);
            
            Debug.Log($"Created profile at: {profilePath}");
        }

        private void CopySettingsToProfile(VolumetricCloudsVolume volume, VolumetricCloudsVolumeProfile profile)
        {
            // Basic
            profile.steps = volume.steps;
            profile.noiseOffset = volume.noiseOffset;
            profile.density = volume.density;
            profile.coverage = volume.coverage;
            profile.edgeFadeDistance = volume.edgeFadeDistance;
            profile.lodNear = volume.lodNear;
            profile.lodFar = volume.lodFar;
            profile.coverageOverHeight = new AnimationCurve(volume.coverageOverHeight.keys);

            // Wind
            profile.windDirection = volume.windDirection;

            // Weather Map
            profile.useWeatherMap = volume.useWeatherMap;
            profile.weatherMap = volume.weatherMap;
            profile.weatherMapWrapMode = volume.weatherMapWrapMode;
            profile.weatherMapTiling = volume.weatherMapTiling;
            profile.weatherMapOffset = volume.weatherMapOffset;
            profile.weatherMapWindMultiplier = volume.weatherMapWindMultiplier;
            profile.anvilBias = volume.anvilBias;

            // Shape
            profile.shapeTiling = volume.shapeTiling;
            profile.shapeOffset = volume.shapeOffset;
            profile.shapeWindMultiplier = volume.shapeWindMultiplier;

            // Detail
            profile.detailTiling = volume.detailTiling;
            profile.detailOffset = volume.detailOffset;
            profile.detailNoiseWeight = volume.detailNoiseWeight;
            profile.turbulence = volume.turbulence;
            profile.detailWindMultiplier = volume.detailWindMultiplier;

            // Lighting
            profile.maxAdditionalLights = volume.maxAdditionalLights;
            profile.lightSteps = volume.lightSteps;
            profile.heightInfluence = volume.heightInfluence;
            profile.lightAbsorptionThroughCloud = volume.lightAbsorptionThroughCloud;
            profile.lightAbsorptionTowardsSun = volume.lightAbsorptionTowardsSun;
            profile.scatterTint = volume.scatterTint;
            profile.darknessThreshold = volume.darknessThreshold;

            // Sun Scattering
            profile.forwardScattering = volume.forwardScattering;
            profile.baseBrightness = volume.baseBrightness;
            profile.phaseFactor = volume.phaseFactor;
            profile.silverIntensity = volume.silverIntensity;
            profile.silverSpread = volume.silverSpread;
        }
        
        private void LoadFromProfile(VolumetricCloudsVolume volume)
        {
            if (volume.profile == null) return;
            
            Undo.RecordObject(volume, "Load Profile Settings");
            
            var profile = volume.profile;
            
            // Basic
            volume.steps = profile.steps;
            volume.noiseOffset = profile.noiseOffset;
            volume.density = profile.density;
            volume.coverage = profile.coverage;
            volume.edgeFadeDistance = profile.edgeFadeDistance;
            volume.lodNear = profile.lodNear;
            volume.lodFar = profile.lodFar;
            volume.coverageOverHeight = new AnimationCurve(profile.coverageOverHeight.keys);

            // Wind
            volume.windDirection = profile.windDirection;

            // Weather Map
            volume.useWeatherMap = profile.useWeatherMap;
            volume.weatherMap = profile.weatherMap;
            volume.weatherMapWrapMode = profile.weatherMapWrapMode;
            volume.weatherMapTiling = profile.weatherMapTiling;
            volume.weatherMapOffset = profile.weatherMapOffset;
            volume.weatherMapWindMultiplier = profile.weatherMapWindMultiplier;
            volume.anvilBias = profile.anvilBias;

            // Shape
            volume.shapeTiling = profile.shapeTiling;
            volume.shapeOffset = profile.shapeOffset;
            volume.shapeWindMultiplier = profile.shapeWindMultiplier;

            // Detail
            volume.detailTiling = profile.detailTiling;
            volume.detailOffset = profile.detailOffset;
            volume.detailNoiseWeight = profile.detailNoiseWeight;
            volume.turbulence = profile.turbulence;
            volume.detailWindMultiplier = profile.detailWindMultiplier;

            // Lighting
            volume.maxAdditionalLights = profile.maxAdditionalLights;
            volume.lightSteps = profile.lightSteps;
            volume.heightInfluence = profile.heightInfluence;
            volume.lightAbsorptionThroughCloud = profile.lightAbsorptionThroughCloud;
            volume.lightAbsorptionTowardsSun = profile.lightAbsorptionTowardsSun;
            volume.scatterTint = profile.scatterTint;
            volume.darknessThreshold = profile.darknessThreshold;

            // Sun Scattering
            volume.forwardScattering = profile.forwardScattering;
            volume.baseBrightness = profile.baseBrightness;
            volume.phaseFactor = profile.phaseFactor;
            volume.silverIntensity = profile.silverIntensity;
            volume.silverSpread = profile.silverSpread;
            
            volume.BakeCoverageHeightCurve();
            EditorUtility.SetDirty(volume);
            volume.IsDirty = true;
            
            Debug.Log("Loaded settings from profile.");
        }

        private void ApplyToProfile(VolumetricCloudsVolume volume)
        {
            if (volume.profile == null) return;
            
            Undo.RecordObject(volume.profile, "Apply Settings to Profile");
            
            CopySettingsToProfile(volume, volume.profile);
            
            EditorUtility.SetDirty(volume.profile);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"Applied settings to profile: {volume.profile.name}");
        }
    }    
}

