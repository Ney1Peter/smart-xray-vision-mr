using UnityEngine;
using Unity.Mathematics;
using GaussianSplatting.Runtime;
using System;
using System.Reflection;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatViewDrag : MonoBehaviour
{
    public float dragRadius = 0.1f; // 拖拽半径
    public bool accumulateDeformation = false; // 是否累计形变
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer viewBuffer, posBuffer;
    private float3[] positions, originalPositions;
    private int splatCount;

    private bool isDragging = false;
    private Vector2 prevMouse;
    private Ray dragRay;

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        posBuffer = renderer.GetGpuPosData();
        viewBuffer = GetViewBuffer();

        if (posBuffer == null || viewBuffer == null)
        {
            Debug.LogError("无法获取 GpuPosData 或 GpuViewData！");
            enabled = false;
            return;
        }

        splatCount = renderer.splatCount;
        positions = new float3[splatCount];
        originalPositions = new float3[splatCount];
        posBuffer.GetData(positions);
        Array.Copy(positions, originalPositions, splatCount);
    }

    void Update()
    {
        // 空格强制恢复形变
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Array.Copy(originalPositions, positions, splatCount);
            posBuffer.SetData(positions);
        }

        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            prevMouse = Input.mousePosition;
            dragRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            if (accumulateDeformation)
                Array.Copy(positions, originalPositions, splatCount);
            else
                Array.Copy(originalPositions, positions, splatCount);
        }

        if (isDragging)
        {
            Vector2 curMouse = Input.mousePosition;
            Vector2 delta = curMouse - prevMouse;
            float2 deltaNorm = new float2(delta.x / Screen.width, delta.y / Screen.height);
            float2 mouseNorm = new float2(curMouse.x / Screen.width, curMouse.y / Screen.height);
            prevMouse = curMouse;

            int stride = viewBuffer.stride; // usually 40
            int count = viewBuffer.count;
            int floatsPerEntry = stride / sizeof(float);
            float[] raw = new float[count * floatsPerEntry];
            viewBuffer.GetData(raw);

            


            Ray newRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 offset = newRay.origin - dragRay.origin;
            float3 offsetLocal = (float3)transform.InverseTransformVector(offset);
            int affectedCount = 0;

            for (int i = 0; i < splatCount; i++)
            {
                float3 worldPos = transform.TransformPoint(originalPositions[i]);
                float3 screenPoint = Camera.main.WorldToScreenPoint(worldPos);
                float2 screenPos2D = new float2(screenPoint.x, screenPoint.y);
                float2 mousePos = new float2(Input.mousePosition.x, Input.mousePosition.y);

                float dist = math.distance(screenPos2D, mousePos);

                if (dist < dragRadius * Screen.height)
                {
                    float strength = 1.0f - dist / (dragRadius * Screen.height);

                    positions[i] = originalPositions[i] + offsetLocal * strength;
                    affectedCount++;
                }
                else
                {
                    positions[i] = originalPositions[i];
                }
            }
/*            Debug.Log($"[Update] 本帧被拖动的 GS 点数: {affectedCount}");
            posBuffer.SetData(positions);*/

            /*            for (int i = 0; i < count; i++)
                        {
                            int offset = i * floatsPerEntry;
                            float w = raw[offset + 3];
                            if (Mathf.Abs(w) < 1e-5f) continue;

                            float2 screenPos = new float2(raw[offset + 0], raw[offset + 1]) / w;
                            float dist = math.distance(screenPos, mouseNorm);

                            if (dist < dragRadius)
                            {
                                float3 axis1 = new float3(raw[offset + 4], raw[offset + 5], raw[offset + 6]);
                                float3 axis2 = new float3(raw[offset + 7], raw[offset + 8], raw[offset + 9]);

                                // 这里添加非刚体形变，拖动强度根据鼠标位置的距离决定
                                float3 drag3D = axis1 * deltaNorm.x + axis2 * deltaNorm.y;
                                // 非刚体形变的核心部分，使用鼠标偏移影响形变
                                int idx = i; // 替换原来的 raw[offset + 13]
                                positions[idx] = originalPositions[idx] + drag3D * (1.0f - dist / dragRadius);  // 形变强度与距离成反比
                                affectedCount++;
                            }
                        }*/

            Debug.Log($"[ViewBuffer 拖拽] 受影响点数: {affectedCount}");
            posBuffer.SetData(positions);
        }
    }

    GraphicsBuffer GetViewBuffer()
    {
        var field = typeof(GaussianSplatRenderer).GetField("m_GpuView", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(renderer) as GraphicsBuffer;
    }

    void OnDestroy()
    {
        positions = null;
        originalPositions = null;
    }
}
