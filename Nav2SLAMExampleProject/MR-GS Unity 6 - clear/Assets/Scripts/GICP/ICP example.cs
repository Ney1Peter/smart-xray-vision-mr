
using GaussianSplatting.Runtime;
using Meta.XR.BuildingBlocks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ICP_GICPWrapperInterface;

public class ICPexample : MonoBehaviour
{
    // 目标点云来自 GaussianSplatRenderer，目标物体 z-scale 必须保持 -1
    // 参考点云来自房间网格
    [SerializeField] private RoomMeshEvent roomMeshEvent;

    [SerializeField] private GaussianSplatRenderer GaussianRendererObject;

    private List<Vector3> cloudTarget = new List<Vector3>(); // 来自 targetMeshObject
    private List<Vector3> cloudSource = new List<Vector3>(); // 来自 GaussianRendererObject

    public int maxIterations = 500;
    public float transformationEpsilon = 0.00001f;
    public bool doVoxelPCL = false;
    public bool doVoxelUnity = false;
    public bool useRANSAC = false;
    public float voxelSize = 0.2f;



    void Start()
    {
        if (roomMeshEvent == null || GaussianRendererObject == null)
        {
            Debug.LogError("请指定 targetMeshObject 和 sourceRenderer");
            return;
        }

        GraphicsBuffer gpuPosBuffer = GaussianRendererObject.GetGpuPosData();
        if (gpuPosBuffer == null)
        {
            Debug.LogError("无法获取目标 GaussianSplatRenderer 的 GPU 位置信息。");
            return;
        }

        cloudSource = ExtractPointCloudFromGraphicsBuffer(gpuPosBuffer,GaussianRendererObject.transform);
        if (cloudSource == null || cloudSource.Count == 0)
        {
            Debug.LogError("提取的 GaussianSplat 点云数据为空！");
            return;
        }
        Debug.Log("成功获取目标 GaussianSplatRenderer 的 GPU 位置信息，并提取了点云数据。");

        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshLoaded);
        }
        else
        {
            Debug.LogWarning("RoomMeshEvent 没有设置，请在 Inspector 中赋值。");
        }
    }

    private void OnDestroy()
    {
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.RemoveListener(OnRoomMeshLoaded);
        }
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

    private void OnRoomMeshLoaded(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("未找到可导出的网格数据。");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Transform t = mf.transform;
        Vector3[] vertices = mesh.vertices;
        // 预先设置容量
        
        // 缓存 localToWorldMatrix，避免重复调用 TransformPoint
        Matrix4x4 localToWorld = t.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            cloudTarget.Add(localToWorld.MultiplyPoint3x4(vertices[i]));
        }
        Debug.Log($"从场景网格中提取了 {cloudTarget.Count} 个点作为参考点云数据。");

        if (cloudTarget.Count == 0 || cloudTarget.Count == 0)
        {
            Debug.LogError("无法运行 ICP，因为其中一个点云数据为空。");
            return;
        }

        if (doVoxelUnity)
        {
            cloudTarget = DownsamplePointCloud(cloudTarget, voxelSize);
/*            cloudSource = DownsamplePointCloud(cloudSource, voxelSize);
*/        }



        float[] targetArray = ConvertPointCloudToFloatArray(cloudTarget);
        float[] sourceArray = ConvertPointCloudToFloatArray(cloudSource);
        /*        float[] cloudAArray = ConvertPointCloudToFloatArray(cloudA);
                float[] cloudBArray = ConvertPointCloudToFloatArray(cloudB);*/

        var result = ICP_GICPWrapperInterface.RunGICP(
            targetArray, targetArray.Length,
            sourceArray, sourceArray.Length,
            maxIterations, transformationEpsilon,
            doVoxelPCL,
            voxelSize,
            useRANSAC
        );

        if (!result.converged)
        {
            Debug.LogError("ICP 未收敛！");
            return;
        }
        Debug.Log("ICP 收敛！");

        // 直接使用按转换规则构造 Unity 的矩阵（消除冗余赋值）
        Matrix4x4 T_pcl = new Matrix4x4();
        // 根据你的转换规则：C++ 中的矩阵是行主序，而 Unity 是列主序
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

        Debug.Log("Unity原始变换矩阵: \n" + T_pcl);
        Vector3 centroidSource = new Vector3(result.centroid_target_before[0], result.centroid_target_before[1], result.centroid_target_before[2]);
        Vector3 centroidTarget = new Vector3(result.centroid_ref_before[0], result.centroid_ref_before[1], result.centroid_ref_before[2]);


        Debug.Log("targetRenderer 质心 from pcl: " + centroidTarget);
        Debug.Log("sourceRenderer 质心 from pcl: " + centroidSource);

        Vector3 centroidTargetUnity = ComputeCentroid(cloudTarget);
        Vector3 centroidSourceUnity = ComputeCentroid(cloudSource);

        Debug.Log("targetRenderer 质心 from unity: " + centroidTargetUnity);
        Debug.Log("sourceRenderer 质心 from unity: " + centroidSourceUnity);

        Debug.Log("targetRenderer privot unity: " + roomMeshEvent.transform.position);
        Debug.Log("sourceRenderer privot unity: " + GaussianRendererObject.transform.position);

        Matrix4x4 T_pcl_unity = T_pcl;

        // 步骤2：分解变换矩阵为旋转和平移
        Quaternion R = ExtractRotation(T_pcl_unity);

        Vector3 t_tran = ExtractTranslation(T_pcl_unity);

        // 步骤3：计算质心偏移补偿
        // PCL的变换是基于质心坐标系的，需要转换到Unity的世界坐标系
        Vector3 deltaSource = centroidSource - roomMeshEvent.transform.position;
        Vector3 deltaTarget = centroidTarget - GaussianRendererObject.transform.position;


        Vector3 finalTranslation = t_tran + Quaternion.Inverse(R) * GaussianRendererObject.transform.position;
        Debug.Log($"补偿量计算centirod：R*ΔT({deltaTarget}) + R*ΔS({R * deltaSource}) = {deltaTarget + Quaternion.Inverse(R) * deltaSource}");
        Debug.Log($"补偿量计算pivot：R*ΔS({Quaternion.Inverse(R) * GaussianRendererObject.transform.position})");



        GaussianRendererObject.transform.rotation = Quaternion.Inverse(R) * GaussianRendererObject.transform.rotation;
        GaussianRendererObject.transform.position = finalTranslation;
        roomMeshEvent.gameObject.SetActive( false );


        Debug.Log("targetRenderer t: " + t);
        Debug.Log($"最终平移：{finalTranslation}");

    }

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
        foreach (Vector3 p in points) sum += p;
        return (points.Count > 0) ? sum / points.Count : Vector3.zero;
    }

    List<Vector3> CenterCloud(List<Vector3> cloud, Vector3 centroid)
    {
        List<Vector3> centered = new List<Vector3>();
        foreach (Vector3 p in cloud) centered.Add(p - centroid);
        return centered;
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

