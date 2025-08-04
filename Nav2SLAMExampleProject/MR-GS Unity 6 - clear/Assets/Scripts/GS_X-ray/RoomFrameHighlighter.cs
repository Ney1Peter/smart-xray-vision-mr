// RoomFrameHighlighter.cs — URP 兼容，虚线 + Emission + 可收缩墙框
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
    [Header("线宽 (m)")] public float lineWidth = 0.01f;
    [Header("离平面偏移 (m)")] public float offset = 0.008f;
    [Header("墙体内收 (m)")] public float wallInset = 0.02f;    // New: 墙框向内缩 2 cm

    // ─── 颜色设置 ───
    [Header("颜色设置")]
    public Color wallColor = new Color32(193, 168, 117, 108);
    public Color floorColor = new Color32(140, 100, 100, 110);  // 暗灰红  (≈#8C6464)
    public Color ceilingColor = new Color32(100, 120, 100, 110);

    Shader _urpUnlit;
    static Texture2D _dashTex;  // 共享虚线纹理

    IEnumerator Start()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (_dashTex == null) _dashTex = MakeDashTex();
        BuildFrames(MRUK.Instance.GetCurrentRoom());
    }

    /* ---------- 主入口 ---------- */
    void BuildFrames(MRUKRoom room)
    {
        int total = 0;
        total += BuildFrom(room, "WallAnchors", wallColor, "WallFrame");
        total += BuildFrom(room, "FloorAnchors", floorColor, "FloorFrame");
        total += BuildFrom(room, "FloorAnchor", floorColor, "FloorFrame");
        total += BuildFrom(room, "CeilingAnchors", ceilingColor, "CeilingFrame");
        total += BuildFrom(room, "CeilingAnchor", ceilingColor, "CeilingFrame");

        Debug.Log($"RoomFrameHighlighter ▶ 绘制框架 {total} 个（无法线兜底）");
    }

    /* ---------- 通过反射读取集合或单个 ---------- */
    int BuildFrom(object obj, string member, Color clr, string prefix)
    {
        MemberInfo mi = obj.GetType()
                           .GetMember(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           .FirstOrDefault();
        if (mi == null) return 0;

        object val = mi switch
        {
            FieldInfo f => f.GetValue(obj),
            PropertyInfo p => p.GetValue(obj),
            _ => null
        };
        if (val == null) return 0;

        int n = 0;
        if (val is IEnumerable<MRUKAnchor> list)
        {
            foreach (var a in list) if (DrawFrame(a, clr, $"{prefix}_{n}", prefix.StartsWith("Wall"))) n++;
        }
        else if (val is MRUKAnchor single)
        {
            if (DrawFrame(single, clr, $"{prefix}_0", prefix.StartsWith("Wall"))) n = 1;
        }
        return n;
    }

    /* ---------- 画 LineRenderer ---------- */
    bool DrawFrame(MRUKAnchor a, Color clr, string goName, bool isWall)
    {
        if (!a || !a.PlaneRect.HasValue) return false;

        var rect = a.PlaneRect.Value;
        float hx = rect.size.x * 0.5f;
        float hy = rect.size.y * 0.5f;

        /* — 对墙体内收 — */
        if (isWall)
        {
            hx = Mathf.Max(0, hx - wallInset);
            hy = Mathf.Max(0, hy - wallInset);
        }

        Transform t = a.transform;
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

        var parent = new GameObject(goName);
        parent.transform.SetParent(transform, false);

        // —— 虚线材质（共享） ——
        var dashMat = new Material(_urpUnlit)
        {
            color = clr,
            enableInstancing = true,
            mainTexture = _dashTex
        };
        dashMat.SetTextureScale("_BaseMap", new Vector2(20, 1));
        dashMat.EnableKeyword("_EMISSION");
        dashMat.SetColor("_EmissionColor", clr * 1.5f);

        // 外粗线
        CreateLR(parent, "outer", pts, lineWidth, dashMat);
        // 内细线
        CreateLR(parent, "inner", pts, lineWidth * 0.4f, dashMat);

        return true;
    }

    /* ---------- LineRenderer 工具 ---------- */
    void CreateLR(GameObject parent, string name, Vector3[] pts, float width, Material mat)
    {
        var lrObj = new GameObject(name);
        lrObj.transform.SetParent(parent.transform, false);

        var lr = lrObj.AddComponent<LineRenderer>();
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        lr.widthMultiplier = width;
        lr.useWorldSpace = true;
        lr.numCornerVertices = 2;
        lr.loop = false;
        lr.textureMode = LineTextureMode.Tile;
        lr.material = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /* ---------- 生成 2×2 虚线纹理 ---------- */
    static Texture2D MakeDashTex()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };
        Color32 O = new Color32(255, 255, 255, 255);
        Color32 T = new Color32(255, 255, 255, 0);
        tex.SetPixels32(new[] { O, T, O, T });
        tex.Apply();
        return tex;
    }
}
