using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine.Profiling;
using Random = Unity.Mathematics.Random; // 明确使用 Mathematics.Random

// [修改] 确保这两个 using 指向你项目中 KNN API 所在的实际命名空间
using KNN;
using KNN.Jobs; // Job 类型可能在子命名空间中

public class BasicKnnUsage : MonoBehaviour
{
    [Header("演示参数")]
    public int numPoints = 10000; // 要创建的随机点数量
    public int kNeighbours = 5;   // 每次查询要找的最近邻数量 K

    // --- 内部数据 ---
    private NativeArray<float3> m_Points;       // 存储点云数据
    private KnnContainer m_Container;          // KNN 加速结构
    private bool m_IsReady = false;           // 标记是否初始化完成

    void Start()
    {
        Debug.Log("--- KNN API 基础演示 ---");
        Profiler.BeginSample("KnnDemo.Setup");

        // 1. 创建点云数据
        m_Points = new NativeArray<float3>(numPoints, Allocator.Persistent);
        var rand = new Random(123456);
        for (int i = 0; i < m_Points.Length; ++i)
        {
            m_Points[i] = rand.NextFloat3() * 10f; // 在 10x10x10 立方体内
        }
        Debug.Log($"创建了 {numPoints} 个随机点。");

        // 2. 创建 KNN 容器并立即构建加速结构
        try
        {
            // 假设构造函数 KnnContainer(NativeArray<float3> points, bool buildNow, Allocator allocator)
            m_Container = new KnnContainer(m_Points, true, Allocator.Persistent);
            m_IsReady = true;
            Debug.Log("KnnContainer 创建并构建完成。按空格键执行查询...");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"创建或构建 KnnContainer 时出错: {ex.Message}");
            m_IsReady = false;
        }

        Profiler.EndSample();
    }

    void Update()
    {
        if (!m_IsReady) return;

        // 按下空格键时执行一次 K 近邻查询
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Profiler.BeginSample("Demo.SingleKnnQuery");
            float3 queryPosition = new float3(5f, 5f, 5f);
            Debug.Log($"执行 K近邻查询 (K={kNeighbours})，查询点: {queryPosition}");

            // 准备结果数组 (临时)
            var results = new NativeArray<int>(kNeighbours, Allocator.TempJob);

            // 创建查询 Job
            // 假设构造函数 QueryKNearestJob(KnnContainer container, float3 queryPos, NativeArray<int> results)
            var queryJob = new QueryKNearestJob(m_Container, queryPosition, results);

            // 调度 Job 并等待它完成
            JobHandle handle = queryJob.Schedule();
            handle.Complete();

            // 处理并打印结果
            Debug.Log($"查询完成，找到 {kNeighbours} 个最近邻的索引:");
            string resultString = "Indices: ";
            if (results.IsCreated && results.Length > 0) // 检查结果数组是否有效
            {
                for (int i = 0; i < results.Length; ++i)
                {
                    resultString += results[i] + " ";
                }
            }
            else
            {
                resultString += "(Error or No Results)";
            }
            Debug.Log(resultString);

            // **非常重要：释放临时分配的内存！**
            if (results.IsCreated) results.Dispose(); // 添加 IsCreated 检查更安全

            Profiler.EndSample();
            Debug.Log("--- 查询演示结束 ---");
        }
    }

    void OnDestroy()
    {
        Debug.Log("销毁 KNN 资源...");

        // 释放持久化分配的 NativeArray
        if (m_Points.IsCreated)
            m_Points.Dispose();

        // 释放 KnnContainer
        if (m_IsReady) // 只有初始化成功才释放
        {
            // 假设 KnnContainer 实现了 IDisposable
            try { m_Container.Dispose(); } catch { } // Dispose 不应抛异常，但安全起见
        }

        m_IsReady = false;
        Debug.Log("KNN 资源已释放。");
    }
}