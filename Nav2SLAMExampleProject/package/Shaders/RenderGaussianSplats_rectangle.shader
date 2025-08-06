// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats Rectangle"
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

            /* ---------- 仅保留：盒体（OBB）早剔除 ---------- */
            StructuredBuffer<float4> _BoxCenterHalf; // xyz=center, w=halfDepth
            StructuredBuffer<float4> _BoxAxisR;      // xyz=axisR (unit)
            StructuredBuffer<float4> _BoxAxisU;      // xyz=axisU (unit)
            StructuredBuffer<float4> _BoxAxisN;      // xyz=axisN (unit)
            StructuredBuffer<float4> _BoxHalfRU;     // x=halfWidth, y=halfHeight
            uint                     _BoxCount;

            inline bool InAnyBoxRegion(float3 P)
            {
                [loop] for(uint i=0;i<_BoxCount;i++)
                {
                    float3 C = _BoxCenterHalf[i].xyz;
                    float  hN= _BoxCenterHalf[i].w;
                    float2 hRU=float2(_BoxHalfRU[i].x, _BoxHalfRU[i].y);
                    float3 R = _BoxAxisR[i].xyz;
                    float3 U = _BoxAxisU[i].xyz;
                    float3 N = _BoxAxisN[i].xyz;

                    float3 v = P - C;
                    float  pr = dot(v, R);
                    float  pu = dot(v, U);
                    float  pn = dot(v, N);

                    if (abs(pr) <= hRU.x && abs(pu) <= hRU.y && abs(pn) <= hN)
                        return true;
                }
                return false;
            }

            /* ---------- 像素级矩形遮罩（世界空间参数） ---------- */
            float3 _CutCenter;      // 中心（在墙面上）
            float3 _CutAxisR;       // 右轴（单位向量，墙面内）
            float3 _CutAxisU;       // 上轴（单位向量，墙面内）
            float3 _CutAxisN;       // 法线（单位向量，垂直墙面）
            float2 _CutRectHalf;    // 半宽/半高（m）
            float  _CutMinAlpha;    // 洞内最小 α（0=全透）

            /* ---------- 点云相关（原样） ---------- */
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
                float3 worldPos : TEXCOORD1;   // splat 中心（WS）
                float4 vertex   : SV_POSITION; // clip-space
            };

            v2f vert(uint vtxID:SV_VertexID,uint instID:SV_InstanceID)
            {
                v2f o=(v2f)0;
                instID=_OrderBuffer[instID];

                float alphaFactor=_GroupAlpha[_GroupId[instID]];

                // world pos
                SplatData splat=LoadSplatData(instID);
                float3 centerW = mul(unity_ObjectToWorld,float4(splat.pos,1)).xyz;
                o.worldPos = centerW;

                // OBB 早剔除：盒内的 splat 直接丢弃（让“长方体”硬起来）
                if(InAnyBoxRegion(centerW)){o.vertex=asfloat(0x7fc00000);return o;}

                // clip pos
                SplatViewData view=_SplatViewData[instID];
                float4 centerC = _OptimizeForQuest?mul(UNITY_MATRIX_VP,float4(centerW,1)):view.pos;
                if(centerC.w<=0){o.vertex=asfloat(0x7fc00000);return o;}

                // color + α
                o.col.r=f16tof32(view.color.x>>16);
                o.col.g=f16tof32(view.color.x);
                o.col.b=f16tof32(view.color.y>>16);
                o.col.a=f16tof32(view.color.y)*alphaFactor;

                // 生成屏幕四边形
                uint idx=vtxID;
                float2 q=float2(idx&1,(idx>>1)&1)*2-1; q*=2;
                o.pos=q;
                o.vertex=centerC;
                o.vertex.xy += (q.x*view.axis1 + q.y*view.axis2)*2/_ScreenParams.xy * centerC.w;
                return o;
            }

            /* ---------- Fragment：像素级矩形判定 ---------- */
            half4 frag(v2f i):SV_Target
            {
                // 0) 高斯 α
                half alphaBase = exp(-dot(i.pos,i.pos));

                // 1) 相机→像素的世界射线，与墙平面求交
                float2 ndc = i.vertex.xy / i.vertex.w;                 // [-1,1]
                float4 viewFar = mul(unity_CameraInvProjection, float4(ndc, 1, 1));
                viewFar /= max(viewFar.w, 1e-8);
                float3 dirVS  = normalize(viewFar.xyz);
                float3 dirWS  = normalize(mul((float3x3)UNITY_MATRIX_I_V, dirVS));
                float3 camWS  = _WorldSpaceCameraPos;

                float alphaHole = 1.0;
                float denom = dot(dirWS, _CutAxisN);
                if (abs(denom) > 1e-6)
                {
                    float t = dot(_CutCenter - camWS, _CutAxisN) / denom;
                    if (t > 0.0)
                    {
                        float3 x = camWS + t * dirWS; // 交点（WS）
                        float2 p = float2(dot(x - _CutCenter, _CutAxisR),
                                          dot(x - _CutCenter, _CutAxisU));
                        float2 d = abs(p) - _CutRectHalf;    // d<0 → inside
                        float  sd = max(d.x, d.y);
                        if (sd < 0.0) alphaHole = _CutMinAlpha;
                    }
                }

                // 2) 合成 α
                float finalA = alphaBase * alphaHole;
                if(finalA < 1.0/255.0) discard;

                half3 rgb = i.col.rgb * finalA;
                return half4(rgb, finalA);
            }
            ENDCG
        }
    }
}
