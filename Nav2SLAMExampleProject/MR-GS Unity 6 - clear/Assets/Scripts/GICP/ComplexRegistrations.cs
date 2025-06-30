// MyRegistration.cs
// Unity C# script using new RunFGRGICP DLL interface

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;

public class ComplexRegistration : MonoBehaviour
{
    [SerializeField] private RoomMeshEvent roomMeshEvent;
    [SerializeField] private GaussianSplatRenderer GaussianRendererObject;

    [Header("FGR + GICP Settings")]
    [SerializeField] private bool doDownsample = true;
    [SerializeField] private float voxelSize = 0.05f;
    [SerializeField] private int gicp_max_iter = 50;
    [SerializeField] private float gicp_epsilon = 1e-6f;
    [SerializeField] private bool userFGR = false;
    [SerializeField] private float voxelSizeFGR = 0.05f;


    private List<Vector3> cloudTarget = new List<Vector3>();
    private List<Vector3> cloudSource = new List<Vector3>();

    [DllImport("FGR_GICP.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern GICPResult RunFGRGICP(
        float[] refPoints, int refTotalFloats,
        float[] tgtPoints, int tgtTotalFloats,
        bool doDownsample,
        float voxelSize,
        int gicp_max_iter,
        float gicp_epsilon,
        bool userFDR,
        float voxelSizeFGR


);

    [StructLayout(LayoutKind.Sequential)]
    public struct GICPResult
    {
        // Field 1: Matches C++ 'converged' (bool)
        // Assuming C++ bool is 1 byte, which is common on Windows/MSVC.
        // If it were different, you might need UnmanagedType.Bool (4 bytes) or others.
        [MarshalAs(UnmanagedType.I1)]
        public bool converged;

        // Field 2: Matches C++ 'matrix' (float[16])
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;

        // Field 3: Matches C++ 'centroid_ref_before' (float[3])
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_ref_before;

        // Field 4: Matches C++ 'centroid_target_before' (float[3])
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_target_before;
    }

    void Start()
    {
        if (roomMeshEvent == null || GaussianRendererObject == null)
        {
            Debug.LogError("请指定 RoomMeshEvent 和 GaussianRendererObject");
            return;
        }

        GraphicsBuffer gpuPosBuffer = GaussianRendererObject.GetGpuPosData();
        


        if (gpuPosBuffer == null)
        {
            Debug.LogError("无法获取 GPU 点云数据");
            return;
        }

        cloudSource = ExtractPointCloudFromGraphicsBuffer(gpuPosBuffer, GaussianRendererObject.transform);
        if (cloudSource.Count == 0)
        {
            Debug.LogError("提取的 source 点云为空");
            return;
        }
        else
        {
            Debug.Log("提取的 source 点云数量为"+ cloudSource.Count);
        }

        roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshLoaded);
    }

    private void OnDestroy()
    {
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.RemoveListener(OnRoomMeshLoaded);
        }
    }

    private void OnRoomMeshLoaded(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("未找到可导出的网格数据。");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Transform t = mf.transform;
        Vector3[] vertices = mesh.vertices;
        // 预先设置容量

        // 缓存 localToWorldMatrix，避免重复调用 TransformPoint
        Matrix4x4 localToWorld = t.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            cloudTarget.Add(localToWorld.MultiplyPoint3x4(vertices[i]));
        }
        Debug.Log($"从场景网格中提取了 {cloudTarget.Count} 个点作为参考点云数据。");

        if (cloudTarget.Count == 0 || cloudTarget.Count == 0)
        {
            Debug.LogError("无法运行 ICP，因为其中一个点云数据为空。");
            return;
        }




        float[] targetArray = ConvertPointCloudToFloatArray(cloudTarget);
        float[] sourceArray = ConvertPointCloudToFloatArray(cloudSource);

        GICPResult result = RunFGRGICP(
            targetArray, targetArray.Length,
            sourceArray, sourceArray.Length,
            doDownsample,
            voxelSize,
            gicp_max_iter, gicp_epsilon,
            userFGR,
            voxelSizeFGR
            );

        if (!result.converged)
        {
            Debug.LogError("FGR+GICP 配准未收敛");
            return;
        }

        Matrix4x4 T = GetFinalTransformation(result);
        ApplyTransformationToTarget(T);

        Debug.Log("配准完成");
        roomMeshEvent.gameObject.SetActive(false);
    }

    private Matrix4x4 GetFinalTransformation(GICPResult result)
    {
        float[] m = result.matrix;
        Matrix4x4 T = new Matrix4x4();

        T.m00 = m[0]; T.m01 = m[4]; T.m02 = m[8]; T.m03 = m[3];
        T.m10 = m[1]; T.m11 = m[5]; T.m12 = m[9]; T.m13 = m[7];
        T.m20 = m[2]; T.m21 = m[6]; T.m22 = m[10]; T.m23 = m[11];
        T.m30 = m[12]; T.m31 = m[13]; T.m32 = m[14]; T.m33 = m[15];

        return T;
    }

    private void ApplyTransformationToTarget(Matrix4x4 T)
    {
        Quaternion R = ExtractRotation(T);
        Vector3 t = ExtractTranslation(T);

        Vector3 finalTranslation = t + Quaternion.Inverse(R) * GaussianRendererObject.transform.position;
        GaussianRendererObject.transform.rotation = Quaternion.Inverse(R) * GaussianRendererObject.transform.rotation;
        GaussianRendererObject.transform.position = finalTranslation;
    }

    private Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        Vector3 forward = matrix.GetColumn(2);
        Vector3 up = matrix.GetColumn(1);
        return Quaternion.LookRotation(forward, up);
    }

    private Vector3 ExtractTranslation(Matrix4x4 matrix)
    {
        return matrix.GetColumn(3);
    }

    private float[] ConvertPointCloudToFloatArray(List<Vector3> cloud)
    {
        float[] arr = new float[cloud.Count * 3];
        for (int i = 0; i < cloud.Count; i++)
        {
            arr[i * 3 + 0] = cloud[i].x;
            arr[i * 3 + 1] = cloud[i].y;
            arr[i * 3 + 2] = cloud[i].z;
        }
        return arr;
    }

    private List<Vector3> ExtractPointCloudFromGraphicsBuffer(GraphicsBuffer buffer, Transform objTransform)
    {
        int totalFloats = buffer.count;
        int numPoints = totalFloats / 3;
        float[] data = new float[totalFloats];
        buffer.GetData(data);

        List<Vector3> points = new List<Vector3>(numPoints);
        for (int i = 0; i < numPoints; i++)
        {
            int idx = i * 3;
            Vector3 localPoint = new Vector3(data[idx], data[idx + 1], data[idx + 2]);
            Vector3 worldPoint = objTransform.TransformPoint(localPoint);
            points.Add(worldPoint);
        }
        return points;
    }

    private List<Vector3> ExtractPointCloudFromGraphicsBuffer_Norm11(
        GraphicsBuffer buffer,
        Transform objTransform
        )
    {
        int totalUShorts = buffer.count;
        int numPoints = totalUShorts / 3;
        Vector3 boundMin = GaussianRendererObject.editSelectedBounds.min;
        Vector3 boundsMax = GaussianRendererObject.editSelectedBounds.max;

        ushort[] data = new ushort[totalUShorts];
        buffer.GetData(data);
        

        List<Vector3> points = new List<Vector3>(numPoints);
        for (int i = 0; i < numPoints; i++)
        {
            int idx = i * 3;

            ushort xBits = data[idx];
            ushort yBits = data[idx + 1];
            ushort zBits = data[idx + 2];

            Vector3 norm = DecodeNorm11(xBits, yBits, zBits); // [-1, 1]
            Vector3 local = DecodeNorm11ToPosition(norm, boundMin, boundsMax);
            Vector3 world = objTransform.TransformPoint(local);

            points.Add(world);
        }

        return points;
    }

    private Vector3 DecodeNorm11(ushort xBits, ushort yBits, ushort zBits)
    {
        float x = (xBits / 2047.0f) * 2f - 1f;
        float y = (yBits / 2047.0f) * 2f - 1f;
        float z = (zBits / 2047.0f) * 2f - 1f;
        return new Vector3(x, y, z);
    }

    private Vector3 DecodeNorm11ToPosition(Vector3 norm11Vec, Vector3 boundsMin, Vector3 boundsMax)
    {
        Vector3 norm01 = (norm11Vec + Vector3.one) * 0.5f;
        return Vector3.Scale(norm01, boundsMax - boundsMin) + boundsMin;
    }


}
