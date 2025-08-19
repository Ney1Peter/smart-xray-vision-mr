using UnityEngine;

/// <summary>
/// ����/ɾ�������е� OVRSpatialAnchor�����س־û�����
/// ���� ControllerButtonsMapper �󶨵��ã����ֶ��ӱ�Ľű����á�
/// </summary>
public class AnchorSaveHelper : MonoBehaviour
{
    [Header("��ѡ��������ê������ĸ���ɫ����Ϊ��ʾ")]
    public bool tintAfterSave = true;
    public Color savedColor = new Color(0.2f, 0.9f, 0.4f, 1f);

    /// <summary>���泡�������� OVRSpatialAnchor�����ش洢����</summary>
    public void SaveAll()
    {
        var anchors = FindObjectsOfType<OVRSpatialAnchor>(includeInactive: false);
        if (anchors.Length == 0)
        {
            Debug.Log("[AnchorSaveHelper] ������û�пɱ����ê��");
            return;
        }

        Debug.Log($"[AnchorSaveHelper] ���� {anchors.Length} ��ê�����ش洢��");
        foreach (var a in anchors)
        {
            // ֱ�ӵ��� Save��Ĭ�ϱ��浽 Local Storage��
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

    /// <summary>ɾ������������ê��ͬʱ����ӱ��ش洢Ĩ������</summary>
    public void EraseAll()
    {
        var anchors = FindObjectsOfType<OVRSpatialAnchor>(includeInactive: false);
        if (anchors.Length == 0)
        {
            Debug.Log("[AnchorSaveHelper] ������û�п�ɾ����ê��");
            return;
        }

        Debug.Log($"[AnchorSaveHelper] ɾ�� {anchors.Length} ��ê�������Դӱ��ش洢Ĩ������");
        foreach (var a in anchors)
        {
            a.Erase((anchor, ok) =>
            {
                Debug.Log($"[AnchorSaveHelper] Erase {(ok ? "OK" : "FAIL")}  id={anchor.Uuid}");
            });

            // ͬ���ѳ�����Ŀ��Ӷ������ٵ�
            Destroy(a.gameObject);
        }
    }
}
