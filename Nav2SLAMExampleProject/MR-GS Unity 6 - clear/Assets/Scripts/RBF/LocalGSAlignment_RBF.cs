// LocalGSAlignment_RBF.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GaussianSplatting.Runtime; // Assuming this namespace is correct
using Meta.XR.BuildingBlocks; // Assuming this namespace is correct for RoomMeshEvent
using System; // For Array.Copy
using System.Linq; // For potential LINQ usage if needed later

// Ensure you have GaussianSplatting.Runtime assembly referenced
// Ensure you have Meta.XR.BuildingBlocks assembly referenced (or equivalent for RoomMeshEvent)

public class LocalGSAlignment_RBF : MonoBehaviour
{
    [Header("References")]
    public GaussianSplatRenderer GaussianRendererObject;
    public RoomMeshEvent roomMeshEvent; // Used to get Room Mesh (Target Points)
    public Material boxMaterial; // Material for the selection sphere

    [Header("Selection & Interaction")]
    public Vector3 boxSize = new Vector3(0.5f, 0.5f, 0.5f); // Initial size of the selection sphere
    public float moveSpeed = 0.5f; // Speed to move the sphere

    [Header("RBF Alignment Parameters")]
    public bool accumulateDeformation = true; // Apply deformation cumulatively?
    [Range(10, 500)] public int numControlPointsN = 50; // N: Number of RBF control points
    public int rbfKernelType = 0; // 0: Gaussian, 1: TPS(r), 2: Multiquadric, 3: InvMultiquadric
    public float rbfSigma = 0.1f; // Sigma for Gaussian/MQ/IMQ kernels
    public bool includeAffineTerm = true; // Include affine term in RBF
    public float correspondenceMaxDistance = 0.2f; // Max distance for valid correspondences
    public float regularizationLambda = 1e-6f; // Regularization for RBF solver

    // --- Private Variables ---
    private GameObject selectionBox; // The sphere used for selection
    private List<Vector3> roomMeshPointsAll = new List<Vector3>(); // All points from RoomMesh (Target)
    private bool roomMeshReady = false;
    private bool gaussianDataReady = false;
    private bool alignerInitialized = false;

    // Gaussian Splatting Data
    private int splatCount;
    private GraphicsBuffer posBuffer; // GPU buffer for positions
    private float3[] positions; // Current positions (local space) read from/written to GPU
    private float3[] originalPositions; // Stores initial state or last deformed state if accumulating

    // --- DLL Import Definition ---
    private const string DllName = "RbfAlignerDll"; // MUST match your compiled DLL name

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAligner(IntPtr targetReferenceCloudPtr, int numTargetPoints);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ShutdownAligner();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int AlignClusterRBF(
        IntPtr clusterSourcePointsPPtr, // float* for source points (World Space)
        int numClusterPointsK,
        int numControlPointsN,
        int rbfKernelType,
        float rbfSigma,
        bool includeAffineTerm,
        float correspondenceMaxDistance,
        float regularizationLambda,
        IntPtr outDeformedPointsXPrimePtr // float* for output deformed points (World Space)
    );

    // --- Unity Methods ---

    void Start()
    {
        if (GaussianRendererObject == null)
        {
            Debug.LogError("GaussianSplatRenderer object not assigned.");
            enabled = false;
            return;
        }
        // Ensure we have the component instance
        GaussianRendererObject = GaussianRendererObject.GetComponent<GaussianSplatRenderer>();
        if (GaussianRendererObject == null)
        {
            Debug.LogError("GaussianSplatRenderer component not found on the assigned object.");
            enabled = false;
            return;
        }


        // Attempt to get Gaussian Splatting data
        if (TryInitializeGaussianData())
        {
            gaussianDataReady = true;
        }
        else
        {
            enabled = false; // Stop if GS data fails
            return;
        }

        // Subscribe to Room Mesh event
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.AddListener(OnRoomMeshReady);
            Debug.Log("Waiting for RoomMesh to load...");
        }
        else
        {
            Debug.LogWarning("RoomMeshEvent not assigned. Reference point cloud will not be loaded automatically.");
        }
    }

    void Update()
    {
        // --- Selection Sphere Creation (X Button / Button.Three) ---
        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            if (selectionBox != null) Destroy(selectionBox);

            selectionBox = GameObject.CreatePrimitive(PrimitiveType.Sphere); // Use sphere for better bounds check?
            selectionBox.transform.localScale = boxSize;
            // Position slightly in front of the camera
            selectionBox.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.5f;
            selectionBox.transform.rotation = Quaternion.identity;
            selectionBox.GetComponent<Collider>().enabled = false; // Disable collider

            // Apply transparent material
            Renderer renderer = selectionBox.GetComponent<MeshRenderer>();
            Material mat = boxMaterial != null ? new Material(boxMaterial) : new Material(Shader.Find("Standard")); // Use instance
            if (boxMaterial == null) // Basic fallback transparency
            {
                mat.SetFloat("_Mode", 3); // Set to Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            mat.color = new Color(0, 1, 1, 0.3f); // Cyan, semi-transparent
            renderer.material = mat;

            Debug.Log("Selection Sphere created. Use Thumbstick Y to move, Y Button to align.");
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
            PerformLocalRegistrationRBF(selectionBox);
        }

        // Example: Re-initialize if 'R' is pressed (useful if VST cloud changes)
        if (Input.GetKeyDown(KeyCode.R)) // Using Keyboard R for easy testing
        {
            Debug.Log("Re-initializing Aligner...");
            CleanupAligner(); // Shutdown first
            InitializeDLLWithRoomMesh(); // Re-initialize
        }
    }

    void OnDestroy()
    {
        CleanupAligner(); // Cleanup C++ resources
        // Unsubscribe if necessary (though object destruction usually handles this)
        if (roomMeshEvent != null)
        {
            roomMeshEvent.OnRoomMeshLoadCompleted.RemoveListener(OnRoomMeshReady);
        }
    }

    void OnApplicationQuit()
    {
        CleanupAligner(); // Cleanup C++ resources
    }


    // --- Initialization and Data Handling ---

    bool TryInitializeGaussianData()
    {
        try
        {
            // Using the Runtime Extensions helper if available
            posBuffer = GaussianSplatRendererExtensions.GetGpuPosData(GaussianRendererObject);

            if (posBuffer == null || posBuffer.count <= 0)
            {
                Debug.LogError("Failed to get valid GPU position buffer from GaussianSplatRenderer.");
                return false;
            }

            splatCount = GaussianRendererObject.splatCount;
            positions = new float3[splatCount];
            originalPositions = new float3[splatCount]; // Allocate buffer for original/accumulated positions

            // Read initial data from GPU
            posBuffer.GetData(positions);
            // Copy initial state to originalPositions
            Array.Copy(positions, originalPositions, splatCount);

            Debug.Log($"Initialized Gaussian Splatting data: {splatCount} points.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception during Gaussian data initialization: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }


    private void OnRoomMeshReady(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("RoomMesh loaded but MeshFilter or sharedMesh is null.");
            roomMeshReady = false;
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            Debug.LogError("RoomMesh has no vertices.");
            roomMeshReady = false;
            return;
        }

        Transform t = mf.transform; // Use the transform of the mesh filter provided
        Matrix4x4 localToWorld = t.localToWorldMatrix;

        roomMeshPointsAll.Clear();
        roomMeshPointsAll.Capacity = vertices.Length; // Optimize allocation
        foreach (var v in vertices)
        {
            roomMeshPointsAll.Add(localToWorld.MultiplyPoint3x4(v)); // Convert vertex to world space
        }

        roomMeshReady = true;
        Debug.Log($"RoomMesh loaded and processed. Extracted {roomMeshPointsAll.Count} points (World Space).");

        // --- Now that RoomMesh is ready, Initialize the DLL ---
        InitializeDLLWithRoomMesh();
    }

    /// <summary>
    /// Initializes the C++ DLL with the loaded Room Mesh points.
    /// </summary>
    void InitializeDLLWithRoomMesh()
    {
        if (!roomMeshReady || roomMeshPointsAll.Count == 0)
        {
            Debug.LogError("Cannot initialize aligner: Room Mesh not ready or empty.");
            return;
        }

        if (alignerInitialized)
        {
            Debug.LogWarning("Aligner already initialized. Skipping re-initialization.");
            // Consider calling Shutdown first if re-init is desired.
            return;
        }

        Debug.Log($"Initializing RBF Aligner DLL with {roomMeshPointsAll.Count} reference points...");

        float[] targetPointsFlat = VectorsToFloatArray(roomMeshPointsAll);
        GCHandle handle = GCHandle.Alloc(targetPointsFlat, GCHandleType.Pinned);
        int result = -1;

        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            result = InitializeAligner(ptr, roomMeshPointsAll.Count); // Call C++ Init

            if (result == 0)
            {
                alignerInitialized = true;
                Debug.Log("RBF Aligner Initialized Successfully.");
            }
            else
            {
                Debug.LogError($"Failed to initialize RBF aligner DLL. Error code: {result}");
                alignerInitialized = false;
            }
        }
        catch (DllNotFoundException)
        {
            Debug.LogError($"DLL Not Found: Make sure {DllName}.dll is in Assets/Plugins/x64/");
            alignerInitialized = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception during InitializeAligner: {e.Message}\n{e.StackTrace}");
            alignerInitialized = false;
            result = -99;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }


    /// <summary>
    /// Calls the C++ DLL to shutdown the aligner and release resources.
    /// </summary>
    void CleanupAligner()
    {
        if (alignerInitialized)
        {
            Debug.Log("Shutting down RBF Aligner DLL...");
            try
            {
                ShutdownAligner(); // Call C++ Shutdown
            }
            catch (Exception e) { Debug.LogError($"Exception during ShutdownAligner: {e.Message}"); }
            finally { alignerInitialized = false; } // Mark as not initialized even if shutdown fails
        }
    }


    // --- RBF Alignment Logic ---

    /// <summary>
    /// Performs RBF alignment for points within the selection box.
    /// </summary>
    void PerformLocalRegistrationRBF(GameObject box)
    {
        if (!alignerInitialized)
        {
            Debug.LogError("RBF Aligner not initialized. Cannot perform registration.");
            return;
        }
        if (!gaussianDataReady)
        {
            Debug.LogError("Gaussian Splatting data not ready.");
            return;
        }
        if (roomMeshPointsAll.Count == 0)
        {
            Debug.LogError("RoomMesh points (target) are missing.");
            return;
        }

        Transform tf = GaussianRendererObject.transform; // Transform for GS object
        Bounds boxBounds = new Bounds(box.transform.position, box.transform.localScale); // Use sphere bounds

        // --- 1. Select Source Points (Cluster) in World Space ---
        List<Vector3> clusterSourcePoints_World = new List<Vector3>();
        List<int> clusterIndices = new List<int>(); // Store original indices of selected points
        int K = 0; // Number of points in cluster

        // Decide which positions to use as the source for deformation calculation
        // - 'positions' = current potentially deformed state
        // - 'originalPositions' = state before last deformation (or initial state)
        // Using 'originalPositions' makes deformations relative to a more stable state if accumulating.
        // Using 'positions' makes deformations relative to the current visual state. Let's use original for potentially better stability.
        float3[] sourceStateToDeform = accumulateDeformation ? originalPositions : positions;

        for (int i = 0; i < splatCount; i++)
        {
            // Convert the chosen source state point to world space for selection check
            Vector3 worldPos = tf.TransformPoint(sourceStateToDeform[i]);
            if (boxBounds.Contains(worldPos)) // Check if point is inside the sphere
            {
                clusterSourcePoints_World.Add(worldPos); // Store world position for DLL
                clusterIndices.Add(i); // Store the index
                K++;
            }
        }
        Debug.Log($"Extracted {K} GS points inside selection sphere for RBF.");
        if (K == 0)
        {
            Debug.LogWarning("No Gaussian points found inside the selection sphere.");
            return;
        }

        // --- 2. Prepare Data for DLL ---
        float[] sourceFloatArray = VectorsToFloatArray(clusterSourcePoints_World);
        float[] deformedFloatArray = new float[K * 3]; // Allocate output buffer

        // --- 3. Pin Memory & Call DLL ---
        GCHandle sourceHandle = GCHandle.Alloc(sourceFloatArray, GCHandleType.Pinned);
        GCHandle deformedHandle = GCHandle.Alloc(deformedFloatArray, GCHandleType.Pinned);
        int result = -1;

        try
        {
            IntPtr sourcePtr = sourceHandle.AddrOfPinnedObject();
            IntPtr deformedPtr = deformedHandle.AddrOfPinnedObject();

            Debug.Log($"Calling AlignClusterRBF with K={K}, N={numControlPointsN}, Kernel={rbfKernelType}, Sigma={rbfSigma}, Affine={includeAffineTerm}, MaxDist={correspondenceMaxDistance}, Lambda={regularizationLambda}");

            // Call the core alignment function in the C++ DLL
            result = AlignClusterRBF(
                sourcePtr,
                K,                     // Pass K (number of points in the cluster)
                numControlPointsN,     // RBF Param N
                rbfKernelType,         // RBF Param Kernel
                rbfSigma,              // RBF Param Sigma
                includeAffineTerm,     // RBF Param Affine Flag
                correspondenceMaxDistance, // RBF Param Max Dist
                regularizationLambda,  // RBF Param Lambda
                deformedPtr            // Output buffer pointer
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception during AlignClusterRBF call: {e.Message}\n{e.StackTrace}");
            result = -99; // Indicate exception
        }
        finally
        {
            // --- 4. VERY IMPORTANT: Free GCHandles ---
            if (sourceHandle.IsAllocated) sourceHandle.Free();
            if (deformedHandle.IsAllocated) deformedHandle.Free();
        }

        // --- 5. Process Results ---
        if (result == 0)
        {
            Debug.Log("RBF Alignment successful (DLL returned 0).");
            // Convert deformed world points back to Vector3 list
            List<Vector3> deformedPoints_World = FloatArrayToVectors(deformedFloatArray, K);

            if (deformedPoints_World != null)
            {
                // --- 6. Update Main 'positions' Array ---
                // Apply the calculated deformation to the points
                int updatedCount = 0;
                for (int i = 0; i < K; i++)
                {
                    int originalIndex = clusterIndices[i]; // Get the index in the main splat array
                    if (originalIndex >= 0 && originalIndex < splatCount)
                    {
                        Vector3 newWorldPos = deformedPoints_World[i];
                        // Transform deformed world position back to the GS object's local space
                        float3 newLocalPos = tf.InverseTransformPoint(newWorldPos);

                        // Update the 'positions' array (which is linked to the GPU buffer)
                        positions[originalIndex] = newLocalPos;

                        // If accumulating, update the 'original' state as well
                        if (accumulateDeformation)
                        {
                            originalPositions[originalIndex] = newLocalPos;
                        }
                        updatedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Invalid original index {originalIndex} encountered during update.");
                    }
                }

                // --- 7. Upload Updated Data to GPU ---
                if (updatedCount > 0)
                {
                    // Check if posBuffer is still valid (it might become invalid if scene changes etc.)
                    if (posBuffer != null && posBuffer.IsValid())
                    {
                        posBuffer.SetData(positions);
                        Debug.Log($"Updated {updatedCount} points in GPU buffer via RBF.");
                    }
                    else
                    {
                        Debug.LogError("Position buffer became invalid. Cannot upload data.");
                        gaussianDataReady = false; // Mark data as not ready
                    }
                }
                else
                {
                    Debug.LogWarning("RBF Alignment successful, but no points were actually updated (indices might be wrong?).");
                }

            }
            else
            {
                Debug.LogError("Failed to convert deformed points float array back to Vector3 list.");
            }
        }
        else
        {
            Debug.LogError($"RBF Alignment failed in C++ DLL. Error code: {result}");
            // Maybe revert visual state or do nothing?
        }
    }


    // --- Helper Functions ---

    private float[] VectorsToFloatArray(List<Vector3> vectors)
    {
        if (vectors == null || vectors.Count == 0) return new float[0];
        float[] floatArray = new float[vectors.Count * 3];
        // Consider parallelizing if vectors.Count is huge, but likely fine sequentially
        for (int i = 0; i < vectors.Count; i++)
        {
            floatArray[i * 3 + 0] = vectors[i].x;
            floatArray[i * 3 + 1] = vectors[i].y;
            floatArray[i * 3 + 2] = vectors[i].z;
        }
        return floatArray;
    }

    private List<Vector3> FloatArrayToVectors(float[] floatArray, int pointCount)
    {
        if (floatArray == null || floatArray.Length != pointCount * 3)
        {
            Debug.LogError($"Invalid float array size ({floatArray?.Length}) for {pointCount} Vector3 conversion.");
            return null;
        }
        List<Vector3> vectors = new List<Vector3>(pointCount);
        // Consider parallelizing if pointCount is huge
        for (int i = 0; i < pointCount; i++)
        {
            vectors.Add(new Vector3(
                floatArray[i * 3 + 0],
                floatArray[i * 3 + 1],
                floatArray[i * 3 + 2]
            ));
        }
        return vectors;
    }

    // Simple Centroid Calculation (if needed, though RBF handles offsets internally)
    Vector3 ComputeCentroid(List<Vector3> points)
    {
        if (points == null || points.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points) sum += p;
        return sum / points.Count;
    }
}