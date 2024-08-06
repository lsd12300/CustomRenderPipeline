
/*
    渲染物体到各个 GBuffer
*/
Shader "Custom/Deferred/DeferredGBuffer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Space(20)]

        _Metallic ("Metallic", Range(0, 1)) = 0.5
        _Roughness ("Roughness", Range(0, 1)) = 0.5
        [Toggle] _Use_Metal_Map ("Use Metal Map", Float) = 1
        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        [Space(25)]

        _EmissionMap ("Emission Map", 2D) = "black" {}
        [Space(25)]

        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        [Space(25)]

        [Toggle] _Use_Normal_Map ("Use Normal Map", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="GBuffer" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;
            };

            TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

            TEXTURE2D(_MetallicGlossMap);
			SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);
			SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_OcclusionMap);
			SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_BumpMap);
			SAMPLER(sampler_BumpMap);

			
			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
                float _Metallic;
                float _Roughness;
                float _Use_Metal_Map;
                float _Use_Normal_Map;
			CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = TransformObjectToWorldNormal(v.normal);
                return o;
            }

            void frag (
                v2f i,
                out float4 GT0 : SV_Target0,
                out float4 GT1 : SV_Target1,
                out float4 GT2 : SV_Target2,
                out float4 GT3 : SV_Target3
            )
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb;
                half3 normal = i.normal; // TODO: 读取 _BumpMap
                float ao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).g;

                float metallic = _Metallic;
                float roughness = _Roughness;

                if (_Use_Metal_Map)
                {
                    float4 metal = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                    metallic = metal.b;
                    roughness = metal.g;
                }

                // if (_Use_Normal_Map) normal = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));

                GT0 = col;
                GT1 = float4(normal * 0.5 + 0.5, 0);
                GT2 = float4(0,0,roughness,metallic);
                GT3 = float4(emission,ao);
            }
            ENDHLSL
        }
    }
}
