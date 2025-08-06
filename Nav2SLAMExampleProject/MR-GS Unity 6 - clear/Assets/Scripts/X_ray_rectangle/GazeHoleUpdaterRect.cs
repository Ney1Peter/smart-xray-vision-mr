using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// 通过凝视射线在墙面点云上开一个“近大远小”的可透视洞。<br/>
/// 洞的宽/高随相机到墙面的距离按 <see cref="distToScale"/> 曲线缩放，<br/>
/// 厚度 <see cref="boxThickness"/> 保持不变。
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdaterRect : MonoBehaviour
{
    /*──────── Inspector ────────*/
    [Header("裁剪器 (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipperRect clipper;

    [Header("基础矩形洞尺寸 (m)  宽 × 高")]
    [SerializeField] Vector2 rectSize = new Vector2(0.40f, 0.25f);   // 基础尺寸

    [Header("洞尺寸随距离缩放 (距离→倍率)")]
    [Tooltip("X 轴 = 相机到墙面的距离 (m)，Y 轴 = 尺寸倍率。")]
    [SerializeField]
    AnimationCurve distToScale =
        AnimationCurve.Linear(0.5f, 1.5f, 3.0f, 0.5f);

    [Header("长方体厚度 (m)")]
    [Tooltip("厚度仅向远离相机方向扩展：近端贴在命中点上，远端向里推进。")]
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
    public static float CutDepth { get; private set; } // 当前洞厚度
    public static float CutRadius { get; private set; } // 当前洞最大半径

    /*──────── 内部状态 ────────*/
    Camera cam;
    int mask = ~0;

    Vector3 holePos, holeNormal, holeAxisR, holeAxisU;
    float alpha = 1f, targetA = 1f;     // 1=关闭, 0=完全打开
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

        /* ── 1. 根据相机 → 墙距离计算当前洞尺寸 ── */
        float viewDist = maxDist;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var viewHit, maxDist, mask) &&
            viewHit.collider.name.StartsWith("WallBox"))
        {
            viewDist = viewHit.distance;
        }
        else if (holeOpen) // 洞已开但当前未命中（视线偏移）
        {
            viewDist = Vector3.Distance(cam.transform.position, holePos);
        }

        float scale = distToScale?.Evaluate(viewDist) ?? 1f;
        Vector2 curRectSize = rectSize * Mathf.Max(0.01f, scale);

        // 对外同步
        CutDepth = boxThickness;
        CutRadius = Mathf.Max(curRectSize.x, curRectSize.y) * 0.5f;

        /* ── 2. Raycast 最近墙 ── */
        bool hitWall = Physics.Raycast(cam.transform.position, cam.transform.forward,
                                       out var hit, maxDist, mask) &&
                       hit.collider.name.StartsWith("WallBox");

        bool onSameSpot = hitWall && holeOpen &&
                          Vector3.Distance(hit.point, holePos) < dwellRadius;

        /* ── 3. 状态切换 ── */
        if (hitWall && (!holeOpen || (alpha >= 1f && !onSameSpot)))
        {
            holePos = hit.point;
            holeNormal = hit.normal.normalized;

            Transform wt = hit.collider.transform;
            holeAxisR = wt.right.normalized;
            holeAxisU = wt.up.normalized;

            alpha = 1f;
            targetA = 0f;        // 开始渐隐（开洞）
            holeOpen = true;

            if (boxActive) { clipper.ClearBoxes(); boxActive = false; }
            InvalidateApplied();
        }
        else if (hitWall && onSameSpot)
        {
            targetA = 0f;        // 洞持续开启
        }
        else if (holeOpen)
        {
            targetA = 1f;        // 渐显（关洞）
        }
        else
        {
            return;              // 没洞，无需往下
        }

        /* ── 4. 插值 α ── */
        float step = (alpha > targetA)
                     ? Time.deltaTime / Mathf.Max(0.0001f, fadeOutTime)
                     : Time.deltaTime / Mathf.Max(0.0001f, fadeInTime);
        alpha = Mathf.MoveTowards(alpha, targetA, step);

        // 开合进度 t：0（关闭）→ 1（完全打开）
        float t = easeInOut ? Smoothstep01(1f - alpha) : 1f - alpha;

        /* ── 5. 设置像素级矩形遮罩（全局参数） ── */
        Shader.SetGlobalVector(ID_Center, holePos);
        Shader.SetGlobalVector(ID_AxisR, holeAxisR);
        Shader.SetGlobalVector(ID_AxisU, holeAxisU);
        Shader.SetGlobalVector(ID_AxisN, holeNormal);
        Shader.SetGlobalVector(ID_RectHalf, curRectSize * 0.5f);
        Shader.SetGlobalFloat(ID_MinAlpha, alpha);

        /* ── 6. 依进度 t 更新 OBB ── */
        Vector3 camWS = cam.transform.position;
        Vector3 backDir = Vector3.Dot(holeNormal, (camWS - holePos)) > 0f
                          ? -holeNormal : holeNormal;

        float halfDepth = Mathf.Max(0f, (boxThickness * 0.5f) * t);
        Vector3 obbCenter = holePos + backDir * halfDepth;

        Vector2 halfRU = curRectSize * 0.5f;

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
            clipper.AddBox(obbCenter, holeAxisR, holeAxisU, holeNormal,
                           halfRU, halfDepth);
            boxActive = true;

            lastAppliedHalfDepth = halfDepth;
            lastAppliedHalfRU = halfRU;
            lastAppliedCenter = obbCenter;
            lastAppliedAxisR = holeAxisR;
            lastAppliedAxisU = holeAxisU;
            lastAppliedAxisN = holeNormal;
        }

        /* ── 7. 洞完全关闭时清理 ── */
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

    /* ────────── Helpers ──────────*/

    static float Smoothstep01(float t) => t * t * (3f - 2f * t);

    void InvalidateApplied()
    {
        lastAppliedHalfDepth = -1f;
        lastAppliedHalfRU = new Vector2(-1f, -1f);
        lastAppliedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
        lastAppliedAxisR = lastAppliedAxisU = lastAppliedAxisN = Vector3.zero;
    }
}
