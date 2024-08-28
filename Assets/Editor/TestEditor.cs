using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using static UnityEditor.ShaderData;


public static class TestEditor
{

    [MenuItem("GameObject/Log")]
    static void Log()
    {
        var obj = Selection.activeGameObject;
        if (obj == null) return;

        var camera = obj.GetComponent<Camera>();
        if (camera == null)
        {
            return;
        }


        var projInv = camera.projectionMatrix.inverse;
        var screenPos = new Vector2(100, 100);

        var posVS = Screen2View(screenPos, new Vector2(camera.pixelWidth, camera.pixelHeight), projInv); // 相机空间
        Debug.Log($"VS1:::{posVS.x}, {posVS.y}, {posVS.z}, {posVS.w}");


        var posCS = camera.projectionMatrix * posVS; // 投影空间
        Debug.Log($"Clip2:::{posCS.x}, {posCS.y}, {posCS.z}, {posCS.w}");
        var posNDC = posCS / posCS.w; // NDC 空间
        Debug.Log($"NDC2:::{posNDC.x}, {posNDC.y}, {posNDC.z}, {posNDC.w}");
        var posSS = (posNDC + Vector4.one) * 0.5f; // 屏幕空间

        Debug.Log($"ScreenUV2:::{posSS.x}, {posSS.y}, {posSS.z}, {posSS.w}");
        Debug.Log($"ScreenPos:::{posSS.x * camera.pixelWidth}, {posSS.y * camera.pixelHeight}");
    }


    public static Vector4 Screen2View(Vector2 screenPos, Vector2 screenSize, Matrix4x4 projInv)
    {
        // 屏幕位置 转 屏幕空间.  屏幕uv取值 [0,1]
        Vector2 screenUV = new Vector2(screenPos.x / screenSize.x, screenPos.y / screenSize.y);
        Debug.Log($"ScreenUV1:::{screenUV.x}, {screenUV.y}");

        // 屏幕空间 转 NDC空间.  NDC空间取值范围 [-1, 1]
        Vector2 ndcPos = screenUV * 2f - Vector2.one;
        Debug.Log($"NDC1:::{ndcPos.x}, {ndcPos.y}");


        Vector4 clipPos = new Vector4(ndcPos.x, ndcPos.y, 0f, 1f);

        return Clip2View(clipPos, projInv);
    }

    public static Vector4 Clip2View(Vector4 clipPos, Matrix4x4 projInv)
    {
        // 相机空间
        var viewPos = projInv * clipPos;

        // 透视投影
        viewPos /= viewPos.w;

        return viewPos;
    }
}
