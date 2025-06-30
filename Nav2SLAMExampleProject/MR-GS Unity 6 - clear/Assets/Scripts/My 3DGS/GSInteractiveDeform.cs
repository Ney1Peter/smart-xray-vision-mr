using UnityEngine;
using Unity.Mathematics;
using System;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatDragDeform : MonoBehaviour
{
    public bool accumulateDeformation = false; // 是否累计形变
    public float dragRadius = 0.2f;
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;
    private int splatCount;

    private bool isDragging = false;
    private Ray dragRay;

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        posBuffer = renderer.GetGpuPosData();

        if (posBuffer == null)
        {
            Debug.LogError("未能获取 m_GpuPosData。");
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

        // 鼠标按下开始拖动
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            dragRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        }

        // 鼠标松开结束拖动
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;

            if (!accumulateDeformation)
            {
                // 不累计形变，则还原位置
                Array.Copy(originalPositions, positions, splatCount);
                posBuffer.SetData(positions);
            }
            else
            {
                // 累计形变，则更新原始位置为当前形变位置
                Array.Copy(positions, originalPositions, splatCount);
            }
        }

        // 拖动中，实时更新部分点的位置
        if (isDragging)
        {
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
            Debug.Log($"[Update] 本帧被拖动的 GS 点数: {affectedCount}");
            posBuffer.SetData(positions);
        }
    }

    void OnDestroy()
    {
        positions = null;
        originalPositions = null;
    }
}
