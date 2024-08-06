using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;


public static class TestEditor
{

    [MenuItem("GameObject/Log")]
    static void Log()
    {
        var obj = Selection.activeGameObject;
        if (obj == null) return;

        Debug.Log(obj.transform.forward);

        var child = obj.transform.GetChild(0);
        Debug.Log((child.position - obj.transform.position).normalized);
    }
}
