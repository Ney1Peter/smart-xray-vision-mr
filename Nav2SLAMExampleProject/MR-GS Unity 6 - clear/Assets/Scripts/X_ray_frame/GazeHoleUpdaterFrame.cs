using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// 洞裁剪 + 驻留放大（>2 s 放大 20%，离开 1 s 恢复）。<br/>
/// 厚度 = 命中 BoxCollider 的 size.z * lossyScale.z
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdaterFrame : MonoBehaviour
{
    /* ===== Inspector ===== */
    [Header("裁剪器 (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipperFrame clipper;

    [Header("洞半径 (m)")][SerializeField] float baseRadius = 0.30f;

    [Header("洞中心最小透明度 (α)")]
    [Range(0, 1)] public float centerAlpha = 0.05f;

    [Header("最大检测距离 (m)")][SerializeField] float maxDistance = 10f;
    [Header("每帧重置裁剪段")][SerializeField] bool clearEachFrame = true;

    /* ===== 驻留放大 ===== */
    [Header("驻留放大")]
    [SerializeField] float dwellTimeToEnlarge = 2f;
    [SerializeField] float enlargeFactor = 1.2f;
    [SerializeField] float releaseDelay = 1f;
    [SerializeField] float lerpSpeed = 4f;

    /* ===== 对外静态 ===== */
    public static float CutRadius { get; private set; }
    public static float CutDepth { get; private set; }

    /* ===== 内部 ===== */
    Camera cam; int mask = ~0;
    float dwellTimer, releaseTimer, targetRadius;
    Vector3 lastHit; bool isEnlarged;

    void Awake()
    {
        cam = GetComponent<Camera>();
        CutRadius = baseRadius;
        targetRadius = baseRadius;
    }

    void Update()
    {
        if (clipper == null || WallBoxBuilding.wallMat == null) return;
        if (clearEachFrame) clipper.ClearAll();

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out var hit, maxDistance, mask) &&
            hit.collider.name.StartsWith("WallBox"))
        {
            /* ---------- 厚度直接取 Collider ---------- */
            var bc = hit.collider as BoxCollider;
            float depth = bc ? bc.size.z * hit.collider.transform.lossyScale.z : 0.10f;
            CutDepth = depth;                         // 对外可用


            Vector3 p = hit.point;
            Vector3 back = -hit.normal * depth;

            Debug.Log($"depth={depth:F2}  backLen={back.magnitude:F2}");


            /* ---------- 驻留放大 ---------- */
            bool sameSpot = Vector3.Distance(p, lastHit) < 0.02f;
            if (sameSpot)
            {
                dwellTimer += Time.deltaTime;
                releaseTimer = 0f;
                if (!isEnlarged && dwellTimer >= dwellTimeToEnlarge)
                {
                    isEnlarged = true;
                    targetRadius = baseRadius * enlargeFactor;
                }
            }
            else
            {
                dwellTimer = 0f;
                releaseTimer = 0f;
                if (isEnlarged) { isEnlarged = false; targetRadius = baseRadius; }
            }
            lastHit = p;

            /* ---------- 半径插值 ---------- */
            CutRadius = Mathf.Lerp(CutRadius, targetRadius, Time.deltaTime * lerpSpeed);

            /* ---------- 更新 Shader ---------- */
            WallBoxBuilding.wallMat.SetVector("_CutCenterR",
                new Vector4(p.x, p.y, p.z, CutRadius));
            WallBoxBuilding.wallMat.SetFloat("_CutMinAlpha", centerAlpha);

            /* ---------- 写入裁剪段 ---------- */
            clipper.AddSegment(p, p + back, CutRadius);
        }
        else
        {
            releaseTimer += Time.deltaTime;
            if (releaseTimer >= releaseDelay && isEnlarged)
            {
                isEnlarged = false;
                targetRadius = baseRadius;
            }
            CutRadius = Mathf.Lerp(CutRadius, targetRadius, Time.deltaTime * lerpSpeed);
            WallBoxBuilding.wallMat.SetVector("_CutCenterR", Vector4.zero);
        }
    }
}
