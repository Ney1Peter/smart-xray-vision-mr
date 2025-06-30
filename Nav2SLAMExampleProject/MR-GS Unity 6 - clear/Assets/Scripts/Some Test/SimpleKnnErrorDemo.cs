using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.Profiling;
using Random = Unity.Mathematics.Random; // 明确使用 Mathematics.Random

// 假设 KNN 功能在你项目中的 KNN 命名空间下
using KNN;
using KNN.Jobs;


public class SimpleKnnErrorDemo : MonoBehaviour
{
    [Header("参数设置")]
    public int numSourcePoints = 100000; // 源点云数量 (模拟 P')
    public int numTargetPoints = 100000; // 目标点云数量 (模拟 Q)
    public float pointCloudScale = 5.0f;  // 点云生成范围的尺度
    public float sourceOffset = 0.1f;     // 给源点云加一点偏移，模拟 GICP 后的残差

    // --- 内部数据 ---
    private NativeArray<float3> m_SourcePoints;       // 源点 P' (持久化)
    private NativeArray<float3> m_TargetPoints;       // 目标点 Q (持久化)
    private KnnContainer m_TargetKnnContainer;     // 目标点 Q 的 KNN 结构 (持久化)
    private NativeArray<float> m_ErrorMagnitudes;     // 存储误差结果 (持久化)
    private bool m_IsReady = false;                  // 标记是否初始化完成

    // --- Job 句柄 ---
    private JobHandle m_ErrorCalculationHandle;      // 用于跟踪计算 Job

    void Start()
    {
        Debug.Log("--- 基础 KNN 误差计算演示 ---");
        Profiler.BeginSample("SimpleKnnDemo.Setup");

        // 1. 创建目标点云 Q
        m_TargetPoints = new NativeArray<float3>(numTargetPoints, Allocator.Persistent);
        var rand = new Random(123456); // 固定种子
        for (int i = 0; i < m_TargetPoints.Length; ++i)
        {
            m_TargetPoints[i] = (rand.NextFloat3() - 0.5f) * pointCloudScale; // 在中心附近的立方体内
        }
        Debug.Log($"创建了 {numTargetPoints} 个目标点 (Q)。");

        // 2. 创建源点云 P' (给它加一点偏移)
        m_SourcePoints = new NativeArray<float3>(numSourcePoints, Allocator.Persistent);
        float3 offset = new float3(sourceOffset, 0, 0);
        for (int i = 0; i < m_SourcePoints.Length; ++i)
        {
            // 在目标点云相似的位置上加一点偏移
            int targetIndex = rand.NextInt(0, numTargetPoints); // 从目标点随机取样位置
            m_SourcePoints[i] = m_TargetPoints[targetIndex] + offset + (rand.NextFloat3() - 0.5f) * 0.01f; // 加偏移和微小抖动
            // 或者像之前一样完全随机生成，但确保范围有重叠
            // m_SourcePoints[i] = (rand.NextFloat3() - 0.5f) * pointCloudScale + offset;
        }
        Debug.Log($"创建了 {numSourcePoints} 个源点 (P')。");


        // 3. 为目标点云 Q 创建 KNN 容器并构建
        try
        {
            m_TargetKnnContainer = new KnnContainer(m_TargetPoints, true, Allocator.Persistent);
            Debug.Log("目标点云 KNN 容器创建并构建完成。");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"创建或构建 KnnContainer 时出错: {ex.Message}");
            OnDestroy(); // 出错时清理已分配的内存
            return;
        }


        // 4. 创建用于存储误差结果的 NativeArray
        m_ErrorMagnitudes = new NativeArray<float>(numSourcePoints, Allocator.Persistent);

        m_IsReady = true;
        Profiler.EndSample(); // End Setup
        Debug.Log("初始化完成。按空格键计算误差...");
    }

    void Update()
    {
        if (!m_IsReady) return;

        // 确保之前的 Job 已完成（如果允许连续按键）
        m_ErrorCalculationHandle.Complete();

        // 按下空格键时执行误差计算
        if (Input.GetKeyDown(KeyCode.Space))
        {
            m_ErrorCalculationHandle = CalculateErrors(); // 启动计算并获取 JobHandle
            // 在这个简单的例子里，我们将在 LateUpdate 中等待它完成
            // 在实际应用中，你可能希望将依赖传递给下一个 Job
        }
    }

    void LateUpdate()
    {
        // 等待当前帧启动的 Job 完成
        m_ErrorCalculationHandle.Complete();

        // (可选) 如果需要，可以在这里读取并处理 m_ErrorMagnitudes 的结果
        // 例如，打印前几个误差值
        if (Input.GetKeyDown(KeyCode.Space)) // 确保只在计算后打印一次
        {
            if (m_ErrorMagnitudes.IsCreated && m_ErrorMagnitudes.Length > 0)
            {
                string errorStr = "计算出的前 10 个误差距离: ";
                for (int i = 0; i < math.min(10, m_ErrorMagnitudes.Length); ++i)
                {
                    errorStr += m_ErrorMagnitudes[i].ToString("F4") + " ";
                }
                Debug.Log(errorStr);
            }
        }
    }


    /// <summary>
    /// 启动异步计算误差的 Job 链
    /// </summary>
    /// <returns>最终计算 Job 的 Handle</returns>
    JobHandle CalculateErrors()
    {
        if (!m_IsReady) return default;

        Profiler.BeginSample("Schedule Error Calculation Jobs");

        // --- 1. 准备 K=1 查询结果的 NativeArray (临时) ---
        // 分配内存用于存储每个源点最近邻的目标点索引
        var knnResultsIndices = new NativeArray<int>(numSourcePoints, Allocator.TempJob);

        // --- 2. 调度 K=1 的最近邻查询 Job ---
        var knnQueryJob = new QueryKNearestBatchJob(
            m_TargetKnnContainer,      // 在目标点云中查找
            m_SourcePoints,           // 查询点是源点
            knnResultsIndices         // 结果写入这里 (索引)
        );
        // 调度 K=1 查询
        JobHandle knnHandle = knnQueryJob.ScheduleBatch(numSourcePoints, 64); // 64 可调

        // --- 3. 调度计算距离的 Job ---
        var calcDistJob = new CalculateDistanceJob
        {
            SourcePoints = m_SourcePoints,          // 输入源点
            TargetPoints = m_TargetPoints,          // 输入目标点
            NearestIndices = knnResultsIndices,    // 输入最近邻索引
            OutputErrorDistances = m_ErrorMagnitudes // 输出误差距离到持久化数组
        };
        // 依赖于 KNN Job 完成
        JobHandle finalHandle = calcDistJob.Schedule(numSourcePoints, 32, knnHandle); // 32 可调

        // --- 4. 将临时 NativeArray 的 Dispose 加入依赖链 ---
        // 确保 knnResultsIndices 在使用它的 Job (calcDistJob) 完成后被释放
        finalHandle = knnResultsIndices.Dispose(finalHandle);

        Profiler.EndSample();
        return finalHandle; // 返回 Job Handle
    }

    // --- 计算距离的 Job (与之前相同) ---
    [BurstCompile]
    private struct CalculateDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> SourcePoints;
        [ReadOnly] public NativeArray<float3> TargetPoints;
        [ReadOnly] public NativeArray<int> NearestIndices; // K=1 的结果

        [WriteOnly] public NativeArray<float> OutputErrorDistances;

        public void Execute(int index)
        {
            float3 p_source = SourcePoints[index];
            int targetIndex = NearestIndices[index];

            if (targetIndex >= 0 && targetIndex < TargetPoints.Length)
            {
                float3 q_target = TargetPoints[targetIndex];
                OutputErrorDistances[index] = math.distance(p_source, q_target);
            }
            else
            {
                OutputErrorDistances[index] = float.PositiveInfinity; // 或其他标记值
            }
        }
    }


    // --- 清理持久化资源 ---
    void OnDestroy()
    {
        // 确保 Job 完成，防止在 Job 还在使用时 Dispose
        m_ErrorCalculationHandle.Complete();

        Debug.Log("销毁 KNN 演示资源...");
        if (m_SourcePoints.IsCreated) m_SourcePoints.Dispose();
        if (m_TargetPoints.IsCreated) m_TargetPoints.Dispose();
        m_TargetKnnContainer.Dispose();
        if (m_ErrorMagnitudes.IsCreated) m_ErrorMagnitudes.Dispose();
        m_IsReady = false;
        Debug.Log("KNN 演示资源已释放。");
    }


}