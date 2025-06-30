using Meta.XR.BuildingBlocks;
using System.Collections.Generic;
using UnityEngine;

public class MeshPointCloudExtractor : MonoBehaviour
{
    // 拖拽 RoomMeshEvent 的引用到 Inspector 中
    [SerializeField] private RoomMeshEvent roomMeshEvent;

    private void Awake()
    {
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

    /// <summary>
    /// 当房间网格加载完成时触发，提取其点云数据（转换为全局坐标）。
    /// </summary>
    /// <param name="mf">加载完成的 MeshFilter</param>
    private void OnRoomMeshLoaded(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("未找到可导出的网格数据。");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Transform meshTransform = mf.transform;
        List<Vector3> pointCloud = new List<Vector3>();

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            // 将局部顶点坐标转换为全局坐标
            Vector3 worldVertex = meshTransform.TransformPoint(vertices[i]);
            pointCloud.Add(worldVertex);
        }

        Debug.Log($"从场景网格中提取了 {pointCloud.Count} 个点作为点云数据。");

        // 这里 pointCloud 就包含了当前场景中加载网格的所有点，
        // 后续可以将其用于 ICP 计算等其它处理
    }
}
