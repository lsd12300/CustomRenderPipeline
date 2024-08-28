using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.AI;
using UnityEngine.Rendering;


namespace ClusterLight
{
    /// <summary>
    ///  使用 ComputeShader 加速 和 Zbin
    /// </summary>
    public class ClusterLightZbinPlus : ClusterBase
    {
        public static readonly Vector2Int m_CSThreadCount = new Vector2Int(8, 8); // ComputeShader 每个线程组内 线程数量.  每个线程计算一个Tile
        public const int m_MaxLightCount = 32; // 最大光源数量
        public const int m_MaxTileCountX = 32; // Tile 最大数量
        public const int m_MaxTileCountY = 32;

        private Camera m_cam;
        private ComputeShader m_clusterCs; // 视椎体计算 CS
        private int m_clusterTileKernel;

        private int m_perTileSizeX = 8; // 每个 Tile的屏幕像素尺寸.  例:  屏幕宽度 <= 横向线程组数 * 横向组内线程数 * 横向Tile像素尺寸
        private int m_perTileSizeY = 8;
        private int m_groupCountX = 8; // Dispatch 线程组数量
        private int m_groupCountY = 8;

        private ComputeBuffer m_clusterFrustumsBuffer;

        private ComputeShader m_lightCullCs; // 光源裁剪 CS
        private int m_lightCullTileKernel;
        private int m_lightCullZbinKernel;
        private int m_lightCount;
        private LightInfo[] m_lights;
        private ComputeBuffer m_clusterLightBuffer; // 光源数据
        private ComputeBuffer m_clusterTileBuffer; // Tile视椎体内光源索引
        private ComputeBuffer m_clusterZbinBuffer; // Zbin段内光源索引
        private int m_zbinCount = 32; // Zbin 数量.
        private float m_perZbinSize; // 每段Zbin长度



        public ClusterLightZbinPlus(Camera cam, ComputeShader clusterCs, ComputeShader lightCullCs)
        {
            m_cam = cam;
            m_clusterCs = clusterCs;
            m_clusterTileKernel = m_clusterCs.FindKernel("CSMain");

            m_lightCullCs = lightCullCs;
            m_lightCullTileKernel = m_lightCullCs.FindKernel("CSMain");
            m_lightCullZbinKernel = m_lightCullCs.FindKernel("CSZbinMain");


            // 划分 Tile数量
            CountPerTileSize(m_cam.pixelWidth, m_CSThreadCount.x, m_MaxTileCountX, ref m_perTileSizeX, ref m_groupCountX);
            CountPerTileSize(m_cam.pixelHeight, m_CSThreadCount.y, m_MaxTileCountY, ref m_perTileSizeY, ref m_groupCountY);

            // 一个 Frustum 6个 Plane, 一个 Plane 4个float, 一个float 4字节.  6 * 4 * 4 = 96
            var bufferCount = m_groupCountX * m_CSThreadCount.x * m_groupCountY * m_CSThreadCount.y;
            //var bufferCount = Marshal.SizeOf<Frustum>();
            m_clusterFrustumsBuffer = new ComputeBuffer(bufferCount, 96);


            m_perZbinSize = (m_cam.farClipPlane - m_cam.nearClipPlane) / m_zbinCount;
            m_lights = new LightInfo[m_MaxLightCount];
            m_clusterLightBuffer = new ComputeBuffer(m_MaxLightCount, Marshal.SizeOf<LightInfo>());
            m_clusterTileBuffer = new ComputeBuffer(bufferCount, Marshal.SizeOf<uint>()); // Tile内光源索引.  使用 bit标记
            m_clusterZbinBuffer = new ComputeBuffer(m_zbinCount, Marshal.SizeOf<uint>()); // Zbin内光源索引.  使用 bit标记
        }


        /// <summary>
        ///  调用 ComputeShader 计算
        /// </summary>
        public override void Update(NativeArray<VisibleLight> lights, CommandBuffer cmd)
        {
            // 计算Tile的视椎体
            GenerateFrustums();

            // 收集光源数据
            PrepareLights(lights);

            // 光源裁剪.  光源和视椎体求交
            LightCull();

            // 绑定到 Shader属性
            SetUpShaderParams(cmd);
        }


        /// <summary>
        ///  调用 ComputeShader 计算Tile的视椎体
        /// </summary>
        private void GenerateFrustums()
        {
            m_clusterCs.SetBuffer(m_clusterTileKernel, "m_frustums", m_clusterFrustumsBuffer);
            m_clusterCs.SetInt("m_perTileSizeX", m_perTileSizeX);
            m_clusterCs.SetInt("m_perTileSizeY", m_perTileSizeY);
            m_clusterCs.SetInt("m_groupCountX", m_groupCountX);
            m_clusterCs.SetInt("m_groupCountY", m_groupCountY);
            m_clusterCs.SetInt("m_screenWidth", m_cam.pixelWidth);
            m_clusterCs.SetInt("m_screenHeight", m_cam.pixelHeight);
            m_clusterCs.SetMatrix("m_projInvMt", m_cam.projectionMatrix.inverse);
            m_clusterCs.SetFloat("m_camProjMtM22", m_cam.projectionMatrix.m22);
            m_clusterCs.SetFloat("m_camProjMtM23", m_cam.projectionMatrix.m23);
            m_clusterCs.SetFloat("m_camNearClipPlane", m_cam.nearClipPlane);
            m_clusterCs.SetFloat("m_camFarClipPlane", m_cam.farClipPlane);

            m_clusterCs.Dispatch(m_clusterTileKernel, m_groupCountX, m_groupCountY, 1);
        }

        /// <summary>
        ///  收集光源数据
        /// </summary>
        private void PrepareLights(NativeArray<VisibleLight> lights)
        {
            m_lightCount = lights.Length;
            System.Array.Clear(m_lights, 0, m_MaxLightCount);

            for (int i = 0; i < m_lightCount; i++)
            {
                m_lights[i] = new LightInfo()
                {
                    pos = lights[i].light.transform.position,
                    color = lights[i].finalColor,
                    range = lights[i].lightType == LightType.Directional ? 0 : lights[i].range,
                };
            }
            m_clusterLightBuffer.SetData(m_lights);
        }

        /// <summary>
        ///  调用 ComputerShader 计算光源和视椎体相交数据
        /// </summary>
        private void LightCull()
        {
            // 计算 Tile
            m_lightCullCs.SetMatrix("m_world2ViewMt", m_cam.worldToCameraMatrix);
            m_lightCullCs.SetBuffer(m_lightCullTileKernel, "m_frustums", m_clusterFrustumsBuffer);
            m_lightCullCs.SetBuffer(m_lightCullTileKernel, "m_lights", m_clusterLightBuffer);
            m_lightCullCs.SetBuffer(m_lightCullTileKernel, "m_tileBuffer", m_clusterTileBuffer);
            m_lightCullCs.SetInt("m_lightCount", m_lightCount);
            m_lightCullCs.SetInt("m_groupCountX", m_groupCountX);
            m_lightCullCs.SetInt("m_groupCountY", m_groupCountY);
            m_lightCullCs.Dispatch(m_lightCullTileKernel, m_groupCountX, m_groupCountY, 1);


            // 计算 Zbin
            m_lightCullCs.SetBuffer(m_lightCullZbinKernel, "m_lights", m_clusterLightBuffer);
            m_lightCullCs.SetFloat("m_perZbinSize", m_perZbinSize);
            m_lightCullCs.SetFloat("m_camClipNear", m_cam.nearClipPlane);
            m_lightCullCs.SetBuffer(m_lightCullZbinKernel, "m_zbinBuffer", m_clusterZbinBuffer);
            m_lightCullCs.Dispatch(m_lightCullZbinKernel, m_zbinCount/8, 1, 1);

        }

        public override void SetUpShaderParams(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat("_ClusterPerTileSizeX", m_perTileSizeX);
            cmd.SetGlobalFloat("_ClusterPerTileSizeY", m_perTileSizeY);
            cmd.SetGlobalFloat("_ClusterPerZbinSize", m_perZbinSize);
            cmd.SetGlobalFloat("_ClusterLightCount", m_lightCount);
            cmd.SetGlobalBuffer("_LightsBuffer", m_clusterLightBuffer);
            cmd.SetGlobalBuffer("_TileLightsBuffer", m_clusterTileBuffer);
            cmd.SetGlobalBuffer("_ZbinLightsBuffer", m_clusterZbinBuffer);
        }


        #region 调试

        private uint[] m_debugTiles;
        private uint[] m_debugZbins;
        private LightInfo[] m_debugLights;
        private Frustum[] m_debugFrustums;
        private System.Text.StringBuilder m_debugSb = new System.Text.StringBuilder();

        public void Debug()
        {
            /*
            // Zbin
            m_debugSb.Length = 0;
            if (m_debugZbins == null) 
            {
                m_debugZbins = new uint[m_zbinCount];
            }
            m_clusterZbinBuffer.GetData(m_debugZbins);
            foreach (var zbin in m_debugZbins)
            {
                m_debugSb.Append($"{zbin},");
            }
            UnityEngine.Debug.Log(m_debugSb.ToString());


            // Tile
            m_debugSb.Length = 0;
            if (m_debugTiles == null)
            {
                m_debugTiles = new uint[m_groupCountX * m_CSThreadCount.x * m_groupCountY * m_CSThreadCount.y];
            }
            m_clusterTileBuffer.GetData(m_debugTiles);

            var hCount = m_groupCountX * m_CSThreadCount.x;
            for (int v = 0; v < m_groupCountY * m_CSThreadCount.y; v++)
            {
                for (int h = 0; h < hCount; h++)
                {
                    m_debugSb.Append(m_debugTiles[v * hCount + h]);
                }
                m_debugSb.Append('\n');
            }
            UnityEngine.Debug.Log(m_debugSb.ToString());
            */



            /*
            // Frustum
            m_debugSb.Length = 0;
            if (m_debugFrustums == null)
            {
                m_debugFrustums = new Frustum[m_groupCountX * m_CSThreadCount.x * m_groupCountY * m_CSThreadCount.y];
            }
            m_clusterFrustumsBuffer.GetData(m_debugFrustums);
            var hCount2 = m_groupCountY * m_CSThreadCount.y;
            UnityEngine.Debug.Log(FrustumToString(m_debugFrustums[hCount2 * 0 + 0]));
            */

            /*
            // Light
            m_debugSb.Length = 0;
            if(m_debugLights == null)
            {
                m_debugLights = new LightInfo[m_MaxLightCount];
            }
            m_clusterLightBuffer.GetData(m_debugLights);
            for (int i = 0; i < m_lightCount; i++)
            {
                m_debugSb.Append($"({m_cam.worldToCameraMatrix * m_debugLights[i].pos}:{m_debugLights[i].range})--");
            }
            UnityEngine.Debug.Log(m_debugSb.ToString());
            */


            /*
            for (int i = 0; i < m_lightCount; i++)
            {
                var lightPosVS = m_cam.worldToCameraMatrix * m_debugLights[i].pos;
                lightPosVS.z = -lightPosVS.z;
                UnityEngine.Debug.Log(IntersectSphere(lightPosVS, m_debugLights[i].range, m_debugFrustums[0]));
            }
            */

        }

        private static string FrustumToString(Frustum f)
        {
            return $"{PlaneToString(f.top)}--{PlaneToString(f.down)}--{PlaneToString(f.left)}--{PlaneToString(f.right)}--{PlaneToString(f.forward)}--{PlaneToString(f.back)}";
        }

        private static string PlaneToString(Plane p)
        {
            return $"[{p.normal}::{p.dis2Origin}]";
        }

        private static bool IntersectSphere(Vector3 center, float radius, Frustum frustum)
        {
            bool ret = true;
            float dis0 = Vector3.Dot(frustum.top.normal, center) + frustum.top.dis2Origin;
            float dis1 = Vector3.Dot(frustum.down.normal, center) + frustum.down.dis2Origin;
            float dis2 = Vector3.Dot(frustum.left.normal, center) + frustum.left.dis2Origin;
            float dis3 = Vector3.Dot(frustum.right.normal, center) + frustum.right.dis2Origin;
            float dis4 = Vector3.Dot(frustum.forward.normal, center) + frustum.forward.dis2Origin;
            float dis5 = Vector3.Dot(frustum.back.normal, center) + frustum.back.dis2Origin;
            UnityEngine.Debug.Log($"{dis0}, {dis1}, {dis2}, {dis3}, {dis4}, {dis5}");

            ret = ret && (dis0 >= -radius);
            ret = ret && (dis1 >= -radius);
            ret = ret && (dis2 >= -radius);
            ret = ret && (dis3 >= -radius);
            ret = ret && (dis4 >= -radius);
            ret = ret && (dis5 >= -radius);

            return ret;
        }
        #endregion
    }
}