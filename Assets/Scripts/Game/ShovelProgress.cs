using System.Collections.Generic;
using UnityEngine;
using ItemQuality = GameDataConfig.ItemQuality;

public sealed class ShovelProgress
{
    private const int ApproximateAreaSize = 3;
    private readonly LevelData level;
    private readonly Dictionary<Vector2Int, int> depths = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> clicks = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, LevelData.ChestPlacement> chests =
        new Dictionary<Vector2Int, LevelData.ChestPlacement>();

    public ShovelProgress(LevelData level)
    {
        this.level = level;
        foreach (var chest in level.ChestPlacements)
            chests[chest.Cell] = chest;
    }

    public int GetDepth(Vector2Int cell) => depths.TryGetValue(cell, out int depth) ? depth : 0;
    public int GetClicks(Vector2Int cell) => clicks.TryGetValue(cell, out int count) ? count : 0;
    public void ClearClicks() => clicks.Clear();

    public bool CanDig(Vector2Int cell) => level.IsCellWalkable(cell) && GetDepth(cell) < int.MaxValue;

    public bool RegisterClick(Vector2Int cell, int required)
    {
        int count = GetClicks(cell) + 1;
        if (count < required)
        {
            clicks[cell] = count;
            return false;
        }
        clicks.Remove(cell);
        return true;
    }

    public ItemQuality? DigLayer(Vector2Int cell)
    {
        if (!CanDig(cell)) return null;
        int nextDepth = GetDepth(cell) + 1;
        depths[cell] = nextDepth;
        return chests.TryGetValue(cell, out var chest) && chest.Depth == nextDepth
            ? (ItemQuality?)chest.Quality : null;
    }

    public struct Detection
    {
        public RectInt Area { get; }
        public ItemQuality? Quality { get; }
        public int Count { get; }

        internal Detection(RectInt area, ItemQuality? quality, int count)
        {
            Area = area;
            Quality = quality;
            Count = count;
        }
    }

    public List<Detection> Scan(Vector2Int origin, ChestCatalog.Shovel shovel)
    {
        var groups = new Dictionary<(RectInt area, ItemQuality? quality), int>();
        foreach (var chest in chests.Values)
        {
            Vector2Int delta = chest.Cell - origin;
            int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            int depthBelowSurface = chest.Depth - GetDepth(chest.Cell);
            if (!level.ContainsCell(chest.Cell) || distance > shovel.ScanRadius ||
                depthBelowSurface <= 0 || depthBelowSurface > shovel.ScanDepth)
                continue;

            RectInt area = new RectInt(chest.Cell, Vector2Int.one);
            if (shovel.Precision == ChestCatalog.ScanPrecision.Area)
            {
                Vector2Int corner = chest.Cell / ApproximateAreaSize * ApproximateAreaSize;
                area = new RectInt(corner.x, corner.y,
                    Mathf.Min(ApproximateAreaSize, level.Width - corner.x),
                    Mathf.Min(ApproximateAreaSize, level.Height - corner.y));
            }

            ItemQuality? quality = shovel.Precision == ChestCatalog.ScanPrecision.Quality
                ? (ItemQuality?)chest.Quality : null;
            var key = (area, quality);
            groups.TryGetValue(key, out int count);
            groups[key] = count + 1;
        }

        var result = new List<Detection>(groups.Count);
        foreach (var group in groups)
            result.Add(new Detection(group.Key.area, group.Key.quality, group.Value));
        result.Sort((a, b) =>
        {
            int row = a.Area.y.CompareTo(b.Area.y);
            return row != 0 ? row : a.Area.x.CompareTo(b.Area.x);
        });
        return result;
    }
}
