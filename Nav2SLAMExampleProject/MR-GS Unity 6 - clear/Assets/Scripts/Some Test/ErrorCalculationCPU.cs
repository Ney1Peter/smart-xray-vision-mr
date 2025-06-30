using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Collections.Generic;
using System.Linq; // 需要 LINQ
using UnityEngine.Profiling;
using Random = Unity.Mathematics.Random;

// --- 引入必要的命名空间 ---
// KNN API (你需要确认实际的命名空间)
using KNN;
using KNN.Jobs;
// HdbscanSharp API (你需要确认实际的命名空间)
using Dbscan; // 可能包含 ClusterSet 等基础类型
using HdbscanSharp.Distance;
using HdbscanSharp.Hdbscanstar;
using HdbscanSharp.Runner;

public class KnnErrorAndClusteringProcessor : MonoBehaviour // 重命名类以反映功能
{
    [Header("数据引用 (外部设置)")]
    [Tooltip("GICP 对齐后的源点云 Buffer (例如 GS 的 posBuffer)")]
    public GraphicsBuffer sourcePointsBuffer;
    [Tooltip("目标点云的 CPU 列表 (例如 Room Mesh 的点)")]
    public List<Vector3> targetPointsCPU;

    [Header("参数设置")]
    public int splatCount;                   // 3DGS 点数量 (需要外部设置)
    [Tooltip("聚类时视为高误差的阈值")]
    public float errorThreshold = 0.1f;
    [Tooltip("HDBSCAN* MinPoints 参数")]
    public int hdbscanMinPoints = 15;
    [Tooltip("HDBSCAN* MinClusterSize 参数")]
    public int hdbscanMinClusterSize = 10;
    [Tooltip("KNN 容器节点容量")]
    public int knnNodeCapacity = 64;
    // 注意: searchRadius 在 K=1 NN 误差计算中未使用，但 HDBSCAN* 内部有自己的距离概念

    // --- 内部状态 ---
    private KnnContainer m_TargetKnnContainer;
    private NativeArray<float3> m_TargetPointsNative;
    private NativeArray<float> m_ErrorMagnitudes; // 存储误差 (持久化)
    private bool m_IsTargetSetup = false;
    private JobHandle m_RunningJobHandle;
    // 存储高误差点信息，用于聚类和结果映射
    private List<(int originalIndex, double[] position)> highErrorDataForDbscan;

    // --- 主要公开方法 ---

    /// <summary>
    /// 执行完整的误差计算和聚类流程
    /// </summary>
    /// <returns>包含高误差区域点原始索引的列表，如果出错或无有效簇则返回 null 或空列表</returns>
    public List<List<int>> RunErrorCalculationAndClustering()
    {
        Profiler.BeginSample("RunErrorCalculationAndClustering");

        // 1. 确保目标 KNN 已设置
        if (!m_IsTargetSetup)
        {
            if (!SetupTarget(this.targetPointsCPU)) // 使用当前成员变量
            {
                Profiler.EndSample();
                return null;
            }
        }

        // 2. 计算误差 (异步启动，同步等待，返回 CPU 数组)
        float[] errorsCPU = CalculateErrorsAndWait();
        if (errorsCPU == null)
        {
            Profiler.EndSample();
            return null;
        }

        // 3. 读取源点坐标到 CPU (聚类需要)
        // !! 性能瓶颈点 !!
        Profiler.BeginSample("GetData Source Points for Clustering");
        Vector3[] sourcePointsCPU = new Vector3[splatCount];
        if (sourcePointsBuffer == null || !sourcePointsBuffer.IsValid() || sourcePointsBuffer.count != splatCount)
        {
            Debug.LogError("源点 Buffer 无效或数量不匹配!"); Profiler.EndSample(); Profiler.EndSample(); return null;
        }
        sourcePointsBuffer.GetData(sourcePointsCPU);
        Profiler.EndSample(); // GetData

        // 4. 执行聚类
        Profiler.BeginSample("Run Clustering (HDBSCAN)");
        List<List<int>> clusters = ClusterHighErrorPoints(sourcePointsCPU, errorsCPU);
        Profiler.EndSample(); // Run Clustering

        Profiler.EndSample(); // RunErrorCalculationAndClustering
        return clusters;
    }


    // --- 内部实现 ---

    /// <summary>
    /// 设置或更新目标点云 Q，并构建 KNN 加速结构。
    /// </summary>
    private bool SetupTarget(List<Vector3> targetDataQ)
    {
        Profiler.BeginSample("KNN SetupTarget");
        DisposeTargetResources(); // 清理旧资源

        if (targetDataQ == null || targetDataQ.Count == 0) { Debug.LogError("[KnnErrorCluster] 目标点云 Q 为空!"); Profiler.EndSample(); return false; }

        m_TargetPointsNative = new NativeArray<float3>(targetDataQ.Count, Allocator.Persistent);
        for (int i = 0; i < targetDataQ.Count; ++i) m_TargetPointsNative[i] = targetDataQ[i];

        try
        {
            m_TargetKnnContainer = new KnnContainer(m_TargetPointsNative, true, Allocator.Persistent);
            m_IsTargetSetup = true;
            Debug.Log($"[KnnErrorCluster] 目标点云 KNN 设置完成 ({m_TargetPointsNative.Length} points)");
        }
        catch (System.Exception ex) { Debug.LogError($"[KnnErrorCluster] 创建或构建 KnnContainer 时出错: {ex.Message}"); DisposeTargetResources(); m_IsTargetSetup = false; Profiler.EndSample(); return false; }

        Profiler.EndSample();
        return true;
    }

    /// <summary>
    /// 启动误差计算 Job 并同步等待结果, 返回 CPU 上的误差数组
    /// </summary>
    private float[] CalculateErrorsAndWait()
    {
        if (!m_IsTargetSetup || sourcePointsBuffer == null || !sourcePointsBuffer.IsValid() || splatCount <= 0) { Debug.LogError("CalculateErrorsAndWait: 前提条件未满足!"); return null; }

        // 确保误差 Buffer 已创建或大小正确 (持久化)
        if (!m_ErrorMagnitudes.IsCreated || m_ErrorMagnitudes.Length != splatCount)
        {
            if (m_ErrorMagnitudes.IsCreated) m_ErrorMagnitudes.Dispose();
            m_ErrorMagnitudes = new NativeArray<float>(splatCount, Allocator.Persistent);
        }

        // --- 准备临时 NativeArrays ---
        NativeArray<float3> sourcePointsNativeTemp = default;
        NativeArray<int> knnIndicesTemp = default;
        JobHandle finalHandle = default;
        float[] errorResultsCPU = null;

        try
        {
            Profiler.BeginSample("CalculateErrors_Jobs");
            // --- 1. 准备源点 P' (临时) ---
            sourcePointsNativeTemp = new NativeArray<float3>(splatCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            Vector3[] sourcePointsCPU_temp = new Vector3[splatCount]; // 临时CPU数组用于GetData
            sourcePointsBuffer.GetData(sourcePointsCPU_temp);
            for (int i = 0; i < splatCount; ++i) sourcePointsNativeTemp[i] = sourcePointsCPU_temp[i];
            sourcePointsCPU_temp = null;

            // --- 2. 准备 K=1 查询结果 (临时) ---
            knnIndicesTemp = new NativeArray<int>(splatCount, Allocator.TempJob);

            // --- 3. 调度 KNN 查询 Job ---
            var knnQueryJob = new QueryKNearestBatchJob(m_TargetKnnContainer, sourcePointsNativeTemp, knnIndicesTemp);
            JobHandle knnHandle = knnQueryJob.ScheduleBatch(splatCount, 128);

            // --- 4. 调度计算距离 Job (结果写入持久化 m_ErrorMagnitudes) ---
            var calcDistJob = new CalculateDistanceJob
            {
                SourcePoints = sourcePointsNativeTemp,
                TargetPoints = m_TargetPointsNative,
                NearestIndices = knnIndicesTemp,
                OutputErrorDistances = m_ErrorMagnitudes // 写入成员变量
            };
            finalHandle = calcDistJob.Schedule(splatCount, 64, knnHandle);

            // --- 5. 将临时 NativeArray 的 Dispose 加入依赖链 ---
            finalHandle = sourcePointsNativeTemp.Dispose(finalHandle);
            finalHandle = knnIndicesTemp.Dispose(finalHandle);

            // --- 6. 等待 Job 完成 ---
            finalHandle.Complete();

            // --- 7. 从持久化 NativeArray 获取结果到 CPU ---
            Profiler.BeginSample("GetData Error Magnitudes");
            errorResultsCPU = new float[splatCount];
            m_ErrorMagnitudes.CopyTo(errorResultsCPU); // 从 m_ErrorMagnitudes 读取
            Profiler.EndSample();

            Profiler.EndSample(); // CalculateErrors_Jobs
        }
        catch (System.Exception ex) { Debug.LogError($"[KnnErrorCluster] 计算误差时发生错误: {ex.Message}\n{ex.StackTrace}"); errorResultsCPU = null; }
        finally
        {
            // 确保临时资源在出错时也被尝试释放 (Complete 会保证依赖的 Dispose 执行)
            // 但如果 finalHandle 未赋值就出错，需要手动处理
            if (finalHandle.IsCompleted)
            { // 检查句柄是否有效
                if (sourcePointsNativeTemp.IsCreated) sourcePointsNativeTemp.Dispose();
                if (knnIndicesTemp.IsCreated) knnIndicesTemp.Dispose();
            }
        }
        return errorResultsCPU;
    }

    /// <summary>
    /// 对高误差点进行 HDBSCAN* 聚类
    /// </summary>
    private List<List<int>> ClusterHighErrorPoints(Vector3[] sourcePoints, float[] errors)
    {
        Profiler.BeginSample("ClusterHighErrorPoints (HDBSCAN)");
        List<List<int>> finalClusters = new List<List<int>>();

        // --- 1. 筛选高误差点并准备 HDBSCAN 输入 (double[][]) ---
        highErrorDataForDbscan = new List<(int originalIndex, double[] position)>();
        var highErrorCoordinates = new List<double[]>();

        if (sourcePoints == null || errors == null || sourcePoints.Length != errors.Length) { Debug.LogError("聚类输入数据无效!"); Profiler.EndSample(); return finalClusters; }

        for (int i = 0; i < sourcePoints.Length; i++)
        {
            if (errors[i] > errorThreshold && !float.IsInfinity(errors[i]) && !float.IsNaN(errors[i]))
            {
                highErrorDataForDbscan.Add((i, new double[3] { sourcePoints[i].x, sourcePoints[i].y, sourcePoints[i].z }));
                highErrorCoordinates.Add(highErrorDataForDbscan[highErrorDataForDbscan.Count - 1].position);
            }
        }

        int numHighErrorPoints = highErrorCoordinates.Count;
        Debug.Log($"筛选出 {numHighErrorPoints} 个高误差点 (阈值={errorThreshold}) 进行聚类。");
        if (numHighErrorPoints < hdbscanMinClusterSize || numHighErrorPoints < hdbscanMinPoints) { Profiler.EndSample(); return finalClusters; }

        // --- 2. 执行 HDBSCAN* ---
        HdbscanResult result = null;
        try
        {
            Debug.Log($"开始 HDBSCAN*... MinPoints={hdbscanMinPoints}, MinClusterSize={hdbscanMinClusterSize}");
            // !! 核心调用: 使用 HdbscanRunner.Run !!
            // !! 确认距离函数用法，这里假设使用 DistanceHelpers 和一个 EuclideanDistance 类 !!
            var distanceCalculator = new EuclideanDistance(); // 需要这个类存在
            var distanceFunc = DistanceHelpers.GetFunc(distanceCalculator, highErrorCoordinates.ToArray(), null, false, 0);
            result = HdbscanRunner.Run(highErrorCoordinates.Count, hdbscanMinPoints, hdbscanMinClusterSize, distanceFunc);
            if (result != null && result.HasInfiniteStability) { Debug.LogWarning("HDBSCAN 报告存在无限稳定性问题..."); }
        }
        catch (System.Exception ex) { Debug.LogError($"HdbscanRunner.Run 计算出错: {ex.Message}"); Profiler.EndSample(); return finalClusters; }

        // --- 3. 处理结果 ---
        if (result == null || result.Labels == null || result.Labels.Length != numHighErrorPoints) { Debug.LogError("HDBSCAN 返回结果无效。"); Profiler.EndSample(); return finalClusters; }

        int[] clusterIds = result.Labels;
        int noiseLabel = 0; // !! 假设噪声标签为 0，请根据 HdbscanSharp 文档确认 !!
        int maxClusterId = clusterIds.Max(); // 找到最大簇 ID

        if (maxClusterId >= (noiseLabel == 0 ? 1 : 0))
        {
            Dictionary<int, List<int>> clustersDict = new Dictionary<int, List<int>>();
            for (int i = 0; i < clusterIds.Length; i++)
            {
                int clusterId = clusterIds[i];
                if (clusterId != noiseLabel)
                { // 忽略噪声点
                    if (!clustersDict.ContainsKey(clusterId)) clustersDict[clusterId] = new List<int>();
                    // 通过 highErrorDataForDbscan 映射回原始索引
                    clustersDict[clusterId].Add(highErrorDataForDbscan[i].originalIndex);
                }
            }
            finalClusters.AddRange(clustersDict.Values);
            // (可选) 可以再根据簇大小过滤一次，虽然 minClusterSize 应该已经处理了
            // finalClusters.RemoveAll(list => list.Count < hdbscanMinClusterSize);
        }

        Debug.Log($"HDBSCAN* 完成，找到 {finalClusters.Count} 个高误差簇。");
        Profiler.EndSample(); // ClusterHighErrorPoints
        return finalClusters;
    }


    // --- 计算距离的 Job (不变) ---
    [BurstCompile]
    private struct CalculateDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> SourcePoints;
        [ReadOnly] public NativeArray<float3> TargetPoints;
        [ReadOnly] public NativeArray<int> NearestIndices;
        [WriteOnly] public NativeArray<float> OutputErrorDistances;
        public void Execute(int index)
        {
            float3 p_source = SourcePoints[index];
            int targetIndex = NearestIndices[index];
            if (targetIndex >= 0 && targetIndex < TargetPoints.Length)
            {
                OutputErrorDistances[index] = math.distance(p_source, TargetPoints[targetIndex]);
            }
            else { OutputErrorDistances[index] = float.PositiveInfinity; }
        }
    }

    // --- 清理持久化资源 ---
    void OnDestroy()
    {
        m_RunningJobHandle.Complete(); // 确保所有 Job 完成
        DisposeTargetResources();
        if (m_ErrorMagnitudes.IsCreated) m_ErrorMagnitudes.Dispose();
        Debug.Log("[KnnErrorCluster] OnDestroy: 已释放资源。");
    }
    private void DisposeTargetResources()
    {
        if (m_TargetPointsNative.IsCreated) m_TargetPointsNative.Dispose();
        if (m_IsTargetSetup) { try { m_TargetKnnContainer.Dispose(); } catch { } } // 安全 Dispose
        m_IsTargetSetup = false;
    }

    // --- 辅助类 (用于 HdbscanSharp DistanceHelpers) ---
    // !! 你需要确认 HdbscanSharp 是否自带或需要你自己实现这个 !!
    public class EuclideanDistance : IDistanceCalculator<double[]>
    {
        public double ComputeDistance(int index1, int index2, double[] attributes1, double[] attributes2)
        {
            // 假设是 3D
            if (attributes1 == null || attributes2 == null || attributes1.Length < 3 || attributes2.Length < 3) return double.PositiveInfinity;
            double dx = attributes1[0] - attributes2[0];
            double dy = attributes1[1] - attributes2[1];
            double dz = attributes1[2] - attributes2[2];
            // 返回距离平方可能更快，如果 HDBSCAN 允许或内部处理了
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    // --- KNN 命名空间占位符 (你需要用实际的库) ---
    // namespace KNN { /* ... KnnContainer, QueryKNearestBatchJob 等定义 ... */ }
    // --- HdbscanSharp 需要的定义 (确保 using 正确) ---
    // namespace HdbscanSharp.Distance { public interface IDistanceCalculator<T> { ... } }
    // ... 其他 HdbscanSharp using ...
}