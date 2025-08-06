using UnityEngine;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

/// <summary>
/// 将多条“线段 + 半径”上传到 Shader：
/// ⮩ _ClipStart / _ClipEnd；w 分量存半径。  
/// </summary>
[RequireComponent(typeof(GaussianSplatRenderer))]
public class PointCloudPathClipperFrame : MonoBehaviour
{
    [Header("最多同时裁剪段数")]
    [SerializeField] int maxSegments = 64;

    // internal
    readonly List<Vector4> starts = new();
    readonly List<Vector4> ends = new();
    ComputeBuffer bufStart, bufEnd;

    void Awake()
    {
        bufStart = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        bufEnd = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        Upload();                            // 初始 _ClipCount = 0
    }
    void OnDestroy()
    {
        bufStart?.Release();
        bufEnd?.Release();
    }

    /// <summary>真正使用的接口：A → B，半径 r。</summary>
    public void AddSegment(Vector3 A, Vector3 B, float r)
    {
        if (starts.Count >= maxSegments) return;

        starts.Add(new Vector4(A.x, A.y, A.z, r));
        ends.Add(new Vector4(B.x, B.y, B.z, r));
        Upload();
    }

    /// <summary>
    /// 兼容旧调用（未传半径）：自动取 <see cref="GazeHoleUpdater.CutRadius"/>。
    /// </summary>
    public void AddSegment(Vector3 A, Vector3 B)
        => AddSegment(A, B, GazeHoleUpdaterFrame.CutRadius);

    /// <summary>清空所有裁剪段</summary>
    public void ClearAll()
    {
        starts.Clear(); ends.Clear();
        Upload();
    }

    void Upload()
    {
        int n = starts.Count;
        var dummy = new Vector4[maxSegments];

        bufStart.SetData(n == 0 ? dummy : starts.ToArray());
        bufEnd.SetData(n == 0 ? dummy : ends.ToArray());

        Shader.SetGlobalBuffer("_ClipStart", bufStart);
        Shader.SetGlobalBuffer("_ClipEnd", bufEnd);
        Shader.SetGlobalInt("_ClipCount", n);
    }
}
