using UnityEngine;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]   // 防止忘挂错对象
public class PointCloudPathClipper : MonoBehaviour
{
    [Header("一次可裁剪的线段上限")]
    [SerializeField] private int maxSegments = 32;

    [Header("裁剪半径  (米)")]
    [SerializeField] private float radius = 0.20f;

    [Header("靠近目标端保留的长度  (米)")]  // ← 只改这里！0.10 = 留 10 cm
    [SerializeField] private float endMargin = 1.0f;

    // ───────── internal ─────────
    readonly List<Vector4> starts = new();
    readonly List<Vector4> ends = new();
    ComputeBuffer bufStart, bufEnd;

    void Awake()
    {
        bufStart = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        bufEnd = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        Upload();                        // 初始 _ClipCount = 0
    }
    void OnDestroy()
    {
        bufStart?.Release();
        bufEnd?.Release();
    }

    // ── 外部调用：新增一段 A→B ──
    public void AddSegment(Vector3 A, Vector3 B)
    {
        if (starts.Count >= maxSegments) return;

        // ① 计算 AB 长度
        Vector3 v = B - A;
        float d = v.magnitude;
        if (d <= endMargin) return;      // 太短则不裁剪

        // ② 把目标点往回缩 endMargin 得到 B'
        Vector3 Bprime = A + v * ((d - endMargin) / d);

        // ③ 写入列表
        starts.Add(new Vector4(A.x, A.y, A.z, radius));
        ends.Add(new Vector4(Bprime.x, Bprime.y, Bprime.z, radius));
        Upload();
    }

    // ── 外部调用：清空全部 ──
    public void ClearAll()
    {
        starts.Clear();
        ends.Clear();
        Upload();
    }

    // ── 把 List ➜ ComputeBuffer ➜ Shader（全局 uniform）──
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
