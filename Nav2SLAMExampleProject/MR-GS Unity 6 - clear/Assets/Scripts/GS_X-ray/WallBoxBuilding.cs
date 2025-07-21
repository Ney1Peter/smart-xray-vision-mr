using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;
using GaussianSplatting.Runtime;

/// <summary>
/// 生成四面墙的隐藏 BoxCollider，并把点云材质句柄公开给 GazeHoleUpdater。
/// </summary>
public class WallBoxBuilding : MonoBehaviour
{
    [Header("方块厚度 (Z)")]
    [SerializeField] float thickness = 0.10f;

    [Header("在 X/Y 方向各收缩的缝隙 (m)")]
    [SerializeField] float gap = 0.05f;

    // 供外部访问（GazeHoleUpdater.SetVector）
    public static Material wallMat;

    // --------------- ★① 入口改为协程 ----------------
    void Start() => StartCoroutine(WaitForRoomAndBuild());

    IEnumerator WaitForRoomAndBuild()
    {
        // 每帧轮询，直到 MRUK 准备好房间
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
        {
            yield return null;           // 下一帧再检查
        }

        BuildBoxes();
    }
    // ------------------------------------------------

    void BuildBoxes()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null)
        {
            Debug.LogError("WallBoxBuilder ▶ 仍未拿到 Room");
            return;
        }

        // ★② 拿点云材质。m_MatSplats 是 public，所以能访问
        var gsRenderer = FindObjectOfType<GaussianSplatRenderer>();
        wallMat = gsRenderer ? gsRenderer.m_MatSplats : null;

        int count = 0;
        foreach (var anchor in room.WallAnchors)
        {
            if (!anchor.PlaneRect.HasValue) continue;

            // 获取尺寸 / 方向
            var rect = anchor.PlaneRect.Value;
            float w = Mathf.Max(0, rect.size.x - gap * 2f);
            float h = Mathf.Max(0, rect.size.y - gap * 2f);
            Vector3 c = anchor.transform.position;
            Vector3 f = anchor.transform.forward;
            Vector3 u = anchor.transform.up;

            // 创建父物体 + BoxCollider
            var go = new GameObject($"WallBox_{count}");
            go.transform.SetParent(transform, false);
            go.transform.position = c;
            go.transform.rotation = Quaternion.LookRotation(f, u);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w, h, thickness);

            // 可见参考盒（MeshRenderer）——默认关闭
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(go.transform, false);
            mesh.transform.localScale = box.size;
            var rend = mesh.GetComponent<Renderer>();
            rend.enabled = false;
            Destroy(mesh.GetComponent<Collider>()); // 删除多余 Collider

            count++;
        }

        Debug.Log($"WallBoxBuilder ▶ 共生成 {count} 面墙方块。");
    }
}
