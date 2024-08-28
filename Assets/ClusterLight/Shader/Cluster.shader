Shader "Custom/ClusterLight/Cluster"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="UniversalForward" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ClusterBase.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float viewZ : TEXCOORD2;
                float3 posWS : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);
			
			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
			CBUFFER_END


            Varyings vert (Attributes input)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(input.vertex.xyz);
                OUT.uv = TRANSFORM_TEX(input.uv, _MainTex);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                float3 positionWS = TransformObjectToWorld(input.vertex.xyz);
                OUT.posWS = positionWS;
                OUT.viewZ.x = TransformWorldToView(positionWS).z;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // half4 lightCol = GetLightColor(IN.screenPos.xy / IN.screenPos.w * _ScreenParams.xy, -IN.viewZ);
                half4 lightCol = GetLightColorCS(IN.screenPos.xy / IN.screenPos.w * _ScreenParams.xy, -IN.viewZ.x, IN.posWS);

                return lightCol;
            }
            ENDHLSL
        }
    }
}
