// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent " }

        Pass
        {
            ZWrite Off // 透明物体通常关闭深度写入

            // 混合模式：OneMinusDstAlpha One 是一种累加混合模式，适用于高斯散斑的半透明叠加。
            // 它有助于实现从后往前的透明度累积效果。
            Blend OneMinusDstAlpha One 

            Cull Off // 不进行背面剔除，渲染所有高斯散斑的面
            
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma require compute
        #pragma use_dxc // 启用 DirectX Shader Compiler (DXC)

        #include "UnityCG.cginc"       // 包含 Unity 内置的常用函数和定义
        #include "GaussianSplatting.hlsl" // 包含 SplatData 和 SplatViewData 定义

        // --- 新增：剪裁相关数据结构体 ---
        // 必须与 C# 脚本中 GaussianCutout.ShaderData 结构体完全匹配
        struct GaussianCutoutShaderData
        {
            float4x4 matrix;                  // 剪裁对象的 WorldToLocal 矩阵
            uint typeAndFlags;                // 编码 Type (Ellipsoid/Box) 和 CutoutMode (Discard/TransparentInner/TransparentOuter)
            int cutIndices[8];                // 图层剪裁索引 (本 Shader 示例不直接处理，可扩展)
            float transparencyFadeDistance;   // 平滑过渡距离 (0.0f 表示硬性切换)
            float targetTransparency;         // 目标透明度 (0.0f 完全透明，1.0f 完全不透明)
        };

        // 从 C# 脚本传递过来的剪裁数据，通过 StructuredBuffer 接收
        // 这里假设只处理一个剪裁器，所以只读取索引 0
        StructuredBuffer<GaussianCutoutShaderData> _CutoutDataBuffer; 

        // 原始 Shader 中用于控制渲染顺序和 Splat 数据的 Buffers
        StructuredBuffer<uint> _OrderBuffer;
        StructuredBuffer<SplatViewData> _SplatViewData;
        ByteAddressBuffer _SplatSelectedBits; // 用于处理选中状态的位图
        uint _SplatBitsValid;                 // 标志位，指示选中位图是否有效
        uint _OptimizeForQuest;               // 用于 Quest 平台的优化标志

        // 顶点着色器输出到片段着色器的结构体
        struct v2f
        {
            half4 col : COLOR0; // 包含原始 Splat 颜色和 Alpha (可能为 -1 表示选中)
            float2 pos : TEXCOORD0; // 像素在屏幕空间投影椭圆内的归一化坐标 [-1, 1]
            float4 vertex : SV_POSITION; // 裁剪空间位置 (xy 用于屏幕位置, w 用于深度)
            
            // --- 新增：用于片段着色器中重建世界坐标所需的数据 ---
            // 直接传递裁剪空间位置，让片段着色器自行重建世界坐标
            float4 clipPosForWorldRecon : TEXCOORD1; 
        };

        // 顶点着色器
        v2f vert (uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
        {
            v2f o = (v2f)0; // 初始化为零

            // 获取当前实例在排序缓冲中的实际 ID
            instID = _OrderBuffer[instID];

            // 从 SplatViewData 中获取当前 Splat 的视图相关数据
            SplatViewData view = _SplatViewData[instID];

            float4 centerClipPos = view.pos; // Splat 中心点的裁剪空间位置

            // Quest 平台优化，如果启用则重新计算世界和裁剪空间位置
            if (_OptimizeForQuest) {
                SplatData splat = LoadSplatData(instID); // 从另一个 Buffer 加载 Splat 原始数据
                float3 centerWorldPos = mul(unity_ObjectToWorld, float4(splat.pos, 1)).xyz; // 从对象空间到世界空间
                centerClipPos = mul(UNITY_MATRIX_VP, float4(centerWorldPos, 1)); // 从世界空间到裁剪空间
            }

            // 如果 Splat 在摄像机后面，丢弃该图元 (通过设置 NaN)
            bool behindCam = centerClipPos.w <= 0;
            if (behindCam)
            {
                o.vertex = asfloat(0x7fc00000); // 设置为 NaN，GPU 会丢弃此图元
            }
            else
            {
                // 从 SplatViewData 中提取颜色和原始 Alpha (半浮点转换)
                o.col.r = f16tof32(view.color.x >> 16);
                o.col.g = f16tof32(view.color.x);
                o.col.b = f16tof32(view.color.y >> 16);
                o.col.a = f16tof32(view.color.y); // Splat 原始 alpha

                // 根据 vtxID (0-3) 计算 Quad 的顶点位置，范围 [-1, 1]
                uint idx = vtxID;
                float2 quadPos = float2(idx&1, (idx>>1)&1) * 2.0 - 1.0; 
                // quadPos *= 2; // !!! 原有代码这里乘以 2，如果 i.pos 预期是 [-1,1]，请确保这里是注释的。
                                  // 保持原有行为，假设 i.pos 会在 frag 中被正确使用
                o.pos = quadPos; // 像素在投影椭圆内的归一化坐标，传递到片段着色器

                // 计算屏幕空间中像素相对于 Splat 中心的偏移，并添加到中心点的裁剪空间位置
                float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
                o.vertex = centerClipPos;
                o.vertex.xy += deltaScreenPos * centerClipPos.w; // 保持原有裁剪空间位置计算

                // --- 新增：传递裁剪空间位置到片段着色器，用于重建世界位置 ---
                o.clipPosForWorldRecon = o.vertex; 
                // --- 结束新增 ---

                // 处理 Splat 选中状态
                if (_SplatBitsValid)
                {
                    uint wordIdx = instID / 32;
                    uint bitIdx = instID & 31;
                    uint selVal = _SplatSelectedBits.Load(wordIdx * 4);
                    if (selVal & (1 << bitIdx))
                    {
                        o.col.a = -1; // 用特殊值 -1 标记选中状态
                    }
                }
            }
            return o;
        }

        // 片段着色器
        half4 frag (v2f i) : SV_Target
        {
            // --- 原始高斯散斑颜色和 Alpha 计算 ---
            // 基于 i.pos (像素在投影椭圆内的位置) 计算高斯衰减
            float power = -dot(i.pos, i.pos);
            half splatAlpha = exp(power); 

            half3 baseColor = i.col.rgb; // 原始颜色
            half baseAlphaFromVertex = i.col.a; // 从顶点着色器传递的 alpha (可能为 -1)

            // --- 处理 Splat 选中状态 (保留原有逻辑) ---
            half currentRenderAlpha; // 实际用于渲染的 alpha 值

            if (baseAlphaFromVertex < 0) // 如果 col.a 是 -1，表示这个 splat 被选中了
            {
                half3 selectedColor = half3(1,0,1); // 洋红色
                if (splatAlpha > 7.0/255.0) // 如果高斯衰减 alpha 足够大
                {
                    if (splatAlpha < 10.0/255.0) // 如果在特定范围内，用于描边效果
                    {
                        currentRenderAlpha = 1; // 变为完全不透明
                        baseColor = selectedColor; // 颜色变为洋红色
                    }
                    else {
                        currentRenderAlpha = saturate(splatAlpha + 0.3); // 增加不透明度
                    }
                }
                else {
                    currentRenderAlpha = splatAlpha; // 否则保持高斯衰减 alpha
                }
                baseColor = lerp(baseColor, selectedColor, 0.5); // 颜色混合，使其略带洋红色
            }
            else
            {
                // 如果未选中，将高斯衰减 alpha 与原始 splat alpha 结合
                currentRenderAlpha = saturate(splatAlpha * baseAlphaFromVertex); 
            }
            
            half finalAlpha = currentRenderAlpha; // 最终的 Alpha，默认是经过选择处理后的 alpha

            // --- 应用剪裁/透明化逻辑 ---
            // 假设我们只处理 _CutoutDataBuffer 中的第一个剪裁器
            GaussianCutoutShaderData cutout = _CutoutDataBuffer[0]; 

            // 检查是否禁用剪裁 (来自 C# 中的 typeAndFlags = 0xFFFFFFFFu)
            if (cutout.typeAndFlags != 0xFFFFFFFFu)
            {
                // 解析 typeAndFlags: 类型 (Ellipsoid/Box), 模式 (Discard/TransparentInner/TransparentOuter), Invert (仅Discard)
                uint type = cutout.typeAndFlags & 0xFFu;          // 低8位表示 Type
                uint mode = (cutout.typeAndFlags >> 8) & 0xFFu;    // 中间8位表示 CutoutMode
                bool invertDiscard = (cutout.typeAndFlags >> 16) & 0x1u; // 第16位表示 Discard 模式的 Invert 标志

                // --- 关键：重建当前像素的世界位置 ---
                // 利用顶点着色器传递的裁剪空间位置 (i.clipPosForWorldRecon)
                // 这个方法通用且相对准确，但依赖于 i.clipPosForWorldRecon.w 的正确深度
                float4 clipPos = i.clipPosForWorldRecon;
                clipPos.xyz /= clipPos.w; // 透视除法，得到 NDC 空间位置 (x,y,z)
                
                // 将 NDC 空间位置转换到视图空间，再转换到世界空间
                float4 viewPos = mul(unity_CameraInvProjection, float4(clipPos.x, clipPos.y, clipPos.z, 1.0)); 
                viewPos.xyz /= viewPos.w; // 再次透视除法，得到 View Space 3D 坐标
                float4 worldPosH = mul(unity_CameraToWorld, float4(viewPos.xyz, 1.0)); // 世界空间齐次坐标
                float3 pixelWorldPos = worldPosH.xyz / worldPosH.w; // 得到世界空间 3D 坐标 (非齐次)
                
                // 将像素的世界位置转换到剪裁对象的局部空间
                float3 cutoutLocalPos = mul(cutout.matrix, float4(pixelWorldPos, 1)).xyz;

                // 计算像素到剪裁形状边界的精确距离
                // distToBoundary < 0 表示在形状内部，distToBoundary > 0 表示在形状外部
                float distToBoundary = 0.0; 
                if (type == 0) // Ellipsoid (局部空间为单位球体)
                {
                    distToBoundary = length(cutoutLocalPos) - 1.0;
                }
                else if (type == 1) // Box (局部空间为单位立方体，范围 [-1,1]³)
                {
                    // 计算点到 AABB 边界的距离
                    float3 d = abs(cutoutLocalPos) - 1.0;
                    distToBoundary = length(max(0, d)); // 外部距离 (点在外面时，到最近面的距离)
                    if (all(abs(cutoutLocalPos) < 1.0)) // 如果点在盒子内部
                    {
                        // 内部距离 (点在里面时，到最远面的距离，取负值)
                        distToBoundary = -min(d.x, min(d.y, d.z)); 
                    }
                }
                
                // --- 根据不同的模式和距离计算最终的 Alpha ---
                float alphaMultiplier = 1.0; // 默认不影响 alpha

                if (mode == 0) // Discard (传统硬性剪裁)
                {
                    bool discardThisPixel = (distToBoundary < 0 && !invertDiscard) || (distToBoundary > 0 && invertDiscard);
                    if (discardThisPixel)
                    {
                        discard; // 立即丢弃像素
                    }
                }
                else if (mode == 1) // TransparentInner (盒内透明，带平滑过渡)
                {
                    // smoothstep(min, max, value)
                    // 当 distToBoundary 从 -fadeDistance 变到 0 时，mixFactor 从 0 变到 1
                    // 此时 alphaMultiplier 从 targetTransparency 平滑过渡到 1.0
                    float mixFactor = smoothstep(-cutout.transparencyFadeDistance, 0, distToBoundary);
                    alphaMultiplier = lerp(cutout.targetTransparency, 1.0, mixFactor); 
                }
                else if (mode == 2) // TransparentOuter (盒外透明，带平滑过渡)
                {
                    // smoothstep(min, max, value)
                    // 当 distToBoundary 从 0 变到 fadeDistance 时，mixFactor 从 0 变到 1
                    // 此时 alphaMultiplier 从 1.0 平滑过渡到 targetTransparency
                    float mixFactor = smoothstep(0, cutout.transparencyFadeDistance, distToBoundary);
                    alphaMultiplier = lerp(1.0, cutout.targetTransparency, mixFactor);
                }

                // 将剪裁/透明化计算出的乘数应用到最终 Alpha
                finalAlpha = saturate(finalAlpha * alphaMultiplier);

            } // end if (cutout.typeAndFlags != 0xFFFFFFFFu)

            // --- 图层剪裁逻辑 (可选，如果需要) ---
            // 如果你的图层剪裁是基于 instID 的，并且你在 v2f 中传递了 instID
            // 你可以在这里添加类似以下伪代码的逻辑：
            /*
            if (IsSplatLayerCut(i.instID, cutout.cutIndices)) // 假设 i.instID 已从 vert 传递
            {
                finalAlpha = 0; // 或 discard;
            }
            */

            // 最终的像素丢弃，避免渲染几乎完全透明的像素
            if (finalAlpha < 1.0/255.0) 
                discard;

            // 返回最终的颜色和 Alpha
            half4 res = half4(baseColor * finalAlpha, finalAlpha);
            return res;
        }

        ENDCG
        }
    }
}