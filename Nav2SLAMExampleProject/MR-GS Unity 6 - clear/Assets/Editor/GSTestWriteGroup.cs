// Assets/Editor/GSTestWriteGroup.cs
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using GaussianSplatting.Runtime;   // ← 换成你的命名空间

public static class GSTestWriteGroup
{
    [MenuItem("Gaussian Test/Make FIRST 5000 splats Group 1")]
    private static void MakeGroup()
    {
        // 找到场景中的 Renderer
        GaussianSplatRenderer gs = Object.FindObjectOfType<GaussianSplatRenderer>();
        if (gs == null) { Debug.LogError("找不到 GaussianSplatRenderer"); return; }

        /* 通过反射拿 m_GroupIdBuffer */
        GraphicsBuffer buf = typeof(GaussianSplatRenderer)
            .GetField("m_GroupIdBuffer", BindingFlags.NonPublic|BindingFlags.Instance)
            ?.GetValue(gs) as GraphicsBuffer;
        if (buf == null) { Debug.LogError("m_GroupIdBuffer 未初始化"); return; }

        int n = Mathf.Min(5000, buf.count);
        uint[] gids = new uint[n];
        for (int i = 0; i < n; i++) gids[i] = 1;      // 组 1
        buf.SetData(gids, 0, 0, n);

        /* 取 & 修改 m_GroupAlpha */
        var alphaField = typeof(GaussianSplatRenderer)
            .GetField("m_GroupAlpha", BindingFlags.NonPublic|BindingFlags.Instance);
        var alphaList  = alphaField?.GetValue(gs) as List<float>;
        while (alphaList.Count < 2) alphaList.Add(1f);
        alphaList[1] = 0f;                            // 组 1 完全透明

        /* 调用 UploadGroupAlpha 推送到材质 */
        typeof(GaussianSplatRenderer)
            .GetMethod("UploadGroupAlpha", BindingFlags.NonPublic|BindingFlags.Instance)
            ?.Invoke(gs, null);

        Debug.Log($"已把前 {n} 个 splat 设为透明组 1");
    }
}
