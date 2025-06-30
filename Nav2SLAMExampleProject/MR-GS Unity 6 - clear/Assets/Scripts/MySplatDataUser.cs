using System.Collections.Generic;
using UnityEngine;
using GaussianSplatting.Runtime;

public class MySplatDataUser : MonoBehaviour
{
    // 在 Inspector 中拖拽你想要获取数据的 GaussianSplatRenderer 实例
    public GaussianSplatRenderer targetRenderer;

    void Start()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("没有指定目标 GaussianSplatRenderer");
            return;
        }

        // 通过扩展方法获取目标 GaussianSplatRenderer 的 GPU 位置信息缓冲区
        GraphicsBuffer gpuPosBuffer = targetRenderer.GetGpuPosData();
        if (gpuPosBuffer == null)
        {
            Debug.LogError("无法获取目标 GaussianSplatRenderer 的 GPU 位置信息。");
            return;
        }

        // 使用之前的工具类从 GPU 缓冲区提取点云数据
        List<Vector3> pointCloud = GaussianSplatPointCloudUtility.GetPointCloudFromGpuPos(gpuPosBuffer);
        if (pointCloud == null || pointCloud.Count == 0)
        {
            Debug.LogError("提取的点云数据为空！");
            return;
        }

        Debug.Log("成功获取目标 GaussianSplatRenderer 的 GPU 位置信息，并提取了点云数据。");

        // 验证点云数据的有效性：计算包围盒并与资产中的边界对比
        GaussianSplatPointCloudUtility.ValidatePointCloud(pointCloud, targetRenderer.asset);
    }
}
