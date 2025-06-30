using System.Collections.Generic;
using UnityEngine;

namespace ICP
{
    public static class ICPAlgorithm
    {
        /// <summary>
        /// 运行 ICP 算法，将目标点云对齐到参考点云。
        /// 返回一个刚性变换矩阵，该矩阵可将目标点云变换到参考点云坐标系下。
        /// </summary>
        /// <param name="referencePoints">参考点云（全局坐标）</param>
        /// <param name="targetPoints">目标点云（全局坐标）</param>
        /// <param name="maxIterations">最大迭代次数</param>
        /// <param name="tolerance">误差收敛阈值（均方根误差）</param>
        /// <returns>将目标点云对齐到参考点云的刚性变换矩阵</returns>
        public static Matrix4x4 RunICP(List<Vector3> referencePoints, List<Vector3> targetPoints, int maxIterations = 50, float tolerance = 0.001f)
        {
            // 构建 KDTree 用于参考点云的快速最近邻搜索
            KDTree kdTree = new KDTree(referencePoints);
            Matrix4x4 totalTransform = Matrix4x4.identity;

            // 拷贝目标点云，后续更新变换
            List<Vector3> currentTarget = new List<Vector3>(targetPoints);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                List<Vector3> corrReference = new List<Vector3>();
                List<Vector3> corrTarget = new List<Vector3>();
                float totalError = 0f;

                // 为当前目标点云中的每个点寻找最近邻（对应的参考点）
                foreach (Vector3 pt in currentTarget)
                {
                    Vector3 nearest = kdTree.NearestNeighbor(pt);
                    corrReference.Add(nearest);
                    corrTarget.Add(pt);
                    totalError += (pt - nearest).sqrMagnitude;
                }
                float rmsError = Mathf.Sqrt(totalError / currentTarget.Count);
                if (rmsError < tolerance)
                {
                    Debug.Log($"ICP 收敛在迭代 {iter} 次，误差 = {rmsError}");
                    break;
                }

                // 计算从 corrTarget 到 corrReference 的最佳刚性变换
                Matrix4x4 deltaTransform = ComputeRigidTransform(corrTarget, corrReference);

                // 更新累计变换
                totalTransform = deltaTransform * totalTransform;

                // 将变换应用于当前目标点云
                for (int i = 0; i < currentTarget.Count; i++)
                {
                    currentTarget[i] = deltaTransform.MultiplyPoint3x4(currentTarget[i]);
                }
            }
            return totalTransform;
        }

        /// <summary>
        /// 使用 Horn 的方法计算从 src 到 dst 的最佳刚性变换。
        /// </summary>
        private static Matrix4x4 ComputeRigidTransform(List<Vector3> src, List<Vector3> dst)
        {
            int n = src.Count;
            Vector3 centroidSrc = Vector3.zero;
            Vector3 centroidDst = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                centroidSrc += src[i];
                centroidDst += dst[i];
            }
            centroidSrc /= n;
            centroidDst /= n;

            // 构建交叉协方差矩阵 H
            float[,] H = new float[3, 3];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = src[i] - centroidSrc;
                Vector3 b = dst[i] - centroidDst;
                H[0, 0] += a.x * b.x; H[0, 1] += a.x * b.y; H[0, 2] += a.x * b.z;
                H[1, 0] += a.y * b.x; H[1, 1] += a.y * b.y; H[1, 2] += a.y * b.z;
                H[2, 0] += a.z * b.x; H[2, 1] += a.z * b.y; H[2, 2] += a.z * b.z;
            }

            // 构造 4x4 矩阵 N，根据 Horn 的方法
            float trace = H[0, 0] + H[1, 1] + H[2, 2];
            float[,] N = new float[4, 4];
            N[0, 0] = trace;
            N[0, 1] = H[1, 2] - H[2, 1];
            N[0, 2] = H[2, 0] - H[0, 2];
            N[0, 3] = H[0, 1] - H[1, 0];

            N[1, 0] = N[0, 1];
            N[1, 1] = H[0, 0] - H[1, 1] - H[2, 2];
            N[1, 2] = H[0, 1] + H[1, 0];
            N[1, 3] = H[0, 2] + H[2, 0];

            N[2, 0] = N[0, 2];
            N[2, 1] = N[1, 2];
            N[2, 2] = -H[0, 0] + H[1, 1] - H[2, 2];
            N[2, 3] = H[1, 2] + H[2, 1];

            N[3, 0] = N[0, 3];
            N[3, 1] = N[1, 3];
            N[3, 2] = N[2, 3];
            N[3, 3] = -H[0, 0] - H[1, 1] + H[2, 2];

            // 求 N 的最大特征值对应的特征向量（使用简单的幂迭代）
            Vector4 q = PowerIteration(N, 100);
            Quaternion rotation = new Quaternion(q.x, q.y, q.z, q.w);
            rotation.Normalize();

            // 计算平移
            Vector3 translation = centroidDst - rotation * centroidSrc;

            // 构造变换矩阵
            return Matrix4x4.TRS(translation, rotation, Vector3.one);
        }

        /// <summary>
        /// 对 4x4 对称矩阵 N 进行幂迭代，返回近似最大特征值对应的特征向量。
        /// </summary>
        private static Vector4 PowerIteration(float[,] N, int iterations)
        {
            Vector4 b = new Vector4(1, 1, 1, 1);
            for (int i = 0; i < iterations; i++)
            {
                b = MultiplyMatrixVector(N, b);
                b.Normalize();
            }
            return b;
        }

        private static Vector4 MultiplyMatrixVector(float[,] M, Vector4 v)
        {
            return new Vector4(
                M[0, 0] * v.x + M[0, 1] * v.y + M[0, 2] * v.z + M[0, 3] * v.w,
                M[1, 0] * v.x + M[1, 1] * v.y + M[1, 2] * v.z + M[1, 3] * v.w,
                M[2, 0] * v.x + M[2, 1] * v.y + M[2, 2] * v.z + M[2, 3] * v.w,
                M[3, 0] * v.x + M[3, 1] * v.y + M[3, 2] * v.z + M[3, 3] * v.w);
        }

        #region KDTree Implementation

        private class KDTree
        {
            private class Node
            {
                public Vector3 point;
                public int index;
                public Node left;
                public Node right;
            }

            private Node root;

            public KDTree(List<Vector3> points)
            {
                List<(Vector3, int)> pts = new List<(Vector3, int)>();
                for (int i = 0; i < points.Count; i++)
                    pts.Add((points[i], i));
                root = Build(pts, 0);
            }

            private Node Build(List<(Vector3, int)> pts, int depth)
            {
                if (pts == null || pts.Count == 0)
                    return null;
                int axis = depth % 3;
                pts.Sort((a, b) =>
                {
                    if (axis == 0)
                        return a.Item1.x.CompareTo(b.Item1.x);
                    else if (axis == 1)
                        return a.Item1.y.CompareTo(b.Item1.y);
                    else
                        return a.Item1.z.CompareTo(b.Item1.z);
                });
                int median = pts.Count / 2;
                Node node = new Node();
                node.point = pts[median].Item1;
                node.index = pts[median].Item2;
                node.left = Build(pts.GetRange(0, median), depth + 1);
                node.right = Build(pts.GetRange(median + 1, pts.Count - median - 1), depth + 1);
                return node;
            }

            public Vector3 NearestNeighbor(Vector3 query)
            {
                return Nearest(root, query, 0, root.point, float.MaxValue);
            }

            private Vector3 Nearest(Node node, Vector3 query, int depth, Vector3 best, float bestDist)
            {
                if (node == null)
                    return best;
                float d = (query - node.point).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = node.point;
                }
                int axis = depth % 3;
                float diff = 0;
                if (axis == 0)
                    diff = query.x - node.point.x;
                else if (axis == 1)
                    diff = query.y - node.point.y;
                else
                    diff = query.z - node.point.z;
                Node nearNode = diff < 0 ? node.left : node.right;
                Node farNode = diff < 0 ? node.right : node.left;
                best = Nearest(nearNode, query, depth + 1, best, bestDist);
                bestDist = (query - best).sqrMagnitude;
                if (diff * diff < bestDist)
                    best = Nearest(farNode, query, depth + 1, best, bestDist);
                return best;
            }
        }

        #endregion
    }
}
