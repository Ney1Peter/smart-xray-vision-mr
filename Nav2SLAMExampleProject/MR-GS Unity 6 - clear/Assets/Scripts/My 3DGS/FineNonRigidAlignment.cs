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
using UnityEngine.Rendering;

public class FineNonRigidAlignment : MonoBehaviour
{
    // --- 枚举定义 ---
    public enum Density : int
    {
        low = 32, medium = 64, high = 128, vHigh = 256, ultra = 512
    }

    // --- 序列化字段 ---
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent;
    // [修改] 恢复并暴露累积变形选项
    [Tooltip("是否累积变形效果")]
    public bool accumulateDeformation = true;
    [SerializeField] private EnvironmentRaycastManager depthManager;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;
    // [新增] Compute Shader 引用
    [SerializeField] private ComputeShader deformationComputeShader;

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
    [SerializeField] private int chunkSize = 1;
    [SerializeField] private int maxPointsPerChunk = 256;
    [SerializeField] private int initialPoolSize = 1000;

    [Space(10)]
    [Header("Camera Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField][Tooltip("Percentage of the field of view")] private float fovMargin = 0.9f;
    [SerializeField][Tooltip("软变形的影响半径 (单位: 米)")] private float viewRadius = 1.0f;

    [Space(10)]
    [Header("Scan Settings")]
    [SerializeField] private float scanInterval = 1.0f;
    [SerializeField] private Density density = Density.medium;
    [SerializeField][Tooltip("The limit is about 5m")] private float maxScanDistance = 5;

    [Space(10)]
    [Header("Rendering Settings")]
    [SerializeField] private GameObject pointPrefab;
    [SerializeField] private float renderingRadius = 10.0f;
    [SerializeField] int maxChunkCount = 15;

    [Space(10)]
    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f); // Initial size of the selection sphere
    public float moveSpeed = 1f; // Speed to move the sphere
    public Material boxMaterial; // Material for the selection sphere
    // --- 私有和内部变量 ---

    private ChunkManager pointsData;
    private Coroutine scanCoroutine = null;
    private List<Vector3> roomMeshPointsAll = new List<Vector3>();
    private List<int> selectedGSIndices = new List<int>();
    private bool roomMeshReady = false;
    private int splatCount = 0;
    private GraphicsBuffer posBuffer;         // 从 Renderer 获取，当前位置 (会被 Compute Shader 修改)
    // [新增] 存储静息位置的 GPU Buffer
    private GraphicsBuffer originalPosBuffer;

    private ComputeBuffer affectedCountBuffer;
    private List<Vector3> currentScanData = new List<Vector3>();
    private uint[] affectedCountResult = new uint[1]; // 用于接收结果的 CPU 数组
    // [新增] 缓存 Compute Shader Kernel 句柄
    private int deformationKernelHandle = -1;
    private GameObject selectionBox; // The sphere used for selection

    // --- DLL 导入和结构体 (保持不变) ---
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


    // --- Start() 函数 [修改] ---
    void Start()
    {
        if (GaussianRendererObject == null)
            GaussianRendererObject = GetComponent<GaussianSplatRenderer>();

        if (GaussianRendererObject != null)
        {
            // 1. 从 Renderer 获取 posBuffer
            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            if (posBuffer == null || !posBuffer.IsValid())
            {
                Debug.LogError("未能从 GaussianSplatRenderer 获取有效的 GPU 位置数据 (posBuffer)");
                enabled = false; return;
            }
            splatCount = GaussianRendererObject.splatCount;
            if (splatCount == 0)
            {
                Debug.LogError("GaussianSplatRenderer 报告 splatCount 为 0");
                enabled = false; return;
            }
            // 创建计数器 Buffer (类型为 Raw 或 Structured 都可以， stride 是 uint 的大小)
            affectedCountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            // 初始值设为 0
            ResetAffectedCountBuffer();

            // 2. 获取初始位置数据到临时的 CPU 数组
            Vector3[] initialPositionsCPU = new Vector3[splatCount];
            posBuffer.GetData(initialPositionsCPU);

            // 3. [新增] 创建并初始化 originalPosBuffer (存储静息位置)
            int stride = sizeof(float) * 3;
            originalPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, stride);
            originalPosBuffer.SetData(initialPositionsCPU); // 上传静息位置到 GPU

            // 4. (可选) 释放临时 CPU 数组内存
            initialPositionsCPU = null;
            Debug.Log($"成功初始化变形器: {splatCount} 个点, posBuffer 和 originalPosBuffer 已设置。");

            // 5. [新增] 获取 Compute Shader Kernel 句柄
            if (deformationComputeShader != null)
            {
                deformationKernelHandle = deformationComputeShader.FindKernel("CSMain");
                if (deformationKernelHandle < 0)
                {
                    Debug.LogError("在指定的 Compute Shader 中未找到名为 'CSMain' 的 Kernel!");
                    enabled = false; return;
                }
            }
            else
            {
                Debug.LogError("Deformation Compute Shader 未在 Inspector 中指定!");
                enabled = false; return;
            }
        }
        else
        {
            Debug.LogError("未能找到 GaussianSplatRenderer 组件!");
            enabled = false; return;
        }

        // --- 你其他的 Start() 初始化代码 ---


        pointsData = new ChunkManager(chunkSize, maxPointsPerChunk);
        if (pointCloudRenderer != null)
            pointCloudRenderer.Initialize(pointPrefab, initialPoolSize);
        else
            Debug.LogWarning("PointCloudRenderer 未指定！");

        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady);
            Debug.Log("等待 RoomMesh 加载...");
        }
        // --- 你其他的 Start() 初始化代码结束 ---
    }

    void ResetAffectedCountBuffer()
    {
        if (affectedCountBuffer != null && affectedCountBuffer.IsValid())
        {
            // 每次计算前将 GPU 上的计数器重置为 0
            // SetData 有少量 CPU->GPU 开销，但对于单个 uint 来说通常可接受
            // 更高效的方式是用一个专门的 Compute Shader Kernel 将其置零
            affectedCountBuffer.SetData(new uint[] { 0 });
        }
        else
        {
            Debug.LogError("无法重置 affectedCountBuffer，它尚未初始化或无效！");
        }
    }

    // [新

    // --- [新增] OnDestroy() - 释放我们创建的 Buffer ---
    void OnDestroy()
    {
        originalPosBuffer?.Release();
        originalPosBuffer = null;
        affectedCountBuffer?.Release();
        affectedCountBuffer = null;
        // posBuffer 通常由 Renderer 管理
        Debug.Log("FineNonRigidAlignment OnDestroy: 释放 originalPosBuffer");
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

    // --- PerformLocalRegistration() 函数 [修改] ---
    void PerformLocalRegistration()
    {
        // 检查 Buffer
        if (posBuffer == null || !posBuffer.IsValid() || originalPosBuffer == null || !originalPosBuffer.IsValid() || splatCount == 0)
        {
            Debug.LogError("GPU GS 数据缓冲区未准备好或无效");
            return;
        }

        Transform tf = GaussianRendererObject.transform;
        //Bounds boxBounds = pointsData.GetPreciseBounds(); // 获取扫描点范围
        Bounds boxBounds = new Bounds(selectionBox.transform.position, selectionBox.transform.localScale);
        selectedGSIndices.Clear(); // 清空上次选择

        // [修改] CPU 选择循环读取原始位置的临时拷贝
        // !! 注意：这仍然是性能瓶颈点 !!
        Vector3[] tempOriginalPosCPU = new Vector3[splatCount];
        originalPosBuffer.GetData(tempOriginalPosCPU); // !! 低效操作 !!

        List<Vector3> selectedGSWorld_ForICP = new List<Vector3>(); // 用于 ICP 的点

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 worldPos = tf.TransformPoint(tempOriginalPosCPU[i]); // 使用原始位置判断
            if (!boxBounds.Contains(worldPos)) continue;
            selectedGSWorld_ForICP.Add(worldPos);
            selectedGSIndices.Add(i);
        }
        tempOriginalPosCPU = null; // 清理

        Debug.Log($"提取到 {selectedGSIndices.Count} 个在扫描范围内的 GS 点");

        currentScanData = pointsData.GetAllPoints();
        Debug.Log($"获取到 {currentScanData.Count} 个当前扫描点");

        if (selectedGSIndices.Count < 10 || currentScanData.Count < 10)
        {
            Debug.LogWarning($"选中区域({selectedGSIndices.Count})或扫描点({currentScanData.Count})数量过少，无法执行局部配准");
            return;
        }

        // --- 调用 ICP (保持不变) ---
        float[] tgt = ConvertToFloatArray(currentScanData);
        float[] src = ConvertToFloatArray(selectedGSWorld_ForICP);
        Debug.Log("调用 FGR_GICP.dll 进行匹配...");
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
        Debug.Log("局部 ICP 成功");

        // --- 获取变换并应用软变形 ---
        Matrix4x4 T_ICP = GetFinalTransformation(result);
        // [修改] 调用新的 Compute Shader 函数, 并传入用于计算中心的数据
        ApplySoftDeformationWithComputeShader(T_ICP, selectedGSWorld_ForICP);
        Debug.Log("局部软变形应用完成 (Compute Shader)");
    }

    // --- !! [新增] 使用 Compute Shader 应用软变形的函数 !! ---
    void ApplySoftDeformationWithComputeShader(Matrix4x4 T_ICP, List<Vector3> selectedPointsForCenter)
    {
        // 检查资源
        if (deformationComputeShader == null || posBuffer == null || !posBuffer.IsValid() ||
            originalPosBuffer == null || !originalPosBuffer.IsValid() || affectedCountBuffer == null || !affectedCountBuffer.IsValid() ||
            splatCount == 0 || deformationKernelHandle < 0)
        {
            Debug.LogError("Compute Shader 或 Buffers 未正确初始化！无法应用变形。");
            return;
        }

        // --- CPU 工作: 计算参数 ---
        Transform tf = GaussianRendererObject.transform;
        Vector3 t_world = ExtractTranslation(T_ICP);
        Vector3 offsetLocal = tf.InverseTransformVector(t_world);

        // [修改] 使用传入的点列表计算中心，避免再次 GetData
        Vector3 center = Vector3.zero;
        if (selectedPointsForCenter != null && selectedPointsForCenter.Count > 0)
        {
            center = ComputeCentroid(selectedPointsForCenter);
        }
        else
        {
            Debug.LogWarning("传入的选择点列表为空，无法计算中心点，使用原点。");
        }
        // 1. [新增] 重置 GPU 计数器为 0
        ResetAffectedCountBuffer();
        // --- GPU 工作: 设置并调度 Compute Shader ---
        // 绑定缓冲区
        deformationComputeShader.SetBuffer(deformationKernelHandle, "originalPositions", originalPosBuffer);
        deformationComputeShader.SetBuffer(deformationKernelHandle, "positions", posBuffer);

        // 设置参数
        deformationComputeShader.SetVector("center", center);
        deformationComputeShader.SetVector("offsetLocal", offsetLocal);
        deformationComputeShader.SetFloat("viewRadius", viewRadius);
        deformationComputeShader.SetInt("splatCount", splatCount); // 传递 int
        deformationComputeShader.SetMatrix("transformMatrix", tf.localToWorldMatrix);
        deformationComputeShader.SetBuffer(deformationKernelHandle, "affectedCounter", affectedCountBuffer);
        // [修改] 传递累积变形标志
        deformationComputeShader.SetBool("accumulateDeformation", accumulateDeformation);

        // 计算线程组数量
        int threadGroupSize = 64; // 与 HLSL 匹配
        int numGroups = Mathf.CeilToInt((float)splatCount / threadGroupSize);

        // 启动计算！
        deformationComputeShader.Dispatch(deformationKernelHandle, numGroups, 1, 1);

        AsyncGPUReadback.Request(affectedCountBuffer, OnAffectedCountReadbackComplete);
    }

    void OnAffectedCountReadbackComplete(AsyncGPUReadbackRequest request)
    {
        // 检查请求是否出错
        if (request.hasError)
        {
            Debug.LogError("GPU 回读 affectedCounter 时出错!");
            return;
        }

        // 获取数据 (返回一个 NativeArray<uint>)
        // 对于 ComputeBufferType.Raw，可能需要不同的 GetData 调用或 reinterpret
        // 如果使用 ComputeBufferType.Structured 创建，GetData<uint> 通常可以工作
        var data = request.GetData<uint>();

        // 确认获取到了数据
        if (data.Length > 0)
        {
            uint count = data[0]; // 读取第一个 (也是唯一一个) 元素
            Debug.Log($"[Async Readback] 本次变形实际影响了 {count} 个点。");

            // 你可以在这里使用 count 值，例如更新 UI 或用于其他逻辑
            // 注意：这个回调可能不在主线程上执行，如果需要操作 Unity API (如修改 GameObject)，
            // 可能需要将结果暂存并在主线程的 Update 中处理。但对于 Debug.Log 通常没问题。
        }
        else
        {
            Debug.LogWarning("GPU 回读 affectedCounter 成功，但未获取到数据。");
        }
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
        Material mat = boxMaterial != null ? new Material(boxMaterial) : new Material(Shader.Find("Standard"));
        if (boxMaterial == null) { mat.SetFloat("_Mode", 3); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0); mat.DisableKeyword("_ALPHATEST_ON"); mat.EnableKeyword("_ALPHABLEND_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); mat.renderQueue = 3000; }
        mat.color = new Color(0, 1, 1, 0.3f); renderer.material = mat;
        Debug.Log("Selection Sphere created/reset.");
    }
}
