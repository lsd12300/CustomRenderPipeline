using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;


namespace ClusterLight
{

    /// <summary>
    ///  将相机视椎体划分成一个个小视椎体,  进行光源裁剪
    /// </summary>
    public class ClusterFrustums : ClusterBase
    {

        public static readonly Vector3Int m_CSThreadCount = new Vector3Int(8, 8, 8); // ComputeShader 每个线程组内 线程数量.  每个线程计算一个Tile
        public const int m_MaxLightCount = 32; // 最大光源数量
        public const int m_MaxTileCountX = 32; // Tile 最大数量
        public const int m_MaxTileCountY = 32;
        public const int m_MaxTileCountZ = 32;

        private Camera m_cam;
        private ComputeShader m_clusterCs; // 视椎体计算 CS
        private ComputeShader m_lightCullCs; // 光源裁剪 CS
        private int m_frustumKernel;
        private int m_lightCullKernel;
        private ComputeBuffer m_frustumsBuffer; // 视椎体数据
        private ComputeBuffer m_lightBuffer; // 光源数据
        private ComputeBuffer m_frustumLightBuffer; // 视椎体内光源索引

        private Frustum[] m_frustums; // 视椎体划分结果数据,  用于传递到 光源裁剪ComputeShader
        private int m_lightCount; // 光源数量
        private LightInfo[] m_lights;


        private int m_perFrustumSizeX = 32; // 每个 Tile的屏幕像素尺寸.  例:  屏幕宽度 <= 横向线程组数 * 横向组内线程数 * 横向Tile像素尺寸
        private int m_perFrustumSizeY = 32;
        private int m_perFrustumSizeZ = 32;
        private int m_groupCountX = 8; // Dispatch 线程组数量
        private int m_groupCountY = 8;
        private int m_groupCountZ = 8;




        public ClusterFrustums(Camera cam, ComputeShader clusterCs, ComputeShader lightCullCs)
        {
            m_cam = cam;
            m_clusterCs = clusterCs;
            m_frustumKernel = m_clusterCs.FindKernel("CSMain");

            m_lightCullCs = lightCullCs;
            m_lightCullKernel = m_lightCullCs.FindKernel("CSMain");


            // 划分 Frustum 计算时 线程组数量
            CountPerTileSize(m_cam.pixelWidth, m_CSThreadCount.x, m_MaxTileCountX, ref m_perFrustumSizeX, ref m_groupCountX);
            CountPerTileSize(m_cam.pixelHeight, m_CSThreadCount.y, m_MaxTileCountY, ref m_perFrustumSizeY, ref m_groupCountY);
            var clipLen = Mathf.CeilToInt(m_cam.farClipPlane - m_cam.nearClipPlane);
            CountPerTileSize(clipLen, m_CSThreadCount.z, m_MaxTileCountZ, ref m_perFrustumSizeZ, ref m_groupCountZ);

            // 一个 Frustum 6个 Plane, 一个 Plane 4个float, 一个float 4字节.  6 * 4 * 4 = 96
            var bufferCount = m_groupCountX * m_CSThreadCount.x + m_groupCountY * m_CSThreadCount.y;
            //var bufferCount = Marshal.SizeOf<Frustum>();
            m_frustumsBuffer = new ComputeBuffer(bufferCount, 96);
            m_frustums = new Frustum[bufferCount];

            m_lights = new LightInfo[m_MaxLightCount];
            m_lightBuffer = new ComputeBuffer(m_MaxLightCount, Marshal.SizeOf<LightInfo>());
            m_frustumLightBuffer = new ComputeBuffer(bufferCount, Marshal.SizeOf<uint>()); // 小视椎体内光源索引.  使用 bit标记
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
        ///  调用 ComputeShader 划分视椎体
        /// </summary>
        private void GenerateFrustums()
        {
            m_clusterCs.SetBuffer(m_frustumKernel, "m_frustums", m_frustumsBuffer);
            m_clusterCs.SetInt("m_perFrustumSizeX", m_perFrustumSizeX);
            m_clusterCs.SetInt("m_perFrustumSizeY", m_perFrustumSizeY);
            m_clusterCs.SetInt("m_perFrustumSizeZ", m_perFrustumSizeZ);
            m_clusterCs.SetInt("m_groupCountX", m_groupCountX);
            m_clusterCs.SetInt("m_groupCountY", m_groupCountY);
            m_clusterCs.SetInt("m_groupCountZ", m_groupCountZ);
            m_clusterCs.SetFloat("m_camNearClipPlane", m_cam.nearClipPlane);
            m_clusterCs.SetFloat("m_camFarClipPlane", m_cam.farClipPlane);
            m_clusterCs.SetInt("m_screenWidth", m_cam.pixelWidth);
            m_clusterCs.SetInt("m_screenHeight", m_cam.pixelHeight);
            m_clusterCs.SetMatrix("m_projInvMt", m_cam.projectionMatrix.inverse);
            m_clusterCs.SetFloat("m_camProjMtM22", m_cam.projectionMatrix.m22);
            m_clusterCs.SetFloat("m_camProjMtM23", m_cam.projectionMatrix.m23);

            m_clusterCs.Dispatch(m_frustumKernel, m_groupCountX, m_groupCountY, m_groupCountZ);
        }

        private void PrepareLights(NativeArray<VisibleLight> lights)
        {

        }

        private void LightCull()
        {

        }
    }
}