using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using Meta.XR.MRUtilityKit;

[DefaultExecutionOrder(100)]
public class RoomFrameHighlighter : MonoBehaviour
{
    [Header("Line Width (m)")]     public float lineWidth = 0.01f;
    [Header("Offset from Plane (m)")] public float offset = 0.008f;
    [Header("Wall Inset (m)")]      public float wallInset = 0.02f;

    [Header("Color Settings")]
    public Color wallColor    = new Color32(95,  70,  45,  108);
    public Color ceilingColor = new Color32(60,  65,  70,  110);
    public Color floorColor   = new Color32(80,  60, 100, 108);

    private GameObject wallParent;
    private GameObject ceilingParent;
    private GameObject floorParent;

    private InputDevice rightController;
    private bool prevA, prevB, prevStick;

    private Shader _urpUnlit;
    private static Texture2D _dashTex;

    void Awake()
    {
        // 订阅设备连接事件
        InputDevices.deviceConnected += OnDeviceConnected;
        RefreshController();
    }

    void OnDestroy()
    {
        InputDevices.deviceConnected -= OnDeviceConnected;
    }

    private void OnDeviceConnected(InputDevice device)
    {
        // 如果右手控制器接入，则缓存
        if ((device.characteristics & InputDeviceCharacteristics.Right) != 0 &&
            (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
        {
            rightController = device;
            Debug.Log("RoomFrameHighlighter ▶ Right controller connected");
        }
    }

    private void RefreshController()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeldInHand |
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller,
            devices);
        if (devices.Count > 0)
            rightController = devices[0];
    }

    IEnumerator Start()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
            yield return null;

        _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (_dashTex == null) _dashTex = MakeDashTex();

        wallParent    = new GameObject("WallFrames");
        ceilingParent = new GameObject("CeilingFrames");
        floorParent   = new GameObject("FloorFrames");
        wallParent.transform.SetParent(transform, false);
        ceilingParent.transform.SetParent(transform, false);
        floorParent.transform.SetParent(transform, false);

        var room = MRUK.Instance.GetCurrentRoom();
        BuildFrom(room, "WallAnchors",    wallColor,    "WallFrame",    wallParent.transform,    true);
        BuildFrom(room, "CeilingAnchors", ceilingColor, "CeilingFrame", ceilingParent.transform, false);
        BuildFrom(room, "CeilingAnchor",  ceilingColor, "CeilingFrame", ceilingParent.transform, false);
        BuildFrom(room, "FloorAnchors",   floorColor,   "FloorFrame",   floorParent.transform,   false);
        BuildFrom(room, "FloorAnchor",    floorColor,   "FloorFrame",   floorParent.transform,   false);

        Debug.Log("RoomFrameHighlighter ▶ Frames built and bound to controls");
    }

    void Update()
    {
        // 如果控制器无效，尝试刷新
        if (!rightController.isValid)
            RefreshController();

        if (rightController.isValid)
        {
            if (rightController.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a && !prevA)
                wallParent.SetActive(!wallParent.activeSelf);
            prevA = a;

            if (rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b) && b && !prevB)
                ceilingParent.SetActive(!ceilingParent.activeSelf);
            prevB = b;

            if (rightController.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stick) && stick && !prevStick)
                floorParent.SetActive(!floorParent.activeSelf);
            prevStick = stick;
        }

        // 键盘备选
        if (Input.GetKeyDown(KeyCode.W)) wallParent.SetActive(!wallParent.activeSelf);
        if (Input.GetKeyDown(KeyCode.C)) ceilingParent.SetActive(!ceilingParent.activeSelf);
        if (Input.GetKeyDown(KeyCode.F)) floorParent.SetActive(!floorParent.activeSelf);
    }

    int BuildFrom(object obj, string member, Color clr, string prefix, Transform parent, bool isWall)
    {
        var mi = obj.GetType().GetMember(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
        if (mi == null) return 0;
        object val = mi is FieldInfo f ? f.GetValue(obj) : mi is PropertyInfo p ? p.GetValue(obj) : null;
        if (val == null) return 0;

        int count = 0;
        if (val is IEnumerable<MRUKAnchor> list)
        {
            foreach (var a in list)
                if (DrawFrame(a, clr, $"{prefix}_{count}", parent, isWall)) count++;
        }
        else if (val is MRUKAnchor single)
        {
            if (DrawFrame(single, clr, $"{prefix}_0", parent, isWall)) count = 1;
        }
        return count;
    }

    bool DrawFrame(MRUKAnchor a, Color clr, string goName, Transform parent, bool isWall)
    {
        if (a == null || !a.PlaneRect.HasValue) return false;
        var rect = a.PlaneRect.Value;
        float hx = rect.size.x * 0.5f - (isWall ? wallInset : 0);
        float hy = rect.size.y * 0.5f - (isWall ? wallInset : 0);
        Vector3 c = a.transform.position + a.transform.forward * offset;
        Vector3 r = a.transform.right, u = a.transform.up;
        Vector3[] pts = {
            c + (-r * hx) + (-u * hy),
            c + ( r * hx) + (-u * hy),
            c + ( r * hx) + ( u * hy),
            c + (-r * hx) + ( u * hy),
            c + (-r * hx) + (-u * hy)
        };

        var go = new GameObject(goName);
        go.transform.SetParent(parent, false);

        var mat = new Material(_urpUnlit) { color = clr, enableInstancing = true, mainTexture = _dashTex };
        mat.SetTextureScale("_BaseMap", new Vector2(20, 1));
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", clr * 1.5f);

        CreateLR(go, "outer", pts, lineWidth, mat);
        CreateLR(go, "inner", pts, lineWidth * 0.4f, mat);
        return true;
    }

    void CreateLR(GameObject parent, string name, Vector3[] pts, float width, Material mat)
    {
        var lrGo = new GameObject(name);
        lrGo.transform.SetParent(parent.transform, false);
        var lr = lrGo.AddComponent<LineRenderer>();
        lr.positionCount     = pts.Length;
        lr.SetPositions(pts);
        lr.widthMultiplier   = width;
        lr.useWorldSpace     = true;
        lr.numCornerVertices = 2;
        lr.loop              = false;
        lr.textureMode       = LineTextureMode.Tile;
        lr.material          = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Texture2D MakeDashTex()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Repeat
        };
        var O = new Color32(255, 255, 255, 255);
        var T = new Color32(255, 255, 255,   0);
        tex.SetPixels32(new[] { O, T, O, T });
        tex.Apply();
        return tex;
    }
}
