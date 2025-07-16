// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent " }

        Pass
        {
            ZWrite Off



            Blend OneMinusDstAlpha One


            Cull Off
            
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#pragma target 5.0


#include "UnityCG.cginc"
#include "GaussianSplatting.hlsl"

// ===== ★ 新增：裁剪线段数据 =====
StructuredBuffer<float4> _ClipStart;  // xyz 起点, w 半径
StructuredBuffer<float4> _ClipEnd;    // xyz 终点, w 半径
uint                     _ClipCount;  // 条数

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

        if (length(P - proj) < r)
            return true;
    }
    return false;
}

StructuredBuffer<uint> _OrderBuffer;

// === X-Ray groups ===
StructuredBuffer<uint>  _GroupId;       // 每个 splat 的组号
float                   _GroupAlpha[32]; // 先写死 32 组足够调试


struct v2f
{
    half4 col : COLOR0;
    float2 pos : TEXCOORD0;
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

	// ---------- X-Ray: 取该 splat 的分组透明度 ----------
	uint gid          = _GroupId[instID];          // 0,1,2…
	float alphaFactor = _GroupAlpha[gid];          // 组对应 α


	SplatViewData view = _SplatViewData[instID];

	// 先默认 worldPos = mul(ObjectToWorld, splat.pos)
	SplatData splat = LoadSplatData(instID);
	float3 centerWorldPos = mul(unity_ObjectToWorld, float4(splat.pos, 1)).xyz;

	// Quest 分支仍可覆盖 clipPos，但 worldPos 已可用
	float4 centerClipPos = view.pos;
	if (_OptimizeForQuest) {
		centerClipPos = mul(UNITY_MATRIX_VP, float4(centerWorldPos, 1));
	}

	bool behindCam = centerClipPos.w <= 0;
	if (behindCam)
	{
		o.vertex = asfloat(0x7fc00000); // NaN discards the primitive
	}
	else
	{

		// ===== ★ 新增：裁剪判定 =====
		if (InAnyClipRegion(centerWorldPos))
		{
			// 方法 A：完全丢弃
			o.vertex = asfloat(0x7fc00000);   // NaN → primitive discard
			return o;
			// 如果你想留着混合，可改为：
			// o.col.a = 0;
		}

		o.col.r = f16tof32(view.color.x >> 16);
		o.col.g = f16tof32(view.color.x);
		o.col.b = f16tof32(view.color.y >> 16);
		o.col.a = f16tof32(view.color.y);
		o.col.a *= alphaFactor;                        // 应用透明度表


		uint idx = vtxID;
		float2 quadPos = float2(idx&1, (idx>>1)&1) * 2.0 - 1.0;
		quadPos *= 2;

		o.pos = quadPos;

		float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
		o.vertex = centerClipPos;
		o.vertex.xy += deltaScreenPos * centerClipPos.w;

		// is this splat selected?
		if (_SplatBitsValid)
		{
			uint wordIdx = instID / 32;
			uint bitIdx = instID & 31;
			uint selVal = _SplatSelectedBits.Load(wordIdx * 4);
			if (selVal & (1 << bitIdx))
			{
				o.col.a = -1;				
			}
		}
	}
    return o;
}

half4 frag (v2f i) : SV_Target
{
	float power = -dot(i.pos, i.pos);
	half alpha = exp(power);
	if (i.col.a >= 0)
	{
		alpha = saturate(alpha * i.col.a);
	}
	else
	{
		// "selected" splat: magenta outline, increase opacity, magenta tint
		half3 selectedColor = half3(1,0,1);
		if (alpha > 7.0/255.0)
		{
			if (alpha < 10.0/255.0)
			{
				alpha = 1;
				i.col.rgb = selectedColor;
			}
			alpha = saturate(alpha + 0.3);
		}
		i.col.rgb = lerp(i.col.rgb, selectedColor, 0.5);
	}
	
    if (alpha < 1.0/255.0)
        discard;

    half4 res = half4(i.col.rgb * alpha, alpha);
    return res;
}

ENDCG
        }
    }
}
