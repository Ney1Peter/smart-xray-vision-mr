using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;
using GaussianSplatting.Runtime;
using MIConvexHull;

public class Vertex3 : IVertex
{
    public double[] Position { get; set; }
    public int originalIndex; // 跟踪原始索引（可选）

    public Vertex3(Vector3 v, int idx)
    {
        Position = new double[] { v.x, v.y, v.z };
        originalIndex = idx;
    }

    public Vector3 ToVector3() => new Vector3((float)Position[0], (float)Position[1], (float)Position[2]);
}

[RequireComponent(typeof(GaussianSplatRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[ExecuteInEditMode]
public class ConvexHullColliderGenerator : MonoBehaviour
{
    private GaussianSplatRenderer renderer;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private int splatCount;

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
        posBuffer.GetData(positions);

        GenerateConvexHullMesh();
    }

    void GenerateConvexHullMesh()
    {
        float maxDistance = 10f; // 最大允许距离（单位：米）

        // Step 1: 计算点云中心
        Vector3 center = Vector3.zero;
        for (int i = 0; i < splatCount; i++)
        {
            center += (Vector3)positions[i];
        }
        center /= splatCount;
        List<Vector3> worldPoints = new List<Vector3>();
        List<Vertex3> verts = new List<Vertex3>();

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 world = positions[i];
            if (Vector3.Distance(world, center) > maxDistance) continue; // 剔除过远点

            verts.Add(new Vertex3(world, i));
            worldPoints.Add(world);
        }

        var result = ConvexHull.Create<Vertex3, DefaultConvexFace<Vertex3>>(verts);

        if (result.Result == null)
        {
            Debug.LogError($"[ConvexHull] 计算失败: {result.Outcome} - {result.ErrorMessage}");
            return;
        }

        var hull = result.Result;

        List<Vector3> meshVerts = new List<Vector3>();
        List<int> triangles = new List<int>();
        Dictionary<int, int> vertIndexMap = new Dictionary<int, int>();

        foreach (var v in hull.Points)
        {
            Vector3 p = v.ToVector3();
            int meshIndex = meshVerts.Count;
            meshVerts.Add(p);
            vertIndexMap[v.originalIndex] = meshIndex;
        }

        foreach (var face in hull.Faces)
        {
            int a = vertIndexMap[face.Vertices[0].originalIndex];
            int b = vertIndexMap[face.Vertices[1].originalIndex];
            int c = vertIndexMap[face.Vertices[2].originalIndex];
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = meshVerts.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        var collider = GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;

        if (TryGetComponent<MeshRenderer>(out var mr))
            mr.enabled = false;

        Debug.Log($"[ConvexHull] Mesh + Collider 初始化完成，共 {meshVerts.Count} 点，{triangles.Count / 3} 面");
    }
}
