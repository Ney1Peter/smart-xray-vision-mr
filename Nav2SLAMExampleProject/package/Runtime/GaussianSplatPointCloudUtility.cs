using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    public static class GaussianSplatPointCloudUtility
    {
        /// <summary>
        /// 从 GPU 端的 m_GpuPosData 中提取点云数据（假设数据采用 Float32 格式，每个点由 3 个 float 构成）。
        /// </summary>
        /// <param name="gpuPosData">GPU 端存储位置信息的 GraphicsBuffer</param>
        /// <returns>点云数据列表</returns>
        public static List<Vector3> GetPointCloudFromGpuPos(GraphicsBuffer gpuPosData)
        {
            List<Vector3> points = new List<Vector3>();
            if (gpuPosData == null)
            {
                Debug.LogError("gpuPosData 为 null！");
                return points;
            }

            // gpuPosData.count 表示的是总的 float 数量（每个元素4字节）
            int totalFloats = gpuPosData.count;
            if (totalFloats % 3 != 0)
            {
                Debug.LogWarning("gpuPosData 中的 float 数量不是 3 的倍数，可能数据格式不对！");
            }
            int numPoints = totalFloats / 3;

            // 分配一个数组来读取数据
            float[] posData = new float[totalFloats];
            gpuPosData.GetData(posData);

            for (int i = 0; i < numPoints; i++)
            {
                int idx = i * 3;
                Vector3 p = new Vector3(posData[idx], posData[idx + 1], posData[idx + 2]);
                points.Add(p);
            }

            Debug.Log($"从 GPU buffer 中提取了 {points.Count} 个点");
            return points;
        }

        /// <summary>
        /// 计算提取的点云数据包围盒，并输出对比信息，验证数据是否与资产记录一致
        /// </summary>
        /// <param name="points">提取的点云数据</param>
        /// <param name="asset">GaussianSplatAsset 对象</param>
        public static void ValidatePointCloud(List<Vector3> points, GaussianSplatAsset asset)
        {
            if (points == null || points.Count == 0)
            {
                Debug.LogError("点云为空，无法验证！");
                return;
            }

            Vector3 computedMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 computedMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            foreach (var p in points)
            {
                computedMin = Vector3.Min(computedMin, p);
                computedMax = Vector3.Max(computedMax, p);
            }

            Debug.Log($"计算得到的包围盒：Min {computedMin}, Max {computedMax}");
            Debug.Log($"Asset 中记录的包围盒：Min {asset.boundsMin}, Max {asset.boundsMax}");
        }

        /// <summary>
        /// 检查资产中位置信息的格式是否为 Float32，并输出相关信息
        /// </summary>
        /// <param name="asset">GaussianSplatAsset 对象</param>
        public static void CheckDataFormat(GaussianSplatAsset asset)
        {
            if (asset.posFormat == GaussianSplatAsset.VectorFormat.Float32)
            {
                Debug.Log("资产的位置信息采用 Float32 格式");
            }
            else
            {
                Debug.LogWarning($"资产的位置信息不是 Float32，而是 {asset.posFormat}，请根据需要实现对应的解码逻辑！");
            }
        }
    }
}
