using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;

[DisallowMultipleComponent]
public class GSViewProjectionAligner : MonoBehaviour
{
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent;
    public LayerMask roomMeshLayer;

    [Header("View Projection Settings")]
    [Range(0.01f, 1f)]
    public float viewRadius = 0.1f;
    [Range(8, 2048)]
    public int rayCount = 256;
    public bool onlyCameraFacing = true;

    [Header("ICP Settings")]
    public bool doDownsample = true;
    public float voxelSize = 0.05f;
    public int gicp_max_iter = 50;
    public float gicp_epsilon = 1e-6f;
    public bool useFGR = false;
    public float voxelSizeFGR = 0.05f;

    [Header("Matching Settings")]
    public bool matchFullRoomMesh = false;

    private GraphicsBuffer posBuffer;
    private float3[] gsPositions;
    private List<Vector3> selectedGSWorld = new();
    private List<Vector3> roomMeshPointsAll = new();
    private bool roomMeshReady = false;

    [DllImport("FGR_GICP.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern GICPResult RunFGRGICP(
        float[] refPoints, int refTotalFloats,
        float[] tgtPoints, int tgtTotalFloats,
        bool doDownsample,
        float voxelSize,
        int gicp_max_iter,
        float gicp_epsilon,
        bool userFGR,
        float voxelSizeFGR
    );

    [StructLayout(LayoutKind.Sequential)]
    public struct GICPResult
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool success;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;
    }

    void Start()
    {
        if (GaussianRendererObject != null)
        {
            posBuffer = GaussianRendererObject.GetGpuPosData();
            int count = posBuffer.count / 3;
            gsPositions = new float3[count];
            posBuffer.GetData(gsPositions);
        }

        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady);
            Debug.Log("Waiting for RoomMesh to load...");
        }
    }

    void OnRoomMeshReady(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("RoomMesh load failed or mesh is null");
            return;
        }

        var mesh = mf.sharedMesh;
        var transformMatrix = mf.transform.localToWorldMatrix;
        roomMeshPointsAll.Clear();

        foreach (var v in mesh.vertices)
            roomMeshPointsAll.Add(transformMatrix.MultiplyPoint3x4(v));

        roomMeshReady = true;
        Debug.Log($"RoomMesh loaded with {roomMeshPointsAll.Count} points");
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three)) // X
        {
            Debug.Log("Selecting view-projected points...");
            PerformViewProjectionMatching();
        }

        if (OVRInput.GetDown(OVRInput.Button.Four)) // Y
        {
            Debug.Log("Running ICP alignment...");
            RunICP();
        }
    }

    void PerformViewProjectionMatching()
    {
        if (!roomMeshReady || gsPositions == null || posBuffer == null)
        {
            Debug.LogWarning("Required data not ready");
            return;
        }

        selectedGSWorld.Clear();

        var cam = Camera.main;
        var tf = GaussianRendererObject.transform;

        // 1. 收集所有在相机视野中的 GS 点
        foreach (var pos in gsPositions)
        {
            Vector3 worldPos = tf.TransformPoint(pos);
            Vector3 viewPos = cam.WorldToViewportPoint(worldPos);
            if (viewPos.z > 0 && viewPos.x >= 0 && viewPos.x <= 1 && viewPos.y >= 0 && viewPos.y <= 1)
            {
                if (!onlyCameraFacing || Vector3.Dot((cam.transform.position - worldPos).normalized, cam.transform.forward) >= 0.7f)
                    selectedGSWorld.Add(worldPos);
            }
        }



        Debug.Log($"Matched {selectedGSWorld.Count} GS points");
    }


    void RunICP()
    {
        if (selectedGSWorld.Count < 3 || roomMeshPointsAll.Count < 3)
        {
            Debug.LogWarning("Not enough points for ICP");
            return;
        }

        float[] tgt = ConvertToFloatArray(roomMeshPointsAll);
        float[] src = ConvertToFloatArray(selectedGSWorld);

        var result = RunFGRGICP(
            tgt, tgt.Length,
            src, src.Length,
            doDownsample, voxelSize, gicp_max_iter, gicp_epsilon,
            useFGR, voxelSizeFGR
        );

        if (!result.success)
        {
            Debug.LogError("ICP failed");
            return;
        }

        ApplyTransformationWithFalloff(GetFinalTransformation(result));
    }

    void ApplyTransformationWithFalloff(Matrix4x4 T)
    {
        Quaternion R = ExtractRotation(T);
        Vector3 t = ExtractTranslation(T);
        Transform tf = GaussianRendererObject.transform;

        float radius = viewRadius;
        float3[] updated = new float3[gsPositions.Length];

        for (int i = 0; i < gsPositions.Length; i++)
        {
            Vector3 world = tf.TransformPoint(gsPositions[i]);
            float minDist = float.MaxValue;

            foreach (var sel in selectedGSWorld)
            {
                float d = Vector3.Distance(world, sel);
                if (d < minDist) minDist = d;
            }

            if (minDist < radius)
            {
                float strength = 1.0f - (minDist / radius);
                Vector3 moved = t + Quaternion.Inverse(R) * world;
                Vector3 blended = Vector3.Lerp(world, moved, strength);
                updated[i] = tf.InverseTransformPoint(blended);
            }
            else
            {
                updated[i] = gsPositions[i];
            }
        }

        posBuffer.SetData(updated);
        gsPositions = updated;
        Debug.Log("Transformation applied with falloff");
    }

    float[] ConvertToFloatArray(List<Vector3> points)
    {
        float[] arr = new float[points.Count * 3];
        for (int i = 0; i < points.Count; i++)
        {
            arr[i * 3 + 0] = points[i].x;
            arr[i * 3 + 1] = points[i].y;
            arr[i * 3 + 2] = points[i].z;
        }
        return arr;
    }

    Matrix4x4 GetFinalTransformation(GICPResult result)
    {
        float[] m = result.matrix;
        Matrix4x4 T = new Matrix4x4();
        T.m00 = m[0]; T.m01 = m[4]; T.m02 = m[8]; T.m03 = m[3];
        T.m10 = m[1]; T.m11 = m[5]; T.m12 = m[9]; T.m13 = m[7];
        T.m20 = m[2]; T.m21 = m[6]; T.m22 = m[10]; T.m23 = m[11];
        T.m30 = m[12]; T.m31 = m[13]; T.m32 = m[14]; T.m33 = m[15];
        return T;
    }

    Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        return Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
    }

    Vector3 ExtractTranslation(Matrix4x4 matrix)
    {
        return matrix.GetColumn(3);
    }
}