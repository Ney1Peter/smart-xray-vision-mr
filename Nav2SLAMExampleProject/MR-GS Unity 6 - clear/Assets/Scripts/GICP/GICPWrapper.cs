using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class GICPWrapper : MonoBehaviour
{
    // 和 C++ 中的结构体保持一致
    [StructLayout(LayoutKind.Sequential)]
    public struct MyGICPResult
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool converged;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public float[] matrix;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_ref_before;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] centroid_target_before;
    }

    // DLL 导入
    [DllImport("ICP_GICPWrapper2", CallingConvention = CallingConvention.Cdecl)]
    public static extern MyGICPResult RunGICP(
        [In] float[] referencePoints, int refTotalFloats,
        [In] float[] targetPoints, int tgtTotalFloats,
        int maxIterations, float transformationEpsilon,
        bool doDownsample,
        float voxelSize,
        bool usePCA,
        bool pcaBasedOnVoxel,
        bool useRANSAC,
        bool ransacBasedOnVoxel,
        int ransacMaxIterations,
        float ransacMaxCorrespondDist,
        float ransacMinSampleDist,
        float fpfhRadius
    );

    // 示例函数：在 Unity 中调用注册功能
    public static Matrix4x4 AlignPointClouds(float[] refCloud, float[] tgtCloud)
    {
        int refCount = refCloud.Length;
        int tgtCount = tgtCloud.Length;

        // 参数设置（你可以外部暴露为可配置参数）
        int maxIter = 50;
        float transEps = 1e-6f;
        bool doDownsample = true;
        float voxelSize = 0.02f;
        bool usePCA = true;
        bool pcaVoxel = true;
        bool useRANSAC = true;
        bool ransacVoxel = true;
        int ransacIter = 60;
        float ransacCorr = 0.2f;
        float ransacMinDist = 0.05f;
        float fpfhRadius = 0.1f;

        MyGICPResult result = RunGICP(
            refCloud, refCount,
            tgtCloud, tgtCount,
            maxIter, transEps,
            doDownsample, voxelSize,
            usePCA, pcaVoxel,
            useRANSAC, ransacVoxel,
            ransacIter, ransacCorr, ransacMinDist, fpfhRadius
        );

        if (!result.converged)
        {
            Debug.LogWarning("GICP did not converge.");
            return Matrix4x4.identity;
        }

        // 构建 Unity 的 Matrix4x4
        Matrix4x4 mat = new Matrix4x4();
        for (int row = 0; row < 4; ++row)
            for (int col = 0; col < 4; ++col)
                mat[row, col] = result.matrix[row * 4 + col];

        Debug.Log("GICP converged! Transform matrix:\n" + mat);

        return mat;
    }
}
