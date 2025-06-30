using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Linq;
using System;
using Appletea.Dev.PointCloud;
using Meta.XR;
using System.Collections;

public class FineNonRigidAlignment1 : MonoBehaviour
{
    public enum Density : int
    {
        low = 32,
        medium = 64,
        high = 128,
        vHigh = 256,
        ultra = 512
    }

    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent;
    public bool accumulateDeformation = true;
    [SerializeField] private EnvironmentRaycastManager depthManager;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    [Space(10)]
    [Header("ICP Settings")]
    public bool doDownsample = true;
    public float voxelSize = 0.05f;
    public int gicp_max_iter = 50;
    public float gicp_epsilon = 1e-6f;
    public bool useFGR = false;
    public float voxelSizeFGR = 0.05f;

    [Space(10)]
    [Header("Chunk Settings")]
    [SerializeField]
    private int chunkSize = 1;
    [SerializeField]
    private int maxPointsPerChunk = 256;
    [SerializeField]
    private int initialPoolSize = 1000;

    [Space(10)]
    [Header("Camera Settings")]
    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    [Tooltip("Percentage of the field of view")]
    private float fovMargin = 0.9f;
    [SerializeField] private float viewRadius = 5f;

    [Space(10)]
    [Header("Scan Settings")]
    [SerializeField]
    private float scanInterval = 1.0f;
    [SerializeField]
    private Density density = Density.medium;
    [SerializeField]
    [Tooltip("The limit is about 5m")]
    private float maxScanDistance = 5;

    [Space(10)]
    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f); // Initial size of the selection sphere
    public float moveSpeed = 1f; // Speed to move the sphere
    public Material boxMaterial; // Material for the selection sphere

    [Space(10)]
    [Header("Rendering Settings")]
    [SerializeField]
    private GameObject pointPrefab;
    [SerializeField]
    private float renderingRadius = 10.0f;
    [SerializeField]
    int maxChunkCount = 15;

    private ChunkManager pointsData;
    private Coroutine scanCoroutine = null;


    private List<Vector3> roomMeshPointsAll = new List<Vector3>();
    private List<int> selectedGSIndices = new List<int>();
    private bool roomMeshReady = false;
    private int splatCount;
    private GraphicsBuffer posBuffer;
    private float3[] positions;
    private float3[] originalPositions;
    private List<Vector3> currentScanData = new List<Vector3>();
    private GameObject selectionBox; // The sphere used for selection



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


    // Start is called once before the first execution of Update after the MonoBehaviour is created
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
        pointsData = new ChunkManager(chunkSize, maxPointsPerChunk);
        pointCloudRenderer.Initialize(pointPrefab, initialPoolSize);

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

    // Update is called once per frame
    void Update()
    {
        if (selectionBox != null)
        {
            float move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y * moveSpeed * Time.deltaTime;
            selectionBox.transform.position += Camera.main.transform.forward * move;
        }
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            CreateOrResetSelectionSphere();
        }
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            if (scanCoroutine == null)
            {
                Debug.Log("扫描开始 (按下 B 键)");
                scanCoroutine = StartCoroutine(ScanRoutine());
            }
            //currentScanData = pointsData.GetAllPoints();

        }
        else if (OVRInput.GetUp(OVRInput.RawButton.B))
        {
            // 只有在当前有扫描协程在运行时才停止它
            if (scanCoroutine != null)
            {
                Debug.Log("扫描停止 (松开 B 键)");
                StopCoroutine(scanCoroutine);
                scanCoroutine = null; // 清除引用，表示已停止
            }
        }

        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            Debug.Log("开始执行局部配准");
            PerformLocalRegistration();
        }

        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            Debug.Log("清除所有已存储的点云数据 (按下 X 键)...");

            // 1. (推荐) 如果正在扫描，先停止扫描
            if (scanCoroutine != null)
            {
                Debug.Log("正在停止当前扫描...");
                StopCoroutine(scanCoroutine);
                scanCoroutine = null;
            }

            // 2. 调用 ChunkManager 的清除方法
            if (pointsData != null)
            {
                // !!!  ChunkManager 类有 Clear() 
                pointsData.Clear(); // 
                currentScanData.Clear();
                Debug.Log("pointsData 内部数据已清除。");
            }
            else
            {
                Debug.LogWarning("pointsData 引用为空，无法清除。");
            }

            // 3. (推荐) 更新渲染器，传入空列表以清除视觉显示
            if (pointCloudRenderer != null)
            {
                pointCloudRenderer.UpdatePointCloud(new List<Vector3>()); // 传入空列表
                Debug.Log("点云渲染器显示已清除。");
            }
            else
            {
                Debug.LogWarning("pointCloudRenderer 引用为空，无法清除显示。");
            }

            Debug.Log("点云清除操作完成。");
        }
    }

    void PerformLocalRegistration()
    {
        if (positions == null || posBuffer == null)
        {
            Debug.LogError("无法获取 GPU GS 点云数据");
            return;
        }

        Transform tf = GaussianRendererObject.transform;
        Vector3 scanDatacentriod = ComputeCentroid(currentScanData);
        //Bounds boxBounds = pointsData.GetApproximateBounds();
        Bounds boxBounds = new Bounds(selectionBox.transform.position, selectionBox.transform.localScale);

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
        currentScanData = pointsData.GetAllPoints();
        //List<Vector3> roomMeshPoints = ExtractRoomMeshPointsInsideBounds(boxBounds);
        Debug.Log($"提取到 {currentScanData.Count} 个 RoomMesh 点");

        if (selectedGSIndices.Count == 0 || currentScanData.Count == 0)
        {
            Debug.LogWarning("选中区域中点不足，无法执行局部配准");
            return;
        }

        float[] tgt = ConvertToFloatArray(currentScanData);
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
        float3[] updatedSubset = new float3[selectedGSIndices.Count];

        // 获取所有选中点的世界坐标（用于中心计算）
        List<Vector3> selectedGSWorld = new List<Vector3>();
        foreach (int idx in selectedGSIndices)
            selectedGSWorld.Add(tf.TransformPoint(originalPositions[idx]));

        // 计算选区的几何中心
        Vector3 center = selectionBox.transform.position;
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

    IEnumerator ScanRoutine()
    {
        while (true)
        {
            ScanAndStorePointCloud(((int)density), pointsData);

            List<Vector3> points = pointsData.GetPointsInRadius(mainCamera.transform.position, renderingRadius, maxChunkCount);
            //List<Vector3> points = pointsData.GetAllPoints();
            pointCloudRenderer.UpdatePointCloud(points);

            yield return new WaitForSeconds(scanInterval);
        }
    }

    void ScanAndStorePointCloud(int sampleSize, ChunkManager pointsData)
    {
        // Generate Rays
        List<Vector2> viewSpaceCoords = GenerateViewSpaceCoords(sampleSize, sampleSize);
        List<Ray> rays = new List<Ray>();
        foreach (Vector2 i in viewSpaceCoords)
        {
            rays.Add(mainCamera.ViewportPointToRay(new Vector3(i.x, i.y, 0)));
        }

        List<EnvironmentRaycastHit> results = new List<EnvironmentRaycastHit>();
        foreach (Ray ray in rays)
        {
            EnvironmentRaycastHit result;
            depthManager.Raycast(ray, out result, maxScanDistance);

            // Cutout distant points
            if (Vector3.Distance(result.point, mainCamera.transform.position) < maxScanDistance)
                results.Add(result);
        }

        //Randomize
        ListExtensions.Shuffle(results);

        foreach (var result in results)
        {
            pointsData.AddPoint(result.point);
        }
    }

    List<Vector2> GenerateViewSpaceCoords(int xSize, int zSize)
    {
        List<Vector2> coords = new List<Vector2>();

        // Get the camera's field of view and aspect ratio
        float fovY = mainCamera.fieldOfView * fovMargin;
        float fovX = Camera.VerticalToHorizontalFieldOfView(fovY, mainCamera.aspect) * fovMargin;

        // Calculate the dimensions of the view frustum at a distance of 1 unit
        float frustumHeight = 2.0f * Mathf.Tan(fovY * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = 2.0f * Mathf.Tan(fovX * 0.5f * Mathf.Deg2Rad);

        // Calculate the step sizes
        float stepX = frustumWidth / (xSize - 1);
        float stepY = frustumHeight / (zSize - 1);

        // Generate coordinates
        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                // Calculate normalized device coordinates (NDC)
                float ndcX = (x * stepX - frustumWidth * 0.5f) / (frustumWidth * 0.5f);
                float ndcY = (z * stepY - frustumHeight * 0.5f) / (frustumHeight * 0.5f);

                // Convert NDC to view space coordinates
                float xCoord = (ndcX + 1) * 0.5f;
                float yCoord = (ndcY + 1) * 0.5f;

                coords.Add(new Vector2(xCoord, yCoord));
            }
        }

        return coords;
    }

    List<Vector2> GenerateViewSpaceCoordsView(int xSize, int zSize)
    {
        // --- 输入验证 ---
        if (xSize <= 0 || zSize <= 0)
        {
            Debug.LogError($"输入参数 xSize ({xSize}) 或 zSize ({zSize}) 必须为正数。");
            return new List<Vector2>(); // 对于无效输入，返回空列表
        }

        // 读取成员变量 fovMargin 并将其限制在 [0, 1] 范围内
        // 注意：这里 fovMargin 的含义已被重新解释为视口边距比例
        float viewportMargin = Mathf.Clamp01(this.fovMargin);
        if (this.fovMargin != viewportMargin) // 如果原始值超出范围，给个提示
        {
            Debug.LogWarning($"成员变量 fovMargin ({this.fovMargin}) 超出有效范围 [0, 1]，已自动限制为 {viewportMargin}。");
        }
        // ----------------

        // --- 核心计算逻辑 (直接在视口空间操作) ---
        List<Vector2> coords = new List<Vector2>(xSize * zSize); // 预设列表容量

        // 根据视口边距计算目标子区域的最小/最大视口坐标
        float halfMargin = viewportMargin * 0.5f;
        float minX = 0.5f - halfMargin;
        float maxX = 0.5f + halfMargin;
        float minY = 0.5f - halfMargin; // Y轴使用相同的边距
        float maxY = 0.5f + halfMargin;

        // --- 使用原有的循环变量名 x 和 z ---
        // 外层循环变量 'z' 代表网格的高度索引 (对应视口 Y 坐标)
        for (int z = 0; z < zSize; z++)
        {
            // 计算 Y 方向的插值比例 (0% 到 100%)
            // 当 zSize = 1 时，比例为 0.5 (中心)
            float yFraction = (zSize > 1) ? (float)z / (zSize - 1) : 0.5f;
            // 计算 Y 视口坐标，变量名用回 'yCoord'
            float yCoord = minY + yFraction * (maxY - minY);

            // 内层循环变量 'x' 代表网格的宽度索引 (对应视口 X 坐标)
            for (int x = 0; x < xSize; x++)
            {
                // 计算 X 方向的插值比例 (0% 到 100%)
                // 当 xSize = 1 时，比例为 0.5 (中心)
                float xFraction = (xSize > 1) ? (float)x / (xSize - 1) : 0.5f;
                // 计算 X 视口坐标，变量名用回 'xCoord'
                float xCoord = minX + xFraction * (maxX - minX);

                // 添加计算出的坐标点 Vector2(xCoord, yCoord)
                coords.Add(new Vector2(xCoord, yCoord));
            }
        }

        return coords; // 返回填充好的列表
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

    void CreateOrResetSelectionSphere()
    {
        if (selectionBox != null) Destroy(selectionBox);
        selectionBox = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionBox.transform.localScale = boxSize;
        selectionBox.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 0.5f;
        selectionBox.transform.rotation = Quaternion.identity;
        Collider col = selectionBox.GetComponent<Collider>(); if (col != null) col.enabled = false;
        Renderer renderer = selectionBox.GetComponent<MeshRenderer>();
        Material mat = boxMaterial != null ? new Material(boxMaterial) : new Material(Shader.Find("Lit"));
        if (boxMaterial == null) { mat.SetFloat("_Mode", 3); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0); mat.DisableKeyword("_ALPHATEST_ON"); mat.EnableKeyword("_ALPHABLEND_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); mat.renderQueue = 3000; }
        mat.color = new Color(0, 1, 1, 0.3f); renderer.material = mat;
        Debug.Log("Selection Sphere created/reset.");
    }
}
