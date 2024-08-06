using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace ForwardRender
{

    [CreateAssetMenu(menuName = "RenderPipeline/Deferred")]
    public class DeferredPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            var rp = new DeferredPipeline();

            return rp;
        }
    }
}