using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace ForwardRender
{

    [CreateAssetMenu(menuName = "RenderPipeline/Forward")]
    public class ForwardPipelineAsset : RenderPipelineAsset
    {

        public ComputeShader m_clusterCs;
        public ComputeShader m_clusterLightCullCs;
        public ForwardPipeline Pipeline { get; private set; }


        protected override RenderPipeline CreatePipeline()
        {
            Pipeline = new ForwardPipeline();

            Pipeline.SetClusterCS(m_clusterCs, m_clusterLightCullCs);

            return Pipeline;
        }
    }
}