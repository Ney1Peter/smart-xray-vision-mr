// LocalGSAlignment_RBF_Normal.cs (Uses RoomMesh Target, Internal Normals in DLL)
using UnityEngine;
using Unity.Mathematics;
using System.Collections; // Keep for IEnumerator if needed elsewhere, though ScanRoutine removed
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime; // Assuming this namespace is correct
using Meta.XR.BuildingBlocks; // Assuming this namespace is correct for RoomMeshEvent
using System; // For Array.Copy, Math
using System.Linq; // For potential LINQ usage if needed later

// Ensure you have necessary assemblies referenced

// *** RENAMED CLASS ***
public class LocalGSAlignment_RBF_Normal : MonoBehaviour
{
    // --- Serialized Fields ---
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent; // Used to get Room Mesh (Target Points)
    public Material boxMaterial; // Material for the selection sphere

    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f); // Initial size of the selection sphere
    public float moveSpeed = 0.5f; // Speed to move the sphere
    [SerializeField] private Camera mainCamera;
    [Header("RBF Alignment Parameters")]
    public bool accumulateDeformation = true; // Apply deformation cumulatively?
    [Range(10, 500)] public int numControlPointsN = 50; // N: Number of RBF control points
    public int rbfKernelType = 0; // 0: Gaussian, 1: TPS(r), 2: Multiquadric, 3: InvMultiquadric
    public float rbfSigma = 0.1f; // Sigma for Gaussian/MQ/IMQ kernels
    public bool includeAffineTerm = true; // Include affine term in RBF
    public float correspondenceMaxDistance = 0.2f; // Max distance for valid correspondences
    public float regularizationLambda = 1e-6f; // Regularization for RBF solver
    [Space(5)]
    [Header("Correspondence Filtering (Executed in DLL)")]
    [Tooltip("Enable Reciprocal Nearest Neighbor check in DLL")]
    public bool useReciprocalCheck = true; // ADDED: Control RNN Check
    [Tooltip("Enable Normal Vector consistency check in DLL (DLL calculates normals)")]
    public bool useNormalCheck = true; // ADDED: Control Normal Check
    [Range(-1.0f, 1.0f)]
    [Tooltip("Cosine of max angle difference for normal check (e.g., 0.866=30deg, 0.707=45deg)")]
    public float normalConsistencyThreshold = 0.866f; // ADDED: Control Threshold

    // --- Private Variables ---
    private GameObject selectionBox; // The sphere used for selection
    private List<Vector3> roomMeshPointsAll = new List<Vector3>(); // All points from RoomMesh (Target, World Space)
    private bool roomMeshReady = false;
    private bool gaussianDataReady = false;
    private bool alignerInitialized = false; // Flag for RBF DLL initialization

    // Gaussian Splatting Data
    private int splatCount;
    private GraphicsBuffer posBuffer; // GPU buffer for positions
    private GraphicsBuffer originalPosBuffer; // Stores initial state or last deformed state if accumulating
    private Vector3[] positionsCPU; // CPU copy of current positions (Local Space)
    private Vector3[] originalPositionsCPU; // CPU copy of resting/accumulated state (Local Space)

    // --- RBF DLL Import ---
    private const string DllName = "RbfAlignerDllNormal"; // Ensure this matches your DLL name

    // UPDATED InitializeAligner Signature (No normals pointer)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAligner(IntPtr targetReferenceCloudPtr, int numTargetPoints);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShutdownAligner();

    // UPDATED AlignClusterRBF Signature (No normals pointer)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int AlignClusterRBF(
        IntPtr clusterSourcePointsPPtr, // World Space
                                        // IntPtr clusterSourceNormalsPtr, // REMOVED
        int numClusterPointsK,
        int numControlPointsN,
        int rbfKernelType,
        float rbfSigma,
        bool includeAffineTerm,
        float correspondenceMaxDistance,
        float regularizationLambda,
        bool use_reciprocal_check,          // ADDED
        bool use_normal_check,              // ADDED
        float normal_consistency_threshold, // ADDED
        IntPtr outDeformedPointsXPrimePtr    // World Space
    );

    // --- Unity Methods ---

    void Start()
    {
        if (GaussianRendererObject == null) GaussianRendererObject = GetComponent<GaussianSplatRenderer>();
        if (GaussianRendererObject == null) { Debug.LogError("GS component not found!"); enabled = false; return; }
        if (TryInitializeGaussianData()) { gaussianDataReady = true; } else { enabled = false; return; }

        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady); Debug.Log("Waiting for RoomMesh...");
        }
        else { Debug.LogWarning("RoomMeshEvent not assigned. Reference point cloud (for RBF target) will not be loaded automatically."); }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) { Debug.LogError("Main Camera not found!"); enabled = false; return; }

    }

    void Update()
    {
        // --- Selection Sphere Creation (X Button / Button.Three) ---
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            CreateOrResetSelectionSphere();
        }

        // --- Selection Sphere Movement (Thumbstick Y) ---
        if (selectionBox != null)
        {
            float move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y * moveSpeed * Time.deltaTime;
            selectionBox.transform.position += Camera.main.transform.forward * move;
        }

        // --- Trigger Alignment (Y Button / Button.Four) ---
        if (selectionBox != null && OVRInput.GetDown(OVRInput.Button.Four))
        {
            Debug.Log("Align button pressed. Starting RBF alignment process...");
            PerformLocalRegistrationRBF(selectionBox); // Call RBF alignment
        }

        // --- Re-initialize Aligner (Keyboard R for testing) ---
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("Re-initializing Aligner with RoomMesh data...");
            CleanupAligner();
            InitializeDLLWithRoomMesh(); // Re-initialize with RoomMesh
        }
    }

    void OnDestroy()
    {
        CleanupAligner();
        originalPosBuffer?.Release(); originalPosBuffer = null;
        if (roomMeshEvent != null) roomMeshEvent.OnRoomMeshLoadCompleted.RemoveListener(OnRoomMeshReady);
        Debug.Log("Cleaned up resources.");
    }
    void OnApplicationQuit()
    {
        CleanupAligner();
        originalPosBuffer?.Release(); originalPosBuffer = null;
    }


    // --- Initialization and Data Handling ---

    bool TryInitializeGaussianData()
    {
        // This function remains the same as your previous version
        try
        {
            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);
            if (posBuffer == null || !posBuffer.IsValid() || posBuffer.count <= 0) { Debug.LogError("Failed posBuffer."); return false; }
            splatCount = GaussianRendererObject.splatCount; if (splatCount <= 0) { Debug.LogError("Zero splats."); return false; }
            //if (posBuffer.count != splatCount) { Debug.LogWarning($"Buffer/Splat count mismatch: {posBuffer.count} vs {splatCount}. Using buffer count."); splatCount = posBuffer.count; }
            positionsCPU = new Vector3[splatCount]; originalPositionsCPU = new Vector3[splatCount];
            posBuffer.GetData(positionsCPU); Array.Copy(positionsCPU, originalPositionsCPU, splatCount);
            int stride = Marshal.SizeOf(typeof(Vector3));
            if (posBuffer.stride != stride) { Debug.LogWarning($"posBuffer stride ({posBuffer.stride}) != Vector3 stride ({stride})."); }
            originalPosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, stride); originalPosBuffer.SetData(originalPositionsCPU);
            // Removed sourceNormalsCPU initialization
            Debug.Log($"Initialized GS data: {splatCount} points."); gaussianDataReady = true; return true;
        }
        catch (Exception e) { Debug.LogError($"Exception during GS data init: {e.Message}"); return false; }
    }


    private void OnRoomMeshReady(MeshFilter mf)
    {
        // This function remains the same
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("RoomMesh loaded but MeshFilter/sharedMesh is null."); roomMeshReady = false; return; }
        Mesh mesh = mf.sharedMesh; Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0) { Debug.LogError("RoomMesh has no vertices."); roomMeshReady = false; return; }
        Transform t = mf.transform; Matrix4x4 localToWorld = t.localToWorldMatrix;
        roomMeshPointsAll.Clear(); roomMeshPointsAll.Capacity = vertices.Length;
        foreach (var v in vertices) roomMeshPointsAll.Add(localToWorld.MultiplyPoint3x4(v));
        roomMeshReady = true;
        Debug.Log($"RoomMesh loaded. Extracted {roomMeshPointsAll.Count} points (World Space).");
        InitializeDLLWithRoomMesh(); // Initialize DLL now
    }

    // UPDATED Initialize function (No normals pointer)
    void InitializeDLLWithRoomMesh()
    {
        if (!roomMeshReady || roomMeshPointsAll.Count == 0) { Debug.LogError("Cannot init aligner: Room Mesh not ready/empty."); return; }
        if (alignerInitialized) { Debug.LogWarning("Aligner already initialized."); return; }
        Debug.Log($"Initializing RBF Aligner DLL with {roomMeshPointsAll.Count} RoomMesh reference points...");
        float[] targetPointsFlat = VectorsToFloatArray(roomMeshPointsAll);
        // float[] targetNormalsFlat = null; // REMOVED

        GCHandle pointsHandle = default; int result = -1;
        try
        {
            pointsHandle = GCHandle.Alloc(targetPointsFlat, GCHandleType.Pinned);
            IntPtr pointsPtr = pointsHandle.AddrOfPinnedObject();
            // IntPtr normalsPtr = IntPtr.Zero; // REMOVED

            result = InitializeAligner(pointsPtr, /*normalsPtr,*/ roomMeshPointsAll.Count); // Call UPDATED C++ Init

            if (result == 0) { alignerInitialized = true; Debug.Log("RBF Aligner Initialized with RoomMesh data."); }
            else { Debug.LogError($"Failed to initialize RBF DLL. Error: {result}"); alignerInitialized = false; }
        }
        catch (DllNotFoundException) { Debug.LogError($"DLL Not Found: {DllName}.dll"); alignerInitialized = false; }
        catch (Exception e) { Debug.LogError($"Exception during InitializeAligner: {e.Message}"); alignerInitialized = false; }
        finally
        {
            if (pointsHandle.IsAllocated) pointsHandle.Free();
            // REMOVED normal handle free
        }
    }

    void CleanupAligner()
    {
        if (alignerInitialized) { Debug.Log("Shutting down RBF Aligner DLL..."); try { ShutdownAligner(); } catch (Exception e) { Debug.LogError($"Exception during ShutdownAligner: {e.Message}"); } finally { alignerInitialized = false; } }
    }


    // --- RBF Alignment Logic ---

    /// <summary>
    /// Performs RBF alignment for points within the selection sphere.
    /// </summary>
    void PerformLocalRegistrationRBF(GameObject box)
    {
        // --- Pre-checks ---
        if (!alignerInitialized) { Debug.LogError("RBF Aligner not initialized. Press R after RoomMesh loads."); return; }
        if (!gaussianDataReady) { Debug.LogError("Gaussian Splatting data not ready."); return; }
        if (!roomMeshReady || roomMeshPointsAll.Count == 0) { Debug.LogError("RoomMesh points (target) are missing or not ready."); return; }
        if (box == null) { Debug.LogError("Selection box is null."); return; }

        Transform tf = GaussianRendererObject.transform;
        Bounds boxBounds = new Bounds(box.transform.position, box.transform.localScale);

        // --- 1. Select Source Points (Cluster) in World Space ---
        List<Vector3> clusterSourcePoints_World = new List<Vector3>();
        // List<Vector3> clusterSourceNormals_World = new List<Vector3>(); // REMOVED
        List<int> clusterIndices = new List<int>();
        Vector3[] sourceStateCPU = accumulateDeformation ? originalPositionsCPU : positionsCPU;
        if (sourceStateCPU == null) { Debug.LogError("Source state CPU array null!"); return; }
        // Vector3[] sourceNormalsStateCPU = sourceNormalsCPU; // REMOVED

        for (int i = 0; i < splatCount; i++)
        {
            Vector3 worldPos = tf.TransformPoint(sourceStateCPU[i]);
            if (boxBounds.Contains(worldPos))
            {
                clusterSourcePoints_World.Add(worldPos);
                clusterIndices.Add(i);
                // No need to prepare source normals here
            }
        }

        int K = clusterSourcePoints_World.Count;
        Debug.Log($"Extracted {K} GS points inside selection sphere for RBF.");
        if (K == 0) { Debug.LogWarning("No Gaussian points found inside the selection sphere."); return; }
        // Removed normal count check

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
                useReciprocalCheck, useNormalCheck, normalConsistencyThreshold, // Pass flags/threshold
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

    // Helper to create the selection sphere
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
