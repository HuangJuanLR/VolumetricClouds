using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace

namespace VolumetricClouds
{
    [ExecuteAlways]
    public class VolumetricCloudsManager : MonoBehaviour
    {
        private static VolumetricCloudsManager s_Instance;

        public LayerMask culledLayers;
        
        private List<Renderer> m_CulledRenderers = new List<Renderer>();
        private bool m_IsCulling = false;

        public static VolumetricCloudsManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    Debug.LogError("VolumetricClouds Manager is not presented");
                }

                return s_Instance;
            }
        }

        private void OnEnable()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            DisableCulling();
        }

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (m_CulledRenderers.Count == 0)
            {
                CacheRenderers();
            }
            
            // EnabledCulling();
        }

        private void CacheRenderers()
        {
            m_CulledRenderers.Clear();

            var renderers = FindObjectsOfType<Renderer>();

            foreach (var r in renderers)
            {
                if (((1 << r.gameObject.layer) & culledLayers.value) != 0)
                {
                    m_CulledRenderers.Add(r);
                }
            }
        }

        public void EnabledCulling()
        {
            if (m_IsCulling) return;
            m_IsCulling = true;

            foreach (var r in m_CulledRenderers)
            {
                if (r != null)
                {
                    // r.renderingLayerMask &= 0b00;
                    r.enabled = false;
                }
            }
        }

        public void DisableCulling()
        {
            if (!m_IsCulling) return;
            m_IsCulling = false;
            
            foreach (var r in m_CulledRenderers)
            {
                if (r != null)
                {
                    // r.renderingLayerMask |= 0b01;
                    r.enabled = true;
                }
            }
        }
    }
}
