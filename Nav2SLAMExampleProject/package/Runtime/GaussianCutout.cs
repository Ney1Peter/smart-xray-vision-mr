// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    public class GaussianCutout : MonoBehaviour
    {
        public enum Type
        {
            Ellipsoid,
            Box
        }

        // 新增：剪裁模式
        public enum CutoutMode
        {
            Discard,       // 传统剪裁：内部/外部完全丢弃（默认行为）
            TransparentInner, // 内部透明：盒内变透明，盒外不透明
            TransparentOuter  // 外部透明：盒外变透明，盒内不透明 (反转效果)
        }

        public Type m_Type = Type.Ellipsoid;
        public CutoutMode m_Mode = CutoutMode.Discard; // 默认是传统剪裁
        // 注意：m_Invert 现在会影响 Discard 模式，而在 Transparent 模式下，TransparentInner/Outer 已经定义了效果
        // 如果想让 m_Invert 普遍应用于所有模式的反转，需要更复杂的逻辑。
        // 这里为了简化，我们先假设 TransparentInner/Outer 已经包含了所需的反转含义。
        // 如果需要，可以把 m_Invert 移除或者调整其含义。
        // 为了兼容旧逻辑，我们保留 m_Invert，并让它仅对 Discard 模式生效。
        public bool m_Invert = false;

        // 新增：透明度渐变的距离 (影响平滑度)
        [Range(0.0f, 1.0f)] // 0表示硬性透明/不透明，1表示平滑过渡
        public float m_TransparencyFadeDistance = 0.1f;

        // 新增：盒内（或盒外）物体透明度目标值 (仅对 Transparent 模式有效)
        [Range(0.0f, 1.0f)] // 0表示完全透明，1表示完全不透明
        public float m_TargetTransparency = 0.0f; // 默认完全透明

        public bool[] layersToCut = Array.Empty<bool>();

        // ShaderData 结构体需要与 HLSL 中的结构体保持一致
        public unsafe struct ShaderData
        {
            public Matrix4x4 matrix;
            public uint typeAndFlags; // 包含 Type 和 CutoutMode 信息
            public fixed int cutIndices[8];
            public float transparencyFadeDistance; // 新增字段
            public float targetTransparency;       // 新增字段
        }

        public static ShaderData GetShaderData(GaussianCutout self, Matrix4x4 rendererMatrix, GaussianSplatAsset asset)
        {
            ShaderData sd = default;
            if (!(self && self.isActiveAndEnabled))
            {
                sd.typeAndFlags = ~0u; // Sentinel value to disable cutout
                return sd;
            }

            unsafe
            {
                for (int i = 0; i < 8; i++)
                {
                    sd.cutIndices[i] = -1;
                }
            }

            var tr = self.transform;
            sd.matrix = tr.worldToLocalMatrix * rendererMatrix;

            // 编码 Type 和 CutoutMode 到 typeAndFlags
            // 低 8 位用于 Type (Ellipsoid/Box)
            // 接下来 8 位用于 CutoutMode (Discard/TransparentInner/TransparentOuter)
            // 再下一个位用于 m_Invert (仅对 Discard 模式有效)
            uint flags = (uint)self.m_Type;
            flags |= ((uint)self.m_Mode) << 8; // 将 CutoutMode 编码到更高的位
            if (self.m_Mode == CutoutMode.Discard && self.m_Invert) // 只有 Discard 模式才考虑 m_Invert
            {
                flags |= 0x10000u; // 设置一个更高的位来表示 Invert
            }
            sd.typeAndFlags = flags;

            // 新增：传递透明度参数
            sd.transparencyFadeDistance = self.m_TransparencyFadeDistance;
            sd.targetTransparency = self.m_TargetTransparency;

            for (int layer = 0; layer < Math.Min(4, self.layersToCut.Length); layer++)
            {
                if (self.layersToCut[layer] && asset.layerInfo.TryGetValue(layer, out int count))
                {
                    int idxFrom = asset.layerInfo.Where(kv => kv.Key < layer).Sum(kv => kv.Value);
                    unsafe
                    {
                        sd.cutIndices[layer * 2] = idxFrom;
                        sd.cutIndices[layer * 2 + 1] = idxFrom + count;
                    }
                }
            }

            return sd;
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            // Gizmo 矩阵现在不包含 Softness 缩放，因为 Softness/FadeDistance 在 Shader 中处理
            Gizmos.matrix = transform.localToWorldMatrix;

            var color = Color.magenta;
            color.a = 0.2f;

            if (Selection.Contains(gameObject))
                color.a = 0.9f;
            else
            {
                var activeGo = Selection.activeGameObject;
                if (activeGo != null)
                {
                    var activeSplat = activeGo.GetComponent<GaussianSplatRenderer>();
                    if (activeSplat != null)
                    {
                        if (activeSplat.m_Cutouts != null && activeSplat.m_Cutouts.Contains(this))
                            color.a = 0.5f;
                    }
                }
            }

            // 根据不同的模式调整 Gizmo 颜色以进行可视化区分
            if (m_Mode == CutoutMode.TransparentInner || m_Mode == CutoutMode.TransparentOuter)
            {
                color = Color.cyan; // 用青色表示透明模式
                // 绘制一个稍微大一点的框来表示渐变区域
                Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
                if (m_Type == Type.Ellipsoid)
                {
                    Gizmos.DrawWireSphere(Vector3.zero, 1.0f + m_TransparencyFadeDistance);
                    Gizmos.DrawWireSphere(Vector3.zero, 1.0f - m_TransparencyFadeDistance);
                }
                if (m_Type == Type.Box)
                {
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2 * (1.0f + m_TransparencyFadeDistance));
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2 * (1.0f - m_TransparencyFadeDistance));
                }
            }


            Gizmos.color = color;
            if (m_Type == Type.Ellipsoid)
            {
                Gizmos.DrawWireSphere(Vector3.zero, 1.0f);
            }
            if (m_Type == Type.Box)
            {
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2);
            }
        }
#endif // #if UNITY_EDITOR
    }
}