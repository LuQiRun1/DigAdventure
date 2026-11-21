using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public sealed class ChestCatalog : ScriptableObject
{
    public const int ShapeSize = 5;

    public enum ScanPrecision { Area, Cell, Quality }
    public enum DigMode { Manual, Automatic }

    [Serializable]
    public sealed class Shovel : Item
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, Min(0)] private int scanRadius = 2;
        [SerializeField, Min(0)] private int scanDepth = 1;
        [SerializeField] private ScanPrecision scanPrecision;
        [SerializeField, Min(0)] private float scanSeconds = 1.6f;
        [SerializeField] private DigMode digMode;
        [SerializeField, Min(0.05f)] private float digSecondsPerLayer = 1.2f;
        [SerializeField, Min(1)] private int clicksPerLayer = 3;

        public string Id => string.IsNullOrEmpty(id) ? $"legacy-{Quality}" : id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "铲子" : displayName;
        public int ScanRadius => Mathf.Max(0, scanRadius);
        public int ScanDepth => Mathf.Max(0, scanDepth);
        public ScanPrecision Precision => scanPrecision;
        public float ScanSeconds => Mathf.Max(0, scanSeconds);
        public DigMode Mode => digMode;
        public float DigSecondsPerLayer => Mathf.Max(0.05f, digSecondsPerLayer);
        public int ClicksPerLayer => Mathf.Max(1, clicksPerLayer);
    }

    [Serializable]
    public class Item
    {
        [SerializeField] private int shape = 1;
        [SerializeField] private float singleCellCost = 1f;
        [SerializeField] private GameDataConfig.ItemQuality quality;
        [SerializeField, TextArea] private string description = "";
        public GameDataConfig.ItemQuality Quality => quality;
        public string Description => description;
        public int StackLimit => (shape & (shape - 1)) == 0 ? StackLimitFor(singleCellCost) : 1;
        public float BackpackCost
        {
            get
            {
                int count = 0;
                for (int bits = shape; bits != 0; bits &= bits - 1) count++;
                return count == 1 ? singleCellCost : count;
            }
        }
        public static int StackLimitFor(float cost)
        {
            if (!(cost > 0f) || cost >= 1f) return 1;
            if (cost <= 1d / int.MaxValue) return int.MaxValue;
            return (int)Math.Min(int.MaxValue, decimal.Floor(1m / (decimal)cost));
        }
        public bool Occupies(int x, int y) => x >= 0 && x < ShapeSize && y >= 0 && y < ShapeSize
            && (shape & (1 << (y * ShapeSize + x))) != 0;
    }

    [Serializable]
    public sealed class Chest : Item
    {
        [SerializeField] private Vector2Int capacity = Vector2Int.one;
        [SerializeField] private int[] weights;
        [SerializeField, Min(1)] private int minDepth = 1;
        [SerializeField, Min(0)] private int maxDepth; // 0 表示不限深度。
        [SerializeField] private DepthWeight[] depthWeights = Array.Empty<DepthWeight>();

        [Serializable]
        private struct DepthWeight
        {
            public int depth;
            public int weight;
        }

        public bool CanAppearAtDepth(int depth) =>
            depth >= minDepth && (maxDepth == 0 || depth <= maxDepth);

#if UNITY_EDITOR
        private int FirstGeneratedDepth => Mathf.Max(1, minDepth);
        private int LastGeneratedDepth => Mathf.Max(FirstGeneratedDepth, maxDepth);

        private long GetTotalDepthWeight()
        {
            long total = (long)LastGeneratedDepth - FirstGeneratedDepth + 1;
            foreach (var entry in depthWeights)
                if (entry.depth >= FirstGeneratedDepth && entry.depth <= LastGeneratedDepth)
                    total += (long)Mathf.Max(0, entry.weight) - 1;
            return total;
        }

        public int RollDepth()
        {
            long total = GetTotalDepthWeight();
            if (total <= 0) return FirstGeneratedDepth;

            long ticket = RollWeight(total);
            long nextDepth = FirstGeneratedDepth;
            // 权重记录按层数升序保存，未显式配置的层权重为 1。
            foreach (var entry in depthWeights)
            {
                if (entry.depth < nextDepth || entry.depth > LastGeneratedDepth) continue;
                long gap = entry.depth - nextDepth;
                if (ticket < gap) return (int)(nextDepth + ticket);
                ticket -= gap;
                int weight = Mathf.Max(0, entry.weight);
                if (ticket < weight) return entry.depth;
                ticket -= weight;
                nextDepth = (long)entry.depth + 1;
            }

            return (int)(nextDepth + ticket);
        }

        private static long RollWeight(long total)
        {
            if (total <= int.MaxValue)
                return UnityEngine.Random.Range(0, (int)total);

            const long range = (long)int.MaxValue * int.MaxValue;
            long limit = range - range % total;
            long sample;
            do
            {
                sample = (long)UnityEngine.Random.Range(0, int.MaxValue) * int.MaxValue
                    + UnityEngine.Random.Range(0, int.MaxValue);
            } while (sample >= limit);
            return sample % total;
        }
#endif

        public Vector2Int Capacity => capacity;

        public double Probability(GameDataConfig.ItemQuality quality)
        {
            long total = 0;
            foreach (int weight in weights) total += weight;
            return total > 0 ? (double)weights[(int)quality] / total : 0;
        }
    }

    [SerializeField] private Chest[] tiers;
    [SerializeField] private Item key = new Item(), coin = new Item();
    [SerializeField, HideInInspector] private Shovel shovel = new Shovel(); // 旧目录的默认铲子。
    [FormerlySerializedAs("shovelTiers")]
    [SerializeField] private Shovel[] shovels = Array.Empty<Shovel>();
    public IReadOnlyList<Shovel> Shovels => shovels ?? Array.Empty<Shovel>();
    public Item GetItem(GameDataConfig.ItemType type, GameDataConfig.ItemQuality chestQuality = GameDataConfig.ItemQuality.White)
    {
        switch (type)
        {
            case GameDataConfig.ItemType.Key: return key;
            case GameDataConfig.ItemType.Coin: return coin;
            case GameDataConfig.ItemType.Shovel: return GetShovel(chestQuality);
            case GameDataConfig.ItemType.TreasureChest: return Get(chestQuality);
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
    public Chest Get(GameDataConfig.ItemQuality quality) => tiers[(int)quality];

    public Shovel GetShovel(GameDataConfig.ItemQuality quality)
    {
        Shovel firstWhite = null;
        foreach (var item in Shovels)
        {
            if (item == null) continue;
            if (item.Quality == quality) return item;
            if (firstWhite == null && item.Quality == GameDataConfig.ItemQuality.White)
                firstWhite = item;
        }
        return firstWhite ?? shovel;
    }

    public Shovel GetShovel(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var item in Shovels)
            if (item != null && string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
        return null;
    }
}
