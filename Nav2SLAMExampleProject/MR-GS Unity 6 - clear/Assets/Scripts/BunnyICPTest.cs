using System.Collections.Generic;
using UnityEngine;

public class BunnyICPTest_Centered : MonoBehaviour
{
    [Header("Stanford Bunny 对象")]
    public GameObject bunnyA;  // 参考（目标）模型
    public GameObject bunnyB;  // 待对齐（源）模型

    [Header("ICP 参数")]
    public int maxIterations = 500;
    public float transformationEpsilon = 0.00001f;
    public bool doDownsample = false;

    void Start()
    {
        if (bunnyA == null || bunnyB == null)
        {
            Debug.LogError("请指定 bunnyA 和 bunnyB");
            return;
        }

        // 1. 提取点云（世界坐标）
        List<Vector3> cloudA = GetMeshPointCloud(bunnyA);
        List<Vector3> cloudB = GetMeshPointCloud(bunnyB);

        // 2. 计算质心
        Vector3 centroidA = ComputeCentroid(cloudA);
        Vector3 centroidB = ComputeCentroid(cloudB);
        Debug.Log("bunnyA 质心: " + centroidA);
        Debug.Log("bunnyB 质心: " + centroidB);

        // 3. 中心化点云
        List<Vector3> centeredA = CenterCloud(cloudA, centroidA);
        List<Vector3> centeredB = CenterCloud(cloudB, centroidB);

        // 4. 转换为 float 数组
/*        float[] cloudAArray = ConvertPointCloudToFloatArray(centeredA);
        float[] cloudBArray = ConvertPointCloudToFloatArray(centeredB);*/

        float[] cloudAArray = ConvertPointCloudToFloatArray(cloudA);
        float[] cloudBArray = ConvertPointCloudToFloatArray(cloudB);

        // 5. 运行 ICP（DLL返回PCL坐标系的原始矩阵）
        var result = ICP_GICPWrapperInterface.RunGICP(
            cloudAArray, cloudAArray.Length,
            cloudBArray, cloudBArray.Length,
            maxIterations, transformationEpsilon,
            doDownsample,
            0.1f,
            false
        );
        Debug.Log($"ICP前参考点云质心: ({result.centroid_ref_before[0]}, {result.centroid_ref_before[1]}, {result.centroid_ref_before[2]})");
        Debug.Log($"ICP前目标点云质心: ({result.centroid_target_before[0]}, {result.centroid_target_before[1]}, {result.centroid_target_before[2]})");

        if (!result.converged)
        {
            Debug.LogError("ICP 未收敛！");
            return;
        }
        Debug.Log("ICP 收敛！");

        // 6. 构造PCL坐标系下的矩阵（列优先存储）
        Matrix4x4 T_pcl = Matrix4x4.identity;
        for (int i = 0; i < 16; i++) T_pcl[i] = result.matrix[i];
        
        T_pcl.m00 = result.matrix[0];  // 第一行第一列
        T_pcl.m01 = result.matrix[4];  // 第一行第二列
        T_pcl.m02 = result.matrix[8];  // 第一行第三列
        T_pcl.m03 = result.matrix[3]; // 第一行第四列（平移 x）3

        T_pcl.m10 = result.matrix[1];  // 第二行第一列
        T_pcl.m11 = result.matrix[5];  // 第二行第二列
        T_pcl.m12 = result.matrix[9];  // 第二行第三列
        T_pcl.m13 = result.matrix[7]; // 第二行第四列（平移 y）7

        T_pcl.m20 = result.matrix[2];  // 第三行第一列
        T_pcl.m21 = result.matrix[6];  // 第三行第二列
        T_pcl.m22 = result.matrix[10]; // 第三行第三列
        T_pcl.m23 = result.matrix[11]; // 第三行第四列（平移 z）11

        T_pcl.m30 = result.matrix[12];  // 第四行第一列 12
        T_pcl.m31 = result.matrix[13];  // 第四行第二列 13
        T_pcl.m32 = result.matrix[14]; // 第四行第三列 14
        T_pcl.m33 = result.matrix[15]; // 第四行第四列（通常为 1）15

        Debug.Log("Unity原始变换矩阵: \n" + T_pcl);


        /*        // 7. 安全提取旋转和平移
                Vector3 forward = T_pcl.GetColumn(2);
                Vector3 up = T_pcl.GetColumn(1);
                Quaternion R = Quaternion.LookRotation(forward, up);
                Vector3 t = T_pcl.GetColumn(3);
        */
        /*       Vector3 initialPosition = bunnyB.transform.position;
               Quaternion initialRotation = bunnyB.transform.rotation;

               // 计算新的绝对变换
               Vector3 newPosition = initialPosition + t;          // 平移偏移叠加到当前的位置
               Quaternion newRotation = initialRotation * Quaternion.Inverse(R);         // 相对旋转偏移叠加到当前旋转

               bunnyB.transform.SetPositionAndRotation(newPosition, newRotation);
               Debug.Log("新位置: " + newPosition + " 旋转欧拉角: " + newRotation.eulerAngles);*/




            Vector3 pivotB = bunnyB.transform.position;
/*            Vector3 centroidB = ComputeCentroid(cloudB); // 请确保此处返回的是世界坐标下的 bunnyB 质心
*/           Vector3 d = centroidB - pivotB;

            // 2. 提取 ICP 变换中的旋转和平移
            Vector3 t_icp = T_pcl.GetColumn(3);    // ICP 输出的平移向量
            Vector3 forward = T_pcl.GetColumn(2);
            Vector3 up = T_pcl.GetColumn(1);
            Quaternion R_icp = Quaternion.LookRotation(forward, up);  // ICP 输出的旋转（这里用的是 LookRotation，需要根据具体情况验证）

            // 3. 根据公式，新 pivot P2 应满足：
            //    P2 + d = R_icp * (P1 + d) + t_icp
            // 因此：
            //    P2 = R_icp * (P1 + d) + t_icp - d

            // 4. 更新旋转：根据原始代码逻辑，新的旋转为
            //    newRotation = initialRotation * Quaternion.Inverse(R_icp)
            Quaternion initialRotation = bunnyB.transform.rotation;
            Quaternion newRotation = initialRotation * Quaternion.Inverse(R_icp);
            Vector3 newPivot = pivotB + t_icp + (d - (newRotation * d));

        // 5. 应用变换
            bunnyB.transform.SetPositionAndRotation(newPivot, newRotation);

            Debug.Log("新 pivot (位置): " + newPivot + " 旋转欧拉角: " + newRotation.eulerAngles);


    }

    // ------------------ 工具函数 ------------------
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

    List<Vector3> GetMeshPointCloud(GameObject obj)
    {
        List<Vector3> points = new List<Vector3>();
        MeshFilter mf = obj.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("对象 " + obj.name + " 缺少 MeshFilter 或 Mesh");
            return points;
        }
        Mesh mesh = mf.sharedMesh;
        foreach (Vector3 v in mesh.vertices)
            points.Add(obj.transform.TransformPoint(v));
        
        return points;
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



    void ApplyICPTransform(Matrix4x4 matrix, Vector3 targetCentroid, Vector3 sourceCentroid)
    {
        // 提取旋转和平移
        Quaternion R = matrix.rotation;
        Vector3 t = matrix.GetColumn(3);

        // 计算最终位置（质心对齐公式）
        Vector3 newPosition =
            targetCentroid +
            R * (bunnyB.transform.position - sourceCentroid) +
            t;

        bunnyB.transform.SetPositionAndRotation(newPosition, R);
        Debug.Log($"新位置: {newPosition}, 旋转: {R.eulerAngles}");
    }
}