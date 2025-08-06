using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;
using GaussianSplatting.Runtime;

/// <summary>
/// 根据 MRUK 四面墙生成隐藏 BoxCollider（供射线检测），
/// 并缓存点云材质供 Shader 做裁剪。
/// </summary>
public class WallBoxBuildingBase : MonoBehaviour
{
    const float GAP_XY = 0.05f;              // 每边收缩量

    public static Material wallMat;          // 供 GazeHoleUpdater 写洞

    void Start() => StartCoroutine(WaitAndBuild());

    IEnumerator WaitAndBuild()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        BuildBoxes();
    }

    void BuildBoxes()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null)
        {
            Debug.LogError("WallBoxBuilding ▶ Room 未就绪"); return;
        }

        var gs = FindObjectOfType<GaussianSplatRenderer>();
        wallMat = gs ? gs.m_MatSplats : null;

        int count = 0;
        foreach (var anchor in room.WallAnchors)
        {
            if (!anchor.PlaneRect.HasValue) continue;

            var rect = anchor.PlaneRect.Value;
            float w = Mathf.Max(0, rect.size.x - GAP_XY * 2f);
            float h = Mathf.Max(0, rect.size.y - GAP_XY * 2f);
            float z = Mathf.Max(0.01f, GazeHoleUpdaterBase.CutDepth); // 取当前厚度

            Vector3 c = anchor.transform.position;
            Vector3 f = anchor.transform.forward;
            Vector3 u = anchor.transform.up;

            var root = new GameObject($"WallBox_{count}");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(c, Quaternion.LookRotation(f, u));

            var bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(w, h, z);

            // 帮助可视 Cube（默认隐藏）
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = bc.size;
            mesh.GetComponent<Renderer>().enabled = false;
            Destroy(mesh.GetComponent<Collider>());

            count++;
        }

        Debug.Log($"WallBoxBuilding ▶ 生成 {count} 面墙 BoxCollider (厚度={GazeHoleUpdaterBase.CutDepth:F2}m)");
    }
}
