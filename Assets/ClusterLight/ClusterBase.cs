using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;



namespace ClusterLight
{

    // 平面
    struct Plane
    {
        public Vector3 normal; // 法线
        public float dis2Origin; // 沿法线方向与原点的距离
    };

    // 视椎体
    struct Frustum
    {
        //Plane[] faces; // 视椎体6个面.  上下左右前后
        public Plane top, down, left, right, forward, back;
    };

    // 光源结构
    struct LightInfo
    {
        public Color color;
        public Vector3 pos; // 坐标
        public float range; // 点光源范围半径. 0--表示方向光
    };



    public class ClusterBase
    {


        public virtual void Init(Camera cam)
        {

        }

        /// <summary>
        ///  更新视野裁切. 光源裁剪
        /// </summary>
        /// <param name="lights"></param>
        /// <param name="cmd"></param>
        public virtual void Update(NativeArray<VisibleLight> lights, CommandBuffer cmd)
        {

        }

        /// <summary>
        ///  设置Shader属性
        /// </summary>
        /// <param name="cmd"></param>
        public virtual void SetUpShaderParams(CommandBuffer cmd)
        {

        }


        /// <summary>
        ///  计算每个 Tile 的屏幕尺寸.
        ///  
        ///     移动端 Tile-Base.  
        ///         屏幕上一个Tile像素大小 从 8x8到32x32之间
        ///         屏幕上的 一个Tile 会在同一个线程组中执行 像素着色器
        ///         一个线程组内 要尽量在逻辑分支时  执行相同的分支
        /// </summary>
        /// <param name="totleSize">总长度. 如屏幕宽度或高度</param>
        /// <param name="baseSize">基础长度</param>
        /// <param name="maxGroupCount">最大分组上限</param>
        /// <returns></returns>
        protected void CountPerTileSize(int totleSize, int baseSize, int maxGroupCount, ref int tileSize, ref int groupCount)
        {
            // 优先使用最大分组上限.  当不够覆盖完整区域时   扩大每段长度(tileSize)
            groupCount = maxGroupCount / baseSize;
            while (totleSize > baseSize * groupCount * tileSize)
            {
                tileSize <<= 1;
            }

            //groupCount = Mathf.CeilToInt((float)totleSize / (baseSize * groupCount * tileSize));
        }
    }
}