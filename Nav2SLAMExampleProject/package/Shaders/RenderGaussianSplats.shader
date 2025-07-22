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
            Cull  Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0
            #pragma require  compute
            #pragma use_dxc

            #include "UnityCG.cginc"
            #include "GaussianSplatting.hlsl"

            /* ─────── 可选效果开关 ─────── */
            #define  FADE_ALPHA          // 圆洞渐隐（去掉则硬剪）
            #define  DITHER_EDGE         // 羽化噪声
            #define  DEPTH_FADE          // 深度渐隐
            //#define  EDGE_BLOOM          // 洞边缘高亮 → Bloom

            /* ─────── 数据结构 / Uniform ─────── */
            // (1) 线段裁剪
            StructuredBuffer<float4> _ClipStart;
            StructuredBuffer<float4> _ClipEnd;
            uint                     _ClipCount;

            inline bool InAnyClipRegion(float3 P)
            {
                [loop] for(uint i=0;i<_ClipCount;i++)
                {
                    float3 A=_ClipStart[i].xyz, B=_ClipEnd[i].xyz;
                    float  r=_ClipStart[i].w;

                    float3 pa=P-A, ba=B-A;
                    float  h=saturate(dot(pa,ba)/dot(ba,ba));
                    if(length(P-(A+h*ba))<r) return true;
                }
                return false;
            }

            // (2) 圆洞
            float4 _CutCenterR;   // xyz=center, w=radius
            float  _CutMinAlpha;  // 最小 α（洞中心）

            // (3) 深度渐隐参数
            float _FadeNear = 0.0;     // m
            float _FadeFar  = 3.0;     // m

            // 其余 buffer...
            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<uint> _GroupId;
            float                  _GroupAlpha[32];
            StructuredBuffer<SplatViewData> _SplatViewData;
            ByteAddressBuffer _SplatSelectedBits;
            uint _SplatBitsValid, _OptimizeForQuest;

            /* ─────── 工具函数 ─────── */
            inline float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            struct v2f
            {
                half4  col      : COLOR0;
                float2 pos      : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 vertex   : SV_POSITION;
            };

            /* ─────── Vertex ─────── */
            v2f vert(uint vtxID:SV_VertexID,uint instID:SV_InstanceID)
            {
                v2f o=(v2f)0;
                instID=_OrderBuffer[instID];

                float alphaFactor=_GroupAlpha[_GroupId[instID]];

                // world pos
                SplatData        splat=LoadSplatData(instID);
                float3 centerW = mul(unity_ObjectToWorld,float4(splat.pos,1)).xyz;
                o.worldPos = centerW;

                // clip pos
                SplatViewData view=_SplatViewData[instID];
                float4 centerC = _OptimizeForQuest?mul(UNITY_MATRIX_VP,float4(centerW,1)):view.pos;
                if(centerC.w<=0){o.vertex=asfloat(0x7fc00000);return o;}

                // 胶囊裁剪
                if(InAnyClipRegion(centerW)){o.vertex=asfloat(0x7fc00000);return o;}

                // base color+α
                o.col.r=f16tof32(view.color.x>>16);
                o.col.g=f16tof32(view.color.x);
                o.col.b=f16tof32(view.color.y>>16);
                o.col.a=f16tof32(view.color.y)*alphaFactor;

                // quad
                uint idx=vtxID;
                float2 q=float2(idx&1,(idx>>1)&1)*2-1; q*=2;
                o.pos=q;
                o.vertex=centerC;
                o.vertex.xy += (q.x*view.axis1 + q.y*view.axis2)*2/ _ScreenParams.xy * centerC.w;
                return o;
            }

            /* ─────── Fragment ─────── */
            half4 frag(v2f i):SV_Target
            {
                /* 0. 原始高斯 α */
                half alphaBase = exp(-dot(i.pos,i.pos));

                /* 1. 圆洞渐隐 or 硬剪 */
                float d = distance(i.worldPos, _CutCenterR.xyz);
                float r = _CutCenterR.w;
            #ifdef FADE_ALPHA
                float w = saturate(1 - d / r);     // 1→中心 0→边缘
            #ifdef DITHER_EDGE
                float noise  = Hash12(i.worldPos.xz*37.13);   // 0~1
                w = saturate(w + (noise-0.5)*0.4);            // 羽化 ±0.1
            #endif
                float alphaHole = lerp(1.0, _CutMinAlpha, w*w); // 二次衰减
            #else
                if(d<r) discard;
                float alphaHole = 1.0;
            #endif

                /* 2. 深度渐隐（离相机越近越透） */
            #ifdef DEPTH_FADE
                float camD = distance(_WorldSpaceCameraPos, i.worldPos);
                float atten = saturate((camD - _FadeNear) / (_FadeFar - _FadeNear));
                alphaHole *= atten;
            #endif

                float finalA = alphaBase * alphaHole;
                if(finalA < 1.0/255.0) discard;

                half3 rgb = i.col.rgb * finalA;

            #ifdef EDGE_BLOOM
                // 在洞边缘输出 HDR >1 高亮，供 Bloom
                float edge = saturate((d - (r*0.9)) / (r*0.1)); // 0 中心 → 1 边缘
                float glow = (1-edge) * 2.5;                    // 提升强度
                rgb += glow;
            #endif

                return half4(rgb, finalA);
            }
            ENDCG
        }
    }
}
