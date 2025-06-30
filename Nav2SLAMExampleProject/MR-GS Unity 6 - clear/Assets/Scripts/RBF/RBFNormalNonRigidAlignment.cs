// RBFNormalNonRigidAlignment.cs (Corrected - No C# Normal Handling for DLL)
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime;
using System;
using System.Linq;
using Appletea.Dev.PointCloud; // Assuming YOUR ChunkManager is in this namespace
using Meta.XR; // Assuming YOUR EnvironmentRaycastManager is in this namespace

public class RBFNormalNonRigidAlignment : MonoBehaviour
{
    // --- Enums ---
    public enum Density : int { low = 32, medium = 64, high = 128, vHigh = 256, ultra = 512 }

    // --- Serialized Fields ---
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    [Tooltip("是否累积变形效果")]
    public bool accumulateDeformation = true;
    [SerializeField] private EnvironmentRaycastManager depthManager;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    [Space(10)]
    [Header("RBF Alignment Parameters")]
    [Range(10, 500)] public int numControlPointsN = 50;
    public int rbfKernelType = 0;
    public float rbfSigma = 0.1f;
    public bool includeAffineTerm = true;
    public float correspondenceMaxDistance = 0.2f;
    public float regularizationLambda = 1e-6f;
    [Space(5)]
    [Header("Correspondence Filtering (Executed in DLL)")]
    [Tooltip("Enable Reciprocal Nearest Neighbor check in DLL")]
    public bool useReciprocalCheck = true;
    [Tooltip("Enable Normal Vector consistency check in DLL (DLL calculates normals)")]
    public bool useNormalCheck = true;
    [Range(-1.0f, 1.0f)]
    [Tooltip("Cosine of max angle difference for normal check")]
    public float normalConsistencyThreshold = 0.866f;

    [Space(10)]
    [Header("Chunk & Scan Settings")]
    [SerializeField] private int chunkSize = 1;
    [SerializeField] private int maxPointsPerChunk = 256;
    [SerializeField] private int initialPoolSize = 1000;
    [SerializeField] private Camera mainCamera;
    [SerializeField][Tooltip("Percentage of the field of view")] private float fovMargin = 0.9f;
    [SerializeField] private float scanInterval = 1.0f;
    [SerializeField] private Density density = Density.medium;
    [SerializeField][Tooltip("The limit is about 5m")] private float maxScanDistance = 5;

    [Space(10)]
    [Header("Rendering Settings")]
    [SerializeField] private GameObject pointPrefab;
    [SerializeField] private float renderingRadius = 10.0f;
    [SerializeField] int maxChunkCount = 15;

    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f); // Initial size of the selection sphere
    public float moveSpeed = 0.5f; // Speed to move the sphere
    public Material boxMaterial; // Material for the selection sphere

    // --- Private Variables ---
    private ChunkManager pointsData; // Uses YOUR ChunkManager
    private Coroutine scanCoroutine = null;
    private bool gaussianDataReady = false;
    private bool alignerInitialized = false;
    private GameObject selectionBox; // The sphere used for selection

    // Gaussian Splatting Data
    private int splatCount = 0;
    private GraphicsBuffer posBuffer;
    private GraphicsBuffer originalPosBuffer;
    private Vector3[] positionsCPU;
    private Vector3[] originalPositionsCPU;

    // Scan Data (Target for RBF)
    private List<Vector3> currentScanData = new List<Vector3>();
    // private List<Vector3> currentScanNormals = new List<Vector3>(); // REMOVED

    // --- RBF DLL Import ---
    private const string DllName = "RbfAlignerDllNormal";

    // InitializeAligner Signature (No normals pointer)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAligner(IntPtr targetReferenceCloudPtr, int numTargetPoints);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShutdownAligner();

    // AlignClusterRBF Signature (No normals pointer)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int AlignClusterRBF(
        IntPtr clusterSourcePointsPPtr, int numClusterPointsK, int numControlPointsN, int rbfKernelType,
        float rbfSigma, bool includeAffineTerm, float correspondenceMaxDistance, float regularizationLambda,
        bool use_reciprocal_check, bool use_normal_check, float normal_consistency_threshold,
        IntPtr outDeformedPointsXPrimePtr
    );

    // --- Unity Methods ---
    void Start()
    {
        if (GaussianRendererObject == null) GaussianRendererObject = GetComponent<GaussianSplatRenderer>();
        if (GaussianRendererObject == null) { Debug.LogError("GS component not found!"); enabled = false; return; }
        if (TryInitializeGaussianData()) { gaussianDataReady = true; } else { enabled = false; return; }
        pointsData = new ChunkManager(chunkSize, maxPointsPerChunk);
        if (pointCloudRenderer != null) pointCloudRenderer.Initialize(pointPrefab, initialPoolSize); else Debug.LogWarning("PointCloudRenderer not assigned!");
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) { Debug.LogError("Main Camera not found!"); enabled = false; return; }
        Debug.Log("RBFNormalNonRigidAlignment Started.");

    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            CreateOrResetSelectionSphere();
        }
        if (OVRInput.GetDown(OVRInput.RawButton.B)) { if (scanCoroutine == null) { Debug.Log("Scan Started"); scanCoroutine = StartCoroutine(ScanRoutine()); } }
        else if (OVRInput.GetUp(OVRInput.RawButton.B)) { if (scanCoroutine != null) { Debug.Log("Scan Stopped"); StopCoroutine(scanCoroutine); scanCoroutine = null; } }
        if (OVRInput.GetDown(OVRInput.RawButton.A)) { Debug.Log("Alignment Triggered"); PerformRBFAlignment(); }
        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            Debug.Log("Clearing scanned data...");
            if (scanCoroutine != null) { StopCoroutine(scanCoroutine); scanCoroutine = null; }
            if (pointsData != null) { pointsData.Clear(); currentScanData.Clear(); } // Clear pointsData
            if (pointCloudRenderer != null) { pointCloudRenderer.UpdatePointCloud(new List<Vector3>()); }
            CleanupAligner(); Debug.Log("Scanned data cleared.");
        }
        if (selectionBox != null)
        {
            float move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y * moveSpeed * Time.deltaTime;
            selectionBox.transform.position += Camera.main.transform.forward * move;
        }
        if (Input.GetKeyDown(KeyCode.R)) { Debug.Log("Re-initializing Aligner..."); CleanupAligner(); InitializeDLLWithScanData(); }
    }

    void OnDestroy() { CleanupAligner(); originalPosBuffer?.Release(); originalPosBuffer = null; Debug.Log("Cleaned up resources."); }
    void OnApplicationQuit() { CleanupAligner(); originalPosBuffer?.Release(); originalPosBuffer = null; }

    // --- Initialization and Data Handling ---
    bool TryInitializeGaussianData()
    {
        try
        {
            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            if (posBuffer == null || !posBuffer.IsValid() || posBuffer.count <= 0) { Debug.LogError("Failed posBuffer."); return false; }
            splatCount = GaussianRendererObject.splatCount; if (splatCount <= 0) { Debug.LogError("Zero splats."); return false; }
            positionsCPU = new Vector3[splatCount]; originalPositionsCPU = new Vector3[splatCount];
            posBuffer.GetData(positionsCPU); Array.Copy(positionsCPU, originalPositionsCPU, splatCount);
            int stride = Marshal.SizeOf(typeof(Vector3));
            if (posBuffer.stride != stride) { Debug.LogWarning($"posBuffer stride ({posBuffer.stride}) != Vector3 stride ({stride})."); }
            originalPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, stride); originalPosBuffer.SetData(originalPositionsCPU);
            Debug.Log($"Initialized GS data: {splatCount} points."); gaussianDataReady = true; return true;
        }
        catch (Exception e) { Debug.LogError($"Exception during GS data init: {e.Message}"); return false; }
    }

    // Removed OnRoomMeshReady

    // UPDATED Initialize function (No normals pointer)
    bool InitializeDLLWithScanData()
    {
        currentScanData = pointsData?.GetAllPoints() ?? new List<Vector3>();
        if (currentScanData.Count == 0) { Debug.LogError("Cannot init: No scan data."); return false; }
        if (alignerInitialized) { Debug.LogWarning("Aligner already initialized."); return true; }

        Debug.Log($"Initializing RBF DLL with {currentScanData.Count} scanned points...");
        float[] targetPointsFlat = VectorsToFloatArray(currentScanData);
        // float[] targetNormalsFlat = null; // REMOVED

        GCHandle pointsHandle = default; int result = -1;
        try
        {
            pointsHandle = GCHandle.Alloc(targetPointsFlat, GCHandleType.Pinned);
            IntPtr pointsPtr = pointsHandle.AddrOfPinnedObject();
            // IntPtr normalsPtr = IntPtr.Zero; // REMOVED

            result = InitializeAligner(pointsPtr, /*normalsPtr,*/ currentScanData.Count); // Call UPDATED C++ Init

            if (result == 0) { alignerInitialized = true; Debug.Log("RBF Aligner Initialized."); }
            else { Debug.LogError($"Failed to initialize RBF DLL. Error: {result}"); alignerInitialized = false; }
        }
        catch (DllNotFoundException) { Debug.LogError($"DLL Not Found: {DllName}.dll"); alignerInitialized = false; }
        catch (Exception e) { Debug.LogError($"Exception during InitializeAligner: {e.Message}"); alignerInitialized = false; }
        finally
        {
            if (pointsHandle.IsAllocated) pointsHandle.Free();
            // REMOVED normal handle free
        }
        return alignerInitialized;
    }

    void CleanupAligner()
    {
        if (alignerInitialized) { Debug.Log("Shutting down RBF Aligner DLL..."); try { ShutdownAligner(); } catch (Exception e) { Debug.LogError($"Exception during ShutdownAligner: {e.Message}"); } finally { alignerInitialized = false; } }
    }

    // --- RBF Alignment Logic ---
    void PerformRBFAlignment()
    {
        // --- Pre-checks ---
        if (!alignerInitialized)
        {
            Debug.Log("Aligner not initialized. Attempting initialization...");
            if (!InitializeDLLWithScanData()) { Debug.LogError("Init failed. Cannot align."); return; }
        }
        if (!gaussianDataReady) { Debug.LogError("GS data not ready."); return; }
        if (pointsData == null) { Debug.LogWarning("No scan data for bounds."); return; }

        Transform tf = GaussianRendererObject.transform;
        //Bounds scanBounds = pointsData.GetPreciseBounds();
        Bounds boxBounds = new Bounds(selectionBox.transform.position, selectionBox.transform.localScale);

        // --- 1. Select Source Points (World Space) ---
        List<Vector3> clusterSourcePoints_World = new List<Vector3>();
        List<int> clusterIndices = new List<int>();
        Vector3[] sourceStateCPU = accumulateDeformation ? originalPositionsCPU : positionsCPU;
        if (sourceStateCPU == null) { Debug.LogError("Source state CPU array null!"); return; }

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 worldPos = tf.TransformPoint(sourceStateCPU[i]);
            if (boxBounds.Contains(worldPos))
            {
                clusterSourcePoints_World.Add(worldPos);
                clusterIndices.Add(i);
            }
        }

        int K = clusterSourcePoints_World.Count;
        Debug.Log($"Extracted {K} GS points within scan bounds for RBF.");
        if (K == 0) { Debug.LogWarning("No GS points in scan bounds."); return; }

        // --- 2. Prepare Data for DLL ---
        float[] sourceFloatArray = VectorsToFloatArray(clusterSourcePoints_World);
        // float[] sourceNormalsFloatArray = null; // REMOVED
        float[] deformedFloatArray = new float[K * 3];

        // --- 3. Pin Memory & Call DLL ---
        GCHandle sourceHandle = default; /*GCHandle normalHandle = default;*/ GCHandle deformedHandle = default;
        int result = -1;
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            sourceHandle = GCHandle.Alloc(sourceFloatArray, GCHandleType.Pinned);
            // REMOVED normal handle pinning
            deformedHandle = GCHandle.Alloc(deformedFloatArray, GCHandleType.Pinned);
            IntPtr sourcePtr = sourceHandle.AddrOfPinnedObject();
            // IntPtr normalPtr = IntPtr.Zero; // REMOVED
            IntPtr deformedPtr = deformedHandle.AddrOfPinnedObject();

            Debug.Log($"Calling AlignClusterRBF: K={K}, N={numControlPointsN}, Kernel={rbfKernelType}, Sigma={rbfSigma}, Affine={includeAffineTerm}, MaxDist={correspondenceMaxDistance}, Lambda={regularizationLambda}, RNN={useReciprocalCheck}, Normals={useNormalCheck}, NormThresh={normalConsistencyThreshold}");

            result = AlignClusterRBF( // Call UPDATED signature (without normalPtr)
                sourcePtr, /*normalPtr,*/ K, numControlPointsN, rbfKernelType, rbfSigma,
                includeAffineTerm, correspondenceMaxDistance, regularizationLambda,
                useReciprocalCheck, useNormalCheck, normalConsistencyThreshold,
                deformedPtr
            );
        }
        catch (Exception e) { Debug.LogError($"Exception during AlignClusterRBF call: {e.Message}\n{e.StackTrace}"); result = -99; }
        finally
        {
            stopwatch.Stop();
            if (sourceHandle.IsAllocated) sourceHandle.Free();
            // REMOVED normal handle free
            if (deformedHandle.IsAllocated) deformedHandle.Free();
        }
        Debug.Log($"AlignClusterRBF DLL call took: {stopwatch.ElapsedMilliseconds} ms");

        // --- 5. Process Results ---
        if (result == 0)
        {
            Debug.Log("RBF Alignment successful.");
            List<Vector3> deformedPoints_World = FloatArrayToVectors(deformedFloatArray, K);
            if (deformedPoints_World != null)
            {
                int updatedCount = 0; bool needsGpuOriginalUpdate = false;
                for (int i = 0; i < K; i++)
                {
                    int originalIndex = clusterIndices[i];
                    if (originalIndex >= 0 && originalIndex < splatCount)
                    {
                        Vector3 newWorldPos = deformedPoints_World[i];
                        Vector3 newLocalPos = tf.InverseTransformPoint(newWorldPos);
                        positionsCPU[originalIndex] = newLocalPos;
                        if (accumulateDeformation) { originalPositionsCPU[originalIndex] = newLocalPos; needsGpuOriginalUpdate = true; }
                        updatedCount++;
                    }
                    else { Debug.LogWarning($"Invalid index {originalIndex}."); }
                }
                if (updatedCount > 0)
                {
                    if (posBuffer != null && posBuffer.IsValid())
                    {
                        posBuffer.SetData(positionsCPU); Debug.Log($"Updated posBuffer for {updatedCount} points.");
                        if (accumulateDeformation && needsGpuOriginalUpdate)
                        {
                            if (originalPosBuffer != null && originalPosBuffer.IsValid()) { originalPosBuffer.SetData(originalPositionsCPU); Debug.Log("Updated originalPosBuffer."); }
                            else { Debug.LogError("originalPosBuffer invalid."); }
                        }
                    }
                    else { Debug.LogError("posBuffer invalid."); gaussianDataReady = false; }
                }
                else { Debug.LogWarning("Alignment OK, but no points updated."); }
            }
            else { Debug.LogError("Failed to convert deformed points back."); }
        }
        else { Debug.LogError($"RBF Alignment failed. Code: {result}"); }
    }


    // --- Helper Functions ---
    private float[] VectorsToFloatArray(List<Vector3> vectors) { if (vectors == null || vectors.Count == 0) return new float[0]; float[] f = new float[vectors.Count * 3]; for (int i = 0; i < vectors.Count; i++) { f[i * 3] = vectors[i].x; f[i * 3 + 1] = vectors[i].y; f[i * 3 + 2] = vectors[i].z; } return f; }
    private float[] VectorsToFloatArray(Vector3[] vectors) { if (vectors == null || vectors.Length == 0) return new float[0]; float[] f = new float[vectors.Length * 3]; for (int i = 0; i < vectors.Length; i++) { f[i * 3] = vectors[i].x; f[i * 3 + 1] = vectors[i].y; f[i * 3 + 2] = vectors[i].z; } return f; }
    private List<Vector3> FloatArrayToVectors(float[] floatArray, int pointCount) { if (floatArray == null || floatArray.Length != pointCount * 3) { Debug.LogError($"Invalid float array size ({floatArray?.Length}) for {pointCount} Vector3."); return null; } List<Vector3> v = new List<Vector3>(pointCount); for (int i = 0; i < pointCount; i++) { v.Add(new Vector3(floatArray[i * 3], floatArray[i * 3 + 1], floatArray[i * 3 + 2])); } return v; }
    Vector3 ComputeCentroid(List<Vector3> points) { if (points == null || points.Count == 0) return Vector3.zero; Vector3 sum = Vector3.zero; foreach (Vector3 p in points) sum += p; return sum / points.Count; }

    // --- Scanning Logic (Modify ScanAndStorePointCloud) ---
    IEnumerator ScanRoutine()
    {
        while (true)
        {
            ScanAndStorePointCloud(((int)density), pointsData);
            if (pointCloudRenderer != null)
            {
                List<Vector3> pointsToRender = pointsData.GetPointsInRadius(mainCamera.transform.position, renderingRadius, maxChunkCount);
                pointCloudRenderer.UpdatePointCloud(pointsToRender);
            }
            currentScanData = pointsData.GetAllPoints(); // Update C# side cache of points
            // currentScanNormals removed
            yield return new WaitForSeconds(scanInterval);
        }
    }

    // UPDATED: Only add points, assuming your ChunkManager has AddPoint
    void ScanAndStorePointCloud(int sampleSize, ChunkManager pointsData)
    {
        if (depthManager == null) { Debug.LogWarning("Depth Manager not set."); return; }
        List<Vector2> viewSpaceCoords = GenerateViewSpaceCoords(sampleSize, sampleSize);
        List<Ray> rays = new List<Ray>(); foreach (Vector2 i in viewSpaceCoords) rays.Add(mainCamera.ViewportPointToRay(new Vector3(i.x, i.y, 0)));
        List<EnvironmentRaycastHit> hits = new List<EnvironmentRaycastHit>(); // Still get full hit info
        foreach (Ray ray in rays)
        {
            EnvironmentRaycastHit result;
            if (depthManager.Raycast(ray, out result, maxScanDistance))
            {
                if (Vector3.Distance(result.point, mainCamera.transform.position) < maxScanDistance)
                    hits.Add(result);
            }
        }
        if (typeof(ListExtensions).GetMethod("Shuffle") != null) { ListExtensions.Shuffle(hits); } else { Debug.LogWarning("ListExtensions.Shuffle not found."); }

        // *** Store only points using AddPoint (assuming it exists) ***
        foreach (var hit in hits)
        {
            // Replace AddPointNormal with your method to add just the point
            pointsData.AddPoint(hit.point); // ASSUMPTION: You have pointsData.AddPoint(Vector3)
        }
    }

    List<Vector2> GenerateViewSpaceCoords(int xSize, int zSize)
    { /* ... (same) ... */
        List<Vector2> coords = new List<Vector2>(); if (mainCamera == null) return coords;
        float fovY = mainCamera.fieldOfView * fovMargin; float fovX = Camera.VerticalToHorizontalFieldOfView(fovY, mainCamera.aspect) * fovMargin;
        float hFovYRad = fovY * 0.5f * Mathf.Deg2Rad; float hFovXRad = fovX * 0.5f * Mathf.Deg2Rad;
        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                float nX = (xSize > 1) ? (float)x / (xSize - 1) * 2f - 1f : 0f; float nY = (zSize > 1) ? (float)z / (zSize - 1) * 2f - 1f : 0f;
                float vpX = (nX + 1f) * 0.5f; float vpY = (nY + 1f) * 0.5f; coords.Add(new Vector2(vpX, vpY));
            }
        }
        return coords;
    }

    void CreateOrResetSelectionSphere()
    {
        if (selectionBox != null) Destroy(selectionBox);
        selectionBox = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionBox.transform.localScale = boxSize;
        selectionBox.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 0.5f;
        selectionBox.transform.rotation = Quaternion.identity;
        Collider col = selectionBox.GetComponent<Collider>(); if (col != null) col.enabled = false;
        Renderer renderer = selectionBox.GetComponent<MeshRenderer>();
        Material mat = boxMaterial != null ? new Material(boxMaterial) : new Material(Shader.Find("Standard"));
        if (boxMaterial == null) { mat.SetFloat("_Mode", 3); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0); mat.DisableKeyword("_ALPHATEST_ON"); mat.EnableKeyword("_ALPHABLEND_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); mat.renderQueue = 3000; }
        mat.color = new Color(0, 1, 1, 0.3f); renderer.material = mat;
        Debug.Log("Selection Sphere created/reset.");
    }
}

// --- REMOVED ALL PLACEHOLDER IMPLEMENTATIONS ---
// You MUST have your own implementations for:
// - Appletea.Dev.PointCloud.ChunkManager (with AddPoint, GetAllPoints, GetApproximateBounds, Count, Clear, GetPointsInRadius)
// - Appletea.Dev.PointCloud.PointCloudRenderer
// - Meta.XR.EnvironmentRaycastManager (Raycast returning hit with point and normal)
// - Meta.XR.EnvironmentRaycastHit
// - ListExtensions (Shuffle)
// Or adjust the script to use your actual class/method names.
