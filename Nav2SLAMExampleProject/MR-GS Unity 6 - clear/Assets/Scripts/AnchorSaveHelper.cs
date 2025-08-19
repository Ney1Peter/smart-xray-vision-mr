using UnityEngine;

/// <summary>
/// 保存/删除场景中的 OVRSpatialAnchor（本地持久化）。
/// 可用 ControllerButtonsMapper 绑定调用，或手动从别的脚本调用。
/// </summary>
public class AnchorSaveHelper : MonoBehaviour
{
    [Header("可选：保存后把锚的球体改个颜色，作为提示")]
    public bool tintAfterSave = true;
    public Color savedColor = new Color(0.2f, 0.9f, 0.4f, 1f);

    /// <summary>保存场景中所有 OVRSpatialAnchor（本地存储）。</summary>
    public void SaveAll()
    {
        var anchors = FindObjectsOfType<OVRSpatialAnchor>(includeInactive: false);
        if (anchors.Length == 0)
        {
            Debug.Log("[AnchorSaveHelper] 场景内没有可保存的锚。");
            return;
        }

        Debug.Log($"[AnchorSaveHelper] 保存 {anchors.Length} 个锚到本地存储…");
        foreach (var a in anchors)
        {
            // 直接调用 Save（默认保存到 Local Storage）
            a.Save((anchor, ok) =>
            {
                Debug.Log($"[AnchorSaveHelper] Save {(ok ? "OK" : "FAIL")}  id={anchor.Uuid}");

                if (ok && tintAfterSave)
                {
                    var rend = anchor.GetComponentInChildren<MeshRenderer>();
                    if (rend != null)
                    {
                        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                        { color = savedColor };
                        rend.material = mat;
                    }
                }
            });
        }
    }

    /// <summary>删除场景中所有锚（同时请求从本地存储抹除）。</summary>
    public void EraseAll()
    {
        var anchors = FindObjectsOfType<OVRSpatialAnchor>(includeInactive: false);
        if (anchors.Length == 0)
        {
            Debug.Log("[AnchorSaveHelper] 场景内没有可删除的锚。");
            return;
        }

        Debug.Log($"[AnchorSaveHelper] 删除 {anchors.Length} 个锚（并尝试从本地存储抹除）…");
        foreach (var a in anchors)
        {
            a.Erase((anchor, ok) =>
            {
                Debug.Log($"[AnchorSaveHelper] Erase {(ok ? "OK" : "FAIL")}  id={anchor.Uuid}");
            });

            // 同步把场景里的可视对象销毁掉
            Destroy(a.gameObject);
        }
    }
}
