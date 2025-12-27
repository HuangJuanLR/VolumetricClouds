using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

namespace VolumetricClouds
{
    public class TransparentAboveCloudsPass : ScriptableRenderPass
    {
        private FilteringSettings m_FilteringSettings;
        private VolumetricClouds.VolumetricCloudsSetting m_Setting;
        
        private ScriptableCullingParameters m_CullingParams; 

        private static ShaderTagId s_ShaderTagId = new ShaderTagId("UniversalForward");

        public TransparentAboveCloudsPass(VolumetricClouds.VolumetricCloudsSetting setting)
        {
            m_Setting = setting;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.transparent, m_Setting.transparentLayerMask);

            m_CullingParams = new ScriptableCullingParameters();
            var cam = renderingData.cameraData.camera;
            cam.TryGetCullingParameters(out m_CullingParams);
            m_CullingParams.cullingMask = (uint)m_Setting.transparentLayerMask.value;
        }

        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            var camColorRT = renderingData.cameraData.renderer.cameraColorTargetHandle;
            
            cmd.SetRenderTarget(camColorRT);
            cmd.ClearRenderTarget(false, false, Color.black);
            
            var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
            var drawingSettings = CreateDrawingSettings(s_ShaderTagId, ref renderingData, sortFlags);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var customCullingResults = context.Cull(ref m_CullingParams);
            
            context.DrawRenderers(customCullingResults, ref drawingSettings, ref m_FilteringSettings);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
    }
    
}

