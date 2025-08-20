using UnityEngine;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

/// <summary>
/// Uploads multiple "segment + radius" pairs to the Shader:
/// ⮩ _ClipStart / _ClipEnd; w component stores radius.  
/// </summary>
[RequireComponent(typeof(GaussianSplatRenderer))]
public class PointCloudPathClipperBase : MonoBehaviour
{
    [Header("Maximum Number of Segments")]
    [SerializeField] int maxSegments = 64;

    // internal
    readonly List<Vector4> starts = new();
    readonly List<Vector4> ends = new();
    ComputeBuffer bufStart, bufEnd;

    void Awake()
    {
        bufStart = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        bufEnd = new ComputeBuffer(maxSegments, sizeof(float) * 4);
        Upload(); // Initial _ClipCount = 0
    }

    void OnDestroy()
    {
        bufStart?.Release();
        bufEnd?.Release();
    }

    /// <summary>Main interface in use: A → B with radius r.</summary>
    public void AddSegment(Vector3 A, Vector3 B, float r)
    {
        if (starts.Count >= maxSegments) return;

        starts.Add(new Vector4(A.x, A.y, A.z, r));
        ends.Add(new Vector4(B.x, B.y, B.z, r));
        Upload();
    }

    /// <summary>
    /// Backward-compatible call (no radius passed): automatically uses <see cref="GazeHoleUpdater.CutRadius"/>.
    /// </summary>
    public void AddSegment(Vector3 A, Vector3 B)
        => AddSegment(A, B, GazeHoleUpdaterBase.CutRadius);

    /// <summary>Clears all clip segments</summary>
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
