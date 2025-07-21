using UnityEngine;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

/// <summary>
/// 把多段 “线段 + 半径” 上传到 Shader：
/// • 开始点缓冲 _ClipStart（xyz = A，w = r）  
/// • 结束点缓冲 _ClipEnd   （xyz = B，w = r）
/// </summary>
[RequireComponent(typeof(GaussianSplatRenderer))]
public class PointCloudPathClipper : MonoBehaviour
{
    [Header("最多可同时裁剪的段数")]
    [SerializeField] int maxSegments = 64;

    [Header("默认裁剪半径 (m) - 兼容旧 AddSegment")]
    [SerializeField] float defaultRadius = 0.2f;

    // ───────── internal ─────────
    readonly List<Vector4> starts = new();
    readonly List<Vector4> ends = new();

    ComputeBuffer bufStart, bufEnd;

    void Awake()
    {
        bufStart = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        bufEnd = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        Upload();                       // 让 _ClipCount 初始为 0
    }

    void OnDestroy()
    {
        bufStart?.Release();
        bufEnd?.Release();
    }

    /// <summary>
    /// 向裁剪列表里加入 “A→B，半径 r”。
    /// 若已达上限，则忽略。
    /// </summary>
    public void AddSegment(Vector3 A, Vector3 B, float r)
    {
        if (starts.Count >= maxSegments) return;

        starts.Add(new Vector4(A.x, A.y, A.z, r));
        ends.Add(new Vector4(B.x, B.y, B.z, r));
        Upload();
    }

    /// <summary>
    /// 兼容旧写法（自动使用 defaultRadius）
    /// </summary>
    public void AddSegment(Vector3 A, Vector3 B)
    {
        AddSegment(A, B, defaultRadius);
    }

    /// <summary>清空所有裁剪段</summary>
    public void ClearAll()
    {
        starts.Clear();
        ends.Clear();
        Upload();
    }

    // 把 List ➜ ComputeBuffer ➜ Shader (全局 uniform)
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
