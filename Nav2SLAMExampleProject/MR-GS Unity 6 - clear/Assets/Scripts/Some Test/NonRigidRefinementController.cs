using UnityEngine;
using System.Collections.Generic;
using System.Linq; // 如果在其他地方使用了 LINQ
using System.Runtime.InteropServices; // 需要 P/Invoke
using GaussianSplatting.Runtime; // 假设存在
using Meta.XR.BuildingBlocks; // 来自你的代码


public class NonRigidRefinementController : MonoBehaviour // 你的脚本名
{
    // --- DLL 接口定义 ---
    // !! 确保这里的 DLL 名字与你编译生成的 DLL 文件名一致 !!
    private const string DllName = "ErrorClusterDLL"; // 或者 NonRigidRefinementDLL

    /* [移除] 不再需要 Vec3Marshal 结构体
    [StructLayout(LayoutKind.Sequential)]
    public struct Vec3Marshal { public float x, y, z; }
    */

    // C# 端对应的返回结构体 (保持不变)
    [StructLayout(LayoutKind.Sequential)]
    public struct ClusterResultInfo
    {
        public int numClustersFound;
        public int numPointsProcessed;
        public int numHighErrorPoints;
        [MarshalAs(UnmanagedType.I1)] public bool success;
    }

    // [修改] DllImport 签名，使用 float[]
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ClusterResultInfo CalculateAndClusterErrors(
        [In] float[] sourcePointsFlat, // <-- 改为 float[]
        int numSourcePoints,          // <-- 传递的是 *点的数量*
        [In] float[] targetPointsFlat, // <-- 改为 float[]
        int numTargetPoints,          // <-- 传递的是 *点的数量*
        float errorThreshold,
        float clusterTolerance,
        int clusterMinPoints,
        float nnSearchMaxDistSq,
        [Out] int[] outClusterIds    // 输出不变 (int[])
    );

    // --- 参数 ---
    [Header("数据引用")]
    public GaussianSplatRenderer GaussianRendererObject; // 需要设置
    public RoomMeshEvent roomMeshEvent; // 如果需要 Room Mesh 数据

    [Header("非刚性配准参数")]
    [Tooltip("聚类时视为高误差的阈值")]
    public float errorThreshold = 0.1f;
    [Tooltip("PCL欧式聚类的距离容差 (epsilon)")]
    public float clusterTolerance = 0.1f;
    [Tooltip("PCL欧式聚类的最小簇点数")]
    public int clusterMinPoints = 15;
    [Tooltip("误差计算中最近邻搜索的最大距离")]
    public float maxCorrespondenceDistance = 0.2f;


    [Header("Gizmo 可视化设置")]
    public bool visualizeClustersWithGizmos = true;
    public float centroidGizmoSize = 0.05f;
    public bool showAABB = true;
    public bool showRadius = true;
    // public Color noiseColor = Color.gray; // 可以保留用于显示未聚类点
    // public bool showLowErrorPoints = false;
    public Color[] clusterPalette = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white };

    // --- 运行时数据 (示例) ---
    // 这些需要在使用前被正确填充
    private GraphicsBuffer sourcePointsBuffer; // 代表 P'
    private int splatCount;                  // P' 的点数
    private List<Vector3> targetPointsCPU;     // 代表 Q
    private List<Vector3> sourcePointsCPU;
    private bool roomMeshReady = false;
    private bool gaussianBufferReady = false;
    private List<ClusterInfo> m_CalculatedClusterInfos;
    // --- 结果存储 ---
    [HideInInspector] public int[] RefinedClusterIds;      // 存储每个点的簇 ID (-1 表示无簇/低误差)
    [HideInInspector] public int numberOfValidClusters;  // 存储找到的有效簇数量
    [HideInInspector] public int numberOfHighErrorPoints;

    public struct ClusterInfo
    {
        public int ClusterId;              // 簇的 ID (例如 0, 1, 2...)
        public Vector3 Centroid;             // 簇的几何中心点坐标
        public Bounds AABB;                 // 簇的轴对齐包围盒
        public float MaxRadiusFromCentroid; // 簇内点到中心点的最大距离 (作为影响半径)
        public List<int> OriginalPointIndices; // 属于这个簇的点的原始索引列表
    }

    /// <summary>
    /// 根据聚类结果计算每个簇的中心点、包围盒和半径等信息。
    /// </summary>
    /// <param name="allSourcePoints">所有源点的坐标</param>
    /// <param name="allClusterIds">每个源点对应的簇 ID 数组</param>
    /// <param name="numClusters">有效簇的数量</param>
    private void CalculateClusterProperties(IList<Vector3> allSourcePoints, int[] allClusterIds, int numClusters)
    {
        if (allClusterIds == null || allSourcePoints == null || allClusterIds.Length != allSourcePoints.Count || numClusters <= 0)
        {
            m_CalculatedClusterInfos = new List<ClusterInfo>(); // 返回空列表
            return;
        }

        m_CalculatedClusterInfos = new List<ClusterInfo>(numClusters);
        var pointsInClusterTemp = new List<Vector3>(); // 临时列表存储坐标
        var indicesInClusterTemp = new List<int>();   // 临时列表存储索引

        // 遍历每一个有效的簇 ID
        for (int k = 0; k < numClusters; k++)
        {
            pointsInClusterTemp.Clear();
            indicesInClusterTemp.Clear();

            // 找到所有属于当前簇 k 的点
            for (int i = 0; i < allClusterIds.Length; ++i)
            {
                if (allClusterIds[i] == k)
                {
                    indicesInClusterTemp.Add(i); // 记录原始索引
                    pointsInClusterTemp.Add(allSourcePoints[i]); // 记录坐标
                }
            }

            // 如果这个簇实际有点 (理论上应该有，因为聚类算法过滤了)
            if (pointsInClusterTemp.Count > 0)
            {
                // a. 计算中心点 (Centroid)
                Vector3 sum = Vector3.zero;
                foreach (Vector3 pos in pointsInClusterTemp) { sum += pos; }
                Vector3 centroid = sum / pointsInClusterTemp.Count;

                // b. 计算包围盒 (AABB)
                Bounds bounds = new Bounds(pointsInClusterTemp[0], Vector3.zero);
                for (int i = 1; i < pointsInClusterTemp.Count; ++i)
                {
                    bounds.Encapsulate(pointsInClusterTemp[i]);
                }

                // c. 计算最大半径 (从中心点出发)
                float maxRadiusSq = 0f;
                foreach (Vector3 pos in pointsInClusterTemp)
                {
                    maxRadiusSq = Mathf.Max(maxRadiusSq, Vector3.SqrMagnitude(pos - centroid));
                }
                float maxRadius = Mathf.Sqrt(maxRadiusSq);

                // 存储这个簇的信息
                m_CalculatedClusterInfos.Add(new ClusterInfo
                {
                    ClusterId = k,
                    Centroid = centroid,
                    AABB = bounds,
                    MaxRadiusFromCentroid = maxRadius,
                    OriginalPointIndices = new List<int>(indicesInClusterTemp) // 复制索引列表
                });
            }
        }
        Debug.Log($"计算了 {m_CalculatedClusterInfos.Count} 个簇的属性。");
    }


    void OnDrawGizmos()
    {
        if (!visualizeClustersWithGizmos || m_CalculatedClusterInfos == null)
        {
            return; // 未启用或数据未计算
        }

        // 可选：绘制低误差/噪声点 (需要 RefinedClusterIds 和 sourcePointsCPU)
        // if (showLowErrorPoints && RefinedClusterIds != null && sourcePointsCPU != null) { ... }

        // 遍历计算出的每个簇的信息
        foreach (var clusterInfo in m_CalculatedClusterInfos)
        {
            // 设置颜色
            Gizmos.color = clusterPalette[clusterInfo.ClusterId % clusterPalette.Length];

            // 绘制中心点
            Gizmos.DrawSphere(clusterInfo.Centroid, centroidGizmoSize);

            // 绘制包围盒
            if (showAABB)
            {
                Gizmos.DrawWireCube(clusterInfo.AABB.center, clusterInfo.AABB.size);
            }

            // 绘制最大半径球
            if (showRadius)
            {
                Gizmos.DrawWireSphere(clusterInfo.Centroid, clusterInfo.MaxRadiusFromCentroid);
            }
        }
    }

    private void Start()
    {
        InitializeData();

        
    }

    private void Update()
    {
        if (roomMeshReady)
        {
            RunClusteringStep();
            roomMeshReady = false;
            

        }
    }

    void InitializeData() // 
    {
        // 获取目标点云 (例如从 Room Mesh)
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady);
            Debug.Log("等待 RoomMesh 加载...");
        }
        // 获取 GraphicsBuffer (假设 GaussianRendererObject 已设置)
        if (GaussianRendererObject != null)
        {
            sourcePointsBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            splatCount = GaussianRendererObject.splatCount;
            sourcePointsCPU = ExtractPointCloudFromGraphicsBuffer(sourcePointsBuffer, GaussianRendererObject.transform);
            gaussianBufferReady = true;
        }
        else
        {
            Debug.LogError("GaussianRendererObject 未设置!");
            return;
        }
        if (sourcePointsBuffer == null || splatCount == 0)
        {
            Debug.LogError("无法获取有效的源点云 Buffer!");
            return;
        }




        //Debug.Log($"初始化完成: Source Points (GPU Buffer): {splatCount}, Target Points (CPU List): {targetPointsCPU.Count}");
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

        targetPointsCPU.Clear();
        foreach (var v in vertices)
        {
            targetPointsCPU.Add(localToWorld.MultiplyPoint3x4(v));
        }

        
        Debug.Log($"RoomMesh 加载完成，提取 {targetPointsCPU.Count} 个点");
        roomMeshReady = true;
    }


    // --- 触发聚类计算的函数 ---
    /// <summary>
    /// 调用 C++ DLL 计算误差并执行聚类
    /// </summary>
    /// <returns>聚类步骤是否成功</returns>
    /// 
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

    public bool RunClusteringStep()
    {

        Debug.Log($"初始化完成: Source Points (GPU Buffer): {splatCount}, Target Points (CPU List): {targetPointsCPU.Count}");
        // 0. 确保数据已初始化 (调用 InitializeData 或类似函数)
        if (sourcePointsBuffer == null || targetPointsCPU == null || splatCount == 0)
        {
            Debug.LogError("需要先初始化源点和目标点数据！");
            InitializeData(); // 尝试初始化
            if (sourcePointsBuffer == null || targetPointsCPU == null || splatCount == 0) return false; // 再次检查
        }

        // 1. 从 GraphicsBuffer 获取源点到 CPU (性能瓶颈!)
        
        

        // 2. 准备扁平化的 float[] 数组
        
        float[] sourcePointsFlat = new float[splatCount * 3];
        for (int i = 0; i < splatCount; ++i)
        {
            sourcePointsFlat[i * 3 + 0] = sourcePointsCPU[i].x;
            sourcePointsFlat[i * 3 + 1] = sourcePointsCPU[i].y;
            sourcePointsFlat[i * 3 + 2] = sourcePointsCPU[i].z;
        }

        int numTargetPoints = targetPointsCPU.Count;
        float[] targetPointsFlat = new float[numTargetPoints * 3];
        for (int i = 0; i < numTargetPoints; ++i)
        {
            targetPointsFlat[i * 3 + 0] = targetPointsCPU[i].x;
            targetPointsFlat[i * 3 + 1] = targetPointsCPU[i].y;
            targetPointsFlat[i * 3 + 2] = targetPointsCPU[i].z;
        }
        

        // 3. 分配输出内存
        // RefinedClusterIds 在类级别声明，确保其大小正确
        if (RefinedClusterIds == null || RefinedClusterIds.Length != splatCount)
        {
            RefinedClusterIds = new int[splatCount];
        }
        // C++ 端会初始化为 -1，这里无需再次初始化


        // 4. 调用 DLL
        Debug.Log("调用 C++ DLL (CalculateAndClusterErrors 使用 float[])...");
        
        ClusterResultInfo result = CalculateAndClusterErrors(
            sourcePointsFlat, splatCount,          // 传入扁平数组和 *点数*
            targetPointsFlat, numTargetPoints,    // 传入扁平数组和 *点数*
            errorThreshold,
            clusterTolerance,
            clusterMinPoints,
            maxCorrespondenceDistance * maxCorrespondenceDistance, // 传入距离平方
            RefinedClusterIds // 传入 C# 分配的输出数组
        );
        

        // 5. 处理结果
        this.numberOfValidClusters = result.numClustersFound;
        this.numberOfHighErrorPoints = result.numHighErrorPoints;
        if (result.success)
        {
            Debug.Log($"C++ DLL 误差计算和聚类成功完成，找到 {result.numHighErrorPoints} 个High error 点。");
            Debug.Log($"C++ DLL 误差计算和聚类成功完成，找到 {numberOfValidClusters} 个簇。");
            CalculateClusterProperties(sourcePointsCPU, this.RefinedClusterIds, this.numberOfValidClusters);
            // 现在 this.RefinedClusterIds 数组包含了每个点的簇 ID (-1 表示低误差/噪声)
            // 你可以进行下一步：根据这个 ID 列表触发局部 GICP
            // TriggerLocalGICPs(this.RefinedClusterIds, this.numberOfValidClusters);
            return true;
        }
        else
        {
            Debug.LogError("C++ DLL 误差计算或聚类失败。");
            this.RefinedClusterIds = null; // 清空结果
            this.numberOfValidClusters = 0;
            return false;
        }
    }


}