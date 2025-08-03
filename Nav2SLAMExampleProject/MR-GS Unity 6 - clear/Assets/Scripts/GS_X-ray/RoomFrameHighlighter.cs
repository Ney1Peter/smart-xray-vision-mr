// RoomFrameHighlighter.cs  — URP 兼容，只有显式锚点，无法线兜底
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Meta.XR.MRUtilityKit;

[DefaultExecutionOrder(100)]
public class RoomFrameHighlighter : MonoBehaviour
{
    [Header("线宽 (m)")]      public float lineWidth = 0.01f;
    [Header("离平面偏移 (m)")] public float offset    = 0.008f;

    [Header("颜色设置")]
    public Color wallColor    = new Color32( 90, 110, 140, 110); // 冷灰蓝
    public Color floorColor   = new Color32(100, 120, 100, 110); // 暗灰绿
    public Color ceilingColor = new Color32(140, 100, 100, 110); // 暗灰红

    Shader _urpUnlit;

    IEnumerator Start()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        BuildFrames(MRUK.Instance.GetCurrentRoom());
    }

    /* ---------- 主入口 ---------- */
    void BuildFrames(MRUKRoom room)
    {
        int total = 0;
        total += BuildFrom(room, "WallAnchors",    wallColor,    "WallFrame");
        total += BuildFrom(room, "FloorAnchors",   floorColor,   "FloorFrame");
        total += BuildFrom(room, "FloorAnchor",    floorColor,   "FloorFrame");
        total += BuildFrom(room, "CeilingAnchors", ceilingColor, "CeilingFrame");
        total += BuildFrom(room, "CeilingAnchor",  ceilingColor, "CeilingFrame");

        Debug.Log($"RoomFrameHighlighter ▶ 绘制框架 {total} 个（无法线兜底）");
    }

    /* ---------- 通过反射读取集合或单个 ---------- */
    int BuildFrom(object obj, string memberName, Color clr, string prefix)
    {
        MemberInfo m = obj.GetType().GetMember(memberName,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       .FirstOrDefault();
        if (m == null) return 0;

        object value = m switch
        {
            FieldInfo    f => f.GetValue(obj),
            PropertyInfo p => p.GetValue(obj),
            _              => null
        };
        if (value == null) return 0;

        int n = 0;

        if (value is IEnumerable<MRUKAnchor> list)
        {
            foreach (var a in list)
                if (DrawFrame(a, clr, $"{prefix}_{n}")) n++;
        }
        else if (value is MRUKAnchor single)
        {
            if (DrawFrame(single, clr, $"{prefix}_0")) n = 1;
        }

        return n;
    }

    /* ---------- 画 LineRenderer ---------- */
    bool DrawFrame(MRUKAnchor anchor, Color clr, string goName)
    {
        if (!anchor || !anchor.PlaneRect.HasValue) return false;

        var rect = anchor.PlaneRect.Value;
        float hx = rect.size.x * 0.5f;
        float hy = rect.size.y * 0.5f;

        Transform t = anchor.transform;
        Vector3 c = t.position + t.forward * offset;
        Vector3 r = t.right, u = t.up;

        Vector3[] pts =
        {
            c + (-r*hx) + (-u*hy),
            c + ( r*hx) + (-u*hy),
            c + ( r*hx) + ( u*hy),
            c + (-r*hx) + ( u*hy),
            c + (-r*hx) + (-u*hy)
        };

        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount     = pts.Length;
        lr.SetPositions(pts);
        lr.widthMultiplier   = lineWidth;
        lr.useWorldSpace     = true;
        lr.numCornerVertices = 2;
        lr.loop              = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.material          = new Material(_urpUnlit)
        {
            color = clr, enableInstancing = true
        };
        return true;
    }
}
