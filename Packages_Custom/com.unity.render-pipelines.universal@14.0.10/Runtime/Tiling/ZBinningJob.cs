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
            var batchIndex = jobIndex % batchCount; // ���α��
            var viewIndex = jobIndex / batchCount; // ��Ļ���.  VR֮�����������ʾ��Ļ,  һ���ƶ���ֻ��һ��

            var binStart = batchSize * batchIndex;
            var binEnd = math.min(binStart + batchSize, binCount) - 1;

            var binOffset = viewIndex * binCount;  // ��ͬ�����Ļ�� ZBin ��ʼƫ��.  ��������Ļ ������ͬһ��������.

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
        ///  ���� ZBin����
        ///     ZBin�����ڴ��ʽ  (ZBin���� �����С��Դ���� + �����С̽������ + ��Դ״̬1 + ��Դ״̬2...).
        ///     
        ///         ÿ�����ݶ��� 32-bit. ���Ե��ܹ�Դ��������32��ʱ  ��Ҫ�����Դ״̬����
        ///         
        /// </summary>
        /// <param name="binStart">ZBin ����ʼ���.  ��128��ZBinһ������ һ�����</param>
        /// <param name="binEnd">ZBin �������</param>
        /// <param name="itemStart">������ʼ����.  ��Դ��̽�� ���ڷ���ͬһ����Դ������ (��Դ��ǰ, ֮����̽��)</param>
        /// <param name="itemEnd"></param>
        /// <param name="headerIndex">0--��Դ; 1--̽��</param>
        /// <param name="itemOffset">��ͬ�����Ļ�� ��Դ��ʼƫ��.  ��������Ļ�Ĺ�Դ������ͬһ��������</param>
        /// <param name="binOffset">��ͬ�����Ļ�� ZBin ��ʼƫ��.  ��������Ļ ������ͬһ��������</param>
        void FillZBins(int binStart, int binEnd, int itemStart, int itemEnd, int headerIndex, int itemOffset, int binOffset)
        {
            for (var index = itemStart; index < itemEnd; index++)
            {
                // ���ݹ�Դ�����СZֵ �����Դ�����ǵ�ZBin����
                var minMax = minMaxZs[itemOffset + index]; // ��ǰ����Ļ, ����Ϊindex �Ĺ�Դ�� �����СZֵ
                var minBin = math.max((int)((isOrthographic ? minMax.x : math.log2(minMax.x)) * zBinScale + zBinOffset), binStart);
                var maxBin = math.min((int)((isOrthographic ? minMax.y : math.log2(minMax.y)) * zBinScale + zBinOffset), binEnd);

                var wordIndex = index / 32; // ÿ32����Դ �洢�� һ�� word��
                var bitMask = 1u << (index % 32); // ��Դ���� ӳ�䵽 bit

                for (var binIndex = minBin; binIndex <= maxBin; binIndex++)
                {
                    // ÿ��ZBin���� 2����Դ���������С���� + ��Դ״̬����
                    //  wordsPerTile -- ��Ҫ���ٸ���Դ״̬����.  ÿ32����Դһ����Դ״̬����32-bit
                    var baseIndex = (binOffset + binIndex) * (headerLength + wordsPerTile); // ���� ZBin����ʼ����
                    var (minIndex, maxIndex) = DecodeHeader(bins[baseIndex + headerIndex]);
                    minIndex = math.min(minIndex, (uint)index);
                    maxIndex = math.max(maxIndex, (uint)index);
                    bins[baseIndex + headerIndex] = EncodeHeader(minIndex, maxIndex); // ���� ZBin�����С��Դ����
                    bins[baseIndex + headerLength + wordIndex] |= bitMask; // ��¼ ��Դ����
                }
            }
        }
    }
}
