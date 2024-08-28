using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;


namespace ClusterLight
{
    /// <summary>
    ///  将视野视椎体 分簇成一个个小的视椎体, 记录小视椎体内接收到的光源. Shader里根据坐标换算出分簇信息,  仅计算分簇内的光照着色
    ///  
    ///     优化:
    ///         ZBin (减少记录的数据, 内存占用小很多):
    ///             1. 按 Z值排序光源.  (不排序的话  ZBin段也需记录光源索引)
    ///             2. 在XY平面划分Tile, 并记录每个Tile内(不考虑Z值, 即整个视椎体)接收到的光源索引
    ///             3. Z方向划分段(即ZBin), 并记录每段内光源的最大最小索引
    ///             4. Shader里 从屏幕空间坐标换算成Tile坐标, 根据深度值Z 换算成ZBin段.  从而计算出当前点需要的光源
    /// </summary>
    public class ClusterLight
    {
        /// <summary>
        ///  Tile的视椎体
        ///     需八个坐标点
        /// </summary>
        public class TileFrustum
        {
            public Vector3[] m_points; // 八个点顺序:  前平面右上角顺时针, 后平面右上角顺时针

            public Plane[] m_planes; // 上下左右前后 六个面


            /// <summary>
            ///  视椎体和球体相交
            ///     球心到平面的距离
            ///         绝对值 如果小于半径,  则平面和球体相交
            ///         大于0, 在平面正面一侧(可能位于视椎体内部,  需要检查6个面).
            ///         小于0, 在平面背面一侧 且 距离超过球体半径时,  此时肯定不会和视椎体有交集
            ///         
            ///         公式
            ///             C--球心坐标 Vector3
            ///             N--平面法线 Vector3
            ///             D--平面沿法线与原点的距离 float
            ///             
            ///             球心到平面的距离 = Dot(C, N) + D
            ///                 解析:  Dot(C, N) 表示球心投影到平面法线上, 即 球心沿平面法线到原点的距离
            ///     
            /// </summary>
            /// <param name="center">球心坐标</param>
            /// <param name="radius">球体半径</param>
            public bool IntersectSphere(Vector3 center, float radius, bool log = false)
            {
                foreach (Plane p in m_planes)
                {
                    // 球心到平面的距离
                    var sphere2PlaneDis = Vector3.Dot(p.Normal, center) + p.D2Origin;
                    if (log)
                    {
                        Debug.Log($"{p.Normal}, {p.D2Origin},  {center}, {radius}, {sphere2PlaneDis}");
                    }


                    // 在平面背面一侧 且距离超过球体半径,  则肯定不会相交
                    if (sphere2PlaneDis < -radius) return false;


                    // 平面与球体相交
                    //if (Mathf.Abs(sphere2PlaneDis) < radius) return true;
                }

                // 球体未与视椎体平面相交,  也未在视椎体平面外部,  则表示球体位于视椎体内部
                return true;
            }


            public void InitPlanes()
            {
                if (m_points == null) return;

                m_planes = new Plane[6];

                m_planes[0] = Plane.Create(m_points[3], m_points[0], m_points[4]); // 上
                m_planes[1] = Plane.Create(m_points[1], m_points[2], m_points[5]); // 下
                m_planes[2] = Plane.Create(m_points[2], m_points[3], m_points[6]); // 左
                m_planes[3] = Plane.Create(m_points[0], m_points[1], m_points[4]); // 右
                m_planes[4] = Plane.Create(m_points[2], m_points[1], m_points[0]); // 前
                m_planes[5] = Plane.Create(m_points[4], m_points[5], m_points[6]); // 后
            }
        }

        /// <summary>
        ///  平面
        /// </summary>
        public class Plane
        {
            public Vector3 Normal; // 单位法线向量
            public float D2Origin; // 平面沿法线方向到原点的距离


            /// <summary>
            ///  从平面一般方程构建
            /// </summary>
            public static Plane Create(float A, float B, float C, float D)
            {
                var k = 1.0f / Mathf.Sqrt(A * A + B * B + C * C);
                var p = new Plane();
                p.Normal = new Vector3(A * k, B * k, C * k);
                p.D2Origin = D * k;
                return p;
            }

            /// <summary>
            ///  平面内三个点构建平面
            /// </summary>
            public static Plane Create(Vector3 p1, Vector3 p2, Vector3 p3)
            {
                //var normal = Vector3.Cross(p1-p2, p1-p3);
                var normal = Vector3.Cross(p2 - p1, p3 - p2);
                normal.Normalize();

                var p = new Plane();
                p.Normal = normal;
                p.D2Origin = -Vector3.Dot(normal, p1);
                return p;
            }
        }


        public const int MaxTileCountX = 32; // 当前方向的最大数量.  每个Tile的尺寸 根据屏幕尺寸 除以最大数量计算
        public const int MaxTileCountY = 32;
        public const int MaxZbinCount = 32;
        public const int MaxLightCount = 32;


        public int m_tileSizeX; // Tile分块尺寸
        public int m_tileSizeY;
        public int m_tileSizeZ; // ZBin分段长度

        private uint[] m_tiles; // 每个Tile用32-bit记录光源,  所以最大仅支持32个光源 (可换成其他数据增加上限)
        private uint[] m_zbins; // 每个zbin段使用32-bit记录光源索引, 最大支持32个光源
        public TileFrustum[] m_tilesFrustum; // Tile分块的视椎体数据

        private int m_visibleLightCount;
        private Vector4[] m_visibleLightColors = new Vector4[MaxLightCount]; // 光源颜色索引

        private Camera m_cam;
        private Transform m_camTran;
        private Matrix4x4 m_projMtInv; // 投影矩阵的逆矩阵
        private Vector3 m_camLocalPos;
        private Quaternion m_camLocalRotation;
        private Vector3 m_camLocalScale;
        private float m_camFov;



        public ClusterLight(Camera cam)
        {
            m_cam = cam;
            m_camTran = cam.transform;
            m_projMtInv = m_cam.projectionMatrix.inverse;

            m_tileSizeX = CountPerSize(MaxTileCountX, m_cam.pixelWidth);
            m_tileSizeY = CountPerSize(MaxTileCountY, m_cam.pixelHeight);
            m_tileSizeZ = CountPerSize(MaxZbinCount, m_cam.farClipPlane - m_cam.nearClipPlane);

            m_tiles = new uint[MaxTileCountX * MaxTileCountY];
            m_zbins = new uint[MaxZbinCount];
            m_tilesFrustum = new TileFrustum[MaxTileCountX * MaxTileCountY];
        }


        /// <summary>
        ///  相机属性变化, 重新计算  Tile和ZBin 数据
        /// </summary>
        /// <param name="lights"></param>
        public void UpdateTileZBin()
        {
            //var viewZ = m_cam.nearClipPlane;
            //var viewZ = m_cam.farClipPlane;
            var viewZ = 1;

            // 计算 Tile视椎体数据
            for (int v = 0; v < MaxTileCountY; v++)
            {
                for (int h = 0; h < MaxTileCountX; h++)
                {
                    // Tile分块的 屏幕坐标
                    var screenTileCorner0 = new Vector3(h * m_tileSizeX + m_tileSizeX, v * m_tileSizeY + m_tileSizeY, viewZ); // 右上
                    var screenTileCorner1 = new Vector3(h * m_tileSizeX + m_tileSizeX, v * m_tileSizeY, viewZ); // 右下
                    var screenTileCorner2 = new Vector3(h * m_tileSizeX, v * m_tileSizeY, viewZ); // 左下
                    var screenTileCorner3 = new Vector3(h * m_tileSizeX, v * m_tileSizeY + m_tileSizeY, viewZ); // 左上

                    // Tile分块的 相机空间坐标.  当ViewZ 取近裁剪面时  就是近裁剪上的Tile坐标
                    var tileCornerVS0 = ScreenToViewSpace(screenTileCorner0);
                    var tileCornerVS1 = ScreenToViewSpace(screenTileCorner1);
                    var tileCornerVS2 = ScreenToViewSpace(screenTileCorner2);
                    var tileCornerVS3 = ScreenToViewSpace(screenTileCorner3);

                    // Tile分块 相机空间 远裁剪面上的坐标
                    var tileCornerFarVS0 = LineIntersectZPlane(Vector3.zero, tileCornerVS0, m_cam.farClipPlane);
                    var tileCornerFarVS1 = LineIntersectZPlane(Vector3.zero, tileCornerVS1, m_cam.farClipPlane);
                    var tileCornerFarVS2 = LineIntersectZPlane(Vector3.zero, tileCornerVS2, m_cam.farClipPlane);
                    var tileCornerFarVS3 = LineIntersectZPlane(Vector3.zero, tileCornerVS3, m_cam.farClipPlane);

                    // Tile分块 相机空间 近裁剪面上的坐标
                    var tileCornerNearVS0 = LineIntersectZPlane(Vector3.zero, tileCornerVS0, m_cam.nearClipPlane);
                    var tileCornerNearVS1 = LineIntersectZPlane(Vector3.zero, tileCornerVS1, m_cam.nearClipPlane);
                    var tileCornerNearVS2 = LineIntersectZPlane(Vector3.zero, tileCornerVS2, m_cam.nearClipPlane);
                    var tileCornerNearVS3 = LineIntersectZPlane(Vector3.zero, tileCornerVS3, m_cam.nearClipPlane);

                    // Tile分块的 视椎体
                    var frustum = new TileFrustum();
                    frustum.m_points = new Vector3[8];
                    frustum.m_points[0] = tileCornerNearVS0;
                    frustum.m_points[1] = tileCornerNearVS1;
                    frustum.m_points[2] = tileCornerNearVS2;
                    frustum.m_points[3] = tileCornerNearVS3;

                    frustum.m_points[4] = tileCornerFarVS0;
                    frustum.m_points[5] = tileCornerFarVS1;
                    frustum.m_points[6] = tileCornerFarVS2;
                    frustum.m_points[7] = tileCornerFarVS3;

                    frustum.InitPlanes();

                    m_tilesFrustum[v * MaxTileCountX + h] = frustum;

                    //if ((v == 0 && h == 0) || (v == 1 && h == 0))
                    //{
                    //    Debug.Log($"{tileCornerFarVS0};;{tileCornerFarVS1};;{tileCornerFarVS2};;{tileCornerFarVS3}");
                    //    Debug.Log($"{tileCornerNearVS0};;{tileCornerNearVS1};;{tileCornerNearVS2};;{tileCornerNearVS3}");
                    //    Debug.Log($"{tileCornerVS0};;{tileCornerVS1};;{tileCornerVS2};;{tileCornerVS3}");
                    //}
                }
            }
        }

        /// <summary>
        ///  计算 Tile和ZBin内的光源索引
        /// </summary>
        /// <param name="lights"></param>
        public void UpdateLight(NativeArray<VisibleLight> lights)
        {
            Clear();

            // 相机属性变化.  重新划分 Tile和Zbin
            if (CheckCameraChange())
            {
                UpdateTileZBin();
            }


            m_visibleLightCount = lights.Length;
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                m_visibleLightColors[i] = light.finalColor;

                if (light.lightType == LightType.Directional) // 平行光.  直接标记接收
                {
                    SetAllTileLight(i);
                    SetAllZbinLight(i);
                    continue;
                }

                var camWorldToLocal = m_cam.transform.worldToLocalMatrix;
                var lightLocalToWorld = light.localToWorldMatrix;
                var lightPosWS = lightLocalToWorld.GetColumn(3); // 世界坐标
                                                                 //var lightDirWS = lightLocalToWorld.GetColumn(2); // 世界朝向
                var lightPosVS = camWorldToLocal * lightPosWS; // 视野空间坐标


                // Zbin 光源索引
                var zbinIndexMin = CountZBinIndex(lightPosVS.z - light.range);
                var zbinIndexMax = CountZBinIndex(lightPosVS.z + light.range);
                for (int j = zbinIndexMin; j <= zbinIndexMax; j++)
                {
                    SetZbinLight(j, i);
                }

                // Tile 光源索引
                for (var tileIndex = 0; tileIndex < m_tilesFrustum.Length; tileIndex++)
                {
                    if (m_tilesFrustum[tileIndex].IntersectSphere(lightPosVS, light.range/*, tileIndex == 32*/))
                    {
                        //Debug.Log($"SetTileLight:  {tileIndex}, {i}");
                        SetTileLight(tileIndex, i);
                    }
                }
            }
        }

        private bool CheckCameraChange()
        {
            bool change = false;
            change = m_camTran.localPosition != m_camLocalPos ||
                m_camTran.localRotation != m_camLocalRotation ||
                m_camTran.localScale != m_camLocalScale ||
                m_cam.fieldOfView != m_camFov;

            m_camLocalPos = m_camTran.localPosition;
            m_camLocalRotation = m_camTran.localRotation;
            m_camLocalScale = m_camTran.localScale;
            m_camFov = m_cam.fieldOfView;

            return change;
        }

        private void Clear()
        {
            if (m_tiles != null)
            {
                for (var i = 0; i < m_tiles.Length; i++)
                {
                    m_tiles[i] = 0;
                }
            }
            if (m_zbins != null)
            {
                for (int i = 0; i < m_zbins.Length; i++)
                {
                    m_zbins[i] = 0;
                }
            }
        }

        private void SetTileLight(int tileIndex, int lightIndex)
        {
            m_tiles[tileIndex] |= 1u << lightIndex;
        }
        private void SetTileLight(int h, int v, int lightIndex)
        {
            m_tiles[v * MaxTileCountX + h] |= 1u << lightIndex;
        }

        private void SetZbinLight(int zbinIndex, int lightIndex)
        {
            if (zbinIndex < 0 || zbinIndex >= m_zbins.Length)
            {
                return;
            }
            m_zbins[zbinIndex] |= 1u << lightIndex;
        }

        private void SetAllTileLight(int lightIndex)
        {
            for (int i = 0; i < m_tiles.Length; i++)
            {
                SetTileLight(i, lightIndex);
            }
        }

        private void SetAllZbinLight(int lightIndex)
        {
            for (int i = 0; i < m_zbins.Length; i++)
            {
                SetZbinLight(i, lightIndex);
            }
        }


        /// <summary>
        ///   给定 最大段数和总长度  计算每段最大尺寸 尽量填满所有最大段数
        /// </summary>
        /// <param name="maxCount"></param>
        /// <param name="totleSize"></param>
        /// <returns></returns>
        private int CountPerSize(int maxCount, float totleSize)
        {
            int size = 16;
            while (size * maxCount < totleSize)
            {
                size <<= 1;
            }
            return size;
        }


        /// <summary>
        ///  设置到 Shader属性
        /// </summary>
        public void SetUpShaderProps(CommandBuffer cmd)
        {
            var tileData = Array.ConvertAll<uint, float>(m_tiles, (uint val) => (float)val);
            cmd.SetGlobalFloatArray("_ClusterTiles", tileData);

            var zbinData = Array.ConvertAll<uint, float>(m_zbins, (uint val) => (float)val);
            cmd.SetGlobalFloatArray("_ClusterZbins", zbinData);

            cmd.SetGlobalFloat("_ClusterLightCount", m_visibleLightCount);
            cmd.SetGlobalVectorArray("_LightColors", m_visibleLightColors);

            cmd.SetGlobalFloat("_ClusterPerTileSizeX", m_tileSizeX);
            cmd.SetGlobalFloat("_ClusterPerTileSizeY", m_tileSizeY);
            cmd.SetGlobalFloat("_ClusterPerZbinSize", m_tileSizeZ);
        }

        public void Log()
        {
            var sb = new System.Text.StringBuilder();
            for (int v = 0; v < MaxTileCountY; v++)
            {
                for (int h = 0; h < MaxTileCountX; h++)
                {
                    sb.Append($",{m_tiles[v * MaxTileCountX + h]}");
                }
                sb.AppendLine();
            }
            Debug.Log($"{sb.ToString()}");

            sb.Length = 0;
            for (int i = 0; i < m_zbins.Length; i++)
            {
                sb.Append($",{m_zbins[i]}");
            }
            Debug.Log($"{sb.ToString()}");

            sb.Length = 0;
            for (int i = 0; i < m_visibleLightCount; i++)
            {
                sb.Append($",{m_visibleLightColors[i]}");
            }
            Debug.Log($"{sb.ToString()}");

            sb.Length = 0;
        }

        #region 工具方法

        public static int Encode2Int(int min, int max)
        {
            return max << 16 | min;
        }

        public static void DecodeFromInt(int val, out int min, out int max)
        {
            min = val & 65535; // 取低16位
            max = (val >> 16) & 65535; // 取高16位
        }


        /// <summary>
        ///  换算 ZBin段索引
        /// </summary>
        public int CountZBinIndex(float z)
        {
            return Mathf.FloorToInt((z - m_cam.nearClipPlane) / m_tileSizeZ);
        }

        public int CountTileXIndex(float x)
        {
            return Mathf.FloorToInt(x / m_tileSizeX);
        }

        public int CountTileYIndex(float y)
        {
            return Mathf.FloorToInt(y / m_tileSizeY);
        }
        public Vector2 CountTileXYIndex(float x, float y)
        {
            return new Vector2(CountTileXIndex(x), CountTileYIndex(y));
        }


        /// <summary>
        ///  直线和Z平面的交点.
        ///     用于计算视椎体远裁剪面上顶点
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public static Vector3 LineIntersectZPlane(Vector3 a, Vector3 b, float z)
        {
            var normal = Vector3.forward; // Z 平面法线
            var ab = b - a;

            // Vector3.Dot 为投影.
            float t = (z - Vector3.Dot(normal, a)) / Vector3.Dot(normal, ab);

            var point = a + t * ab;
            return point;
        }


        #region 坐标转换

        /// <summary>
        ///  裁剪空间 到 视野空间
        /// </summary>
        /// <param name="clipPos"></param>
        /// <returns></returns>
        public Vector4 ClipToViewSpace(Vector4 clipPos)
        {
            var viewPos = m_projMtInv * clipPos;
            viewPos /= viewPos.w; // 透视除法
            return viewPos;
        }

        /// <summary>
        ///  屏幕空间 到 视野空间
        /// </summary>
        /// <param name="screenPos"></param>
        /// <returns></returns>
        public Vector4 ScreenToViewSpace(Vector4 screenPos)
        {
            var coord = new Vector2(screenPos.x / m_cam.pixelWidth, screenPos.y / m_cam.pixelHeight); // 屏幕UV
            var coordNDC = coord * 2 - Vector2.one; // NDC空间.  范围 [-1, 1]
            var clipZ = screenPos.z * m_cam.projectionMatrix.m22 + m_cam.projectionMatrix.m23;
            var clipPos = new Vector4(coordNDC.x, coordNDC.y, clipZ, -screenPos.z);

            return ClipToViewSpace(clipPos);
        }
        #endregion

        #endregion
    }
}