using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace ForwardRender
{

    /// <summary>
    ///  自定义前向渲染
    /// </summary>
    public partial class ForwardPipeline : RenderPipeline
    {

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

            DrawVisibleGeometry(context, camera);


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

            // 清除渲染目标
            var flags = camera.clearFlags;
            m_cmd.ClearRenderTarget(
                flags <= CameraClearFlags.Depth,
                flags == CameraClearFlags.Color,
                flags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear // 要清除不透明颜色, 只能使用相机的背景色
            );


            //m_cmd.BeginSample(SampleName);
            ExecuteBuffer(context, camera);
        }

        /// <summary>
        ///  提交绘制指令
        /// </summary>
        private void Submit(ScriptableRenderContext context, Camera camera)
        {
            //m_cmd.EndSample(SampleName);
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
        //private static ShaderTagId m_DrawShaderID = new ShaderTagId("UniversalForward"); // 需要绘制的 LightMode
        private static ShaderTagId[] m_DrawShaderIDs = new ShaderTagId[] {  // 需要绘制的 LightMode
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };


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
        #endregion

    }
}