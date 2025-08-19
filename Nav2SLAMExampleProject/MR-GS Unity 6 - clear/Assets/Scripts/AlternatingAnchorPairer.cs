using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using GaussianSplatting.Runtime;

// С��ǩ����ѡ��
public class RealAnchorTag : MonoBehaviour { public int index; }
public class GSAnchorTag : MonoBehaviour { public int index; }

public class AlternatingAnchorPairer : MonoBehaviour
{
    [Header("Refs")]
    public Transform gsRoot;                 // 3DGS ����GS ê�����£�
    public GaussianSplatRenderer gs;         // �����ڻ���㣨��Ϊ�գ�
    public Transform rayOrigin;              // ������̬Դ��δ�ҵ��ֱ�ʱʹ�ã�

    [Header("Anchor Visual Prefabs (�������Զ���)")]
    [Tooltip("����ʱ���Դ� Sample/Spawner BuildingBlock ��ȡ")]
    public GameObject realAnchorPrefab;      // ��ʵê��Prefab������+OVRSpatialAnchor��
    [Tooltip("��Ϊ�գ��������� realAnchorPrefab")]
    public GameObject gsAnchorPrefab;        // GS ê��ֻ�����ӣ����Ƴ� OVRSpatialAnchor��

    [Header("Anchor Colors")]
    public Color realAnchorColor = new(0.30f, 0.80f, 1.00f, 1f);  // ��ʵê����
    public Color gsAnchorColor = new(0.65f, 0.45f, 0.95f, 1f);  // GS ê����

    [Header("Follow Hand (�ٷ�ͬ�����)")]
    public bool followRightController = true;          // ʹ�� rightControllerAnchor ����̬
    public Vector3 followLocalOffset = Vector3.zero;   // ����ֱ��ľֲ�λ��(��)
    public Vector3 followLocalEuler = Vector3.zero;   // ����ֱ��ľֲ���ת(��)

    [Header("�Ǹ���ģʽ��ǰ��ƫ��(��)")]
    public float handForwardOffset = 0.0f;             // �� followRightController=false ʱ��Ч

    [Header("Visual Scale")]
    public float realMarkerScale = 1.0f;     // ��ʵê�������ű���
    public float gsMarkerScale = 1.0f;     // GS ê�������ű���

    [Header("Flow / Links")]
    public bool autoSaveRealAnchors = true;  // ����ʵê���Զ�����
    public bool drawLinkOnEachPair = true;  // �ɶԺ����̻�����

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

    // ����̬
    int _nextIndex = 1;
    bool _expectReal = true;   // true=����ʵê��false=��GSê
    OVRSpatialAnchor _lastReal;
    readonly List<(int idx, Transform real, Transform gs, float pe, float re)> _pairs = new();
    readonly List<GameObject> _links = new();

    // ����ڵ� / �豸
    Transform _followPose;      // ����ȡ����λ�˵� Transform
    OVRCameraRig _rig;

    void Start()
    {
        TryAutoFillPrefabsFromBuildingBlock();  // �Զ���Ĭ��ê���
        SetupFollowPose();                      // ���ø���ڵ�
        if (!realAnchorPrefab)
            Debug.LogWarning("[AltPair] δ���Զ��ҵ� Anchor Prefab���������ÿ��Ӷ��ס�");
    }

    void Update()
    {
#if OCULUS_INTEGRATION_PRESENT
        if (OVRInput.GetDown(OVRInput.Button.One)) HandleToggle(); // �豸 A ��
#else
        if (Input.GetKeyDown(toggleKey)) HandleToggle();            // �༭������
#endif
        if (Input.GetKeyDown(evalKey)) EvaluateAndReport();
        if (Input.GetKeyDown(exportKey)) ExportCsv();
    }

    // �ṩ�� Buttons Mapper �Ĺ�������
    public void PlaceNext() => HandleToggle();
    public void EvaluateNow() => EvaluateAndReport();
    public void ExportNow() => ExportCsv();

    /* =================== ������̬Դ =================== */
    void SetupFollowPose()
    {
        if (!followRightController)
        {
            _followPose = rayOrigin ? rayOrigin : (Camera.main ? Camera.main.transform : null);
            if (!_followPose) Debug.LogWarning("[AltPair] δ�ҵ� rayOrigin/Camera�����ý�ʹ�� (0,0,0)��");
            return;
        }

        // �� OVRCameraRig
        _rig = FindObjectOfType<OVRCameraRig>();
        if (_rig == null || _rig.rightControllerAnchor == null)
        {
            Debug.LogWarning("[AltPair] δ�ҵ� OVRCameraRig/rightControllerAnchor�����˵� rayOrigin/Camera��");
            _followPose = rayOrigin ? rayOrigin : (Camera.main ? Camera.main.transform : null);
            return;
        }

        // ��һ������ڵ㣬ֱ�ӵ�����ȡλ�ˡ��Ĳ��գ���ٷ�һ�£�
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
            // ��δ�����ֱ�������ϣ����ǰ��ƫ��ʱ������ Camera/main��
            if (!followRightController && handForwardOffset != 0f)
                p += src.forward * handForwardOffset;
        }
        else
        {
            p = Vector3.zero; q = Quaternion.identity;
        }
    }

    /* =================== �����̣�ͬһ������ =================== */
    void HandleToggle()
    {
        if (_expectReal) PlaceRealAnchor();
        else PlaceGSAnchor();
    }

    // ���� ��ʵê ���� //
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
            // ���ÿ��Ӷ��ף�������ǰ��
            go = CreateBuiltInAnchorVisual(realAnchorColor, realMarkerScale);
        }

        go.name = $"Real_{_nextIndex}";
        go.transform.SetPositionAndRotation(p, q);

        // ȷ������ OVRSpatialAnchor��prefab ��ͨ���Դ���
        var sa = go.GetComponent<OVRSpatialAnchor>();
        if (!sa) sa = go.AddComponent<OVRSpatialAnchor>();

        // ��ʶ & ��ɫ
        go.AddComponent<RealAnchorTag>().index = _nextIndex;
        TintAnchor(go, realAnchorColor);

        if (autoSaveRealAnchors)
            StartCoroutine(SaveWhenReady(sa, _nextIndex));

        _lastReal = sa;
        _expectReal = false;
        Debug.Log($"[AltPair] ������ʵê idx={_nextIndex} @ {p}");
    }

    IEnumerator SaveWhenReady(OVRSpatialAnchor sa, int idx)
    {
        // �� SDK һ֡���壬���⡰������֡�������桱ʧ��
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

    // ���� GS ê������ gsRoot����ͬλ��/��ת�� ���� //
    void PlaceGSAnchor()
    {
        if (_lastReal == null)
        {
            Debug.LogWarning("[AltPair] ��Ҫ�ȷ���ʵê����һ�� A����");
            return;
        }
        if (!gsRoot)
        {
            Debug.LogWarning("[AltPair] ������ gsRoot��GS ê������£���");
            return;
        }

        GetSpawnPose(out Vector3 p, out Quaternion q);

        GameObject prefab = gsAnchorPrefab ? gsAnchorPrefab : realAnchorPrefab;
        GameObject go;
        if (prefab)
        {
            go = Instantiate(prefab);
            go.transform.localScale *= Mathf.Max(0.001f, gsMarkerScale);
            // ��������λ�ˣ��ٹҸ�������ֲ�ƫ��
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

        // ȷ�����ᱻ������ʵê����
        var sa = go.GetComponent<OVRSpatialAnchor>();
        if (sa) Destroy(sa);

        // ��ʶ + ��ɫ
        go.AddComponent<GSAnchorTag>().index = _nextIndex;
        TintAnchor(go, gsAnchorColor);

        // �ɶԼ�¼��������
        float posErr = Vector3.Distance(_lastReal.transform.position, go.transform.position);
        float rotErr = Quaternion.Angle(_lastReal.transform.rotation, go.transform.rotation);
        _pairs.Add((_nextIndex, _lastReal.transform, go.transform, posErr, rotErr));

        if (drawLinkOnEachPair)
            DrawLink(_lastReal.transform.position, go.transform.position, posErr);

        Debug.Log($"[AltPair] �ɶ� idx={_nextIndex}  posErr={posErr * 100f:F1}cm  rotErr={rotErr:F1}��");

        _nextIndex++;
        _lastReal = null;
        _expectReal = true;
    }

    /* =================== ͳ�� & ���� =================== */
    public void EvaluateAndReport()
    {
        if (_pairs.Count == 0) { Debug.Log("[AltPair] ����������ݡ�"); return; }

        ClearLinks();
        foreach (var p in _pairs) DrawLink(p.real.position, p.gs.position, p.pe);

        float mean = _pairs.Average(p => p.pe);
        float rmse = Mathf.Sqrt(_pairs.Average(p => p.pe * p.pe));
        float med = Median(_pairs.Select(p => p.pe));
        float max = _pairs.Max(p => p.pe);

        float meanR = _pairs.Average(p => p.re);
        float medR = Median(_pairs.Select(p => p.re));
        float maxR = _pairs.Max(p => p.re);

        Debug.Log($"[AltPair] ����={_pairs.Count} | " +
                  $"pos mean={mean * 100f:F1}cm med={med * 100f:F1}cm rmse={rmse * 100f:F1}cm max={max * 100f:F1}cm | " +
                  $"rot mean={meanR:F1}�� med={medR:F1}�� max={maxR:F1}��");
    }

    public void ExportCsv()
    {
        if (_pairs.Count == 0) { Debug.LogWarning("[AltPair] ���������һ�ԡ�"); return; }
        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        using var sw = new StreamWriter(path);
        sw.WriteLine("index,pos_err_m,rot_err_deg,real_x,real_y,real_z,gs_x,gs_y,gs_z");
        foreach (var p in _pairs)
        {
            Vector3 a = p.real.position, b = p.gs.position;
            sw.WriteLine($"{p.idx},{p.pe:F6},{p.re:F3},{a.x:F6},{a.y:F6},{a.z:F6},{b.x:F6},{b.y:F6},{b.z:F6}");
        }
        Debug.Log($"[AltPair] CSV ������{path}");
    }

    /* =================== �Զ�ȡ Prefab =================== */
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

        if (!spawner) { Debug.Log("[AltPair] δ�ҵ� Sample/Spawner ������޷��Զ���ȡ Anchor Prefab��"); return; }

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
            Debug.Log("[AltPair] ���Զ��� Building Block ��ȡ Anchor Prefab��");
        }
    }

    /* =================== ����/����/���� =================== */
    Material MakeAlwaysOnTopUnlit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        // ͸��&�ö�
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 5000;
        // �� shader ֧�� _ZTest������Ϊ Always������ URP �汾û�и����ԣ����Լ��ɣ�
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

        // ����
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Core";
        sphere.transform.SetParent(root.transform, false);
        sphere.transform.localScale = Vector3.one * 0.05f;
        Destroy(sphere.GetComponent<Collider>());
        sphere.GetComponent<Renderer>().material = MakeAlwaysOnTopUnlit(c);

        // ������
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

    // ��ɫ�����Ĺ�����ʣ�
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
