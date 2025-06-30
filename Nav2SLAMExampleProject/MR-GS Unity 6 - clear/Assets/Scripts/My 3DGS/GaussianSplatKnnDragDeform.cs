using UnityEngine;
using Unity.Mathematics;
using System;
using GaussianSplatting.Runtime;
using Unity.Collections;
using KNN.Jobs;
using KNN;
using Unity.Jobs;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatKnnDragDeform : MonoBehaviour
{
    public bool accumulateDeformation = false; // 是否累计形变
    public float dragRadius = 0.2f;
    public int kNeighbours = 5000;
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private NativeArray<float3> positions;
    private NativeArray<float3> positionsWorld;
    private NativeArray<float3> originalPositions;
    private int splatCount;

    private bool isDragging = false;
    private Ray dragRay;

    // 小球用于表示鼠标位置
    public GameObject ball; // 将小球与鼠标绑定
    private Transform ballTransform;

    // KNN容器
    private KnnContainer knnContainer;

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        posBuffer = renderer.GetGpuPosData();

        if (posBuffer == null)
        {
            Debug.LogError("Unable to retrieve m_GpuPosData.");
            enabled = false;
            return;
        }

        splatCount = renderer.splatCount;
        positions = new NativeArray<float3>(splatCount, Allocator.Persistent);
        positionsWorld = new NativeArray<float3>(splatCount, Allocator.Persistent);

        originalPositions = new NativeArray<float3>(splatCount, Allocator.Persistent);

        // Create a temporary System.Array to hold the data from GraphicsBuffer
        float3[] tempPositions = new float3[splatCount];
        posBuffer.GetData(tempPositions);

        // Copy data from the System.Array to the NativeArray
        for (int i = 0; i < splatCount; i++)
        {
            positions[i] = tempPositions[i];
            positionsWorld[i] = transform.TransformPoint(tempPositions[i]);
            originalPositions[i] = tempPositions[i];
        }


        // Initialize KNN Container
        knnContainer = new KnnContainer(positionsWorld, false, Allocator.TempJob);

        // Build the K-D tree explicitly
        BuildKnnTree();

        // 获取小球的 Transform 组件
        ballTransform = ball.GetComponent<Transform>();
    }

    // Ensure K-D tree is built
    private void BuildKnnTree()
    {
        var rebuildJob = new KnnRebuildJob(knnContainer);
        JobHandle rebuildHandle = rebuildJob.Schedule();  // Schedule the rebuild job
        rebuildHandle.Complete();
    }

    void Update()
    {
        // 空格强制恢复形变
        if (Input.GetKeyDown(KeyCode.Space))
        {
            originalPositions.CopyTo(positions);
            posBuffer.SetData(positions);
        }

        // 鼠标按下开始拖动
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            dragRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            Debug.Log("Input.mousePosition:" + dragRay.ToString());
        }

        // 鼠标松开结束拖动
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;

            if (!accumulateDeformation)
            {
                // 不累计形变，则还原位置
                originalPositions.CopyTo(positions);
                posBuffer.SetData(positions);
            }
            else
            {
                // 累计形变，则更新原始位置为当前形变位置
                positions.CopyTo(originalPositions);
            }
        }

        // 拖动中，实时更新小球的位置并进行形变
        if (isDragging)
        {
            Ray newRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 offset = newRay.origin - dragRay.origin;
            float3 offsetLocal = (float3)transform.InverseTransformVector(offset);
            int affectedCount = 0;


            
            //float3 worldMousePos = (float3)Input.mousePosition;
            float3 worldMousePos = (float3)ball.transform.position;

            //worldMousePos = Camera.main.WorldToScreenPoint(worldMousePos);
            float2 worldMousePos2D = new float2(worldMousePos.x, worldMousePos.y);

            // Make sure there's data to query
            if (positions.Length > 0)
                {
                    NativeArray<int> knnResults = new NativeArray<int>(kNeighbours, Allocator.TempJob);

                    // Query K nearest neighbors
                    knnContainer.QueryKNearest(worldMousePos, knnResults);
                    for (int i = 0; i < knnResults.Length; i++)
                    {
                        int idx = knnResults[i];
                        float3 originalPos = originalPositions[idx];
                        float3 worldPos = transform.TransformPoint(originalPositions[i]);
                        float3 screenPoint = Camera.main.WorldToScreenPoint(worldPos);
                        float2 screenPos2D = new float2(screenPoint.x, screenPoint.y);

                        //float2 mousePos = new float2(Input.mousePosition.x, Input.mousePosition.y);
                        float2 mousePos = new float2(worldMousePos.x, worldMousePos.y);

                    float dist = math.distance(screenPos2D, mousePos);

                    if (dist < dragRadius * Screen.height)
                        {
                        float strength = 1.0f - dist / (dragRadius * Screen.height);
                        positions[idx] = originalPos + offsetLocal* strength;
                            affectedCount++;
                        }
                        else
                        {
                            positions[idx] = originalPos;
                        }
                    }

                    knnResults.Dispose();
                    Debug.Log($"[Update] 本帧被拖动的 GS 点数: {affectedCount}");
                    posBuffer.SetData(positions);
                }
                else
                {
                    Debug.LogError("No valid data in positions to query.");
                }
            }
       
    }

    void OnDestroy()
    {
        // Dispose NativeArrays
        positions.Dispose();
        originalPositions.Dispose();
        knnContainer.Dispose(); // Release KNN container resources
    }
}
