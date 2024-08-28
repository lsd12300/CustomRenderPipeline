#ifndef CLUSTER_BASE
#define CLUSTER_BASE

#define MAX_CLUSTER_LIGHT_COUNT 32
#define ZbinMaxLenth 32
#define TileMaxLenthX 32
#define TileMaxLenthY 32

float _ClusterLightCount; // 当前场景内灯光数量
float _ClusterPerTileSizeX;
float _ClusterPerTileSizeY;
float _ClusterPerZbinSize;

half4 _LightColors[MAX_CLUSTER_LIGHT_COUNT];
float _ClusterTiles[TileMaxLenthX * TileMaxLenthY];
float _ClusterZbins[ZbinMaxLenth];


// 光源结构
struct LightInfo
{
    float4 color;
    float3 pos; // 坐标
    float range; // 点光源范围半径. 0--表示方向光
};
StructuredBuffer<LightInfo> _LightsBuffer; // 光源数据
StructuredBuffer<uint> _TileLightsBuffer; // Tile数据
StructuredBuffer<uint> _ZbinLightsBuffer; // Zbin数据




// Zbin段 索引
int GetZbinIndex(float viewZ)
{
    return floor((viewZ - _ProjectionParams.y) / _ClusterPerZbinSize);
}

// 屏幕空间坐标 获取 Tile索引
int GetTileIndex(float2 screenPos)
{
    return floor(screenPos.x / _ClusterPerTileSizeX) + floor(screenPos.y / _ClusterPerTileSizeY) * TileMaxLenthX;
}


half4 GetLightColor(float2 screenPos, float viewZ)
{
    float zbinIndex = GetZbinIndex(viewZ);
    float tileIndex = GetTileIndex(screenPos);
    uint lightIndexs = (uint) (_ClusterZbins[zbinIndex]) & (uint)(_ClusterTiles[tileIndex]); // 位操作.  取出共同的光源索引
    
    half4 clr = 0;
    for (int i = 0; i < _ClusterLightCount; i++)
    {
        clr = clr + _LightColors[i] * (lightIndexs & (1u << i));
    }
    
    return clr;
}


// ComputeShader
half4 GetLightColorCS(float2 screenPos, float viewZ, float3 posWS)
{
    float zbinIndex = GetZbinIndex(viewZ);
    float tileIndex = GetTileIndex(screenPos);
    uint lightIndexs = _ZbinLightsBuffer[zbinIndex] & _TileLightsBuffer[tileIndex]; // 位操作.  取出共同的光源索引
    
    half4 clr = 0;
    for (int i = 0; i < _ClusterLightCount; i++)
    {
        half lightType = step(_LightsBuffer[i].range, 1); // range = 0 表示方向光.
        
        // 点光源计算距离衰减.  atten = (1 - (d^2/r^2)^2)^2;  d--表示物体到光源的距离, r--表示光源半径
        float dis = length(_LightsBuffer[i].pos - posWS);
        float d2 = dis * dis;
        float r2 = _LightsBuffer[i].range * _LightsBuffer[i].range;
        float attenBase = saturate(1 - (d2 / r2) * (d2 / r2));
        float atten = attenBase * attenBase;
        half4 lightClr = _LightsBuffer[i].color * lightType + _LightsBuffer[i].color * (1 - lightType) * atten;
        
        clr = clr + lightClr * (lightIndexs & (1u << i));
    }
    
    return clr;
}


#endif // CLUSTER_BASE