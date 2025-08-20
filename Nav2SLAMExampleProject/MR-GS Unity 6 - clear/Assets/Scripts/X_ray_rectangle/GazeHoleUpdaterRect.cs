using UnityEngine;
using GaussianSplatting.Runtime;

/// <summary>
/// Opens a perspective "near-large, far-small" hole on a wall point cloud via gaze ray.<br/>
/// The width/height of the hole scales with the camera-to-wall distance according to <see cref="distToScale"/>,<br/>
/// while thickness <see cref="boxThickness"/> remains constant.
/// </summary>
[RequireComponent(typeof(Camera))]
public class GazeHoleUpdaterRect : MonoBehaviour
{
    /*──────── Inspector ────────*/
    [Header("Clipper (PointCloudPathClipper)")]
    [SerializeField] PointCloudPathClipperRect clipper;

    [Header("Base Rectangular Hole Size (m)  Width × Height")]
    [SerializeField] Vector2 rectSize = new Vector2(0.40f, 0.25f);   // Base size

    [Header("Hole Size Scales with Distance (Distance → Scale Factor)")]
    [Tooltip("X = distance from camera to wall (m), Y = size multiplier.")]
    [SerializeField]
    AnimationCurve distToScale =
        AnimationCurve.Linear(0.5f, 1.5f, 3.0f, 0.5f);

    [Header("Cuboid Thickness (m)")]
    [Tooltip("Thickness only extends away from the camera: front aligns with hit point, back pushed inward.")]
    [SerializeField] float boxThickness = 0.10f;

    [Header("Maximum Detection Distance (m)")]
    [SerializeField] float maxDist = 10f;

    [Header("Same Spot Detection Radius (m)")]
    [SerializeField] float dwellRadius = 0.03f;

    [Header("Fade-Out Time 1→0 (s)")]
    [SerializeField] float fadeOutTime = 1.0f;

    [Header("Fade-In Time 0→1 (s)")]
    [SerializeField] float fadeInTime = 0.8f;

    [Header("Ease Curve for Smoother Transition (Optional)")]
    [SerializeField] bool easeInOut = true;

    /*──────── Public Readonly (for compatibility) ────────*/
    public static float CutDepth { get; private set; } // Current hole thickness
    public static float CutRadius { get; private set; } // Current hole max radius

    /*──────── Internal State ────────*/
    Camera cam;
    int mask = ~0;

    Vector3 holePos, holeNormal, holeAxisR, holeAxisU;
    float alpha = 1f, targetA = 1f;     // 1 = closed, 0 = fully open
    bool boxActive = false;
    bool holeOpen = false;

    // Last applied OBB state, used for update diff check
    float lastAppliedHalfDepth = -1f;
    Vector2 lastAppliedHalfRU = new Vector2(-1f, -1f);
    Vector3 lastAppliedCenter = new Vector3(float.NaN, float.NaN, float.NaN);
    Vector3 lastAppliedAxisR, lastAppliedAxisU, lastAppliedAxisN;

    // Shader global property IDs
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

        /* ── 1. Compute hole size based on camera-to-wall distance ── */
        float viewDist = maxDist;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var viewHit, maxDist, mask) &&
            viewHit.collider.name.StartsWith("WallBox"))
        {
            viewDist = viewHit.distance;
        }
        else if (holeOpen) // Hole already open but not currently hitting wall
        {
            viewDist = Vector3.Distance(cam.transform.position, holePos);
        }

        float scale = distToScale?.Evaluate(viewDist) ?? 1f;
        Vector2 curRectSize = rectSize * Mathf.Max(0.01f, scale);

        // Update external values
        CutDepth = boxThickness;
        CutRadius = Mathf.Max(curRectSize.x, curRectSize.y) * 0.5f;

        /* ── 2. Raycast to closest wall ── */
        bool hitWall = Physics.Raycast(cam.transform.position, cam.transform.forward,
                                       out var hit, maxDist, mask) &&
                       hit.collider.name.StartsWith("WallBox");

        bool onSameSpot = hitWall && holeOpen &&
                          Vector3.Distance(hit.point, holePos) < dwellRadius;

        /* ── 3. State transitions ── */
        if (hitWall && (!holeOpen || (alpha >= 1f && !onSameSpot)))
        {
            holePos = hit.point;
            holeNormal = hit.normal.normalized;

            Transform wt = hit.collider.transform;
            holeAxisR = wt.right.normalized;
            holeAxisU = wt.up.normalized;

            alpha = 1f;
            targetA = 0f;        // Begin fade-out (open hole)
            holeOpen = true;

            if (boxActive) { clipper.ClearBoxes(); boxActive = false; }
            InvalidateApplied();
        }
        else if (hitWall && onSameSpot)
        {
            targetA = 0f;        // Keep hole open
        }
        else if (holeOpen)
        {
            targetA = 1f;        // Begin fade-in (close hole)
        }
        else
        {
            return;              // No hole, skip remaining steps
        }

        /* ── 4. Interpolate alpha ── */
        float step = (alpha > targetA)
                     ? Time.deltaTime / Mathf.Max(0.0001f, fadeOutTime)
                     : Time.deltaTime / Mathf.Max(0.0001f, fadeInTime);
        alpha = Mathf.MoveTowards(alpha, targetA, step);

        // Open-close progress t: 0 (closed) → 1 (fully open)
        float t = easeInOut ? Smoothstep01(1f - alpha) : 1f - alpha;

        /* ── 5. Set pixel-level rectangular clipping (shader globals) ── */
        Shader.SetGlobalVector(ID_Center, holePos);
        Shader.SetGlobalVector(ID_AxisR, holeAxisR);
        Shader.SetGlobalVector(ID_AxisU, holeAxisU);
        Shader.SetGlobalVector(ID_AxisN, holeNormal);
        Shader.SetGlobalVector(ID_RectHalf, curRectSize * 0.5f);
        Shader.SetGlobalFloat(ID_MinAlpha, alpha);

        /* ── 6. Update OBB clipping based on progress t ── */
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

        /* ── 7. Cleanup when hole fully closed ── */
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
