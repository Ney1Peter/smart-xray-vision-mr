using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ICP_GICPWrapperInterface;

public class DualGaussianRendererICPAligner : MonoBehaviour
{
    [Header("目标 GaussianRenderer 对象 (参考)")]
    public GaussianSplatRenderer targetRenderer;  // 参考点云对象

    [Header("源 GaussianRenderer 对象 (待对齐)")]
    public GaussianSplatRenderer sourceRenderer;  // 待对齐对象

    [Header("ICP 参数")]
    public int maxIterations = 500;
    public float transformationEpsilon = 0.00001f;
    public bool doDownsample = false;

    // 点云数据（世界坐标）
    private List<Vector3> cloudTarget = new List<Vector3>(); // 来自 targetRenderer
    private List<Vector3> cloudSource = new List<Vector3>();   // 来自 sourceRenderer

    void Start()
    {
        if (targetRenderer == null || sourceRenderer == null)
        {
            Debug.LogError("请指定 targetRenderer 和 sourceRenderer");
            return;
        }

        // 提取目标对象的点云数据
        GraphicsBuffer gpuPosBufferTarget = targetRenderer.GetGpuPosData();
        if (gpuPosBufferTarget == null)
        {
            Debug.LogError("无法获取 targetRenderer 的 GPU 位置信息。");
            return;
        }
        cloudTarget = ExtractPointCloudFromGraphicsBuffer(gpuPosBufferTarget, targetRenderer.transform);
        if (cloudTarget == null || cloudTarget.Count == 0)
        {
            Debug.LogError("提取的 targetRenderer 点云数据为空！");
            return;
        }
        Debug.Log("成功获取 targetRenderer 的 GPU 点云数据。");

        // 提取源对象的点云数据
        GraphicsBuffer gpuPosBufferSource = sourceRenderer.GetGpuPosData();
        if (gpuPosBufferSource == null)
        {
            Debug.LogError("无法获取 sourceRenderer 的 GPU 位置信息。");
            return;
        }
        cloudSource = ExtractPointCloudFromGraphicsBuffer(gpuPosBufferSource, sourceRenderer.transform);
        if (cloudSource == null || cloudSource.Count == 0)
        {
            Debug.LogError("提取的 sourceRenderer 点云数据为空！");
            return;
        }
        Debug.Log("成功获取 sourceRenderer 的 GPU 点云数据。");

        // 计算两个点云的质心（世界坐标）

        Debug.Log("sourceRenderer数量为" + cloudSource.Count);
        Debug.Log("targetRenderer数量为" + cloudTarget.Count);
        /*        // 可选择中心化处理（这里以中心化后的数据进行 ICP）
                List<Vector3> centeredTarget = CenterCloud(cloudTarget, centroidTarget);
                List<Vector3> centeredSource = CenterCloud(cloudSource, centroidSource);*/

        float[] targetArray = ConvertPointCloudToFloatArray(cloudTarget);
        float[] sourceArray = ConvertPointCloudToFloatArray(cloudSource);

        // 运行 ICP：第一个参数为参考点云（target），第二个为待对齐点云（source）
        var result = ICP_GICPWrapperInterface.RunGICP(
            targetArray, targetArray.Length,
            sourceArray, sourceArray.Length,
            maxIterations, transformationEpsilon,
            doDownsample,
            0.1f,
            false
        );

        if (!result.converged)
        {
            Debug.LogError("ICP 未收敛！");
            return;
        }
        Debug.Log("ICP 收敛！");

        // 构造 PCL 坐标系下的转换矩阵 T_pcl
        Matrix4x4 T_pcl = new Matrix4x4();
        // 注意：C++ 中的矩阵为行主序，而 Unity 的 Matrix4x4 为列主序
        T_pcl.m00 = result.matrix[0];
        T_pcl.m01 = result.matrix[4];
        T_pcl.m02 = result.matrix[8];
        T_pcl.m03 = result.matrix[3];  // 平移 x

        T_pcl.m10 = result.matrix[1];
        T_pcl.m11 = result.matrix[5];
        T_pcl.m12 = result.matrix[9];
        T_pcl.m13 = result.matrix[7];  // 平移 y

        T_pcl.m20 = result.matrix[2];
        T_pcl.m21 = result.matrix[6];
        T_pcl.m22 = result.matrix[10];
        T_pcl.m23 = result.matrix[11]; // 平移 z

        T_pcl.m30 = result.matrix[12];
        T_pcl.m31 = result.matrix[13];
        T_pcl.m32 = result.matrix[14];
        T_pcl.m33 = result.matrix[15];

        Debug.Log("Unity 原始 ICP 变换矩阵: \n" + T_pcl);

        Debug.Log("targetRenderer pivot" + sourceRenderer.transform.position);
        Debug.Log("sourceRenderer pivot" + targetRenderer.transform.position);
        // 对源对象应用 ICP 变换（注意：ICP 得到的变换是基于点云质心对齐的）
        // 1. 计算源对象的 pivot 与几何质心之间的偏移 d
        Vector3  centroidSource = new Vector3 (result.centroid_target_before[0], result.centroid_target_before[1], result.centroid_target_before[2]);
        Vector3 centroidTarget = new Vector3(result.centroid_ref_before[0], result.centroid_ref_before[1], result.centroid_ref_before[2]);


        Debug.Log("targetRenderer 质心 from pcl: " + centroidTarget);
        Debug.Log("sourceRenderer 质心 from pcl: " + centroidSource);

        Vector3 centroidTargetUnity = ComputeCentroid(cloudTarget);
        Vector3 centroidSourceUnity = ComputeCentroid(cloudSource);

        Debug.Log("targetRenderer 质心 from unity: " + centroidTargetUnity);
        Debug.Log("sourceRenderer 质心 from unity: " + centroidSourceUnity);

        /*        Debug.Log($"ICP前参考点云质心: ({result.centroid_ref_before[0]}, {result.centroid_ref_before[1]}, {result.centroid_ref_before[2]})");
                Debug.Log($"ICP前目标点云质心: ({result.centroid_target_before[0]}, {result.centroid_target_before[1]}, {result.centroid_target_before[2]})");*/

        // 关键修正部分开始
        // 步骤1：将PCL的变换矩阵转换到Unity坐标系
/*        Matrix4x4 T_pcl_unity = ConvertPCLMatrixToUnity(T_pcl);
*/        Matrix4x4 T_pcl_unity = T_pcl;


        // 步骤2：分解变换矩阵为旋转和平移
        Quaternion R = ExtractRotation(T_pcl_unity);
        Vector3 t = ExtractTranslation(T_pcl_unity);

        // 步骤3：计算质心偏移补偿
        // PCL的变换是基于质心坐标系的，需要转换到Unity的世界坐标系
        Vector3 deltaSource = centroidSource - sourceRenderer.transform.position;
        Vector3 deltaTarget = centroidTarget - targetRenderer.transform.position;

        // 步骤4：计算最终变换
        // 应用旋转后的质心偏移补偿
        Vector3 finalTranslation = t + Quaternion.Inverse(R) * sourceRenderer.transform.position;
        Debug.Log($"补偿量计算centirod：ΔT({deltaTarget}) - R*ΔS({R * deltaSource}) = {deltaTarget - Quaternion.Inverse(R) * deltaSource}");
        Debug.Log($"补偿量计算pivot：R*ΔS-ΔT({Quaternion.Inverse(R) * sourceRenderer.transform.position}-{targetRenderer.transform.position})  = {Quaternion.Inverse(R) * (sourceRenderer.transform.position - targetRenderer.transform.position)}");

/*        Debug.Log($"补偿量计算pivot：R({R})* (ΔS-ΔT)({sourceRenderer.transform.position - targetRenderer.transform.position})  = {Quaternion.Inverse(R) * (sourceRenderer.transform.position -  targetRenderer.transform.position)}");
*/        // 步骤5：应用变换
        sourceRenderer.transform.rotation = Quaternion.Inverse(R) * sourceRenderer.transform.rotation;
        sourceRenderer.transform.position = finalTranslation;

        

        Debug.Log("targetRenderer t: " + t);
        Debug.Log($"最终平移：{finalTranslation}");

    }

    // ----------------- 工具函数 -----------------

    float[] ConvertPointCloudToFloatArray(List<Vector3> cloud)
    {
        float[] arr = new float[cloud.Count * 3];
        for (int i = 0; i < cloud.Count; i++)
        {
            arr[i * 3 + 0] = cloud[i].x;
            arr[i * 3 + 1] = cloud[i].y;
            arr[i * 3 + 2] = cloud[i].z;
        }
        return arr;
    }

    Vector3 ComputeCentroid(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points)
            sum += p;
        return (points.Count > 0) ? sum / points.Count : Vector3.zero;
    }

    List<Vector3> CenterCloud(List<Vector3> cloud, Vector3 centroid)
    {
        List<Vector3> centered = new List<Vector3>(cloud.Count);
        foreach (Vector3 p in cloud)
            centered.Add(p - centroid);
        return centered;
    }

    List<Vector3> ExtractPointCloudFromGraphicsBuffer(GraphicsBuffer buffer, Transform objTransform)
    {
        if (buffer == null)
        {
            Debug.LogError("GraphicsBuffer 为 null！");
            return new List<Vector3>();
        }

        int totalFloats = buffer.count;
        if (totalFloats % 3 != 0)
        {
            Debug.LogWarning("GPU 数据中的 float 数量不是 3 的倍数！");
        }
        int numPoints = totalFloats / 3;
        List<Vector3> points = new List<Vector3>(numPoints);
        float[] data = new float[totalFloats];
        buffer.GetData(data);
        for (int i = 0; i < numPoints; i++)
        {
            int idx = i * 3;
            Vector3 localPoint = new Vector3(data[idx], data[idx + 1], data[idx + 2]);
            // 如果提供了 Transform，则转换成世界坐标，否则保留局部坐标
            Vector3 worldPoint = objTransform != null ? objTransform.TransformPoint(localPoint) : localPoint;
            points.Add(worldPoint);
        }
        return points;
    }



    Matrix4x4 ConvertPCLMatrixToUnity(Matrix4x4 pclMatrix)
    {
        // 步骤1：创建Z轴翻转矩阵
        Matrix4x4 flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));

        // 步骤2：应用坐标系转换到旋转部分
        Matrix4x4 rotationConverted = flipZ * pclMatrix * flipZ;

        // 步骤3：手动修正平移分量（直接取反Z轴平移）
        rotationConverted.m23 *= -1;

        return rotationConverted;
    }

    Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        // 从矩阵提取旋转分量
        Vector3 forward = matrix.GetColumn(2);
        Vector3 up = matrix.GetColumn(1);
        return Quaternion.LookRotation(forward, up);
    }

    Vector3 ExtractTranslation(Matrix4x4 matrix)
    {
        // 直接获取平移分量
        return matrix.GetColumn(3);
    }

}
