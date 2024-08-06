using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;


namespace ForwardRender
{

    public partial class ForwardPipeline
    {
        private partial void DrawGizmos(ScriptableRenderContext context, Camera camera);
        private partial void DrawUnsupportedShaders(ScriptableRenderContext context, Camera camera);

        private partial void PrepareForSceneWindow(ScriptableRenderContext context, Camera camera); // Scene 视图绘制

        private partial void PrepareBuffer(ScriptableRenderContext context, Camera camera); // 多相机缓冲区显示


#if UNITY_EDITOR
        private string SampleName { get; set; }


        private static ShaderTagId[] m_LegacyShaderIDs = new ShaderTagId[] {  // 原版 LightMode
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
        };

        private static Material m_ErrorMat;


        /// <summary>
        ///  原版Shader
        /// </summary>
        private partial void DrawUnsupportedShaders(ScriptableRenderContext context, Camera camera)
        {
            if (m_ErrorMat == null)
            {
                m_ErrorMat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            }

            var drawSettings = new DrawingSettings(m_LegacyShaderIDs[0], new SortingSettings(camera)) { overrideMaterial = m_ErrorMat };
            for (int i = 1; i < m_LegacyShaderIDs.Length; i++)
            {
                drawSettings.SetShaderPassName(i, m_LegacyShaderIDs[i]);
            }
            var filterSettings = FilteringSettings.defaultValue;
            context.DrawRenderers(m_cullingResults, ref drawSettings, ref filterSettings);
        }


        private partial void DrawGizmos(ScriptableRenderContext context, Camera camera)
        {
            if (Handles.ShouldRenderGizmos())
            {
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
            }
        }

        /// <summary>
        /// Scene视图 绘制
        /// </summary>
        private partial void PrepareForSceneWindow(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera); // Scene视图显示UI
            }
        }


        /// <summary>
        ///  兼容多相机 缓冲区显示
        /// </summary>
        private partial void PrepareBuffer(ScriptableRenderContext context, Camera camera)
        {
            Profiler.BeginSample("Editor Only");
            m_cmd.name = SampleName = camera.name;
            Profiler.EndSample();
        }
#else
        private const string SampleName = m_CmdBufferName;
#endif
    }
}