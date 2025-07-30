using UnityEngine;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(Camera))]
public class GazeHoleUpdater : MonoBehaviour
{
    /*──────── Inspector ────────*/
    [Header("裁剪器 (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipper clipper;

    [Header("矩形洞尺寸 (m)  宽 × 高")]
    [SerializeField] Vector2 rectSize = new Vector2(0.40f, 0.25f); // 宽=沿墙 right， 高=沿墙 up

    [Header("长方体厚度 (m)")]
    [Tooltip("厚度只向“远离相机”的方向扩展：近端贴在命中点上，远端向里推进。")]
    [SerializeField] float boxThickness = 0.10f;

    [Header("最大检测距离 (m)")]
    [SerializeField] float maxDist = 10f;

    [Header("判定同一点半径 (m)")]
    [SerializeField] float dwellRadius = 0.03f;

    [Header("渐隐时长 1→0 (s)")]
    [SerializeField] float fadeOutTime = 1.0f;

    [Header("渐显时长 0→1 (s)")]
    [SerializeField] float fadeInTime = 0.8f;

    [Header("开合曲线（可选：勾勒更丝滑的缓动）")]
    [SerializeField] bool easeInOut = true;

    /*──────── 对外只读（兼容） ────────*/
    public static float CutDepth { get; private set; } // 供其他脚本读取当前厚度
    public static float CutRadius { get; private set; } // 保留但不参与裁剪

    /*──────── 内部状态 ────────*/
    Camera cam;
    int mask = ~0;

    Vector3 holePos, holeNormal, holeAxisR, holeAxisU;
    float alpha = 1f, targetA = 1f; // 1=关闭, 0=完全打开
    bool boxActive = false;
    bool holeOpen = false;

    // 上一次已应用到 OBB 的参数，用于热更新判断
    float lastAppliedHalfDepth = -1f;
    Vector2 lastAppliedHalfRU = new Vector2(-1f, -1f);
    Vector3 lastAppliedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
    Vector3 lastAppliedAxisR, lastAppliedAxisU, lastAppliedAxisN;

    // Shader property IDs（全局）
    static readonly int ID_Center = Shader.PropertyToID("_CutCenter");
    static readonly int ID_AxisR = Shader.PropertyToID("_CutAxisR");
    static readonly int ID_AxisU = Shader.PropertyToID("_CutAxisU");
    static readonly int ID_AxisN = Shader.PropertyToID("_CutAxisN");
    static readonly int ID_RectHalf = Shader.PropertyToID("_CutRectHalf");
    static readonly int ID_MinAlpha = Shader.PropertyToID("_CutMinAlpha");

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        if (clipper == null) return;

        // 对外同步（比如 WallBoxBuilding 若在用）
        CutDepth = boxThickness;
        CutRadius = Mathf.Max(rectSize.x, rectSize.y) * 0.5f;

        /* ── 1. Raycast 最近墙 ── */
        bool hitWall = Physics.Raycast(
            cam.transform.position, cam.transform.forward,
            out var hit, maxDist, mask
        ) && hit.collider.name.StartsWith("WallBox");

        bool onSameSpot = hitWall && holeOpen &&
                          Vector3.Distance(hit.point, holePos) < dwellRadius;

        /* ── 2. 状态切换 ── */
        if (hitWall && (!holeOpen || (alpha >= 1f && !onSameSpot)))
        {
            holePos = hit.point;
            holeNormal = hit.normal.normalized;

            Transform wt = hit.collider.transform;
            holeAxisR = wt.right.normalized;
            holeAxisU = wt.up.normalized;

            alpha = 1f;
            targetA = 0f; // 开始渐隐（开洞）
            holeOpen = true;

            if (boxActive) { clipper.ClearBoxes(); boxActive = false; }
            InvalidateApplied(); // 重置“已应用”缓存
        }
        else if (hitWall && onSameSpot)
        {
            targetA = 0f; // 持续开
        }
        else if (holeOpen)
        {
            targetA = 1f; // 渐显（关洞）
        }
        else
        {
            return; // 没洞，无需往下
        }

        /* ── 3. 插值 α ── */
        float step = (alpha > targetA)
                     ? Time.deltaTime / Mathf.Max(0.0001f, fadeOutTime)
                     : Time.deltaTime / Mathf.Max(0.0001f, fadeInTime);
        alpha = Mathf.MoveTowards(alpha, targetA, step);

        // 映射到开合进度 t：0（关闭）→1（完全打开）
        float t = 1f - alpha;
        if (easeInOut)
        {
            // Smoothstep 缓动
            t = t * t * (3f - 2f * t);
        }

        /* ── 4. 设置像素级矩形遮罩（全局参数） ── */
        Shader.SetGlobalVector(ID_Center, holePos);
        Shader.SetGlobalVector(ID_AxisR, holeAxisR);
        Shader.SetGlobalVector(ID_AxisU, holeAxisU);
        Shader.SetGlobalVector(ID_AxisN, holeNormal);
        Shader.SetGlobalVector(ID_RectHalf, new Vector2(rectSize.x * 0.5f, rectSize.y * 0.5f));
        Shader.SetGlobalFloat(ID_MinAlpha, alpha);

        /* ── 5. 连续地“按进度 t”更新 OBB（早剔除体积）──
               - 厚度只朝“远离相机”的方向增长
               - 当 t→0 时体积 → 0（不上传/移除）；当 t→1 时体积达到设定厚度
        */
        Vector3 camWS = cam.transform.position;
        Vector3 backDir = Vector3.Dot(holeNormal, (camWS - holePos)) > 0f ? -holeNormal : holeNormal;

        float halfDepth = Mathf.Max(0f, (boxThickness * 0.5f) * t);
        Vector3 obbCenter = holePos + backDir * halfDepth; // 近端贴面，远端向里推进

        Vector2 halfRU = new Vector2(rectSize.x * 0.5f, rectSize.y * 0.5f);

        bool needUpload =
            !boxActive ||
            !Mathf.Approximately(halfDepth, lastAppliedHalfDepth) ||
            !Mathf.Approximately(halfRU.x, lastAppliedHalfRU.x) ||
            !Mathf.Approximately(halfRU.y, lastAppliedHalfRU.y) ||
            (obbCenter - lastAppliedCenter).sqrMagnitude > 1e-8f ||
            Vector3.Dot(holeAxisR, lastAppliedAxisR) < 0.9999f ||
            Vector3.Dot(holeAxisU, lastAppliedAxisU) < 0.9999f ||
            Vector3.Dot(holeNormal, lastAppliedAxisN) < 0.9999f;

        if (halfDepth <= 1e-5f)
        {
            if (boxActive)
            {
                clipper.ClearBoxes();
                boxActive = false;
                InvalidateApplied();
            }
        }
        else if (needUpload)
        {
            if (boxActive) clipper.ClearBoxes();
            clipper.AddBox(
                center: obbCenter,
                axisR: holeAxisR,
                axisU: holeAxisU,
                axisN: holeNormal,
                halfRU: halfRU,
                halfDepth: halfDepth
            );
            boxActive = true;

            // 记录已应用
            lastAppliedHalfDepth = halfDepth;
            lastAppliedHalfRU = halfRU;
            lastAppliedCenter = obbCenter;
            lastAppliedAxisR = holeAxisR;
            lastAppliedAxisU = holeAxisU;
            lastAppliedAxisN = holeNormal;
        }

        /* ── 6. 完全关闭时清理 ── */
        if (!hitWall && Mathf.Approximately(alpha, 1f))
        {
            holeOpen = false;
            Shader.SetGlobalVector(ID_RectHalf, Vector2.zero);
            Shader.SetGlobalFloat(ID_MinAlpha, 1f);

            if (boxActive) { clipper.ClearBoxes(); boxActive = false; }
            InvalidateApplied();
        }
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(ID_MinAlpha, 1f);
        Shader.SetGlobalVector(ID_RectHalf, Vector2.zero);
        if (clipper != null && boxActive) clipper.ClearBoxes();
        boxActive = false; holeOpen = false;
        InvalidateApplied();
    }

    void InvalidateApplied()
    {
        lastAppliedHalfDepth = -1f;
        lastAppliedHalfRU = new Vector2(-1f, -1f);
        lastAppliedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
        lastAppliedAxisR = lastAppliedAxisU = lastAppliedAxisN = Vector3.zero;
    }
}
