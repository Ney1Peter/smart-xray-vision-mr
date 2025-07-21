using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;

/// <summary>
/// 运行时在每面墙生成一个红色大方块（静态），
/// 并共享同一个挖洞材质 wallMat。
/// </summary>
public class WallBoxBuilder : MonoBehaviour
{
    [Header("方块厚度 (Z)")]
    [SerializeField] float thickness = 0.1f;

    [Header("收缩缝隙 (XY)")]
    [SerializeField] float gap = 0.05f;

    [Header("Shader 名称")]
    const string kShaderName = "Custom/WallBoxCutout";

    /// <summary>全局共享材质，供 GazeHoleUpdater 访问</summary>
    public static Material wallMat;

    void Start() => StartCoroutine(BuildWhenRoomReady());

    IEnumerator BuildWhenRoomReady()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        BuildWallBoxes();
    }

    void BuildWallBoxes()
    {
        var room = MRUK.Instance.GetCurrentRoom();

        // 创建一次材质
        wallMat = new Material(Shader.Find(kShaderName));
        wallMat.SetColor("_Color", Color.red);   // 保证红色

        // 放在场景根下
        var root = new GameObject("WallBoxesRoot").transform;

        int id = 0;
        foreach (var a in room.WallAnchors)
        {
            if (!a.PlaneRect.HasValue) continue;

            var rect = a.PlaneRect.Value;
            float w = Mathf.Max(0, rect.size.x - gap * 2f);
            float h = Mathf.Max(0, rect.size.y - gap * 2f);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"WallBox_{id++}";
            box.transform.SetParent(root);
            box.transform.position = a.transform.position;
            box.transform.rotation = Quaternion.LookRotation(a.transform.forward, a.transform.up);
            box.transform.localScale = new Vector3(w, h, thickness);

            box.GetComponent<Renderer>().sharedMaterial = wallMat;
        }

        Debug.Log($"WallBoxBuilder ▶ 共生成 {id} 面墙方块。");
    }
}
