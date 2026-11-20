using System;
using UnityEngine;

public sealed class ChestCatalog : ScriptableObject
{
    public const int ShapeSize = 5;

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

        public bool CanAppearAtDepth(int depth) =>
            depth >= minDepth && (maxDepth == 0 || depth <= maxDepth);

        public Vector2Int Capacity => capacity;

        public double Probability(GameDataConfig.ItemQuality quality)
        {
            long total = 0;
            foreach (int weight in weights) total += weight;
            return total > 0 ? (double)weights[(int)quality] / total : 0;
        }
    }

    [SerializeField] private Chest[] tiers;
    [SerializeField] private Item key = new Item(), coin = new Item(), shovel = new Item();
    public Item GetItem(GameDataConfig.ItemType type, GameDataConfig.ItemQuality chestQuality = GameDataConfig.ItemQuality.White)
    {
        switch (type)
        {
            case GameDataConfig.ItemType.Key: return key;
            case GameDataConfig.ItemType.Coin: return coin;
            case GameDataConfig.ItemType.Shovel: return shovel;
            case GameDataConfig.ItemType.TreasureChest: return Get(chestQuality);
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
    public Chest Get(GameDataConfig.ItemQuality quality) => tiers[(int)quality];
}
