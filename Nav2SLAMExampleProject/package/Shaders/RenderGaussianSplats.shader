// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats Minimal"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One     // 与旧版相同
            Cull  Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   5.0
            #pragma require  compute
            #pragma use_dxc

            #include "UnityCG.cginc"
            #include "GaussianSplatting.hlsl"

            /* ---------- 仅保留“线段裁剪 + 圆洞”所需 uniform ---------- */
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

            /* ---- 洞口参数：中心 xyz + 半径 w / 最小 α ---- */
            float4 _CutCenterR;   // xyz=center, w=radius
            float  _CutMinAlpha;  // 0 完全透 – 1 不透

            /* ---- 点云相关 buffer，与原版保持一致 ---- */
            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<uint> _GroupId;
            float                  _GroupAlpha[32];
            StructuredBuffer<SplatViewData> _SplatViewData;
            ByteAddressBuffer      _SplatSelectedBits;
            uint _SplatBitsValid, _OptimizeForQuest;

            struct v2f
            {
                half4  col      : COLOR0;
                float2 pos      : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 vertex   : SV_POSITION;
            };

            /* ---------- Vertex ---------- */
            v2f vert(uint vtxID:SV_VertexID,uint instID:SV_InstanceID)
            {
                v2f o=(v2f)0;
                instID=_OrderBuffer[instID];

                float alphaFactor=_GroupAlpha[_GroupId[instID]];

                // world pos
                SplatData splat=LoadSplatData(instID);
                float3 centerW = mul(unity_ObjectToWorld,float4(splat.pos,1)).xyz;
                o.worldPos = centerW;

                // clip pos
                SplatViewData view=_SplatViewData[instID];
                float4 centerC = _OptimizeForQuest?mul(UNITY_MATRIX_VP,float4(centerW,1)):view.pos;
                if(centerC.w<=0){o.vertex=asfloat(0x7fc00000);return o;}

                // 胶囊裁剪
                if(InAnyClipRegion(centerW)){o.vertex=asfloat(0x7fc00000);return o;}

                // base color + α
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

            /* ---------- Fragment ---------- */
            half4 frag(v2f i):SV_Target
            {
                /* 0. 原始高斯 α */
                half alphaBase = exp(-dot(i.pos,i.pos));

                /* 1. 洞口裁剪：在圆内直接乘 _CutMinAlpha */
                float d = distance(i.worldPos, _CutCenterR.xyz);
                float r = _CutCenterR.w;
                float alphaHole = (d < r) ? _CutMinAlpha : 1.0;

                /* 2. 组合最终 α */
                float finalA = alphaBase * alphaHole;
                if(finalA < 1.0/255.0) discard;

                half3 rgb = i.col.rgb * finalA;
                return half4(rgb, finalA);
            }
            ENDCG
        }
    }
}
