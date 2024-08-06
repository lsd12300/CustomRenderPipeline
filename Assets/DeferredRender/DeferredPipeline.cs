using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace ForwardRender
{

    /// <summary>
    ///  自定义延迟渲染
    ///     GBuffer分配:
    ///         ARGB32          Albedo      G0
    ///         ARGB2101010     Normal      G1
    ///         ARGB64          RG--MotionVector, BA--Roughness/Metallic    G2
    ///         ARGBFloat       RGB--Emission, A--Occlusion     G3
    ///         Depth  RT
    /// </summary>
    public partial class DeferredPipeline : RenderPipeline
    {

        private RenderTexture m_depthRT; // 深度缓冲
        private RenderTexture[] m_gBuffers = new RenderTexture[4]; // GBuffers
        private RenderTargetIdentifier[] m_gBufferIDs = new RenderTargetIdentifier[4];


        public DeferredPipeline()
        {
            var w = Screen.width;
            var h = Screen.height;
            m_depthRT = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            m_gBuffers[0] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            m_gBuffers[1] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
            m_gBuffers[2] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear);
            m_gBuffers[3] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

            for (int i = 0; i < m_gBufferIDs.Length; i++)
            {
                m_gBufferIDs[i] = m_gBuffers[i];
            }
        }


        #region 渲染流程
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                RenderCamera(context, camera);
            }
        }


        /// <summary>
        ///  渲染相机内容
        /// </summary>
        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            PrepareBuffer(context, camera);
            PrepareForSceneWindow(context, camera);
            if (!Cull(context, camera))
            {
                return;
            }

            SetUp(context, camera);

            // 绘制到 GBuffer
            DrawVisibleGeometry(context, camera);

            // 使用 GBuffer 绘制最终画面
            LightPass(context, camera);


            DrawUnsupportedShaders(context, camera);
            DrawGizmos(context, camera);

            Submit(context, camera);
        }

        #endregion


        #region 绘制指令

        // Context 在提交指令前不会实际去渲染, 所以必须通过单独的CommandBuffer间接执行
        private const string m_CmdBufferName = "Render Camera";
        private CommandBuffer m_cmd = new CommandBuffer() { name = m_CmdBufferName };

        /// <summary>
        ///  设置Shader中全局属性
        /// </summary>
        private void SetUp(ScriptableRenderContext context, Camera camera)
        {
            context.SetupCameraProperties(camera); // 相机相关的Shader全局属性.   视图矩阵/投影矩阵/远近裁剪平面

            SetUpLights(); // 灯光全局属性

            // MRT
            m_cmd.SetRenderTarget(m_gBufferIDs, m_depthRT);

            // 设置全局可访问属性
            Shader.SetGlobalTexture("_GDepth", m_depthRT);
            for (int i = 0; i < m_gBuffers.Length; i++)
            {
                Shader.SetGlobalTexture($"_GBuffer{i}", m_gBuffers[i]);
            }


            // 清除渲染目标
            var flags = camera.clearFlags;
            m_cmd.ClearRenderTarget(
                flags <= CameraClearFlags.Depth,
                flags == CameraClearFlags.Color,
                flags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear // 要清除不透明颜色, 只能使用相机的背景色
            );


            m_cmd.BeginSample(SampleName);
            ExecuteBuffer(context, camera);
        }

        private void SetUpLights()
        {
            var lights = m_cullingResults.visibleLights;

            // 获取主光源
            var mainLightIndex = -1; 
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].lightType == LightType.Directional)
                {
                    mainLightIndex = i;
                    break;
                }
            }

            if (mainLightIndex < 0) return;

            Shader.SetGlobalVector("_MainLightPosition", -lights[mainLightIndex].light.transform.forward);
            Shader.SetGlobalColor("_MainLightColor", lights[mainLightIndex].finalColor);
        }

        /// <summary>
        ///  提交绘制指令
        /// </summary>
        private void Submit(ScriptableRenderContext context, Camera camera)
        {
            m_cmd.EndSample(SampleName);
            ExecuteBuffer(context, camera);
            context.Submit();
        }

        private void ExecuteBuffer(ScriptableRenderContext context, Camera camera)
        {
            context.ExecuteCommandBuffer(m_cmd);
            m_cmd.Clear();
        }
        #endregion


        #region 绘制
        private CullingResults m_cullingResults; // 视野裁切数据
        //private static ShaderTagId m_DrawShaderID = new ShaderTagId("SRPDefaultUnlit"); // 需要绘制的 LightMode
        private static ShaderTagId m_DrawShaderID = new ShaderTagId("GBuffer"); // 需要绘制的 LightMode
        private static ShaderTagId[] m_DrawShaderIDs = new ShaderTagId[] {  // 需要绘制的 LightMode
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("GBuffer"),
        };

        private Material m_lightPassMat;


        /// <summary>
        ///  绘制几何体
        /// </summary>
        private void DrawVisibleGeometry(ScriptableRenderContext context, Camera camera)
        {
            // 1. 绘制不透明物体
            var sortSettings = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
            //var drawSettings = new DrawingSettings(m_DrawShaderID, sortSettings);
            var drawSettings = new DrawingSettings(m_DrawShaderIDs[0], sortSettings);
            for (int i = 1; i < m_DrawShaderIDs.Length; i++)
            {
                drawSettings.SetShaderPassName(i, m_DrawShaderIDs[i]);
            }
            var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(m_cullingResults, ref drawSettings, ref filterSettings);


            // 2. 绘制天空盒
            context.DrawSkybox(camera);


            // 3. 绘制透明物体
            sortSettings.criteria = SortingCriteria.CommonTransparent;
            drawSettings.sortingSettings = sortSettings;
            filterSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(m_cullingResults, ref drawSettings, ref filterSettings);
        }

        /// <summary>
        ///  剔除
        /// </summary>
        /// <returns></returns>
        private bool Cull(ScriptableRenderContext context, Camera camera)
        {
            if (camera.TryGetCullingParameters(out var p))
            {
                m_cullingResults = context.Cull(ref p);
                return true;
            }
            return false;
        }


        /// <summary>
        ///  使用各个 GBuffer着色
        /// </summary>
        private void LightPass(ScriptableRenderContext context, Camera camera)
        {
            if (m_lightPassMat == null)
            {
                m_lightPassMat = CoreUtils.CreateEngineMaterial("Custom/Deferred/DeferredLight");
            }

            m_cmd.Blit(m_gBufferIDs[0], BuiltinRenderTextureType.CameraTarget, m_lightPassMat);
            ExecuteBuffer(context, camera);
        }
        #endregion

    }
}