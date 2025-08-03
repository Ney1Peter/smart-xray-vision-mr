using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Meta.XR.MRUtilityKit;

[DefaultExecutionOrder(110)]
public class FurnitureFrameHighlighter : MonoBehaviour
{
    [Header("线宽 (m)")]      public float lineWidth = 0.008f;
    [Header("偏离表面 (m)")]  public float offset    = 0.01f;

    [Header("颜色")]
    public Color furnitureColor = new Color32(140, 100, 100, 110);  // 暗灰红  (≈#8C6464)

    Shader unlit;  // URP Unlit

    IEnumerator Start()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        unlit = Shader.Find("Universal Render Pipeline/Unlit");
        BuildFurnitureFrames(MRUK.Instance.GetCurrentRoom());
    }

    /* --------------------------------------------------- */
    void BuildFurnitureFrames(MRUKRoom room)
    {
        List<MRUKAnchor> list = new();

        // ① 先尝试专属集合 (WindowAnchors / WindowAnchor …)
        list.AddRange(CollectAnchors(room, "WindowAnchors"));
        list.AddRange(CollectAnchors(room, "WindowAnchor"));
        list.AddRange(CollectAnchors(room, "BedAnchors"));
        list.AddRange(CollectAnchors(room, "BedAnchor"));
        list.AddRange(CollectAnchors(room, "TableAnchors"));
        list.AddRange(CollectAnchors(room, "TableAnchor"));

        // ② 若依旧为空 → 遍历 room.Anchors 找标签
        if (list.Count == 0 && room.GetType().GetProperty("Anchors") is PropertyInfo p)
        {
            if (p.GetValue(room) is IEnumerable<MRUKAnchor> all)
            {
                foreach (var a in all)
                {
                    string lab = a.name.ToUpper();
                    if (lab.Contains("WINDOW") || lab.Contains("BED") || lab.Contains("TABLE"))
                        list.Add(a);
                    else if (TryGetLabel(a, out string first) &&
                             (first.Contains("WINDOW") || first.Contains("BED") || first.Contains("TABLE")))
                        list.Add(a);
                }
            }
        }

        int count = 0;
        foreach (var a in list.Distinct())
        {
            if (!a || !a.PlaneRect.HasValue) continue;
            DrawBoundingBox(a, furnitureColor, $"FurnFrame_{count++}");
        }

        Debug.Log($"FurnitureFrameHighlighter ▶ 绘制家具框 {count} 个");
    }

    /* ------------ 反射收集 anchor 列表或单个 ------------ */
    List<MRUKAnchor> CollectAnchors(object obj, string member)
    {
        var mi = obj.GetType().GetMember(member,
                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
        if (mi == null) return new();

        object val = mi switch
        {
            FieldInfo f    => f.GetValue(obj),
            PropertyInfo p => p.GetValue(obj),
            _              => null
        };
        if (val == null) return new();

        return val switch
        {
            IEnumerable<MRUKAnchor> list => list.ToList(),
            MRUKAnchor single            => new() { single },
            _ => new()
        };
    }

    /* ------------ 取得自带标签（若有） ------------ */
    bool TryGetLabel(MRUKAnchor a, out string label)
    {
        label = null;
        var f = a.GetType().GetField("ClassificationLabels");
        if (f?.GetValue(a) is string[] arr && arr.Length > 0)
        {
            label = arr[0].ToUpper();
            return true;
        }
        return false;
    }

    /* ------------ 画立方体线框 ------------ */
    void DrawBoundingBox(MRUKAnchor a, Color clr, string goName)
    {
        // 1) 利用 plane rect + transform 给出局部方块，厚度用 offset
        var rect = a.PlaneRect!.Value;
        float hx = rect.size.x * 0.5f;
        float hy = rect.size.y * 0.5f;
        float hz = rect.size.magnitude * 0.02f + offset; // 简易厚度 ≈ 2% 对角线

        Transform t = a.transform;
        Vector3 c = t.position + t.forward * hz * 0.5f; // 中心往法线方向推进一半作为包围盒中心
        Vector3 r = t.right, u = t.up, f = t.forward;

        // 8 角
        Vector3[] v =
        {
            c + (-r*hx) + (-u*hy) + (-f*hz),
            c + ( r*hx) + (-u*hy) + (-f*hz),
            c + ( r*hx) + ( u*hy) + (-f*hz),
            c + (-r*hx) + ( u*hy) + (-f*hz),

            c + (-r*hx) + (-u*hy) + ( f*hz),
            c + ( r*hx) + (-u*hy) + ( f*hz),
            c + ( r*hx) + ( u*hy) + ( f*hz),
            c + (-r*hx) + ( u*hy) + ( f*hz)
        };

        int[][] edges =
        {
            new[]{0,1,2,3,0}, new[]{4,5,6,7,4}, // 前后面方框
            new[]{0,4}, new[]{1,5}, new[]{2,6}, new[]{3,7} // 侧边连接
        };

        var parent = new GameObject(goName);
        parent.transform.SetParent(transform, false);

        foreach (var e in edges)
        {
            var lr = new GameObject("edge").AddComponent<LineRenderer>();
            lr.transform.SetParent(parent.transform, false);
            lr.positionCount     = e.Length;
            lr.widthMultiplier   = lineWidth;
            lr.useWorldSpace     = true;
            lr.material          = new Material(unlit) { color = clr, enableInstancing = true };
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector3[] pts = e.Select(idx => v[idx]).ToArray();
            lr.SetPositions(pts);
        }
    }
}
