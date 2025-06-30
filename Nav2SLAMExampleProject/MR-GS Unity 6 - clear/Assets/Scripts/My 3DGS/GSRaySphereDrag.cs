using UnityEngine;
using Unity.Mathematics;
using System;
using GaussianSplatting.Runtime;
using System.Collections.Generic;


public enum MarkerShape { Sphere, Cube, Capsule }
[RequireComponent(typeof(GaussianSplatRenderer))]
public class GSRaySphereDrag : MonoBehaviour
{
    public Transform rightHandController; // OVRController 右手对象
    public float interactionRadius = 0.2f; // 拖拽半径
    public bool accumulateDeformation = true; // 是否累计形变
    public Material sphereMaterial;
    public float translationGain = 1.0f; // 拖拽位移增益
    public MarkerShape markerShape = MarkerShape.Sphere;

    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;
    private int splatCount;
    private GameObject visualSphere;

    private Vector3 dragStartPoint;
    private Vector3 previousDragPoint;
    private bool dragStarted = false;

    void Start()
    {
        renderer = GetComponent<GaussianSplatRenderer>();
        posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(renderer);
        if (posBuffer == null) { Debug.LogError("未获取 GPU 点云数据"); enabled = false; return; }

        splatCount = renderer.splatCount;
        Debug.Log($"[Start] GS 点数量: {splatCount}");

        positions = new float3[splatCount];
        originalPositions = new float3[splatCount];
        posBuffer.GetData(positions);
        Array.Copy(positions, originalPositions, splatCount);
    }

    void Update()
    {
        if (rightHandController == null) return;

        // 创建拖拽小球（可视化）
        if (visualSphere == null)
        {
            PrimitiveType type = PrimitiveType.Sphere;
            switch (markerShape)
            {
                case MarkerShape.Cube: type = PrimitiveType.Cube; break;
                case MarkerShape.Capsule: type = PrimitiveType.Capsule; break;
            }
            visualSphere = GameObject.CreatePrimitive(type);
            visualSphere.name = "Drag Sphere";
            visualSphere.SetActive(true);
            visualSphere.GetComponent<Renderer>().enabled = true;
            if (sphereMaterial != null)
            {
                visualSphere.GetComponent<Renderer>().material = sphereMaterial;
            }
            Destroy(visualSphere.GetComponent<Collider>());
        }

        // 获取拖拽点
        Vector3 offset = rightHandController.forward * 0.5f;
        Vector3 dragPoint = rightHandController.position + offset;
        visualSphere.transform.position = dragPoint;
        visualSphere.transform.localScale = Vector3.one * interactionRadius * 2f;

        // 判断是否按下 A 键
        bool triggerDown = OVRInput.Get(OVRInput.Button.One);

        if (!triggerDown)
        {
            dragStarted = false;
            return;
        }

        if (!dragStarted)
        {
            previousDragPoint = dragPoint;
            dragStarted = true;
            return; // 防止初帧位移太大
        }

        Vector3 dragOffset = (dragPoint - previousDragPoint) * translationGain;
        float3 offsetLocal = (float3)transform.InverseTransformVector(dragOffset);

        if (posBuffer == null || positions == null || originalPositions == null)
        {
            Debug.LogWarning("[Update] 点数据未初始化，跳过帧");
            return;
        }

        Debug.Log("drag position" + dragPoint);
        int affectedCount = 0;

        for (int i = 0; i < splatCount; i++)
        {
            float3 worldPos = transform.TransformPoint(originalPositions[i]);
            float dist = Vector3.Distance(worldPos, dragPoint);
            if (i % 200 == 0)
            {
                Debug.Log("两个点的距离为" + dist);
                Debug.Log("该3DGS点的位置为" + worldPos);
                Debug.Log("该3DGS点的RAW位置为" + originalPositions[i]);

            }
            if (dist < interactionRadius)
            {
                float strength = 1.0f - dist / interactionRadius;
                positions[i] = originalPositions[i] + offsetLocal * strength;

                if (accumulateDeformation)
                {
                    originalPositions[i] = positions[i];
                }
                affectedCount++;
            }
            else
            {
                positions[i] = originalPositions[i];
            }
        }

        Debug.Log($"[Update] 本帧被拖动的 GS 点数: {affectedCount}");
        posBuffer.SetData(positions);
        previousDragPoint = dragPoint;
    }


    void OnDisable()
    {
        visualSphere?.SetActive(false);
        if (!accumulateDeformation && originalPositions != null)
            posBuffer.SetData(originalPositions);
    }
}
