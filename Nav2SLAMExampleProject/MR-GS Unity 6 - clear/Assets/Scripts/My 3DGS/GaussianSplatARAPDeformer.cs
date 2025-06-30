using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(GaussianSplatRenderer))]
public class GaussianSplatARAPDeformer : MonoBehaviour
{
    public bool accumulateDeformation = false;
    public float dragRadius = 0.2f;
    public int neighborK = 10;

    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;
    private int splatCount;

    private bool isDragging = false;
    private Ray dragRay;

    [DllImport("ARAPDeformLibigl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void RunARAPDeform_PointCloud(
        float[] vertices,
        int numPoints,
        int k,
        float[] dragCenterWorld,
        float[] dragOffsetWorld,
        float dragRadius,
        float[] outputVertices
    );

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
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Array.Copy(originalPositions, positions, splatCount);
            posBuffer.SetData(positions);
        }

        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            dragRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;

            Vector3 dragOffsetWorld = Camera.main.ScreenPointToRay(Input.mousePosition).origin - dragRay.origin;

            Vector3 dragCenterWorld = Camera.main.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x,
                Input.mousePosition.y,
                Camera.main.WorldToScreenPoint(transform.position).z));

            float[] dragCenterWorldArr = new float[3] {
                dragCenterWorld.x, dragCenterWorld.y, dragCenterWorld.z
            };
            float[] dragOffsetWorldArr = new float[3] {
                dragOffsetWorld.x, dragOffsetWorld.y, dragOffsetWorld.z
            };

            float[] vertsFlat = new float[splatCount * 3];
            float[] outputVerts = new float[splatCount * 3];

            for (int i = 0; i < splatCount; i++)
            {
                vertsFlat[i * 3 + 0] = originalPositions[i].x;
                vertsFlat[i * 3 + 1] = originalPositions[i].y;
                vertsFlat[i * 3 + 2] = originalPositions[i].z;
            }

            RunARAPDeform_PointCloud(
                vertsFlat,
                splatCount,
                neighborK,
                dragCenterWorldArr,
                dragOffsetWorldArr,
                dragRadius,
                outputVerts
            );

            for (int i = 0; i < splatCount; i++)
            {
                positions[i] = new float3(
                    outputVerts[i * 3 + 0],
                    outputVerts[i * 3 + 1],
                    outputVerts[i * 3 + 2]
                );
            }

            posBuffer.SetData(positions);

            if (accumulateDeformation)
            {
                Array.Copy(positions, originalPositions, splatCount);
            }
        }
    }

    void OnDestroy()
    {
        positions = null;
        originalPositions = null;
    }
}
