#ifndef CLUSTER_CS_BASE
#define CLUSTER_CS_BASE

/*
    Cluster ComputeShader 公共库
*/


// 平面
struct Plane
{
    float3 normal; // 法线
    float dis2Origin; // 沿法线方向与原点的距离
};

// 视椎体
struct Frustum
{
    Plane top, down, left, right, forward, back; // 视椎体6个面.  上下左右前后
};


int m_screenWidth;
int m_screenHeight;
float4x4 m_projInvMt; // 相机投影矩阵的 逆矩阵
float m_camProjMtM22; // 相机投影矩阵 元素值
float m_camProjMtM23;



//  裁剪空间 到 视野空间
float4 ClipToViewSpace(float4 clipPos)
{
    float4 viewPos = mul(m_projInvMt, clipPos);
    viewPos /= viewPos.w; // 透视投影
    return viewPos;
}


//  屏幕空间 到 视野空间
float4 ScreenToViewSpace(float4 screenPos)
{
    float2 coord = float2(screenPos.x / m_screenWidth, screenPos.y / m_screenHeight); // 屏幕UV
    float2 coordNDC = coord * 2 - 1; // NDC空间.  范围 [-1, 1]
    //float clipZ = screenPos.z * m_camProjMtM22 + m_camProjMtM23;
    //float4 clipPos = float4(coordNDC.x, coordNDC.y, clipZ, -screenPos.z);
    
    // z=1为远裁剪面,  z=0为近裁剪面
    float4 clipPos = float4(coordNDC, 0, 1);

    return ClipToViewSpace(clipPos);
}


//  直线和Z平面的交点.
//     用于计算视椎体远裁剪面上顶点
float3 LineIntersectZPlane(float3 a, float3 b, float z)
{
    float3 normal = float3(0, 0, 1); // Z 平面法线
    float3 ab = b - a;

    // Dot 为投影.
    float t = (z - dot(normal, a)) / dot(normal, ab);

    return a + t * ab;
}


//  平面内三个点构建平面
Plane CreatePlane(float3 p1, float3 p2, float3 p3)
{
    float3 normal = cross(p2 - p1, p3 - p2);
    normal = normalize(normal);

    Plane p;
    p.normal = normal;
    p.dis2Origin = -dot(normal, p1);
    return p;
}



#endif // CLUSTER_CS_BASE