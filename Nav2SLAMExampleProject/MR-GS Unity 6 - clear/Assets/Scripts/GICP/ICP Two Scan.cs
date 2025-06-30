using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ICPTwoScan : MonoBehaviour
{
    // 指定目标 GaussianSplatRenderer（待注册点云）
    public GaussianSplatRenderer targetRenderer;
    public GaussianSplatRenderer referRenderer;
    // RoomMeshEvent 用于获取房间网格的 MeshFilter（参考点云）
/*    [SerializeField] private RoomMeshEvent roomMeshEvent;
*/
    // 从 GaussianSplatRenderer 中提取的点云数据
    private List<Vector3> gsPointTarget = new List<Vector3>();
    // 从房间网格中提取的点云数据
    private List<Vector3> gsPointRefer = new List<Vector3>();
    public int maxIterations = 500;
    public float transformationEpsilon = 0.00001f;
    public bool doDownsample = false;





    void Start()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("没有指定目标 Target GaussianSplatRenderer");
            return;
        }

        // 获取目标 GaussianSplatRenderer 的 GPU 位置信息缓冲区
        GraphicsBuffer gpuPosBuffer1 = targetRenderer.GetGpuPosData();
        if (gpuPosBuffer1 == null)
        {
            Debug.LogError("无法获取目标 GaussianSplatRenderer 的 GPU 位置信息。");
            return;
        }


        if (referRenderer == null)
        {
            Debug.LogError("没有指定目标 Refer GaussianSplatRenderer");
            return;
        }

        // 获取目标 GaussianSplatRenderer 的 GPU 位置信息缓冲区
        GraphicsBuffer gpuPosBuffer2 = referRenderer.GetGpuPosData();
        if (gpuPosBuffer2 == null)
        {
            Debug.LogError("无法获取目标 GaussianSplatRenderer 的 GPU 位置信息。");
            return;
        }



        // 从 GPU 缓冲区提取 GaussianSplat 点云数据
        gsPointTarget = ExtractPointCloudFromGraphicsBuffer(gpuPosBuffer1);
        if (gsPointTarget == null || gsPointTarget.Count == 0)
        {
            Debug.LogError("提取的 Target GaussianSplat 点云数据为空！");
            return;
        }
        Debug.Log("成功获取目标 Target GaussianSplatRenderer 的 GPU 位置信息，并提取了点云数据。");


        gsPointRefer = ExtractPointCloudFromGraphicsBuffer(gpuPosBuffer2);
        if (gsPointRefer == null || gsPointRefer.Count == 0)
        {
            Debug.LogError("提取的 Refer GaussianSplat 点云数据为空！");
            return;
        }
        Debug.Log("成功获取目标 Refer GaussianSplatRenderer 的 GPU 位置信息，并提取了点云数据。");

        Vector3 centroidA = ComputeCentroid(gsPointRefer);
        Vector3 centroidB = ComputeCentroid(gsPointTarget);
        Debug.Log("bunnyA 质心: " + centroidA);
        Debug.Log("bunnyB 质心: " + centroidB);

        int refCount = gsPointRefer.Count;
        int targetCount = gsPointTarget.Count;

        // 假设两个点云点数一致或至少使用参考点数（你可能需要下采样或匹配数量）
/*        int pointCount = refCount; // 注意：这要求两个点云具有相同点数，否则需要处理
*/      float[] refArray = new float[refCount * 3];
        float[] targetArray = new float[targetCount * 3];

        for (int i = 0; i < refCount; i++)
        {
            refArray[i * 3 + 0] = gsPointRefer[i].x;
            refArray[i * 3 + 1] = gsPointRefer[i].y;
            refArray[i * 3 + 2] = gsPointRefer[i].z;


        }

        for (int i = 0; i < targetCount; i++)
        {
            targetArray[i * 3 + 0] = gsPointTarget[i].x;
            targetArray[i * 3 + 1] = gsPointTarget[i].y;
            targetArray[i * 3 + 2] = gsPointTarget[i].z;
        }

        var result = ICP_GICPWrapperInterface.RunGICP(
            refArray, refArray.Length,
            targetArray, targetArray.Length,
            maxIterations, transformationEpsilon,
            doDownsample,
            0.1f, false
        );

        if (!result.converged)
        {
            Debug.LogError("ICP 未收敛！");
            return;
        }
        Debug.Log("ICP 收敛！");

        // 6. 构造正确的4x4矩阵（从C++返回的行优先数组）
        Matrix4x4 T = Matrix4x4.identity;
        for (int i = 0; i < 16; i++)
        {
            T[i] = result.matrix[i]; // 直接按行主序填充
        }
        Debug.Log("ICP 得到的变换矩阵 T: \n" + T);

        // 7. 提取旋转和平移
        Quaternion R = T.rotation;
        Vector3 t = T.GetColumn(3); // 平移分量

        // 8. 计算最终的世界变换
        // bunnyB的质心应先对齐到bunnyA的质心，再应用ICP变换
        Vector3 newPosition = centroidA + R * (centroidB - centroidA) + t;
        targetRenderer.transform.SetPositionAndRotation(newPosition, R);
        Debug.Log("更新后的 bunnyB 位置: " + newPosition + " 旋转: " + R);
    }



    /// <summary>
    /// 从 GPU 缓冲区提取点云数据，假设数据采用 Float32 格式，每个点 3 个 float
    /// </summary>
    List<Vector3> ExtractPointCloudFromGraphicsBuffer(GraphicsBuffer buffer)
    {
        List<Vector3> points = new List<Vector3>();
        if (buffer == null)
        {
            Debug.LogError("GraphicsBuffer 为 null！");
            return points;
        }
        int totalFloats = buffer.count;
        if (totalFloats % 3 != 0)
        {
            Debug.LogWarning("GPU 数据中的 float 数量不是 3 的倍数！");
        }
        int numPoints = totalFloats / 3;
        float[] data = new float[totalFloats];
        buffer.GetData(data);
        for (int i = 0; i < numPoints; i++)
        {
            int idx = i * 3;
            points.Add(new Vector3(data[idx], data[idx + 1], data[idx + 2]));
        }
        return points;
    }

    Vector3 ComputeCentroid(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points) sum += p;
        return (points.Count > 0) ? sum / points.Count : Vector3.zero;
    }



}
