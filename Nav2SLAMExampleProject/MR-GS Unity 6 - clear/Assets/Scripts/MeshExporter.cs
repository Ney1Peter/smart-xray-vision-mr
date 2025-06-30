using Meta.XR.BuildingBlocks;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

public class MeshExporter : MonoBehaviour
{
    public enum ExportFormat
    {
        OBJ,
        PLY
    }

    [Header("事件与导出设置")]
    [SerializeField] private RoomMeshEvent roomMeshEvent; // 在 Inspector 中拖拽引用
    [SerializeField] private ExportFormat exportFormat = ExportFormat.OBJ; // 选择导出格式

    [Header("场景生成设置")]
    [Tooltip("是否在场景中生成一个显示导入 Mesh 的 GameObject")]
    [SerializeField] private bool generateSceneMesh = false;
    [Tooltip("用于显示 Mesh 的透明材质，记得将 Shader 设置为支持透明效果的材质")]
    [SerializeField] private Material transparentMaterial;

    private void Awake()
    {
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshLoaded);
        }
        else
        {
            Debug.LogWarning("RoomMeshEvent reference not set. Please assign it in the Inspector.");
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
    /// 当房间网格加载完成时调用
    /// </summary>
    /// <param name="mf">加载完成的 MeshFilter</param>
    private void OnRoomMeshLoaded(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("No mesh found to export.");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Transform meshTransform = mf.transform;

        // 根据 Inspector 中的设置决定导出哪种格式
        switch (exportFormat)
        {
            case ExportFormat.OBJ:
                ExportMeshToOBJ(mesh, meshTransform, "RoomMeshExport.obj");
                break;
            case ExportFormat.PLY:
                ExportMeshToPLY(mesh, meshTransform, "RoomMeshExport.ply");
                break;
        }

        // 如果设置了在场景中生成 Mesh，则创建一个 GameObject 显示这个 Mesh
        if (generateSceneMesh)
        {
            GenerateSceneMesh(mesh, meshTransform);
        }
    }

    /// <summary>
    /// 将 Mesh 导出为 OBJ 文件（使用局部坐标，同时记录全局 Transform 信息到注释中）
    /// </summary>
    private void ExportMeshToOBJ(Mesh mesh, Transform meshTransform, string filename)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# OBJ file exported from Quest by MeshExporter");
        // 在文件头中记录全局 Transform 信息
        sb.AppendLine($"# Global Position: {meshTransform.position.x} {meshTransform.position.y} {meshTransform.position.z}");
        sb.AppendLine($"# Global Rotation (Quaternion): {meshTransform.rotation.x} {meshTransform.rotation.y} {meshTransform.rotation.z} {meshTransform.rotation.w}");
        sb.AppendLine($"# Global Scale: {meshTransform.lossyScale.x} {meshTransform.lossyScale.y} {meshTransform.lossyScale.z}");

        // 导出顶点数据（使用局部坐标，不做全局转换）
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            sb.AppendLine($"v {v.x} {v.y} {v.z}");
        }

        // 导出 UV 信息
        Vector2[] uvs = mesh.uv;
        if (uvs != null && uvs.Length > 0)
        {
            for (int i = 0; i < uvs.Length; i++)
            {
                Vector2 uv = uvs[i];
                sb.AppendLine($"vt {uv.x} {uv.y}");
            }
        }

        // 导出法线信息（局部坐标）
        Vector3[] normals = mesh.normals;
        if (normals != null && normals.Length > 0)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 n = normals[i];
                sb.AppendLine($"vn {n.x} {n.y} {n.z}");
            }
        }

        // 导出面信息（假设只有一个 SubMesh）
        int[] triangles = mesh.GetTriangles(0);
        bool hasUV = (uvs != null && uvs.Length > 0);
        bool hasNormals = (normals != null && normals.Length > 0);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v1 = triangles[i] + 1;
            int v2 = triangles[i + 1] + 1;
            int v3 = triangles[i + 2] + 1;

            if (hasUV && hasNormals)
            {
                sb.AppendLine($"f {v1}/{v1}/{v1} {v2}/{v2}/{v2} {v3}/{v3}/{v3}");
            }
            else if (hasUV && !hasNormals)
            {
                sb.AppendLine($"f {v1}/{v1} {v2}/{v2} {v3}/{v3}");
            }
            else if (!hasUV && hasNormals)
            {
                sb.AppendLine($"f {v1}//{v1} {v2}//{v2} {v3}//{v3}");
            }
            else
            {
                sb.AppendLine($"f {v1} {v2} {v3}");
            }
        }

        // 将内容写入文件（写入 Application.persistentDataPath 路径下）
        string path = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"OBJ Mesh exported to: {path}");
    }

    /// <summary>
    /// 将 Mesh 导出为 PLY 格式（使用局部坐标，同时记录全局 Transform 信息到注释中，ASCII格式）
    /// </summary>
    private void ExportMeshToPLY(Mesh mesh, Transform meshTransform, string filename)
    {
        StringBuilder sb = new StringBuilder();

        // PLY 文件头部
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine("comment Exported by MeshExporter");
        // 在注释中记录全局 Transform 信息
        sb.AppendLine($"comment Global Position: {meshTransform.position.x} {meshTransform.position.y} {meshTransform.position.z}");
        sb.AppendLine($"comment Global Rotation (Quaternion): {meshTransform.rotation.x} {meshTransform.rotation.y} {meshTransform.rotation.z} {meshTransform.rotation.w}");
        sb.AppendLine($"comment Global Scale: {meshTransform.lossyScale.x} {meshTransform.lossyScale.y} {meshTransform.lossyScale.z}");
        sb.AppendLine($"element vertex {mesh.vertexCount}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        // 如需导出颜色信息，请取消下面注释并确保 Mesh 具有 colors 属性
        // sb.AppendLine("property uchar red");
        // sb.AppendLine("property uchar green");
        // sb.AppendLine("property uchar blue");
        sb.AppendLine("end_header");

        // 导出顶点数据（局部坐标）
        Vector3[] vertices = mesh.vertices;
        Color[] colors = mesh.colors;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            if (colors != null && colors.Length == vertices.Length)
            {
                Color c = colors[i];
                int r = Mathf.RoundToInt(c.r * 255f);
                int g = Mathf.RoundToInt(c.g * 255f);
                int b = Mathf.RoundToInt(c.b * 255f);
                sb.AppendLine($"{v.x} {v.y} {v.z} {r} {g} {b}");
            }
            else
            {
                sb.AppendLine($"{v.x} {v.y} {v.z}");
            }
        }

        // 将内容写入文件
        string path = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"PLY Mesh exported to: {path}");
    }

    /// <summary>
    /// 在场景中生成一个 GameObject 显示导入的 Mesh，并应用透明材质
    /// </summary>
    /// <param name="mesh">要显示的 Mesh</param>
    /// <param name="meshTransform">原始 Mesh 的 Transform（用于设置生成对象的全局变换）</param>
    private void GenerateSceneMesh(Mesh mesh, Transform meshTransform)
    {
        GameObject sceneMeshGO = new GameObject("SceneMesh");
        // 还原原始 Mesh 的全局变换
        sceneMeshGO.transform.position = meshTransform.position;
        sceneMeshGO.transform.rotation = meshTransform.rotation;
        sceneMeshGO.transform.localScale = meshTransform.lossyScale;

        MeshFilter mf = sceneMeshGO.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = sceneMeshGO.AddComponent<MeshRenderer>();
        if (transparentMaterial != null)
        {
            mr.material = transparentMaterial;
        }
        else
        {
            Debug.LogWarning("Transparent material is not assigned in Inspector.");
        }

        Debug.Log("Scene mesh generated with transparent material.");
    }
}
