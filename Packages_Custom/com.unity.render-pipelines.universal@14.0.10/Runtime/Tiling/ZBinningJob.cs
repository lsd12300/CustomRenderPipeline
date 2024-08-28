using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    struct ZBinningJob : IJobFor
    {
        // Do not use this for the innerloopBatchCount (use 1 for that). Use for dividing the arrayLength when scheduling.
        public const int batchSize = 128;

        public const int headerLength = 2;

        [NativeDisableParallelForRestriction]
        public NativeArray<uint> bins;

        [ReadOnly]
        public NativeArray<float2> minMaxZs;

        public float zBinScale;

        public float zBinOffset;

        public int binCount;

        public int wordsPerTile;

        public int lightCount;

        public int reflectionProbeCount;

        public int batchCount;

        public int viewCount;

        public bool isOrthographic;

        static uint EncodeHeader(uint min, uint max)
        {
            return (min & 0xFFFF) | ((max & 0xFFFF) << 16);
        }

        static (uint, uint) DecodeHeader(uint zBin)
        {
            return (zBin & 0xFFFF, (zBin >> 16) & 0xFFFF);
        }

        public void Execute(int jobIndex)
        {
            var batchIndex = jobIndex % batchCount; // 批次编号
            var viewIndex = jobIndex / batchCount; // 屏幕编号.  VR之类的有两个显示屏幕,  一般移动端只有一个

            var binStart = batchSize * batchIndex;
            var binEnd = math.min(binStart + batchSize, binCount) - 1;

            var binOffset = viewIndex * binCount;  // 不同输出屏幕的 ZBin 起始偏移.  多个输出屏幕 都放在同一个数组中.

            var emptyHeader = EncodeHeader(ushort.MaxValue, ushort.MinValue);
            for (var binIndex = binStart; binIndex <= binEnd; binIndex++)
            {
                bins[(binOffset + binIndex) * (headerLength + wordsPerTile) + 0] = emptyHeader;
                bins[(binOffset + binIndex) * (headerLength + wordsPerTile) + 1] = emptyHeader;
            }

            // Regarding itemOffset: minMaxZs contains [lights view 0, lights view 1, probes view 0, probes view 1] when
            // using XR single pass instanced, and otherwise [lights, probes]. So we figure out what the offset is based
            // on the view count and index.

            // Fill ZBins for lights.
            FillZBins(binStart, binEnd, 0, lightCount, 0, viewIndex * lightCount, binOffset);

            // Fill ZBins for reflection probes.
            FillZBins(binStart, binEnd, lightCount, lightCount + reflectionProbeCount, 1, lightCount * (viewCount - 1) + viewIndex * reflectionProbeCount, binOffset);
        }

        /// <summary>
        ///  计算 ZBin数据
        ///     ZBin数据内存格式  (ZBin段内 最大最小光源索引 + 最大最小探针索引 + 光源状态1 + 光源状态2...).
        ///     
        ///         每个数据都是 32-bit. 所以当总光源数量超过32个时  需要多个光源状态数据
        ///         
        /// </summary>
        /// <param name="binStart">ZBin 的起始编号.  按128个ZBin一个批次 一起计算</param>
        /// <param name="binEnd">ZBin 结束编号</param>
        /// <param name="itemStart">光照起始索引.  光源和探针 放在放在同一个光源数组中 (光源在前, 之后是探针)</param>
        /// <param name="itemEnd"></param>
        /// <param name="headerIndex">0--光源; 1--探针</param>
        /// <param name="itemOffset">不同输出屏幕的 光源起始偏移.  多个输出屏幕的光源都放在同一个数组中</param>
        /// <param name="binOffset">不同输出屏幕的 ZBin 起始偏移.  多个输出屏幕 都放在同一个数组中</param>
        void FillZBins(int binStart, int binEnd, int itemStart, int itemEnd, int headerIndex, int itemOffset, int binOffset)
        {
            for (var index = itemStart; index < itemEnd; index++)
            {
                // 根据光源最大最小Z值 计算光源所覆盖的ZBin区域
                var minMax = minMaxZs[itemOffset + index]; // 当前输屏幕, 索引为index 的光源的 最大最小Z值
                var minBin = math.max((int)((isOrthographic ? minMax.x : math.log2(minMax.x)) * zBinScale + zBinOffset), binStart);
                var maxBin = math.min((int)((isOrthographic ? minMax.y : math.log2(minMax.y)) * zBinScale + zBinOffset), binEnd);

                var wordIndex = index / 32; // 每32个光源 存储到 一个 word中
                var bitMask = 1u << (index % 32); // 光源索引 映射到 bit

                for (var binIndex = minBin; binIndex <= maxBin; binIndex++)
                {
                    // 每个ZBin包含 2个光源索引最大最小编码 + 光源状态数据
                    //  wordsPerTile -- 需要多少个光源状态数据.  每32个光源一个光源状态数据32-bit
                    var baseIndex = (binOffset + binIndex) * (headerLength + wordsPerTile); // 计算 ZBin的起始坐标
                    var (minIndex, maxIndex) = DecodeHeader(bins[baseIndex + headerIndex]);
                    minIndex = math.min(minIndex, (uint)index);
                    maxIndex = math.max(maxIndex, (uint)index);
                    bins[baseIndex + headerIndex] = EncodeHeader(minIndex, maxIndex); // 更新 ZBin最大最小光源索引
                    bins[baseIndex + headerLength + wordIndex] |= bitMask; // 记录 光源索引
                }
            }
        }
    }
}
