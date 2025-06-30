// LocalGSAlignment.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Linq;
using System;

public class LocalGSAlignment : MonoBehaviour
{
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent;
    public Material boxMaterial;
    public bool accumulateDeformation = true; // 是否累计形变

    [Header("Box Settings")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f);
    public float moveSpeed = 0.5f;
    public float viewRadius = 0.05f;

    [Header("ICP Settings")]
    public bool doDownsample = true;
    public float voxelSize = 0.05f;
    public int gicp_max_iter = 50;
    public float gicp_epsilon = 1e-6f;
    public bool useFGR = false;
    public float voxelSizeFGR = 0.05f;

    private GameObject selectionBox;
    private List<Vector3> roomMeshPointsAll = new List<Vector3>();
    private List<int> selectedGSIndices = new List<int>();
    private bool roomMeshReady = false;
    private int splatCount;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;

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
        public bool converged;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_ref_before;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_target_before;
    }

    void Start()
    {
        GaussianRendererObject = GaussianRendererObject.GetComponent<GaussianSplatRenderer>();
        if (GaussianRendererObject != null)
        {

            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            if (posBuffer == null) { Debug.LogError("未获取 GPU 点云数据"); enabled = false; return; }
            splatCount = GaussianRendererObject.splatCount;
            positions = new float3[splatCount];
            originalPositions = new float3[splatCount];
            posBuffer.GetData(positions);
            Array.Copy(positions, originalPositions, splatCount);


        }

        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady);
            Debug.Log("等待 RoomMesh 加载...");
        }
    }

    private void OnRoomMeshReady(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("RoomMesh 加载失败或 Mesh 为空");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Transform t = mf.transform;
        Matrix4x4 localToWorld = t.localToWorldMatrix;

        roomMeshPointsAll.Clear();
        foreach (var v in vertices)
        {
            roomMeshPointsAll.Add(localToWorld.MultiplyPoint3x4(v));
        }

        roomMeshReady = true;
        Debug.Log($"RoomMesh 加载完成，提取 {roomMeshPointsAll.Count} 个点");
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three)) // X键
        {
            if (selectionBox != null) Destroy(selectionBox);

            selectionBox = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            selectionBox.transform.localScale = boxSize;
            selectionBox.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.5f;
            selectionBox.transform.rotation = Quaternion.identity;
            selectionBox.GetComponent<Collider>().enabled = false;

            Material mat = boxMaterial != null ? boxMaterial : new Material(Shader.Find("Standard"));
            mat.color = new Color(0, 1, 1, 0.3f);
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            selectionBox.GetComponent<MeshRenderer>().material = mat;
            Debug.Log("已生成选区 Box");


            //posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            //if (posBuffer == null) { Debug.LogError("未获取 GPU 点云数据"); enabled = false; return; }
            //splatCount = GaussianRendererObject.splatCount;
            //positions = new float3[splatCount];
            //originalPositions = new float3[splatCount];
            //posBuffer.GetData(positions);
            //Array.Copy(positions, originalPositions, splatCount);

        }

        if (selectionBox != null)
        {
            float move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y * moveSpeed * Time.deltaTime;
            selectionBox.transform.position += Camera.main.transform.forward * move;
            Debug.Log("the position of selectionBox is" + selectionBox.transform.position);
        }

        if (selectionBox != null && OVRInput.GetDown(OVRInput.Button.Four)) // Y键
        {
            Debug.Log("开始执行局部配准");
            PerformLocalRegistration(selectionBox);
        }
    }



    void PerformLocalRegistration(GameObject box)
    {
        if (positions == null || posBuffer == null)
        {
            Debug.LogError("无法获取 GPU GS 点云数据");
            return;
        }

        Transform tf = GaussianRendererObject.transform;
        Bounds boxBounds = new Bounds(box.transform.position, box.transform.localScale);

        Vector3 camPos = Camera.main.transform.position;
        Transform camTransform = Camera.main.transform;

        List<Vector3> selectedGSWorld = new List<Vector3>();
        selectedGSIndices.Clear();

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 world = tf.TransformPoint(positions[i]);

            // Step 1: 是否在 Box 内
            if (!boxBounds.Contains(world)) continue;            
            selectedGSWorld.Add(world);
            selectedGSIndices.Add(i);
        }

        Debug.Log($"提取到 {selectedGSIndices.Count} 个 可见 GS 点");

        // RoomMesh 点的提取
        List<Vector3> roomMeshPoints = ExtractRoomMeshPointsInsideBounds(boxBounds);
        Debug.Log($"提取到 {roomMeshPoints.Count} 个 RoomMesh 点");

        if (selectedGSIndices.Count == 0 || roomMeshPoints.Count == 0)
        {
            Debug.LogWarning("选中区域中点不足，无法执行局部配准");
            return;
        }
        
        float[] tgt = ConvertToFloatArray(roomMeshPointsAll);
        //float[] tgt = ConvertToFloatArray(roomMeshPoints);
        float[] src = ConvertToFloatArray(selectedGSWorld);

        Debug.Log("调用 ICP 函数进行匹配");
        GICPResult result = RunFGRGICP(
            tgt, tgt.Length, src, src.Length,
            doDownsample, voxelSize, gicp_max_iter, gicp_epsilon,
            useFGR, voxelSizeFGR
        );

        if (!result.converged)
        {
            Debug.LogError("局部 ICP 配准失败");
            return;
        }

        Debug.Log("ICP 成功，应用变换");
        Matrix4x4 T = GetFinalTransformation(result);
        ApplyTransformationToSelectedPoints(T);
        Debug.Log("局部变换应用完成");
    }



    void ApplyTransformationToSelectedPoints(Matrix4x4 T)
    {
        Quaternion R = ExtractRotation(T);
        Vector3 t = ExtractTranslation(T);
        Transform tf = GaussianRendererObject.transform;

        float radius = viewRadius; // 作用范围

        // 获取所有选中点的世界坐标（用于中心计算）
        List<Vector3> selectedGSWorld = new List<Vector3>();
        foreach (int idx in selectedGSIndices)
            selectedGSWorld.Add(tf.TransformPoint(originalPositions[idx]));

        // 计算选区的几何中心
        Vector3 center = ComputeCentroid(selectedGSWorld);
        Debug.Log("选中的3DGS的position" + center);
        int affectedCount = 0;
        for (int i = 0; i < splatCount; i++)
        {
            float3 worldPos = tf.transform.TransformPoint(originalPositions[i]);
            
            //float dist = t.magnitude;
            float dist = Vector3.Distance(worldPos, center);

            if (dist < viewRadius)
            {
                affectedCount++;
                float strength = 1.0f - dist / viewRadius;
                float3 offsetLocal = (float3)tf.transform.InverseTransformVector(t);
                //positions[i] = originalPositions[i] + new float3(0, 0, -0.5f);

                positions[i] = originalPositions[i] + offsetLocal * strength;

                if (accumulateDeformation)
                {
                    originalPositions[i] = positions[i];
                }

                

            }
            else
            {
                positions[i] = originalPositions[i];
            }

        }

        posBuffer.SetData(positions);


        Debug.Log($"共应用软形变到 {affectedCount} 个 GS 点（transformation={t}）");
        Debug.Log($"共应用软形变到 {affectedCount} 个 GS 点（transformation={tf.transform.InverseTransformVector(t)}）");

    }




    /// <summary>
    /// Applies a local rigid transformation (T) to GS points,
    /// blending the effect smoothly based on distance from the center of the selected points
    /// defined by the bounds of the input box GameObject. Uses Slerp/Lerp blending.
    /// (Simplified version with original variable names)
    /// </summary>
    /// <param name="T">The 4x4 transformation matrix calculated for the selected region (in World Space).</param>
    /// <param name="box">The GameObject (e.g., selection sphere) defining the region of interest.</param>
    // *** FUNCTION NAME MATCHES ORIGINAL, PARAMETER NAME MATCHES ORIGINAL ***
    void ApplyTransformationToSelectedPoints(Matrix4x4 T, GameObject box)
    {

        // --- 2. Decompose Transformation (using original variable names R and t) ---
        Quaternion R = T.rotation; // Use input matrix T
        Vector3 t = T.GetColumn(3); // Use input matrix T
        float innerRadius = 0.5f;
        Transform tf = GaussianRendererObject.transform;

        float radius = viewRadius; // 作用范围

        // 获取所有选中点的世界坐标（用于中心计算）
        List<Vector3> selectedGSWorld = new List<Vector3>();
        foreach (int idx in selectedGSIndices)
            selectedGSWorld.Add(tf.TransformPoint(originalPositions[idx]));
        Vector3 center = ComputeCentroid(selectedGSWorld);
        Debug.Log("选中的3DGS的position" + center);
        int affectedCount = 0;
        // --- 3. Prepare Falloff Parameters (using viewRadius as outerRadius) ---
        float actualInnerRadius = Mathf.Max(0, Mathf.Min(innerRadius, viewRadius)); // Use viewRadius as outer
        float actualOuterRadius = Mathf.Max(actualInnerRadius, viewRadius);
        float transitionRange = actualOuterRadius - actualInnerRadius;
        if (transitionRange < 1e-5f) transitionRange = 1e-5f;

        // --- 4. Iterate and Apply Blended Transform ---
        bool needsGpuOriginalUpdate = false;
        // Create a temporary array to store results if needed, or modify 'positions' directly
        // Let's modify 'positions' directly for simplicity, matching original structure
        // float3[] tempPositions = new float3[splatCount]; // Alternative: Modify a temp array first
        // Array.Copy(positions, tempPositions, splatCount);

        for (int i = 0; i < splatCount; i++)
        {
            // *** Use class members 'positions' and 'originalPositions' (float3[]) ***
            float3 basePosLocal = accumulateDeformation ? originalPositions[i] : positions[i];
            Vector3 worldPosCurrent = tf.TransformPoint(basePosLocal); // Need Vector3 for distance/lerp/slerp
            float dist = Vector3.Distance(worldPosCurrent, center);
            float strength = 0.0f;

            // Calculate strength
            if (dist <= actualInnerRadius)
            {
                strength = 1.0f;
            }
            else if (dist < actualOuterRadius)
            { // Use viewRadius as outer limit
                float t_blend = (dist - actualInnerRadius) / transitionRange; // Renamed blend parameter
                strength = 1.0f - (t_blend * t_blend * (3.0f - 2.0f * t_blend)); // Smoothstep
                strength = Mathf.Clamp01(strength);
            }

            // Apply transform only if strength is significant
            if (strength > 1e-5f)
            {
                Quaternion R_interp = Quaternion.SlerpUnclamped(Quaternion.identity, R, strength); // Use R
                Vector3 t_interp = Vector3.LerpUnclamped(Vector3.zero, t, strength); // Use t
                Vector3 posRelativeToCenter = worldPosCurrent - center;
                Vector3 rotatedPosRelativeToCenter = R_interp * posRelativeToCenter;
                Vector3 newWorldPos = rotatedPosRelativeToCenter + center + t_interp;
                float3 newLocalPos = (float3)tf.InverseTransformPoint(newWorldPos); // Cast back to float3

                // *** Update class member 'positions' ***
                positions[i] = newLocalPos; // Update current position state

                if (accumulateDeformation)
                {
                    // *** Update class member 'originalPositions' ***
                    originalPositions[i] = newLocalPos; // Update resting position state
                    needsGpuOriginalUpdate = true;
                }
            }
            else if (!accumulateDeformation)
            {
                // If not accumulating and no strength, ensure it's the original state
                positions[i] = originalPositions[i];
            }
            else if (accumulateDeformation && strength <= 1e-5f)
            {
                // If accumulating and no strength, ensure current matches original
                positions[i] = originalPositions[i];
            }
        }

        posBuffer.SetData(positions);
        Debug.Log($"共应用软形变到 {affectedCount} 个 GS 点（transformation={t}）");
        Debug.Log($"共应用软形变到 {affectedCount} 个 GS 点（transformation={tf.transform.InverseTransformVector(t)}）");

    }






    Vector3 ComputeCentroid(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points) sum += p;
        return (points.Count > 0) ? sum / points.Count : Vector3.zero;
    }

    Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        Vector3 forward = matrix.GetColumn(2);
        Vector3 up = matrix.GetColumn(1);
        return Quaternion.LookRotation(forward, up);
    }

    Vector3 ExtractTranslation(Matrix4x4 matrix)
    {
        return matrix.GetColumn(3);
    }

    List<Vector3> ExtractRoomMeshPointsInsideBounds(Bounds bounds)
    {
        if (!roomMeshReady)
        {
            Debug.LogWarning("RoomMesh 尚未加载，无法提取点");
            return new List<Vector3>();
        }

        List<Vector3> result = new List<Vector3>();
        foreach (var p in roomMeshPointsAll)
        {
            if (bounds.Contains(p))
                result.Add(p);
        }
        return result;
    }

    float[] ConvertToFloatArray(List<Vector3> points)
    {
        float[] arr = new float[points.Count * 3];
        for (int i = 0; i < points.Count; i++)
        {
            arr[i * 3] = points[i].x;
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

    void OnDisable()
    {
        if (!accumulateDeformation && originalPositions != null)
            posBuffer.SetData(originalPositions);
    }


}
