using UnityEngine;
using GaussianSplatting.Runtime;

[RequireComponent(typeof(Camera))]
public class GazeHoleUpdater : MonoBehaviour
{
    /*──────── Inspector ────────*/
    [Header("裁剪器 (PointCloudPathClipper)")][SerializeField] PointCloudPathClipper clipper;
    [Header("洞半径 (m)")][SerializeField] float cutRadius = 0.30f;
    [Header("洞厚度 (m)")][SerializeField] float cutDepth = 0.10f;
    [Header("最大检测距离 (m)")][SerializeField] float maxDist = 10f;

    [Header("判定同一点半径 (m)")][SerializeField] float dwellRadius = 0.03f;
    [Header("渐隐时长 1→0 (s)")][SerializeField] float fadeOutTime = 1.5f;
    [Header("渐显时长 0→1 (s)")][SerializeField] float fadeInTime = 1.0f;

    /*──────── 内部常量 ────────*/
    const float SEGMENT_THRESHOLD = 0.03f;   // α ≤ 3 % → 写段

    /*──────── 外部只读 ────────*/
    public static float CutRadius { get; private set; }
    public static float CutDepth { get; private set; }

    /*──────── 内部状态 ────────*/
    Camera cam;
    int mask = ~0;

    Vector3 holePos;        // 洞中心（冻结）
    Vector3 holeNormal;     // 对应法线
    float alpha = 1f;   // 当前 α
    float targetA = 1f;   // 目标 α（0 or 1）
    bool segmentAdded = false;
    bool holeOpen = false;   // 是否存在洞

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        if (clipper == null || WallBoxBuilding.wallMat == null) return;
        CutRadius = cutRadius;
        CutDepth = cutDepth;

        /* ── 1. 检测是否命中墙 ── */
        bool hitWall = Physics.Raycast(
            cam.transform.position, cam.transform.forward,
            out var hit, maxDist, mask
        ) && hit.collider.name.StartsWith("WallBox");

        bool onSameSpot = hitWall && holeOpen &&
                          Vector3.Distance(hit.point, holePos) < dwellRadius;

        /* ── 2. 决定 targetAlpha 与是否开启新洞 ── */
        if (hitWall && (!holeOpen || (alpha >= 1f && !onSameSpot)))
        {
            // 创建新洞（第一次 or 旧洞已完全关闭）
            holePos = hit.point;
            holeNormal = hit.normal;
            alpha = 1f;
            targetA = 0f;           // 开始渐隐
            holeOpen = true;

            if (segmentAdded) { clipper.ClearAll(); segmentAdded = false; }
        }
        else if (hitWall && onSameSpot)
        {
            // 继续在同一点 → 目标保持 0
            targetA = 0f;
        }
        else if (holeOpen)   // 离开洞所在区域 → 渐显
        {
            targetA = 1f;

            if (segmentAdded) { clipper.ClearAll(); segmentAdded = false; }
        }

        if (!holeOpen) return;     // 没洞可管，直接退出

        /* ── 3. 用 MoveTowards 插值 α ── */
        float speed = (alpha > targetA)
                      ? Time.deltaTime / Mathf.Max(0.0001f, fadeOutTime)
                      : Time.deltaTime / Mathf.Max(0.0001f, fadeInTime);

        alpha = Mathf.MoveTowards(alpha, targetA, speed);

        /* ── 4. 更新 Shader ── */
        WallBoxBuilding.wallMat.SetVector(
            "_CutCenterR",
            new Vector4(holePos.x, holePos.y, holePos.z, cutRadius)
        );
        WallBoxBuilding.wallMat.SetFloat("_CutMinAlpha", alpha);

        /* ── 5. 控制硬裁剪段 ── */
        if (!segmentAdded && alpha <= SEGMENT_THRESHOLD)
        {
            Vector3 back = -holeNormal * cutDepth;
            clipper.AddSegment(holePos, holePos + back, cutRadius);
            segmentAdded = true;
        }
        else if (segmentAdded && alpha > SEGMENT_THRESHOLD)
        {
            clipper.ClearAll();
            segmentAdded = false;
        }

        /* ── 6. 当 α==1 & 不再注视 → 关闭洞 ── */
        if (!hitWall && Mathf.Approximately(alpha, 1f))
        {
            holeOpen = false;
            WallBoxBuilding.wallMat.SetVector("_CutCenterR", Vector4.zero);
        }
    }
}
