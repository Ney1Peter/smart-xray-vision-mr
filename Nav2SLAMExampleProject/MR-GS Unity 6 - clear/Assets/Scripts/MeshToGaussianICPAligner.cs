using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ICP_GICPWrapperInterface;

public class MeshToGaussianICPAligner : MonoBehaviour
{
    [Header("目标 Mesh 对象 (参考)")]
    public GameObject targetMeshObject;  // 参考点云对象来自 Mesh

    [Header("源 GaussianRenderer 对象 (待对齐)")]
    public GaussianSplatRenderer sourceRenderer;  // 待对齐对象

    [Header("ICP 参数")]
    public int maxIterations = 500;
    public float transformationEpsilon = 0.00001f;
    public bool doDownsample = false;
    public float voxelSize = 0.2f;

    // 点云数据（世界坐标）
    private List<Vector3> cloudTarget = new List<Vector3>(); // 来自 targetMeshObject
    private List<Vector3> cloudSource = new List<Vector3>();   // 来自 sourceRenderer


    void Start()
    {
        if (targetMeshObject == null || sourceRenderer == null)
        {
            Debug.LogError("请指定 targetMeshObject 和 sourceRenderer");
            return;
        }

        // ------------------------------
        // 提取目标 Mesh 的点云数据（转换为世界坐标）
        MeshFilter targetMF = targetMeshObject.GetComponent<MeshFilter>();
        if (targetMF == null || targetMF.sharedMesh == null)
        {
            Debug.LogError("targetMeshObject 不含 MeshFilter 或 Mesh");
            return;
        }
        cloudTarget = ExtractPointCloudFromMesh(targetMF);
        if (cloudTarget == null || cloudTarget.Count == 0)
        {
            Debug.LogError("提取的 targetMeshObject 点云数据为空！");
            return;
        }
        Debug.Log("成功获取 targetMeshObject 的点云数据。");
        cloudTarget = DownsamplePointCloud(cloudTarget, voxelSize);

        // ------------------------------
        // 提取源对象的点云数据（从 GPU 数据，转换为世界坐标）
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


        // ------------------------------
/*        // 中心化处理：以质心为原点，生成中心化后的点云数据供 ICP 使用
        List<Vector3> centeredTarget = CenterCloud(cloudTarget, centroidTarget);
        List<Vector3> centeredSource = CenterCloud(cloudSource, centroidSource);*/

        float[] targetArray = ConvertPointCloudToFloatArray(cloudTarget);
        float[] sourceArray = ConvertPointCloudToFloatArray(cloudSource);

        // ------------------------------
        // 运行 ICP（参考点云为 target，待对齐点云为 source）
        var result = ICP_GICPWrapperInterface.RunGICP(
            targetArray, targetArray.Length,
            sourceArray, sourceArray.Length,
            maxIterations, transformationEpsilon,
            doDownsample,
            0.1f,false
        );
/*
        Debug.Log($"ICP前参考点云质心: ({result.centroid_ref_before[0]}, {result.centroid_ref_before[1]}, {result.centroid_ref_before[2]})");
        Debug.Log($"ICP前目标点云质心: ({result.centroid_target_before[0]}, {result.centroid_target_before[1]}, {result.centroid_target_before[2]})");
*/
        if (!result.converged)
        {
            Debug.LogError("ICP 未收敛！");
            return;
        }
        Debug.Log("ICP 收敛！");

        // ------------------------------
        // 重构转换矩阵 T_pcl（C++ 返回的矩阵为行主序，转换为 Unity 的列主序）
        Matrix4x4 T_pcl = new Matrix4x4();
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

        Vector3 centroidSource = new Vector3(result.centroid_target_before[0], result.centroid_target_before[1], result.centroid_target_before[2]);
        Vector3 centroidTarget = new Vector3(result.centroid_ref_before[0], result.centroid_ref_before[1], result.centroid_ref_before[2]);


        Debug.Log("targetRenderer 质心 from pcl: " + centroidTarget);
        Debug.Log("sourceRenderer 质心 from pcl: " + centroidSource);

        Vector3 centroidTargetUnity = ComputeCentroid(cloudTarget);
        Vector3 centroidSourceUnity = ComputeCentroid(cloudSource);

        Debug.Log("targetRenderer 质心 from unity: " + centroidTargetUnity);
        Debug.Log("sourceRenderer 质心 from unity: " + centroidSourceUnity);

        Debug.Log("targetRenderer privot unity: " + targetMeshObject.transform.position);
        Debug.Log("sourceRenderer privot unity: " + sourceRenderer.transform.position);

        Matrix4x4 T_pcl_unity = T_pcl;


        // 步骤2：分解变换矩阵为旋转和平移
        Quaternion R = ExtractRotation(T_pcl_unity);

        Vector3 t = ExtractTranslation(T_pcl_unity);

        // 步骤3：计算质心偏移补偿
        // PCL的变换是基于质心坐标系的，需要转换到Unity的世界坐标系
        Vector3 deltaSource = centroidSource - sourceRenderer.transform.position;
        Vector3 deltaTarget = centroidTarget - targetMeshObject.transform.position;

        // 步骤4：计算最终变换
        // 应用旋转后的质心偏移补偿
        /*        Vector3 finalTranslation = t + (Quaternion.Inverse(R)* sourceRenderer.transform.position) 
                    + (deltaTarget - Quaternion.Inverse(R) * deltaSource);*/
        Vector3 finalTranslation = t + (Quaternion.Inverse(R) * sourceRenderer.transform.position);
        Debug.Log($"补偿量计算centirod：R*ΔT({deltaTarget}) + R*ΔS({R * deltaSource}) = {deltaTarget + Quaternion.Inverse(R) * deltaSource}");
        Debug.Log($"补偿量计算pivot：R*ΔS({Quaternion.Inverse(R) * sourceRenderer.transform.position})");

/*        Debug.Log($"补偿量计算pivot：R({R})* (ΔS-ΔT)({sourceRenderer.transform.position - targetMeshObject.transform.position})  = {Quaternion.Inverse(R) * (sourceRenderer.transform.position - targetMeshObject.transform.position)}");
*/        
        // 步骤5：应用变换
        
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


    List<Vector3> DownsamplePointCloud(List<Vector3> points, float voxelSize)
    {
        // 使用字典存储每个体素内的点累加值及计数
        Dictionary<Vector3Int, (Vector3 sum, int count)> voxelMap = new Dictionary<Vector3Int, (Vector3, int)>();

        foreach (var p in points)
        {
            // 将点 p 的位置除以 voxelSize，然后向下取整得到体素坐标
            Vector3Int voxelCoord = new Vector3Int(
                Mathf.FloorToInt(p.x / voxelSize),
                Mathf.FloorToInt(p.y / voxelSize),
                Mathf.FloorToInt(p.z / voxelSize)
            );

            if (voxelMap.ContainsKey(voxelCoord))
            {
                var entry = voxelMap[voxelCoord];
                voxelMap[voxelCoord] = (entry.sum + p, entry.count + 1);
            }
            else
            {
                voxelMap.Add(voxelCoord, (p, 1));
            }
        }

        // 用每个体素内所有点的平均值作为代表点
        List<Vector3> downsampled = new List<Vector3>(voxelMap.Count);
        foreach (var kvp in voxelMap)
        {
            Vector3 avgPoint = kvp.Value.sum / kvp.Value.count;
            downsampled.Add(avgPoint);
        }
        return downsampled;
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

    // 从 MeshFilter 中提取点云数据，转换为世界坐标
    List<Vector3> ExtractPointCloudFromMesh(MeshFilter mf)
    {
        List<Vector3> points = new List<Vector3>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("MeshFilter 或 Mesh 缺失！");
            return points;
        }
        Mesh mesh = mf.sharedMesh;
        Transform t = mf.transform;
        Vector3[] vertices = mesh.vertices;
        points = new List<Vector3>(vertices.Length);
        Matrix4x4 localToWorld = t.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            points.Add(localToWorld.MultiplyPoint3x4(vertices[i]));
        }
        Debug.Log($"从 targetMeshObject 中提取了 {points.Count} 个点作为目标点云数据。");
        return points;
    }

    // 从 GraphicsBuffer 中提取点云数据，转换为世界坐标（基于传入的 Transform）
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
            Vector3 worldPoint = objTransform != null ? objTransform.TransformPoint(localPoint) : localPoint;
            points.Add(worldPoint);
        }
        return points;
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
