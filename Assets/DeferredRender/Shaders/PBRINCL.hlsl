#ifndef PBR_INCLUDE
#define PBR_INCLUDE

#define PI 3.14159265359

// D
float Trowbridge_Reita_GGX(float NdotH, float a)
{
    float a2 = a * a;
    float NdotH2 = NdotH * NdotH;

    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return nom / denom;
}


// F
float3 SchlickFresnel(float HdotV, float3 F0)
{
    float m = clamp(1 - HdotV, 0, 1);
    float m2 = m * m;
    float m5 = m2 * m2 * m;
    return F0 + (1.0 - F0) * m5;
}


// G
float SchlickGGX(float NdotV, float k)
{
    float nom = NdotV;
    float denom = NdotV * (1.0 - k) + k;
    
    return nom / denom;
}


// 直接光照 PBR
float3 DirectPBR(float3 N, float3 V, float3 L, float3 albedo, float3 radiance, float roughness, float metallic)
{
    roughness = max(roughness, 0.05); // 保证 光滑物体也有高光
    
    float3 H = normalize(L + V);
    float NdotL = max(dot(N, L), 0);
    float NdotV = max(dot(N, V), 0);
    float NdotH = max(dot(N, H), 0);
    float HdotV = max(dot(H, V), 0);
    float alpha = roughness * roughness;
    float k = ((alpha + 1) * (alpha + 1)) / 8.0;
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    
    float D = Trowbridge_Reita_GGX(NdotH, alpha);
    float3 F = SchlickFresnel(HdotV, F0);
    float G = SchlickGGX(NdotV, k) * SchlickGGX(NdotL, k);

    float3 k_s = F;
    float3 k_d = (1.0 - k_s) * (1.0 - metallic);
    float3 f_diffuse = albedo / PI;
    float3 f_specular = (D * F * G) / (4.0 * NdotV * NdotL + 0.0001);
    
    f_diffuse *= PI;
    f_specular *= PI;
    
    float3 color = (k_d * f_diffuse + f_specular) * radiance * NdotL;
    return color;
}

#endif