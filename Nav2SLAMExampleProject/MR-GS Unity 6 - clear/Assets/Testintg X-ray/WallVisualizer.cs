using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;

public class WallMeshVisualize : MonoBehaviour
{
    [Header("方块厚度 (Z 方向)")]
    [SerializeField] float thickness = 0.1f;

    [Header("在 X / Y 方向收缩的缝隙 (m)")]
    [SerializeField] float gap = 0.05f;   // 每边各缩 gap，四面墙就留出缝隙

    [Header("自定义预制体（可选）")]
    [SerializeField] GameObject markerPrefab;

    // 统一使用 Unlit/Color 材质，保证不会出现粉色
    Material redMat;

    void Start() => StartCoroutine(WaitForRoomAndMark());

    IEnumerator WaitForRoomAndMark()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        // 创建一次性材质
        redMat = new Material(Shader.Find("Unlit/Color"));
        redMat.color = Color.red;

        MarkWallBoxes();
    }

    void MarkWallBoxes()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        int count = 0;

        foreach (var anchor in room.WallAnchors)
        {
            if (!anchor.PlaneRect.HasValue) continue;

            // 1️⃣ 取墙面尺寸
            var rect = anchor.PlaneRect.Value;
            float width = Mathf.Max(0, rect.size.x - gap * 2f);
            float height = Mathf.Max(0, rect.size.y - gap * 2f);

            // 2️⃣ 计算中心 & 方向
            Vector3 center = anchor.transform.position;
            Vector3 forward = anchor.transform.forward;  // 法线
            Vector3 up = anchor.transform.up;

            // 3️⃣ 创建或实例化方块
            GameObject box = markerPrefab
                ? Instantiate(markerPrefab, center, Quaternion.identity)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            box.transform.position = center;
            box.transform.rotation = Quaternion.LookRotation(forward, up);
            box.transform.localScale = new Vector3(width, height, thickness);

            // 4️⃣ 着色
            var rend = box.GetComponent<Renderer>();
            rend.sharedMaterial = redMat;

            box.name = $"WallBox_{count}";
            box.transform.SetParent(this.transform);

            count++;
        }

        Debug.Log($"📦 WallMeshMarker ▶ 放置 {count} 个墙体方块（已留缝隙）");
    }
}
