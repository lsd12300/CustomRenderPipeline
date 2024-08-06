using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace ForwardRender
{

    [CreateAssetMenu(menuName = "RenderPipeline/Forward")]
    public class ForwardPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            var rp = new ForwardPipeline();

            return rp;
        }
    }
}