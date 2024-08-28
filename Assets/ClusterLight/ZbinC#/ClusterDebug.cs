using ForwardRender;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


[ExecuteInEditMode]
public class ClusterDebug : MonoBehaviour
{

    public Transform m_shpere;
    public bool m_showLine = true;

    /*
    private Camera m_cam;

    private ClusterLight m_cluser;
    public ClusterLight Cluser
    {
        get {
            if (m_cluser == null)
            {
                m_cluser = (GraphicsSettings.currentRenderPipeline as ForwardPipelineAsset).Pipeline?.ClusterLight;
            }
            return m_cluser;
        }
    }


    private void Start()
    {
        m_cam = Camera.main;
    }


    private void OnDrawGizmos()
    {
        if (!m_showLine) return;
        if (Cluser == null) return;

        var frustums = Cluser.m_tilesFrustum;
        foreach (var frustum in frustums)
        {
            DrawFrustum(frustum);
        }
    }

    private void DrawFrustum(ClusterLight.TileFrustum frustum)
    {
        if (frustum == null)
        {
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            Gizmos.DrawLine(frustum.m_points[i], frustum.m_points[next]); // 前平面
            Gizmos.DrawLine(frustum.m_points[i+4], frustum.m_points[next+4]); // 后平面
            Gizmos.DrawLine(frustum.m_points[i], frustum.m_points[i+4]); // 棱边
        }
    }
    */
}
