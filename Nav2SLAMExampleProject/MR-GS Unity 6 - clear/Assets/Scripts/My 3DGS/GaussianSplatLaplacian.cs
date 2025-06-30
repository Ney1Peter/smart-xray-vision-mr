using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatDragDeformJob : MonoBehaviour
{
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;
    private bool[] affectedMask;
    private int splatCount;

    private bool isDragging = false;
    private Ray dragRay;
    private float dragRadius = 0.1f;
    private int laplacianIterations = 3;

    private Dictionary<int, List<int>> neighborCache = new();
    private const int maxAffectedPoints = 1000;
    private float neighborRadius = 0.05f;

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
        affectedMask = new bool[splatCount];

        posBuffer.GetData(positions);
        Array.Copy(positions, originalPositions, splatCount);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            dragRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            Array.Clear(affectedMask, 0, affectedMask.Length);
            neighborCache.Clear();
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            // 不再还原，而是保留拖动结果用于非刚体匹配
            posBuffer.SetData(positions);
        }

        if (isDragging)
        {
            Ray newRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 offset = newRay.origin - dragRay.origin;

            for (int i = 0; i < splatCount; i++)
            {
                float3 worldPos = transform.TransformPoint(originalPositions[i]);
                Vector3 screenPoint = Camera.main.WorldToScreenPoint(worldPos);
                Vector2 screenPos2D = new Vector2(screenPoint.x, screenPoint.y);
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

                float dist = Vector2.Distance(screenPos2D, mousePos);

                if (dist < dragRadius * Screen.height)
                {
                    float strength = 1.0f - dist / (dragRadius * Screen.height);
                    positions[i] = originalPositions[i] + (float3)(transform.InverseTransformVector(offset) * strength);
                    affectedMask[i] = true;
                }
                else
                {
                    positions[i] = originalPositions[i];
                }
            }

            ApplyLaplacianSmoothing();
            posBuffer.SetData(positions);
        }
    }

    void ApplyLaplacianSmoothing()
    {
        float3[] smoothed = new float3[splatCount];

        for (int iter = 0; iter < laplacianIterations; iter++)
        {
            int affectedCount = 0;

            for (int i = 0; i < splatCount && affectedCount < maxAffectedPoints; i++)
            {
                if (!affectedMask[i]) continue;

                if (!neighborCache.TryGetValue(i, out List<int> neighbors))
                {
                    neighbors = new List<int>();
                    float3 center = positions[i];
                    for (int j = 0; j < splatCount; j++)
                    {
                        if (i == j) continue;
                        float dist = math.distance(center, positions[j]);
                        if (dist < neighborRadius)
                            neighbors.Add(j);
                    }
                    neighborCache[i] = neighbors;
                }

                float3 sum = float3.zero;
                int count = 0;
                foreach (var j in neighbors)
                {
                    sum += positions[j];
                    count++;
                }

                smoothed[i] = count > 0 ? sum / count : positions[i];
                affectedCount++;
            }

            for (int i = 0; i < splatCount; i++)
            {
                if (affectedMask[i])
                    positions[i] = smoothed[i];
            }
        }
    }

    void OnDestroy()
    {
        positions = null;
        originalPositions = null;
        affectedMask = null;
        neighborCache?.Clear();
    }
}
