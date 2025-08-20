using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// Hole clipping + dwell-based enlargement (enlarges by 20% after >2s gaze, returns after 1s).<br/>
/// Thickness = size.z of the hit BoxCollider * lossyScale.z
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdaterFrame : MonoBehaviour
{
    /* ===== Inspector ===== */
    [Header("Clipper (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipperFrame clipper;

    [Header("Hole Radius (m)")]
    [SerializeField] float baseRadius = 0.30f;

    [Header("Minimum Alpha at Hole Center (α)")]
    [Range(0, 1)] public float centerAlpha = 0.05f;

    [Header("Max Detection Distance (m)")]
    [SerializeField] float maxDistance = 10f;

    [Header("Clear Clip Segments Each Frame")]
    [SerializeField] bool clearEachFrame = true;

    /* ===== Dwell Enlargement ===== */
    [Header("Dwell-Based Enlargement")]
    [SerializeField] float dwellTimeToEnlarge = 2f;
    [SerializeField] float enlargeFactor = 1.2f;
    [SerializeField] float releaseDelay = 1f;
    [SerializeField] float lerpSpeed = 4f;

    /* ===== Public Static ===== */
    public static float CutRadius { get; private set; }
    public static float CutDepth { get; private set; }

    /* ===== Internal ===== */
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
        if (clipper == null || WallBoxBuildingFrame.wallMat == null) return;
        if (clearEachFrame) clipper.ClearAll();

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out var hit, maxDistance, mask) &&
            hit.collider.name.StartsWith("WallBox"))
        {
            /* ---------- Use Collider Depth Directly ---------- */
            var bc = hit.collider as BoxCollider;
            float depth = bc ? bc.size.z * hit.collider.transform.lossyScale.z : 0.10f;
            CutDepth = depth; // Accessible externally

            Vector3 p = hit.point;
            Vector3 back = -hit.normal * depth;

            Debug.Log($"depth={depth:F2}  backLen={back.magnitude:F2}");

            /* ---------- Dwell-Based Enlargement ---------- */
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

            /* ---------- Radius Interpolation ---------- */
            CutRadius = Mathf.Lerp(CutRadius, targetRadius, Time.deltaTime * lerpSpeed);

            /* ---------- Update Shader ---------- */
            WallBoxBuildingFrame.wallMat.SetVector("_CutCenterR",
                new Vector4(p.x, p.y, p.z, CutRadius));
            WallBoxBuildingFrame.wallMat.SetFloat("_CutMinAlpha", centerAlpha);

            /* ---------- Write Clip Segment ---------- */
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
            WallBoxBuildingFrame.wallMat.SetVector("_CutCenterR", Vector4.zero);
        }
    }
}
