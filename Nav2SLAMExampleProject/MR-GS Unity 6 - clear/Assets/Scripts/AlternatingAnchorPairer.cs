using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using GaussianSplatting.Runtime;

// 小标签（可选）
public class RealAnchorTag : MonoBehaviour { public int index; }
public class GSAnchorTag : MonoBehaviour { public int index; }

public class AlternatingAnchorPairer : MonoBehaviour
{
    [Header("Refs")]
    public Transform gsRoot;                 // 3DGS 根（GS 锚挂其下）
    public GaussianSplatRenderer gs;         // 仅用于缓存点（可为空）
    public Transform rayOrigin;              // 备用姿态源（未找到手柄时使用）

    [Header("Anchor Visual Prefabs (可留空自动找)")]
    [Tooltip("启动时尝试从 Sample/Spawner BuildingBlock 读取")]
    public GameObject realAnchorPrefab;      // 真实锚：Prefab（可视+OVRSpatialAnchor）
    [Tooltip("可为空：留空则复用 realAnchorPrefab")]
    public GameObject gsAnchorPrefab;        // GS 锚：只做可视（会移除 OVRSpatialAnchor）

    [Header("Anchor Colors")]
    public Color realAnchorColor = new(0.30f, 0.80f, 1.00f, 1f);  // 真实锚：青
    public Color gsAnchorColor = new(0.65f, 0.45f, 0.95f, 1f);  // GS 锚：紫

    [Header("Follow Hand (官方同款放置)")]
    public bool followRightController = true;          // 使用 rightControllerAnchor 的姿态
    public Vector3 followLocalOffset = Vector3.zero;   // 相对手柄的局部位移(米)
    public Vector3 followLocalEuler = Vector3.zero;   // 相对手柄的局部旋转(度)

    [Header("非跟随模式的前向偏移(米)")]
    public float handForwardOffset = 0.0f;             // 当 followRightController=false 时生效

    [Header("Visual Scale")]
    public float realMarkerScale = 1.0f;     // 真实锚整体缩放倍数
    public float gsMarkerScale = 1.0f;     // GS 锚整体缩放倍数

    [Header("Flow / Links")]
    public bool autoSaveRealAnchors = true;  // 放真实锚后自动保存
    public bool drawLinkOnEachPair = true;  // 成对后立刻画连线

    [Header("Links & Stats")]
    public float linkWidth = 0.006f;
    public Color linkMin = new(0.1f, 1f, 0.6f, 1f);
    public Color linkMax = new(1f, 0.4f, 0.2f, 1f);
    public float linkGate = 0.20f;

    [Header("Hotkeys (Editor Only)")]
    public KeyCode toggleKey = KeyCode.A;
    public KeyCode evalKey = KeyCode.E;
    public KeyCode exportKey = KeyCode.C;

    [Header("CSV")]
    public string csvFileName = "anchor_eval.csv";

    // 运行态
    int _nextIndex = 1;
    bool _expectReal = true;   // true=放真实锚；false=放GS锚
    OVRSpatialAnchor _lastReal;
    readonly List<(int idx, Transform real, Transform gs, float pe, float re)> _pairs = new();
    readonly List<GameObject> _links = new();

    // 跟随节点 / 设备
    Transform _followPose;      // 用于取放置位姿的 Transform
    OVRCameraRig _rig;

    void Start()
    {
        TryAutoFillPrefabsFromBuildingBlock();  // 自动找默认锚外观
        SetupFollowPose();                      // 设置跟随节点
        if (!realAnchorPrefab)
            Debug.LogWarning("[AltPair] 未能自动找到 Anchor Prefab。将用内置可视兜底。");
    }

    void Update()
    {
#if OCULUS_INTEGRATION_PRESENT
        if (OVRInput.GetDown(OVRInput.Button.One)) HandleToggle(); // 设备 A 键
#else
        if (Input.GetKeyDown(toggleKey)) HandleToggle();            // 编辑器键盘
#endif
        if (Input.GetKeyDown(evalKey)) EvaluateAndReport();
        if (Input.GetKeyDown(exportKey)) ExportCsv();
    }

    // 提供给 Buttons Mapper 的公开方法
    public void PlaceNext() => HandleToggle();
    public void EvaluateNow() => EvaluateAndReport();
    public void ExportNow() => ExportCsv();

    /* =================== 放置姿态源 =================== */
    void SetupFollowPose()
    {
        if (!followRightController)
        {
            _followPose = rayOrigin ? rayOrigin : (Camera.main ? Camera.main.transform : null);
            if (!_followPose) Debug.LogWarning("[AltPair] 未找到 rayOrigin/Camera，放置将使用 (0,0,0)。");
            return;
        }

        // 找 OVRCameraRig
        _rig = FindObjectOfType<OVRCameraRig>();
        if (_rig == null || _rig.rightControllerAnchor == null)
        {
            Debug.LogWarning("[AltPair] 未找到 OVRCameraRig/rightControllerAnchor，回退到 rayOrigin/Camera。");
            _followPose = rayOrigin ? rayOrigin : (Camera.main ? Camera.main.transform : null);
            return;
        }

        // 建一个跟随节点，直接当做“取位姿”的参照（与官方一致）
        var go = new GameObject("AnchorFollowPose");
        go.transform.SetParent(_rig.rightControllerAnchor, false);
        go.transform.localPosition = followLocalOffset;
        go.transform.localRotation = Quaternion.Euler(followLocalEuler);
        _followPose = go.transform;
    }

    void GetSpawnPose(out Vector3 p, out Quaternion q)
    {
        var src = _followPose ? _followPose : (rayOrigin ? rayOrigin : (Camera.main ? Camera.main.transform : null));
        if (src)
        {
            p = src.position; q = src.rotation;
            // 当未跟随手柄而你又希望有前向偏移时（例如 Camera/main）
            if (!followRightController && handForwardOffset != 0f)
                p += src.forward * handForwardOffset;
        }
        else
        {
            p = Vector3.zero; q = Quaternion.identity;
        }
    }

    /* =================== 主流程：同一键交替 =================== */
    void HandleToggle()
    {
        if (_expectReal) PlaceRealAnchor();
        else PlaceGSAnchor();
    }

    // —— 真实锚 —— //
    void PlaceRealAnchor()
    {
        GetSpawnPose(out Vector3 p, out Quaternion q);

        GameObject go;
        if (realAnchorPrefab)
        {
            go = Instantiate(realAnchorPrefab);
            go.transform.localScale *= Mathf.Max(0.001f, realMarkerScale);
        }
        else
        {
            // 内置可视兜底（总在最前）
            go = CreateBuiltInAnchorVisual(realAnchorColor, realMarkerScale);
        }

        go.name = $"Real_{_nextIndex}";
        go.transform.SetPositionAndRotation(p, q);

        // 确保存在 OVRSpatialAnchor（prefab 里通常自带）
        var sa = go.GetComponent<OVRSpatialAnchor>();
        if (!sa) sa = go.AddComponent<OVRSpatialAnchor>();

        // 标识 & 改色
        go.AddComponent<RealAnchorTag>().index = _nextIndex;
        TintAnchor(go, realAnchorColor);

        if (autoSaveRealAnchors)
            StartCoroutine(SaveWhenReady(sa, _nextIndex));

        _lastReal = sa;
        _expectReal = false;
        Debug.Log($"[AltPair] 放置真实锚 idx={_nextIndex} @ {p}");
    }

    IEnumerator SaveWhenReady(OVRSpatialAnchor sa, int idx)
    {
        // 给 SDK 一帧缓冲，避免“创建当帧立即保存”失败
        yield return null; yield return null;
        if (sa)
        {
            sa.Save((anchor, ok) =>
            {
                Debug.Log(ok
                    ? $"[AltPair] Save Real idx={idx} OK"
                    : $"[AltPair] Save Real idx={idx} FAIL");
            });
        }
    }

    // —— GS 锚（挂在 gsRoot，下同位置/旋转） —— //
    void PlaceGSAnchor()
    {
        if (_lastReal == null)
        {
            Debug.LogWarning("[AltPair] 需要先放真实锚（按一次 A）。");
            return;
        }
        if (!gsRoot)
        {
            Debug.LogWarning("[AltPair] 请设置 gsRoot（GS 锚需挂其下）。");
            return;
        }

        GetSpawnPose(out Vector3 p, out Quaternion q);

        GameObject prefab = gsAnchorPrefab ? gsAnchorPrefab : realAnchorPrefab;
        GameObject go;
        if (prefab)
        {
            go = Instantiate(prefab);
            go.transform.localScale *= Mathf.Max(0.001f, gsMarkerScale);
            // 先设世界位姿，再挂父，避免局部偏移
            go.transform.SetPositionAndRotation(p, q);
            go.transform.SetParent(gsRoot, true);
        }
        else
        {
            go = CreateBuiltInAnchorVisual(gsAnchorColor, gsMarkerScale, null);
            go.transform.SetPositionAndRotation(p, q);
            go.transform.SetParent(gsRoot, true);
        }

        go.name = $"GS_{_nextIndex}";

        // 确保不会被当成真实锚保存
        var sa = go.GetComponent<OVRSpatialAnchor>();
        if (sa) Destroy(sa);

        // 标识 + 改色
        go.AddComponent<GSAnchorTag>().index = _nextIndex;
        TintAnchor(go, gsAnchorColor);

        // 成对记录并画连线
        float posErr = Vector3.Distance(_lastReal.transform.position, go.transform.position);
        float rotErr = Quaternion.Angle(_lastReal.transform.rotation, go.transform.rotation);
        _pairs.Add((_nextIndex, _lastReal.transform, go.transform, posErr, rotErr));

        if (drawLinkOnEachPair)
            DrawLink(_lastReal.transform.position, go.transform.position, posErr);

        Debug.Log($"[AltPair] 成对 idx={_nextIndex}  posErr={posErr * 100f:F1}cm  rotErr={rotErr:F1}°");

        _nextIndex++;
        _lastReal = null;
        _expectReal = true;
    }

    /* =================== 统计 & 导出 =================== */
    public void EvaluateAndReport()
    {
        if (_pairs.Count == 0) { Debug.Log("[AltPair] 暂无配对数据。"); return; }

        ClearLinks();
        foreach (var p in _pairs) DrawLink(p.real.position, p.gs.position, p.pe);

        float mean = _pairs.Average(p => p.pe);
        float rmse = Mathf.Sqrt(_pairs.Average(p => p.pe * p.pe));
        float med = Median(_pairs.Select(p => p.pe));
        float max = _pairs.Max(p => p.pe);

        float meanR = _pairs.Average(p => p.re);
        float medR = Median(_pairs.Select(p => p.re));
        float maxR = _pairs.Max(p => p.re);

        Debug.Log($"[AltPair] 对数={_pairs.Count} | " +
                  $"pos mean={mean * 100f:F1}cm med={med * 100f:F1}cm rmse={rmse * 100f:F1}cm max={max * 100f:F1}cm | " +
                  $"rot mean={meanR:F1}° med={medR:F1}° max={maxR:F1}°");
    }

    public void ExportCsv()
    {
        if (_pairs.Count == 0) { Debug.LogWarning("[AltPair] 先完成至少一对。"); return; }
        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        using var sw = new StreamWriter(path);
        sw.WriteLine("index,pos_err_m,rot_err_deg,real_x,real_y,real_z,gs_x,gs_y,gs_z");
        foreach (var p in _pairs)
        {
            Vector3 a = p.real.position, b = p.gs.position;
            sw.WriteLine($"{p.idx},{p.pe:F6},{p.re:F3},{a.x:F6},{a.y:F6},{a.z:F6},{b.x:F6},{b.y:F6},{b.z:F6}");
        }
        Debug.Log($"[AltPair] CSV 导出：{path}");
    }

    /* =================== 自动取 Prefab =================== */
    void TryAutoFillPrefabsFromBuildingBlock()
    {
        if (realAnchorPrefab != null) { if (gsAnchorPrefab == null) gsAnchorPrefab = realAnchorPrefab; return; }

        var allMono = FindObjectsOfType<MonoBehaviour>(true);
        MonoBehaviour spawner = allMono.FirstOrDefault(m =>
            m && (
                m.GetType().Name.Contains("SampleSpatialAnchor", StringComparison.OrdinalIgnoreCase) ||
                m.GetType().Name.Contains("SpatialAnchorSpawnerBuildingBlock", StringComparison.OrdinalIgnoreCase) ||
                m.GetType().Name.Contains("SpatialAnchorCoreBuildingBlock", StringComparison.OrdinalIgnoreCase)
            ));

        if (!spawner) { Debug.Log("[AltPair] 未找到 Sample/Spawner 组件，无法自动读取 Anchor Prefab。"); return; }

        GameObject prefab = null;
        var t = spawner.GetType();
        var f = t.GetField("anchorPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) prefab = f.GetValue(spawner) as GameObject;
        if (!prefab)
        {
            var p = t.GetProperty("AnchorPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) prefab = p.GetValue(spawner, null) as GameObject;
        }
        if (prefab)
        {
            realAnchorPrefab = prefab;
            if (gsAnchorPrefab == null) gsAnchorPrefab = realAnchorPrefab;
            Debug.Log("[AltPair] 已自动从 Building Block 读取 Anchor Prefab。");
        }
    }

    /* =================== 可视/画线/工具 =================== */
    Material MakeAlwaysOnTopUnlit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        // 透明&置顶
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 5000;
        // 若 shader 支持 _ZTest，则设为 Always（部分 URP 版本没有该属性，忽略即可）
        if (mat.HasProperty("_ZTest"))
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", c * 0.6f);
        }
        return mat;
    }

    GameObject CreateBuiltInAnchorVisual(Color c, float scale, Transform parent = null)
    {
        var root = new GameObject("BuiltInAnchorVisual");
        if (parent) root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);

        // 球体
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Core";
        sphere.transform.SetParent(root.transform, false);
        sphere.transform.localScale = Vector3.one * 0.05f;
        Destroy(sphere.GetComponent<Collider>());
        sphere.GetComponent<Renderer>().material = MakeAlwaysOnTopUnlit(c);

        // 三个环
        void AddRing(string n, Vector3 axis, float r)
        {
            var go = new GameObject(n);
            go.transform.SetParent(root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.006f;
            lr.material = MakeAlwaysOnTopUnlit(c);
            lr.loop = true; lr.useWorldSpace = false;
            lr.positionCount = 64;
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (lr.positionCount - 1f) * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                lr.SetPosition(i, p);
            }
            if (axis == Vector3.right) go.transform.rotation = Quaternion.Euler(0, 90, 0);
            else if (axis == Vector3.up) go.transform.rotation = Quaternion.identity;
            else if (axis == Vector3.forward) go.transform.rotation = Quaternion.Euler(90, 0, 0);
        }

        AddRing("Ring_XY", Vector3.up, 0.08f);
        AddRing("Ring_YZ", Vector3.right, 0.08f);
        AddRing("Ring_ZX", Vector3.forward, 0.08f);

        return root;
    }

    void DrawLink(Vector3 a, Vector3 b, float err)
    {
        var go = new GameObject("AnchorLink");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2; lr.SetPositions(new[] { a, b });
        lr.widthMultiplier = linkWidth;
        lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        { color = Color.Lerp(linkMin, linkMax, Mathf.Clamp01(err / linkGate)) };
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _links.Add(go);
    }
    void ClearLinks() { foreach (var g in _links) if (g) Destroy(g); _links.Clear(); }

    float Median(IEnumerable<float> v)
    {
        var l = v.ToList(); if (l.Count == 0) return 0;
        l.Sort(); int k = l.Count / 2;
        return (l.Count % 2 == 1) ? l[k] : (l[k - 1] + l[k]) * 0.5f;
    }

    // 改色（不改共享材质）
    void TintAnchor(GameObject root, Color c)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mat = r.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * 0.6f);
            }
        }
    }
}
