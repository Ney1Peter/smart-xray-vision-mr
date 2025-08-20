using UnityEngine;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class PointCloudPathClipperRect : MonoBehaviour
{
    [Header("Maximum Number of Simultaneous Clipping Boxes (OBB)")]
    [SerializeField] int maxBoxes = 32;

    // Clipping Boxes (OBB)
    readonly List<Vector4> boxCenterHalf = new(); // xyz = center, w = halfDepth
    readonly List<Vector4> boxAxisR = new();      // xyz = axisR
    readonly List<Vector4> boxAxisU = new();      // xyz = axisU
    readonly List<Vector4> boxAxisN = new();      // xyz = axisN
    readonly List<Vector4> boxHalfRU = new();     // x = halfWidth, y = halfHeight

    ComputeBuffer bufBoxCenterHalf, bufBoxAxisR, bufBoxAxisU, bufBoxAxisN, bufBoxHalfRU;

    static readonly int ID_BoxCenterHalf = Shader.PropertyToID("_BoxCenterHalf");
    static readonly int ID_BoxAxisR = Shader.PropertyToID("_BoxAxisR");
    static readonly int ID_BoxAxisU = Shader.PropertyToID("_BoxAxisU");
    static readonly int ID_BoxAxisN = Shader.PropertyToID("_BoxAxisN");
    static readonly int ID_BoxHalfRU = Shader.PropertyToID("_BoxHalfRU");
    static readonly int ID_BoxCount = Shader.PropertyToID("_BoxCount");

    void Awake()
    {
        bufBoxCenterHalf = new ComputeBuffer(maxBoxes, sizeof(float) * 4, ComputeBufferType.Structured);
        bufBoxAxisR = new ComputeBuffer(maxBoxes, sizeof(float) * 4, ComputeBufferType.Structured);
        bufBoxAxisU = new ComputeBuffer(maxBoxes, sizeof(float) * 4, ComputeBufferType.Structured);
        bufBoxAxisN = new ComputeBuffer(maxBoxes, sizeof(float) * 4, ComputeBufferType.Structured);
        bufBoxHalfRU = new ComputeBuffer(maxBoxes, sizeof(float) * 4, ComputeBufferType.Structured);
        UploadBoxes(); // Initial clear
    }

    void OnDestroy()
    {
        Shader.SetGlobalInt(ID_BoxCount, 0);
        bufBoxCenterHalf?.Release();
        bufBoxAxisR?.Release();
        bufBoxAxisU?.Release();
        bufBoxAxisN?.Release();
        bufBoxHalfRU?.Release();
    }

    /// <summary>Adds an OBB (center, 3 axes, half size)</summary>
    public void AddBox(Vector3 center, Vector3 axisR, Vector3 axisU, Vector3 axisN,
                       Vector2 halfRU, float halfDepth)
    {
        if (boxCenterHalf.Count >= maxBoxes)
        {
            Debug.LogWarning($"PointCloudPathClipper ▶ Exceeded max number of boxes: {maxBoxes}");
            return;
        }
        boxCenterHalf.Add(new Vector4(center.x, center.y, center.z, halfDepth));
        boxAxisR.Add(new Vector4(axisR.x, axisR.y, axisR.z, 0));
        boxAxisU.Add(new Vector4(axisU.x, axisU.y, axisU.z, 0));
        boxAxisN.Add(new Vector4(axisN.x, axisN.y, axisN.z, 0));
        boxHalfRU.Add(new Vector4(halfRU.x, halfRU.y, 0, 0));
        UploadBoxes();
    }

    /// <summary>Clears all OBBs</summary>
    public void ClearBoxes()
    {
        boxCenterHalf.Clear(); boxAxisR.Clear(); boxAxisU.Clear(); boxAxisN.Clear(); boxHalfRU.Clear();
        UploadBoxes();
    }

    void UploadBoxes()
    {
        int n = boxCenterHalf.Count;
        if (n > 0)
        {
            bufBoxCenterHalf.SetData(boxCenterHalf, 0, 0, n);
            bufBoxAxisR.SetData(boxAxisR, 0, 0, n);
            bufBoxAxisU.SetData(boxAxisU, 0, 0, n);
            bufBoxAxisN.SetData(boxAxisN, 0, 0, n);
            bufBoxHalfRU.SetData(boxHalfRU, 0, 0, n);
        }
        Shader.SetGlobalBuffer(ID_BoxCenterHalf, bufBoxCenterHalf);
        Shader.SetGlobalBuffer(ID_BoxAxisR, bufBoxAxisR);
        Shader.SetGlobalBuffer(ID_BoxAxisU, bufBoxAxisU);
        Shader.SetGlobalBuffer(ID_BoxAxisN, bufBoxAxisN);
        Shader.SetGlobalBuffer(ID_BoxHalfRU, bufBoxHalfRU);
        Shader.SetGlobalInt(ID_BoxCount, n);
    }
}
