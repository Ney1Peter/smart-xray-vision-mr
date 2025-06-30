using UnityEngine;
using Unity.Mathematics;
using System;
using System.Reflection;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatColorPicker : MonoBehaviour
{
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private GraphicsBuffer viewBuffer;
    private float3[] positions;
    private int splatCount;

    public GameObject markerPrefab;  // 可视化 marker prefab
    private GameObject currentMarker;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SplatViewData
    {
        public float4 pos;
        public float2 axis1;
        public float2 axis2;
        public uint colorX;
        public uint colorY;
    }

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        posBuffer = renderer.GetGpuPosData();

        var field = typeof(GaussianSplatRenderer).GetField("m_GpuView", BindingFlags.NonPublic | BindingFlags.Instance);
        viewBuffer = field?.GetValue(renderer) as GraphicsBuffer;

        if (posBuffer == null || viewBuffer == null)
        {
            Debug.LogError("未能获取 GPU 数据。");
            enabled = false;
            return;
        }

        splatCount = renderer.splatCount;
        positions = new float3[splatCount];
        posBuffer.GetData(positions);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            int pickedIndex = FindNearestGSIndex(mousePos);
            if (pickedIndex >= 0)
            {
                Vector3 worldPos = transform.TransformPoint(positions[pickedIndex]);
                Debug.Log($"选中的点索引: {pickedIndex}, 世界坐标: {worldPos}");

                if (markerPrefab != null)
                {
                    if (currentMarker != null) Destroy(currentMarker);
                    currentMarker = Instantiate(markerPrefab, worldPos, Quaternion.identity);
                }
            }
            else
            {
                Debug.Log("未选中任何点。");
            }
        }
    }

    int FindNearestGSIndex(Vector2 screenPos)
    {
        SplatViewData[] viewData = new SplatViewData[splatCount];
        viewBuffer.GetData(viewData);

        int closest = -1;
        float minDist = float.MaxValue;

        for (int i = 0; i < splatCount; ++i)
        {
            float4 clip = viewData[i].pos;
            if (clip.w <= 0) continue;

            float3 ndc = new float3(clip.x, clip.y, clip.z) / clip.w;
            Vector2 proj = new Vector2(
                (ndc.x * 0.5f + 0.5f) * Screen.width,
                (1.0f - (ndc.y * 0.5f + 0.5f)) * Screen.height  // fix y方向
            );

            float dist = Vector2.Distance(screenPos, proj);
            if (dist < 10f && dist < minDist) // picking radius: 10px
            {
                closest = i;
                minDist = dist;
            }
        }

        return closest;
    }

    void OnDestroy()
    {
        positions = null;
    }
}
