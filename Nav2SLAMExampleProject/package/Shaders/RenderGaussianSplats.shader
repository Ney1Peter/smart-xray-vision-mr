// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One
            Cull Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0
            #pragma require  compute
            #pragma use_dxc

            #include "UnityCG.cginc"
            #include "GaussianSplatting.hlsl"

            // ───── ① 线段裁剪（你原来的逻辑） ─────
            StructuredBuffer<float4> _ClipStart; // xyz 起,w 半径
            StructuredBuffer<float4> _ClipEnd;
            uint                     _ClipCount;

            inline bool InAnyClipRegion(float3 P)
            {
                if (_ClipCount == 0) return false;
                [loop]
                for (uint i = 0; i < _ClipCount; i++)
                {
                    float3 A = _ClipStart[i].xyz;
                    float3 B = _ClipEnd[i].xyz;
                    float  r = _ClipStart[i].w;

                    float3 pa = P - A;
                    float3 ba = B - A;
                    float  h  = saturate(dot(pa, ba) / dot(ba, ba));
                    float3 proj = A + h * ba;
                    if (length(P - proj) < r) return true;
                }
                return false;
            }

            // ───── ② 视线洞参数 ─────
            float4 _CutCenterR;   // xyz = 圆心, w = 半径
            float  _CutMinAlpha;  // 圆心最小 α (0~1). 0 = 完全透明

            //#define FADE_ALPHA        // ← 取消注释 = 渐变透明；保留注释 = 硬剪 discard

            // ───── 其余原有 Uniform/Sampler 略 ─────
            StructuredBuffer<uint>  _OrderBuffer;
            StructuredBuffer<uint>  _GroupId;
            float                   _GroupAlpha[32];

            struct v2f
            {
                half4 col : COLOR0;
                float2 pos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            StructuredBuffer<SplatViewData> _SplatViewData;
            ByteAddressBuffer _SplatSelectedBits;
            uint _SplatBitsValid;
            uint _OptimizeForQuest;

            v2f vert (uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
            {
                v2f o = (v2f)0;
                instID = _OrderBuffer[instID];

                uint  gid          = _GroupId[instID];
                float alphaFactor  = _GroupAlpha[gid];

                SplatData splat = LoadSplatData(instID);
                float3 centerWorldPos = mul(unity_ObjectToWorld, float4(splat.pos,1)).xyz;
                o.worldPos = centerWorldPos;

                SplatViewData view = _SplatViewData[instID];
                float4 centerClipPos = _OptimizeForQuest ?
                        mul(UNITY_MATRIX_VP, float4(centerWorldPos,1)) : view.pos;

                if (centerClipPos.w <= 0)
                {
                    o.vertex = asfloat(0x7fc00000);
                    return o;
                }

                // ─── 线段裁剪：如命中则直接抛弃 ───
                if (InAnyClipRegion(centerWorldPos))
                {
                    o.vertex = asfloat(0x7fc00000);
                    return o;
                }

                // color
                o.col.r = f16tof32(view.color.x >> 16);
                o.col.g = f16tof32(view.color.x);
                o.col.b = f16tof32(view.color.y >> 16);
                o.col.a = f16tof32(view.color.y) * alphaFactor;

                // quad verts
                uint   idx = vtxID;
                float2 quadPos = float2(idx & 1, (idx >> 1) & 1) * 2 - 1;
                quadPos *= 2;
                o.pos = quadPos;

                float2 delta = (quadPos.x * view.axis1 + quadPos.y * view.axis2)
                               * 2 / _ScreenParams.xy;
                o.vertex = centerClipPos;
                o.vertex.xy += delta * centerClipPos.w;

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // 初始高斯 α
                half  alpha = exp(-dot(i.pos, i.pos));
                half4 col   = half4(i.col.rgb, 1) * alpha;

                // ─── 圆洞裁剪/渐变 ───
                float d = distance(i.worldPos, _CutCenterR.xyz);
                float r = _CutCenterR.w;

            #ifdef FADE_ALPHA
                float w = saturate(1 - d / r);      // 0→边缘 1→中心
                w = w * w;                          // 二次衰减，可自行调整
                col.a *= lerp(1, _CutMinAlpha, w);
                col.rgb *= col.a;                   // 同步亮度
            #else
                if (d < r) discard;                 // 硬剪
            #endif

                // 最终丢极低 α
                if (col.a < 1.0/255.0) discard;
                return col;
            }

            ENDCG
        }
    }
}
