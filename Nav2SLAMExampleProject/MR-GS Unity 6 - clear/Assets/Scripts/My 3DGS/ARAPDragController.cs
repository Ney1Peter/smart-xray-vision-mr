using UnityEngine;
using Unity.Mathematics;
using GaussianSplatting.Runtime;
using System;
using System.Runtime.InteropServices;

public class ARAPDragController : MonoBehaviour
{
    [Header("拖拽参数")]
    public float dragKNN = 20;
    public bool accumulate = true;

    private GaussianSplatRenderer renderer;
    private GraphicsBuffer viewBuffer, posBuffer;
    private int splatCount;
    private float3[] posData, originalPosData;

    private bool isDragging = false;
    private Vector2 prevMouse;
    private Vector2 dragDelta;

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetViewBuffer(int count, float[] view2D, float[] axis1, float[] axis2, int[] indices);

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetPointCloud(int count, float[] pos3D);

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetDragInput(float[] mouse2D, float[] delta2D, int knn);

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern int RunARAP(int maxCount, int[] outIndices, float[] outDelta3D);

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ClearARAP();

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        viewBuffer = renderer.GetGpuViewData(); // 反射接口
        posBuffer = renderer.GetGpuPosData();
        splatCount = renderer.splatCount;

        posData = new float3[splatCount];
        originalPosData = new float3[splatCount];
        posBuffer.GetData(posData);
        Array.Copy(posData, originalPosData, splatCount);

        UploadViewBuffer(); // 初始化
        UploadPointCloud();
    }

    void UploadViewBuffer()
    {
        if (viewBuffer == null) return;

        int stride = viewBuffer.stride; // 应为 40
        int count = viewBuffer.count;
        int floatsPerEntry = stride / sizeof(float);

        float[] raw = new float[count * floatsPerEntry];
        viewBuffer.GetData(raw);

        float[] view2D = new float[count * 2];
        float[] axis1 = new float[count * 3];
        float[] axis2 = new float[count * 3];
        int[] indices = new int[count];

        int validCount = 0;
        for (int i = 0; i < count; i++)
        {
            int offset = i * floatsPerEntry;
            float w = raw[offset + 3];
            if (Mathf.Abs(w) < 1e-5f) continue;

            view2D[validCount * 2 + 0] = raw[offset + 0] / w;
            view2D[validCount * 2 + 1] = raw[offset + 1] / w;

            axis1[validCount * 3 + 0] = raw[offset + 4];
            axis1[validCount * 3 + 1] = raw[offset + 5];
            axis1[validCount * 3 + 2] = raw[offset + 6];

            axis2[validCount * 3 + 0] = raw[offset + 7];
            axis2[validCount * 3 + 1] = raw[offset + 8];
            axis2[validCount * 3 + 2] = raw[offset + 9];

            indices[validCount] = i;

            validCount++;
        }

        Array.Resize(ref view2D, validCount * 2);
        Array.Resize(ref axis1, validCount * 3);
        Array.Resize(ref axis2, validCount * 3);
        Array.Resize(ref indices, validCount);

        SetViewBuffer(validCount, view2D, axis1, axis2, indices);
    }

    void UploadPointCloud()
    {
        float[] flat = new float[splatCount * 3];
        for (int i = 0; i < splatCount; i++)
        {
            flat[i * 3 + 0] = posData[i].x;
            flat[i * 3 + 1] = posData[i].y;
            flat[i * 3 + 2] = posData[i].z;
        }
        SetPointCloud(splatCount, flat);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Array.Copy(originalPosData, posData, splatCount);
            posBuffer.SetData(posData);
        }

        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            prevMouse = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            if (accumulate)
                Array.Copy(posData, originalPosData, splatCount);
            else
                Array.Copy(originalPosData, posData, splatCount);
        }

        if (isDragging && (Vector2)Input.mousePosition != prevMouse)
        {
            dragDelta = (Vector2)Input.mousePosition - prevMouse;
            Vector2 normDelta = new Vector2(
                dragDelta.x / Screen.width,
                dragDelta.y / Screen.height);

            float[] mouse = { Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height };
            float[] delta = { normDelta.x, normDelta.y };
            SetDragInput(mouse, delta, (int)dragKNN);

            int[] indices = new int[splatCount];
            float[] deltas = new float[splatCount * 3];

            int changed = RunARAP(splatCount, indices, deltas);
            Debug.Log($"RunARAP changed: {changed}");

            for (int i = 0; i < changed; i++)
            {
                int idx = indices[i];
                Debug.Log($"Index {idx} delta ({deltas[i * 3]}, {deltas[i * 3 + 1]}, {deltas[i * 3 + 2]})");
                posData[idx] += new float3(deltas[i * 3 + 0], deltas[i * 3 + 1], deltas[i * 3 + 2]);
            }

            posBuffer.SetData(posData);
            prevMouse = Input.mousePosition;
        }
    }

    void OnDestroy()
    {
        ClearARAP();
        posData = null;
        originalPosData = null;
    }
}
