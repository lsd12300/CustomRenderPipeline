
/*
    最终着色
*/
Shader "Custom/Deferred/DeferredLight"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="UniversalForward" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PBRINCL.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 screenUV : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

            TEXTURE2D(_GDepth);
			SAMPLER(sampler_GDepth);
            TEXTURE2D(_GBuffer0);
			SAMPLER(sampler_GBuffer0);
            TEXTURE2D(_GBuffer1);
			SAMPLER(sampler_GBuffer1);
            TEXTURE2D(_GBuffer2);
			SAMPLER(sampler_GBuffer2);
            TEXTURE2D(_GBuffer3);
			SAMPLER(sampler_GBuffer3);
			
			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
			CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.screenUV = ComputeScreenPos(o.vertex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float4 GB2 = SAMPLE_TEXTURE2D(_GBuffer2, sampler_GBuffer2, i.uv);
                float4 GB3 = SAMPLE_TEXTURE2D(_GBuffer3, sampler_GBuffer3, i.uv);

                // 读取 GBuffer 数据
                float4 albedo = SAMPLE_TEXTURE2D(_GBuffer0, sampler_GBuffer0, i.uv);
                float3 normal = SAMPLE_TEXTURE2D(_GBuffer1, sampler_GBuffer1, i.uv).rgb * 2 - 1;
                float2 motionVec = GB2.rg;
                float roughness = GB2.b;
                float metallic = GB2.a;
                float3 emission = GB3.rgb;
                float ao = GB3.a;

                // 反推 世界坐标
                float2 screenUV = i.screenUV.xy / i.screenUV.w;
                float depth = SAMPLE_DEPTH_TEXTURE(_GDepth, sampler_GDepth, screenUV);
                float3 worldPos = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);


                Light light = GetMainLight(); // 获取主光源.   GetAdditionalLight--副光源
                float3 N = normalize(normal);
                float3 L = normalize(light.direction);
                float3 V = normalize(GetCameraPositionWS() - worldPos.xyz);
                float3 radiance = light.color;

                // 光照
                float3 col = DirectPBR(N, V, L, albedo, radiance, roughness, metallic);
                // float3 col = albedo.rgb;
                col += emission;

                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
