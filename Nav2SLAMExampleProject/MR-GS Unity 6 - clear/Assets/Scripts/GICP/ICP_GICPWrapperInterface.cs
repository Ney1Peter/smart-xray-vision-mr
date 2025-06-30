using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class ICP_GICPWrapperInterface : MonoBehaviour
{
    // 与C++结构体匹配
    [StructLayout(LayoutKind.Sequential)]
    public struct GICPResult
    {
        [MarshalAs(UnmanagedType.I1)] // C++ bool 占1字节
        public bool converged;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_ref_before;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_target_before;
    }

    // DLL函数导入 - 与C++签名严格匹配
    [DllImport("ICP_GICPWrapper", CallingConvention = CallingConvention.Cdecl)]
    public static extern GICPResult RunGICP(
        [In] float[] referencePoints, int refTotalFloats,
        [In] float[] targetPoints, int tgtTotalFloats,
        int maxIterations, float transformationEpsilon,
        bool doDownsample,
        float voxelSize,
        bool useRANSAC
    );

    // 可选：转换为 Unity Matrix4x4
    public static Matrix4x4 ToUnityMatrix(float[] matrix)
    {
        if (matrix == null || matrix.Length != 16)
            throw new ArgumentException("Matrix must be 16 floats.");

        Matrix4x4 mat = new Matrix4x4();
        for (int i = 0; i < 16; i++)
            mat[i] = matrix[i]; // Unity 默认行优先，和你的 C++ 一致

        return mat;
    }

    // 示例调用
    public void TestRunGICP()
    {
        float[] refPoints = new float[] {
            0f, 0f, 0f,
            1f, 0f, 0f,
            0f, 1f, 0f
        };

        float[] tgtPoints = new float[] {
            0.1f, 0f, 0f,
            1.1f, 0f, 0f,
            0.1f, 1f, 0f
        };

        GICPResult result = RunGICP(
            refPoints, refPoints.Length,
            tgtPoints, tgtPoints.Length,
            maxIterations: 100,
            transformationEpsilon: 1e-6f,
            doDownsample: true,
            voxelSize: 0.05f,
            useRANSAC: true
        );

        Debug.Log("Converged: " + result.converged);
        Debug.Log("Transform Matrix:\n" + ToUnityMatrix(result.matrix));
    }
}
