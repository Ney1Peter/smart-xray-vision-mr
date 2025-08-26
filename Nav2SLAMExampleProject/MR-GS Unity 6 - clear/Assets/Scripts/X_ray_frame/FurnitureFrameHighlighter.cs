using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Meta.XR.MRUtilityKit;

[DefaultExecutionOrder(110)]
public class FurnitureFrameHighlighter : MonoBehaviour
{
    [Header("Line Width (m)")]
    public float lineWidth = 0.008f;
    [Header("Offset from Surface (m)")]
    public float offset = 0.01f;

    [Header("Color")]
    public Color furnitureColor = new Color32(90, 110, 140, 110);

    [Header("Frame Groups")]
    public Transform windowFramesRoot;
    public Transform tableFramesRoot;

    Shader unlit;  // URP Unlit

    IEnumerator Start()
    {
        // 等待 MRUK Room 数据就绪
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        // 如果未手动指定，就自动创建父容器
        if (windowFramesRoot == null)
        {
            windowFramesRoot = new GameObject("WindowFrames").transform;
            windowFramesRoot.SetParent(transform, false);
        }
        if (tableFramesRoot == null)
        {
            tableFramesRoot = new GameObject("TableFrames").transform;
            tableFramesRoot.SetParent(transform, false);
        }

        unlit = Shader.Find("Universal Render Pipeline/Unlit");
        BuildFurnitureFrames(MRUK.Instance.GetCurrentRoom());
    }

    void BuildFurnitureFrames(MRUKRoom room)
    {
        // ----- Window frames -----
        var windowAnchors = new List<MRUKAnchor>();
        windowAnchors.AddRange(CollectAnchors(room, "WindowAnchors"));
        windowAnchors.AddRange(CollectAnchors(room, "WindowAnchor"));
        // 回退扫描所有 Anchors 名称或标签包含 WINDOW
        if (windowAnchors.Count == 0 && room.GetType().GetProperty("Anchors") is PropertyInfo pWin)
        {
            if (pWin.GetValue(room) is IEnumerable<MRUKAnchor> allWin)
            {
                foreach (var a in allWin)
                {
                    if (a == null) continue;
                    var nameU = a.name.ToUpper();
                    if (nameU.Contains("WINDOW") || (TryGetLabel(a, out var lab) && lab.Contains("WINDOW")))
                        windowAnchors.Add(a);
                }
            }
        }
        int wCount = 0;
        foreach (var a in windowAnchors.Distinct())
        {
            if (a == null || !a.PlaneRect.HasValue) continue;
            DrawBoundingBox(a, furnitureColor, windowFramesRoot, $"WindowFrame_{wCount++}");
        }
        Debug.Log($"FurnitureFrameHighlighter ▶ Drew {wCount} window frames");

        // ----- Table frames -----
        var tableAnchors = new List<MRUKAnchor>();
        tableAnchors.AddRange(CollectAnchors(room, "TableAnchors"));
        tableAnchors.AddRange(CollectAnchors(room, "TableAnchor"));
        // 回退扫描所有 Anchors 名称或标签包含 TABLE
        if (tableAnchors.Count == 0 && room.GetType().GetProperty("Anchors") is PropertyInfo pTab)
        {
            if (pTab.GetValue(room) is IEnumerable<MRUKAnchor> allTab)
            {
                foreach (var a in allTab)
                {
                    if (a == null) continue;
                    var nameU = a.name.ToUpper();
                    if (nameU.Contains("TABLE") || (TryGetLabel(a, out var lab) && lab.Contains("TABLE")))
                        tableAnchors.Add(a);
                }
            }
        }
        int tCount = 0;
        foreach (var a in tableAnchors.Distinct())
        {
            if (a == null || !a.PlaneRect.HasValue) continue;
            DrawBoundingBox(a, furnitureColor, tableFramesRoot, $"TableFrame_{tCount++}");
        }
        Debug.Log($"FurnitureFrameHighlighter ▶ Drew {tCount} table frames");
    }

    /// <summary>
    /// 使用反射收集指定字段/属性的 Anchors
    /// </summary>
    List<MRUKAnchor> CollectAnchors(object obj, string member)
    {
        var mi = obj.GetType()
                    .GetMember(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault();
        if (mi == null) return new List<MRUKAnchor>();

        object val = mi switch
        {
            FieldInfo f    => f.GetValue(obj),
            PropertyInfo p => p.GetValue(obj),
            _              => null
        };
        if (val == null) return new List<MRUKAnchor>();

        return val switch
        {
            IEnumerable<MRUKAnchor> list => list.ToList(),
            MRUKAnchor single            => new List<MRUKAnchor> { single },
            _                            => new List<MRUKAnchor>()
        };
    }

    /// <summary>
    /// 尝试读取 Anchor 的 ClassificationLabels（如果有）
    /// </summary>
    bool TryGetLabel(MRUKAnchor a, out string label)
    {
        label = null;
        var f = a.GetType().GetField("ClassificationLabels", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f?.GetValue(a) is string[] arr && arr.Length > 0)
        {
            label = arr[0].ToUpper();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 绘制 3D 线框并立即隐藏
    /// </summary>
    void DrawBoundingBox(MRUKAnchor a, Color clr, Transform parent, string goName)
    {
        var rect = a.PlaneRect.Value;
        float hx = rect.size.x * 0.5f;
        float hy = rect.size.y * 0.5f;
        float hz = rect.size.magnitude * 0.02f + offset;

        Transform t = a.transform;
        Vector3 center = t.position + t.forward * hz * 0.5f;
        Vector3 right  = t.right;
        Vector3 up     = t.up;
        Vector3 fwd    = t.forward;

        Vector3[] verts = {
            center + (-right*hx) + (-up*hy) + (-fwd*hz),
            center + ( right*hx) + (-up*hy) + (-fwd*hz),
            center + ( right*hx) + ( up*hy) + (-fwd*hz),
            center + (-right*hx) + ( up*hy) + (-fwd*hz),
            center + (-right*hx) + (-up*hy) + ( fwd*hz),
            center + ( right*hx) + (-up*hy) + ( fwd*hz),
            center + ( right*hx) + ( up*hy) + ( fwd*hz),
            center + (-right*hx) + ( up*hy) + ( fwd*hz)
        };

        int[][] edges = {
            new[]{0,1,2,3,0},
            new[]{4,5,6,7,4},
            new[]{0,4}, new[]{1,5}, new[]{2,6}, new[]{3,7}
        };

        // 创建容器并马上隐藏
        var frameObj = new GameObject(goName);
        frameObj.transform.SetParent(parent, false);
        frameObj.SetActive(false);

        // 线条材质
        var mat = new Material(unlit) { color = clr, enableInstancing = true };

        // 绘制每条边
        foreach (var e in edges)
        {
            var lrGO = new GameObject("edge");
            lrGO.transform.SetParent(frameObj.transform, false);
            var lr = lrGO.AddComponent<LineRenderer>();
            lr.positionCount       = e.Length;
            lr.widthMultiplier     = lineWidth;
            lr.useWorldSpace       = true;
            lr.material            = mat;
            lr.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.SetPositions(e.Select(i => verts[i]).ToArray());
        }
    }
}
