// RBFNonRigidAlignment.cs (Using Scan Data as Target)
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime; // Assuming this namespace is correct
// using Meta.XR.BuildingBlocks; // Removed RoomMesh dependency
using System;
using System.Linq;
using Appletea.Dev.PointCloud; // Assuming this namespace is correct for ChunkManager
using Meta.XR; // Assuming this namespace is correct for EnvironmentRaycastManager

public class RBFNonRigidAlignment : MonoBehaviour
{
    // --- 枚举定义 ---
    public enum Density : int { low = 32, medium = 64, high = 128, vHigh = 256, ultra = 512 }

    // --- 序列化字段 ---
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    // public RoomMeshEvent roomMeshEvent; // Removed RoomMesh dependency
    [Tooltip("是否累积变形效果")]
    public bool accumulateDeformation = true;
    [SerializeField] private EnvironmentRaycastManager depthManager; // Kept for scanning
    [SerializeField] private PointCloudRenderer pointCloudRenderer; // Kept for visualizing scanned points

    [Space(10)]
    [Header("RBF Alignment Parameters")]
    [Range(1, 500)] public int numControlPointsN = 50;
    public int rbfKernelType = 0; // 0: Gaussian, 1: TPS(r), 2: Multiquadric, 3: InvMultiquadric
    public float rbfSigma = 0.1f;
    public bool includeAffineTerm = true;
    public float correspondenceMaxDistance = 0.2f;
    public float regularizationLambda = 1e-6f;

    [Space(10)]
    [Header("Chunk Settings")]
    [SerializeField] private int chunkSize = 1;
    [SerializeField] private int maxPointsPerChunk = 256;
    [SerializeField] private int initialPoolSize = 1000;

    [Space(10)]
    [Header("Camera Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField][Tooltip("Percentage of the field of view")] private float fovMargin = 0.9f;

    [Space(10)]
    [Header("Scan Settings")]
    [SerializeField] private float scanInterval = 1.0f;
    [SerializeField] private Density density = Density.medium;
    [SerializeField][Tooltip("The limit is about 5m")] private float maxScanDistance = 5;

    [Space(10)]
    [Header("Rendering Settings")]
    [SerializeField] private GameObject pointPrefab;
    [SerializeField] private float renderingRadius = 10.0f;
    [SerializeField] int maxChunkCount = 15;

    // --- 私有和内部变量 ---
    private ChunkManager pointsData; // Manages scanned points
    private Coroutine scanCoroutine = null;
    // private List<Vector3> roomMeshPointsAll = new List<Vector3>(); // Removed RoomMesh dependency
    // private bool roomMeshReady = false; // Removed RoomMesh dependency
    private bool gaussianDataReady = false;
    private bool alignerInitialized = false; // Flag for RBF DLL initialization using scan data

    // Gaussian Splatting Data
    private int splatCount = 0;
    private GraphicsBuffer posBuffer;
    private GraphicsBuffer originalPosBuffer;
    private Vector3[] positionsCPU;
    private Vector3[] originalPositionsCPU;

    private List<Vector3> currentScanData = new List<Vector3>(); // Holds latest scanned points

    // --- RBF DLL 导入 ---
    private const string DllName = "RbfAlignerDll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAligner(IntPtr targetReferenceCloudPtr, int numTargetPoints);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShutdownAligner();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int AlignClusterRBF(
        IntPtr clusterSourcePointsPPtr, int numClusterPointsK, int numControlPointsN, int rbfKernelType,
        float rbfSigma, bool includeAffineTerm, float correspondenceMaxDistance, float regularizationLambda,
        IntPtr outDeformedPointsXPrimePtr
    );

    // --- Start() 函数 ---
    void Start()
    {
        if (GaussianRendererObject == null) GaussianRendererObject = GetComponent<GaussianSplatRenderer>();
        if (GaussianRendererObject == null) { Debug.LogError("GaussianSplatRenderer component not found!"); enabled = false; return; }

        if (TryInitializeGaussianData()) { gaussianDataReady = true; }
        else { enabled = false; return; }

        pointsData = new ChunkManager(chunkSize, maxPointsPerChunk);
        if (pointCloudRenderer != null) pointCloudRenderer.Initialize(pointPrefab, initialPoolSize);
        else Debug.LogWarning("PointCloudRenderer not assigned!");

        // Removed RoomMesh subscription
        // if (roomMeshEvent != null) { roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady); ... }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) { Debug.LogError("Main Camera not found!"); enabled = false; return; }

        Debug.Log("RBFNonRigidAlignment Started. Press B to scan, A to align based on scan bounds.");
    }

    // --- Update() 函数 ---
    void Update()
    {
        // Start/Stop Scanning (B Button)
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            if (scanCoroutine == null) { Debug.Log("Scan Started (B pressed)"); scanCoroutine = StartCoroutine(ScanRoutine()); }
        }
        else if (OVRInput.GetUp(OVRInput.RawButton.B))
        {
            if (scanCoroutine != null) { Debug.Log("Scan Stopped (B released)"); StopCoroutine(scanCoroutine); scanCoroutine = null; }
        }

        // Trigger Alignment (A Button)
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            Debug.Log("Alignment Triggered (A pressed). Starting RBF alignment using current scan data...");
            PerformRBFAlignment(); // Call alignment using scan data
        }

        // Clear Scanned Data (X Button)
        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            Debug.Log("Clearing scanned point data (X pressed)...");
            if (scanCoroutine != null) { StopCoroutine(scanCoroutine); scanCoroutine = null; }
            if (pointsData != null) { pointsData.Clear(); currentScanData.Clear(); }
            if (pointCloudRenderer != null) { pointCloudRenderer.UpdatePointCloud(new List<Vector3>()); }
            // Also shutdown the aligner as its reference data is now gone
            CleanupAligner();
            Debug.Log("Scanned data cleared and Aligner shut down.");
        }

        // Re-initialize Aligner (Keyboard R for testing)
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("Attempting to re-initialize RBF Aligner with current scan data...");
            CleanupAligner(); // Ensure previous instance is cleaned up
            InitializeDLLWithScanData(); // Try to initialize with whatever scan data exists
        }
    }

    // --- OnDestroy / OnApplicationQuit ---
    void OnDestroy()
    {
        CleanupAligner();
        originalPosBuffer?.Release(); originalPosBuffer = null;
        Debug.Log("RBFNonRigidAlignment OnDestroy: Cleaned up resources.");
        // Removed RoomMesh unsubscription
    }
    void OnApplicationQuit()
    {
        CleanupAligner();
        originalPosBuffer?.Release(); originalPosBuffer = null;
    }

    // --- Initialization and Data Handling ---

    bool TryInitializeGaussianData()
    {
        // This function remains largely the same, initializing GS buffers and CPU copies
        try
        {
            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            if (posBuffer == null || !posBuffer.IsValid() || posBuffer.count <= 0) { Debug.LogError("Failed posBuffer."); return false; }
            splatCount = GaussianRendererObject.splatCount;
            if (splatCount <= 0) { Debug.LogError("Zero splats."); return false; }
            //if (posBuffer.count != splatCount) { Debug.LogWarning($"Buffer/Splat count mismatch: {posBuffer.count} vs {splatCount}. Using buffer count."); splatCount = posBuffer.count; }

            positionsCPU = new Vector3[splatCount];
            originalPositionsCPU = new Vector3[splatCount];
            posBuffer.GetData(positionsCPU);
            Array.Copy(positionsCPU, originalPositionsCPU, splatCount);

            int stride = Marshal.SizeOf(typeof(Vector3));
            if (posBuffer.stride != stride) { Debug.LogWarning($"posBuffer stride ({posBuffer.stride}) != Vector3 stride ({stride})."); }
            originalPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, stride);
            originalPosBuffer.SetData(originalPositionsCPU);

            Debug.Log($"Initialized Gaussian Splatting data: {splatCount} points.");
            return true;
        }
        catch (Exception e) { Debug.LogError($"Exception during Gaussian data init: {e.Message}"); return false; }
    }

    // Removed OnRoomMeshReady function

    /// <summary>
    /// Initializes the C++ DLL using the currently stored scan data.
    /// </summary>
    bool InitializeDLLWithScanData()
    {
        // Use currentScanData which should be updated by ScanRoutine
        if (currentScanData == null || currentScanData.Count == 0)
        {
            Debug.LogError("Cannot initialize aligner: No scan data available in currentScanData.");
            return false;
        }
        if (alignerInitialized)
        {
            Debug.LogWarning("Aligner already initialized. Call CleanupAligner first if re-initialization is needed.");
            return true; // Already initialized, consider it a success for this attempt
        }

        Debug.Log($"Initializing RBF Aligner DLL with {currentScanData.Count} scanned reference points...");
        float[] targetPointsFlat = VectorsToFloatArray(currentScanData); // Use scan data
        GCHandle handle = default; int result = -1;
        try
        {
            handle = GCHandle.Alloc(targetPointsFlat, GCHandleType.Pinned);
            IntPtr ptr = handle.AddrOfPinnedObject();
            result = InitializeAligner(ptr, currentScanData.Count); // Call C++ Init
            if (result == 0)
            {
                alignerInitialized = true; Debug.Log("RBF Aligner Initialized with scan data.");
            }
            else
            {
                Debug.LogError($"Failed to initialize RBF DLL with scan data. Error: {result}"); alignerInitialized = false;
            }
        }
        catch (DllNotFoundException) { Debug.LogError($"DLL Not Found: {DllName}.dll"); alignerInitialized = false; }
        catch (Exception e) { Debug.LogError($"Exception during InitializeAligner: {e.Message}"); alignerInitialized = false; }
        finally { if (handle.IsAllocated) handle.Free(); }
        return alignerInitialized;
    }

    void CleanupAligner()
    {
        if (alignerInitialized)
        {
            Debug.Log("Shutting down RBF Aligner DLL...");
            try { ShutdownAligner(); }
            catch (Exception e) { Debug.LogError($"Exception during ShutdownAligner: {e.Message}"); }
            finally { alignerInitialized = false; } // Always mark as not initialized after attempt
        }
    }

    // --- RBF Alignment Logic ---

    void PerformRBFAlignment()
    {
        // --- Pre-checks ---
        // 1. Check if aligner needs initialization
        if (!alignerInitialized)
        {
            Debug.Log("Aligner not initialized. Attempting initialization with current scan data...");
            if (!InitializeDLLWithScanData())
            { // Try to initialize
                Debug.LogError("Failed to initialize aligner with scan data. Cannot perform alignment.");
                return;
            }
            // If initialization succeeded, alignerInitialized is now true
        }

        // 2. Check other prerequisites
        if (!gaussianDataReady) { Debug.LogError("Gaussian Splatting data not ready."); return; }
        // Check if scan data used for init is still relevant (optional - depends on workflow)
        if (pointsData == null) { Debug.LogWarning("No scanned points available to define alignment region."); return; }

        Transform tf = GaussianRendererObject.transform;
        Bounds scanBounds = pointsData.GetPreciseBounds(); // Selection based on scan bounds

        // --- 1. Select Source Points (Cluster) in World Space ---
        List<Vector3> clusterSourcePoints_World = new List<Vector3>();
        List<int> clusterIndices = new List<int>();
        Vector3[] sourceStateCPU = accumulateDeformation ? originalPositionsCPU : positionsCPU;
        if (sourceStateCPU == null) { Debug.LogError("Source state CPU array is null!"); return; }

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 worldPos = tf.TransformPoint(sourceStateCPU[i]);
            if (scanBounds.Contains(worldPos))
            {
                clusterSourcePoints_World.Add(worldPos);
                clusterIndices.Add(i);
            }
        }

        int K = clusterSourcePoints_World.Count;
        Debug.Log($"Extracted {K} GS points within scan bounds for RBF.");
        if (K == 0) { Debug.LogWarning("No Gaussian points found within the scan bounds."); return; }

        // --- 2. Prepare Data for DLL ---
        float[] sourceFloatArray = VectorsToFloatArray(clusterSourcePoints_World);
        float[] deformedFloatArray = new float[K * 3];

        // --- 3. Pin Memory & Call DLL ---
        GCHandle sourceHandle = default; GCHandle deformedHandle = default;
        int result = -1;
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            sourceHandle = GCHandle.Alloc(sourceFloatArray, GCHandleType.Pinned);
            deformedHandle = GCHandle.Alloc(deformedFloatArray, GCHandleType.Pinned);
            IntPtr sourcePtr = sourceHandle.AddrOfPinnedObject();
            IntPtr deformedPtr = deformedHandle.AddrOfPinnedObject();

            Debug.Log($"Calling AlignClusterRBF: K={K}, N={numControlPointsN}, Kernel={rbfKernelType}, Sigma={rbfSigma}, Affine={includeAffineTerm}, MaxDist={correspondenceMaxDistance}, Lambda={regularizationLambda}");

            // DLL uses the target data it was initialized with (now the scan data)
            result = AlignClusterRBF(
                sourcePtr, K, numControlPointsN, rbfKernelType, rbfSigma,
                includeAffineTerm, correspondenceMaxDistance, regularizationLambda, deformedPtr
            );
        }
        catch (Exception e) { Debug.LogError($"Exception during AlignClusterRBF call: {e.Message}\n{e.StackTrace}"); result = -99; }
        finally
        {
            stopwatch.Stop();
            if (sourceHandle.IsAllocated) sourceHandle.Free();
            if (deformedHandle.IsAllocated) deformedHandle.Free();
        }
        Debug.Log($"AlignClusterRBF DLL call took: {stopwatch.ElapsedMilliseconds} ms");

        // --- 5. Process Results ---
        if (result == 0)
        {
            Debug.Log("RBF Alignment successful (DLL returned 0).");
            List<Vector3> deformedPoints_World = FloatArrayToVectors(deformedFloatArray, K);

            if (deformedPoints_World != null)
            {
                // --- 6. Update CPU Position Arrays ---
                int updatedCount = 0;
                bool needsGpuOriginalUpdate = false;
                for (int i = 0; i < K; i++)
                {
                    int originalIndex = clusterIndices[i];
                    if (originalIndex >= 0 && originalIndex < splatCount)
                    {
                        Vector3 newWorldPos = deformedPoints_World[i];
                        Vector3 newLocalPos = tf.InverseTransformPoint(newWorldPos);

                        positionsCPU[originalIndex] = newLocalPos; // Update current CPU state

                        if (accumulateDeformation)
                        {
                            originalPositionsCPU[originalIndex] = newLocalPos; // Update resting CPU state
                            needsGpuOriginalUpdate = true;
                        }
                        updatedCount++;
                    }
                    else { Debug.LogWarning($"Invalid original index {originalIndex}."); }
                }

                // --- 7. Upload Updated Data to GPU ---
                if (updatedCount > 0)
                {
                    if (posBuffer != null && posBuffer.IsValid())
                    {
                        posBuffer.SetData(positionsCPU); // Upload updated current positions
                        Debug.Log($"Updated posBuffer on GPU for {updatedCount} points via RBF.");

                        if (accumulateDeformation && needsGpuOriginalUpdate)
                        {
                            if (originalPosBuffer != null && originalPosBuffer.IsValid())
                            {
                                originalPosBuffer.SetData(originalPositionsCPU); // Upload updated resting state
                                Debug.Log("Updated originalPosBuffer on GPU.");
                            }
                            else { Debug.LogError("originalPosBuffer invalid, cannot update accumulated state on GPU."); }
                        }
                    }
                    else { Debug.LogError("Position buffer invalid."); gaussianDataReady = false; }
                }
                else { Debug.LogWarning("RBF Alignment successful, but no points updated."); }
            }
            else { Debug.LogError("Failed to convert deformed points float array back."); }
        }
        else { Debug.LogError($"RBF Alignment failed in C++ DLL. Error code: {result}"); }
    }


    // --- Helper Functions --- (Keep VectorsToFloatArray, FloatArrayToVectors, ComputeCentroid)
    private float[] VectorsToFloatArray(List<Vector3> vectors)
    {
        if (vectors == null || vectors.Count == 0) return new float[0];
        float[] floatArray = new float[vectors.Count * 3];
        for (int i = 0; i < vectors.Count; i++) { floatArray[i * 3 + 0] = vectors[i].x; floatArray[i * 3 + 1] = vectors[i].y; floatArray[i * 3 + 2] = vectors[i].z; }
        return floatArray;
    }
    private List<Vector3> FloatArrayToVectors(float[] floatArray, int pointCount)
    {
        if (floatArray == null || floatArray.Length != pointCount * 3) { Debug.LogError($"Invalid float array size ({floatArray?.Length}) for {pointCount} Vector3 conversion."); return null; }
        List<Vector3> vectors = new List<Vector3>(pointCount);
        for (int i = 0; i < pointCount; i++) { vectors.Add(new Vector3(floatArray[i * 3 + 0], floatArray[i * 3 + 1], floatArray[i * 3 + 2])); }
        return vectors;
    }
    Vector3 ComputeCentroid(List<Vector3> points)
    {
        if (points == null || points.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero; foreach (Vector3 p in points) sum += p; return sum / points.Count;
    }


    // --- Scanning Logic (Keep as is) ---
    IEnumerator ScanRoutine()
    {
        while (true)
        {
            ScanAndStorePointCloud(((int)density), pointsData);
            if (pointCloudRenderer != null)
            {
                // Update visualization with points in radius
                List<Vector3> pointsToRender = pointsData.GetPointsInRadius(mainCamera.transform.position, renderingRadius, maxChunkCount);
                pointCloudRenderer.UpdatePointCloud(pointsToRender);
            }
            // Update currentScanData for potential use in alignment init
            currentScanData = pointsData.GetAllPoints();
            yield return new WaitForSeconds(scanInterval);
        }
    }

    void ScanAndStorePointCloud(int sampleSize, ChunkManager pointsData)
    {
        if (depthManager == null) { Debug.LogWarning("Depth Manager not set, cannot scan."); return; }
        List<Vector2> viewSpaceCoords = GenerateViewSpaceCoords(sampleSize, sampleSize);
        List<Ray> rays = new List<Ray>();
        foreach (Vector2 i in viewSpaceCoords) rays.Add(mainCamera.ViewportPointToRay(new Vector3(i.x, i.y, 0)));
        List<EnvironmentRaycastHit> results = new List<EnvironmentRaycastHit>();
        foreach (Ray ray in rays)
        {
            EnvironmentRaycastHit result;
            if (depthManager.Raycast(ray, out result, maxScanDistance))
            {
                if (Vector3.Distance(result.point, mainCamera.transform.position) < maxScanDistance)
                    results.Add(result);
            }
        }
        if (typeof(ListExtensions).GetMethod("Shuffle") != null) { ListExtensions.Shuffle(results); }
        else { Debug.LogWarning("ListExtensions.Shuffle not found."); }
        foreach (var result in results) pointsData.AddPoint(result.point);
        // Update currentScanData immediately after adding points
        // currentScanData = pointsData.GetAllPoints(); // Moved to end of ScanRoutine loop
    }

    List<Vector2> GenerateViewSpaceCoords(int xSize, int zSize)
    {
        List<Vector2> coords = new List<Vector2>();
        if (mainCamera == null) return coords;
        float fovY = mainCamera.fieldOfView * fovMargin; float fovX = Camera.VerticalToHorizontalFieldOfView(fovY, mainCamera.aspect) * fovMargin;
        float halfFovYRad = fovY * 0.5f * Mathf.Deg2Rad; float halfFovXRad = fovX * 0.5f * Mathf.Deg2Rad;
        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                float normX = (xSize > 1) ? (float)x / (xSize - 1) * 2.0f - 1.0f : 0.0f;
                float normY = (zSize > 1) ? (float)z / (zSize - 1) * 2.0f - 1.0f : 0.0f;
                float vpX = (normX + 1.0f) * 0.5f; float vpY = (normY + 1.0f) * 0.5f;
                coords.Add(new Vector2(vpX, vpY));
            }
        }
        return coords;
    }


}

// Assuming ListExtensions.Shuffle exists elsewhere or implement it if needed:
public static class ListExtensions
{
    private static System.Random rng = new System.Random();
    public static void Shuffle<T>(this IList<T> list)
    {
        int n = list.Count;
        while (n > 1) { n--; int k = rng.Next(n + 1); T value = list[k]; list[k] = list[n]; list[n] = value; }
    }
}
