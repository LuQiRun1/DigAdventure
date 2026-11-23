using System;
using System.Collections.Generic;
using UnityEngine;
using SceneElement = GameDataConfig.SceneElement;
using GeologyElement = GameDataConfig.GeologyElement;
using ItemQuality = GameDataConfig.ItemQuality;

public sealed class LevelData : ScriptableObject, ISerializationCallbackReceiver
{
    [SerializeField, Min(GameDataConfig.MinLevelGridSize)] private int width = GameDataConfig.DefaultLevelWidth;
    [SerializeField, Min(GameDataConfig.MinLevelGridSize)] private int height = GameDataConfig.DefaultLevelHeight;
    [SerializeField] private List<ElementPlacement> sceneElements = new List<ElementPlacement>();
    [SerializeField] private List<ManualOccupancy> manualOccupancy = new List<ManualOccupancy>();
    private Dictionary<Vector2Int, int> manualOccupancyIndices;

    public int Width => width;
    [SerializeField, Min(0)] private int slimeCount = 1;
    [SerializeField, Min(0)] private int wolfCount = 1;
    [SerializeField] private int[] chestCounts = new int[(int)GameDataConfig.ItemQuality.Red + 1];
    [SerializeField] private List<ChestPlacement> chestPlacements = new List<ChestPlacement>();
    public IReadOnlyList<ChestPlacement> ChestPlacements => chestPlacements;

    [Serializable]
    public struct ChestPlacement
    {
        [SerializeField] private Vector2Int cell;
        [SerializeField] private ItemQuality quality;
        [SerializeField] private int depth;

        public Vector2Int Cell => cell;
        public ItemQuality Quality => quality;
        public int Depth => Mathf.Max(1, depth);

        internal ChestPlacement(Vector2Int cell, ItemQuality quality, int depth)
        {
            this.cell = cell;
            this.quality = quality;
            this.depth = depth;
        }
    }

    public int GetChestCount(GameDataConfig.ItemQuality quality)
    {
        int index = (int)quality;
        return chestCounts != null && index >= 0 && index < chestCounts.Length
            ? Mathf.Max(0, chestCounts[index]) : 0;
    }
    public int SlimeCount => Mathf.Max(0, slimeCount);
    public int WolfCount => Mathf.Max(0, wolfCount);
    public int Height => height;
    [SerializeField] private Vector2Int entrance;
    [SerializeField] private Vector2Int exit = new Vector2Int(-1, -1);
    [SerializeField] private GeologyElement entranceDoor = GeologyElement.DoorHorizontal;
    [SerializeField] private GeologyElement exitDoor = GeologyElement.DoorVertical;
    private Vector2Int LastCell => new Vector2Int(width - 1, height - 1);
    public Vector2Int Entrance => Vector2Int.Min(Vector2Int.Max(entrance, Vector2Int.zero), LastCell);
    public Vector2Int Exit
    {
        get
        {
            var candidate = ContainsCell(exit) ? exit : LastCell;
            return candidate != Entrance ? candidate : Entrance == Vector2Int.zero ? LastCell : Vector2Int.zero;
        }
    }
    public GeologyElement EntranceDoor => entranceDoor;
    public GeologyElement ExitDoor => exitDoor;
    [SerializeField] private float entranceScale = 1f;
    [SerializeField] private float exitScale = 1f;
    public float DoorScale(bool isExit)
    {
        float value = isExit ? exitScale : entranceScale;
        return value > 0f ? value : 1f;
    }
    public void SetDoorScale(bool isExit, float value)
    {
        if (isExit) exitScale = value;
        else entranceScale = value;
    }

    public bool PlaceDoor(bool isExit, Vector2Int cell, GeologyElement door)
    {
        if (!ContainsCell(cell) || !GameDataConfig.IsDoor((int)door) || cell == (isExit ? Entrance : Exit)) return false;
        var other = isExit ? Entrance : Exit;
        if (isExit) { entrance = other; exit = cell; exitDoor = door; }
        else { exit = other; entrance = cell; entranceDoor = door; }
        return true;
    }
    public IReadOnlyList<ElementPlacement> SceneElements => sceneElements;

    public static LevelData FromJson(string json, string levelName)
    {
        var data = CreateInstance<LevelData>();
        try
        {
            JsonUtility.FromJsonOverwrite(json, data);
            data.name = levelName;
            data.OnValidate();
            return data;
        }
        catch
        {
            DestroyImmediate(data);
            throw;
        }
    }

#if UNITY_EDITOR
    public bool TryGenerateChestPlacements(ChestCatalog catalog, out long requestedCount, out int availableCount)
    {
        requestedCount = 0;
        for (int quality = 0; quality <= (int)ItemQuality.Red; quality++)
            requestedCount += GetChestCount((ItemQuality)quality);

        var blocked = new HashSet<Vector2Int>(GetManuallyBlockedCells());
        var rules = SceneElementRules.Shared;
        foreach (var placement in sceneElements)
        {
            if (!CanOccupyCells(placement) ||
                (rules && (rules.Get(placement.Element) & SceneElementRules.Traits.Passable) != 0))
                continue;

            // 即使格子被手动标记为可通行，宝箱也避开不可穿过元素的完整占格。
            foreach (Vector2Int offset in placement.OccupiedCellsFromAnchor)
                blocked.Add(placement.Cell + offset);
        }

        var candidates = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var cell = new Vector2Int(x, y);
            if (!blocked.Contains(cell))
                candidates.Add(cell);
        }

        availableCount = candidates.Count;
        if (requestedCount > availableCount)
            return false;

        var generated = new List<ChestPlacement>((int)requestedCount);
        for (int quality = 0; quality <= (int)ItemQuality.Red; quality++)
        {
            int count = GetChestCount((ItemQuality)quality);
            if (count == 0) continue;
            var tier = catalog.Get((ItemQuality)quality);
            for (int i = 0; i < count; i++)
            {
                int next = generated.Count;
                int chosen = UnityEngine.Random.Range(next, availableCount);
                generated.Add(new ChestPlacement(candidates[chosen], (ItemQuality)quality, tier.RollDepth()));
                candidates[chosen] = candidates[next];
            }
        }

        chestPlacements = generated;
        return true;
    }
#endif

    [Serializable]
    private struct ManualOccupancy
    {
        public Vector2Int cell;
        public bool blocked;
        public bool blocksSight;

        public ManualOccupancy(Vector2Int cell, bool blocked)
        {
            this.cell = cell;
            this.blocked = blocked;
            blocksSight = false;
        }
    }

    [Serializable]
    public struct ElementPlacement
    {
        [SerializeField] private Vector2Int cell;
        // Keep the original serialized field name and IDs used by existing levels.
        [SerializeField] private int element;
        [SerializeField] private float scale;
        [SerializeField] private bool hasBakedOccupancy;
        [SerializeField] private Vector2Int[] occupiedCells;

        public Vector2Int Cell => cell;
        public int ElementId => element;
        public SceneElement Element => IsGeology ? SceneElement.None : (SceneElement)element;
        public GeologyElement Geology => IsGeology ? (GeologyElement)element : GeologyElement.None;
        public bool IsGeology => GameDataConfig.IsGeology(element);
        // Older levels have no saved scale and use the original size.
        public float Scale => scale > 0f ? scale : 1f;
        public bool HasBakedOccupancy => hasBakedOccupancy;
        public IReadOnlyList<Vector2Int> OccupiedCellsFromAnchor => occupiedCells ?? Array.Empty<Vector2Int>();
        public Rect BoundsInCells
        {
            get
            {
                Vector2 size = Vector2.one * (GameDataConfig.SceneElementDefaultSizeInCells * Scale);
                Vector2 center = (Vector2)cell + Vector2.one * 0.5f;
                return new Rect(center - size * 0.5f, size);
            }
        }

        internal ElementPlacement(Vector2Int cell, int element, float scale, Vector2Int[] occupiedCells)
        {
            this.cell = cell;
            this.element = element;
            this.scale = scale;
            hasBakedOccupancy = occupiedCells != null;
            this.occupiedCells = occupiedCells;
        }

        internal ElementPlacement WithCell(Vector2Int newCell)
        {
            var moved = this;
            moved.cell = newCell;
            return moved;
        }
    }

    public bool ExpandGrid(Vector2Int direction)
    {
        return ResizeGridEdge(direction, 1);
    }

    public bool ShrinkGrid(Vector2Int direction)
    {
        return ResizeGridEdge(direction, -1);
    }

    private bool ResizeGridEdge(Vector2Int direction, int change)
    {
        bool horizontal = direction.y == 0 && (direction.x == -1 || direction.x == 1);
        bool vertical = direction.x == 0 && (direction.y == -1 || direction.y == 1);
        int size = horizontal ? width : height;
        int newSize = size + change;
        if (newSize == 1 && (horizontal ? height : width) == 1) return false;
        if ((!horizontal && !vertical) || newSize < GameDataConfig.MinLevelGridSize ||
            newSize > GameDataConfig.MaxLevelGridSize)
            return false;

        // Rows increase downwards. The top and left edges are at index zero.
        int edge = direction.x < 0 || direction.y < 0 ? 0 : Mathf.Min(size, newSize);
        if (change < 0)
            sceneElements.RemoveAll(placement => (horizontal ? placement.Cell.x : placement.Cell.y) == edge);

        var shift = horizontal ? new Vector2Int(change, 0) : new Vector2Int(0, change);
        for (int i = 0; i < sceneElements.Count; i++)
        {
            ElementPlacement placement = sceneElements[i];
            int coordinate = horizontal ? placement.Cell.x : placement.Cell.y;
            if (coordinate >= edge)
                sceneElements[i] = placement.WithCell(placement.Cell + shift);
        }

        if (change < 0)
            manualOccupancy.RemoveAll(mark => (horizontal ? mark.cell.x : mark.cell.y) == edge);
        for (int i = 0; i < manualOccupancy.Count; i++)
        {
            ManualOccupancy mark = manualOccupancy[i];
            int coordinate = horizontal ? mark.cell.x : mark.cell.y;
            if (coordinate >= edge)
            {
                mark.cell += shift;
                manualOccupancy[i] = mark;
            }
        }
        manualOccupancyIndices = null;

        if (change < 0)
            chestPlacements.RemoveAll(placement => (horizontal ? placement.Cell.x : placement.Cell.y) == edge);
        for (int i = 0; i < chestPlacements.Count; i++)
        {
            ChestPlacement placement = chestPlacements[i];
            int coordinate = horizontal ? placement.Cell.x : placement.Cell.y;
            if (coordinate >= edge)
                chestPlacements[i] = new ChestPlacement(placement.Cell + shift, placement.Quality, placement.Depth);
        }

        var oldEntrance = Entrance;
        var oldExit = Exit;
        entrance = oldEntrance + ((horizontal ? oldEntrance.x : oldEntrance.y) >= edge ? shift : Vector2Int.zero);
        exit = oldExit + ((horizontal ? oldExit.x : oldExit.y) >= edge ? shift : Vector2Int.zero);
        width += shift.x;
        height += shift.y;
        return true;
    }

    public bool SetSceneElement(Vector2Int cell, SceneElement element, float scale = 1f, Vector2Int[] occupiedCells = null)
    {
        if (!ContainsCell(cell))
            return false;

        if (element == SceneElement.None)
            return RemoveTopElement(cell);

        if (occupiedCells == null || !Enum.IsDefined(typeof(SceneElement), element))
            return false;

        sceneElements.Add(new ElementPlacement(cell, (int)element, scale, occupiedCells));
        return true;
    }

    public bool SetGeologyElement(Vector2Int cell, GeologyElement geology)
    {
        if (!ContainsCell(cell) || !GameDataConfig.IsGeology((int)geology))
            return false;

        int index = FindPlacement(cell, true);
        if (index >= 0 && sceneElements[index].Geology == geology)
            return false;

        var placement = new ElementPlacement(cell, (int)geology, 1f, null);
        if (index < 0)
            sceneElements.Add(placement);
        else
            sceneElements[index] = placement;

        return true;
    }

    public bool SetSceneElementTransform(int placementIndex, float scale, Vector2Int[] occupiedCells)
    {
        if (placementIndex < 0 || placementIndex >= sceneElements.Count)
            return false;

        ElementPlacement existing = sceneElements[placementIndex];
        if (existing.Element == SceneElement.None || occupiedCells == null ||
            (existing.HasBakedOccupancy && existing.Scale.Equals(scale)))
            return false;

        sceneElements[placementIndex] = new ElementPlacement(existing.Cell, existing.ElementId, scale, occupiedCells);
        return true;
    }

    public bool MoveSceneElement(int placementIndex, Vector2Int cell)
    {
        if (!ContainsCell(cell) || placementIndex < 0 || placementIndex >= sceneElements.Count)
            return false;

        ElementPlacement placement = sceneElements[placementIndex];
        if (!CanOccupyCells(placement) || placement.Cell == cell)
            return false;

        sceneElements[placementIndex] = placement.WithCell(cell);
        return true;
    }

    public bool TryGetSceneElement(Vector2Int cell, out ElementPlacement placement)
    {
        int index = FindPlacement(cell, false);
        placement = index >= 0 ? sceneElements[index] : default;
        return index >= 0;
    }

    public bool ContainsCell(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    public IEnumerable<Vector2Int> GetOccupiedCells(ElementPlacement placement)
    {
        if (!CanOccupyCells(placement))
            yield break;

        foreach (var relativeCell in placement.OccupiedCellsFromAnchor)
        {
            Vector2Int cell = placement.Cell + relativeCell;
            if (ContainsCell(cell) && (!TryGetManualOccupancy(cell, out bool blocked) || blocked))
                yield return cell;
        }
    }

    [Flags]
    public enum CellType { Open = 0, BlockMovement = 1, BlockSight = 2, BlockBoth = 3 }

    public CellType GetCellType(Vector2Int cell)
    {
        var type = IsCellWalkable(cell) ? CellType.Open : CellType.BlockMovement;
        return BlocksSight(cell) ? type | CellType.BlockSight : type;
    }

    public bool BlocksSight(Vector2Int cell)
    {
        if (!ContainsCell(cell)) return true;
        if (GetManualOccupancyIndices().TryGetValue(cell, out int index)) return manualOccupancy[index].blocksSight;
        return HasElementTrait(cell, SceneElementRules.Traits.BlocksSight);
    }

    public bool BlocksSmell(Vector2Int cell) =>
        !ContainsCell(cell) || HasElementTrait(cell, SceneElementRules.Traits.BlocksSmell);

    public bool BlocksHearing(Vector2Int cell) =>
        !ContainsCell(cell) || HasElementTrait(cell, SceneElementRules.Traits.BlocksHearing);

    public bool SetManualOccupancy(Vector2Int cell, bool blocked)
    {
        var type = blocked ? CellType.BlockMovement : CellType.Open;
        return SetCellType(cell, BlocksSight(cell) ? type | CellType.BlockSight : type);
    }

    public bool SetCellType(Vector2Int cell, CellType type)
    {
        if (!ContainsCell(cell))
            return false;

        var indices = GetManualOccupancyIndices();
        var mark = new ManualOccupancy(cell, (type & CellType.BlockMovement) != 0)
            { blocksSight = (type & CellType.BlockSight) != 0 };
        if (indices.TryGetValue(cell, out int index))
        {
            if (manualOccupancy[index].blocked == mark.blocked && manualOccupancy[index].blocksSight == mark.blocksSight)
                return false;

            manualOccupancy[index] = mark;
        }
        else
        {
            indices.Add(cell, manualOccupancy.Count);
            manualOccupancy.Add(mark);
        }

        return true;
    }

    public IEnumerable<Vector2Int> GetManuallyBlockedCells()
    {
        foreach (var mark in manualOccupancy)
        {
            if (mark.blocked && ContainsCell(mark.cell))
                yield return mark.cell;
        }
    }

    public IEnumerable<Vector2Int> GetSightBlockingCells()
    {
        foreach (var mark in manualOccupancy)
            if (mark.blocksSight && ContainsCell(mark.cell))
                yield return mark.cell;
    }

    public bool IsCellWalkable(Vector2Int cell)
    {
        return IsCellWalkableIgnoring(cell, SceneElement.None);
    }

    public bool IsCellWalkableIgnoring(Vector2Int cell, SceneElement ignoredElement)
    {
        if (cell == Entrance || cell == Exit) return true;
        if (!ContainsCell(cell))
            return false;

        if (TryGetManualOccupancy(cell, out bool blocked))
            return !blocked;

        return !HasElementTrait(cell, SceneElementRules.Traits.Passable, false, ignoredElement);
    }

    public bool CanMonsterEnter(Vector2Int cell, bool allowPassableElements)
    {
        return IsCellWalkable(cell) && (allowPassableElements || !HasElementTrait(cell, SceneElementRules.Traits.Passable));
    }

    private bool HasElementTrait(Vector2Int cell, SceneElementRules.Traits trait, bool expected = true,
        SceneElement ignoredElement = SceneElement.None)
    {
        var rules = SceneElementRules.Shared;
        foreach (var placement in sceneElements)
        {
            if (!CanOccupyCells(placement) || placement.Element == ignoredElement)
                continue;

            var traits = rules ? rules.Get(placement.Element) : SceneElementRules.Traits.None;
            bool hasTrait = (traits & trait) != 0;
            if (hasTrait != expected) continue;
            Vector2Int relativeCell = cell - placement.Cell;
            var occupiedCells = placement.OccupiedCellsFromAnchor;
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                if (occupiedCells[i] == relativeCell)
                    return true;
            }
        }

        return false;
    }

    private bool TryGetManualOccupancy(Vector2Int cell, out bool blocked)
    {
        if (manualOccupancy.Count > 0 && GetManualOccupancyIndices().TryGetValue(cell, out int index))
        {
            blocked = manualOccupancy[index].blocked;
            return true;
        }

        blocked = false;
        return false;
    }

    private Dictionary<Vector2Int, int> GetManualOccupancyIndices()
    {
        if (manualOccupancyIndices == null)
        {
            manualOccupancyIndices = new Dictionary<Vector2Int, int>(manualOccupancy.Count);
            for (int i = 0; i < manualOccupancy.Count; i++)
                manualOccupancyIndices[manualOccupancy[i].cell] = i;
        }

        return manualOccupancyIndices;
    }

    private bool CanOccupyCells(ElementPlacement placement)
    {
        return placement.Element != SceneElement.None && ContainsCell(placement.Cell);
    }

    private bool RemoveTopElement(Vector2Int cell)
    {
        int index = FindPlacement(cell, false);
        if (index < 0)
            index = FindPlacement(cell, true);
        if (index < 0)
            return false;

        sceneElements.RemoveAt(index);
        return true;
    }

    private int FindPlacement(Vector2Int cell, bool geology)
    {
        for (int i = sceneElements.Count - 1; i >= 0; i--)
        {
            if (sceneElements[i].Cell == cell && sceneElements[i].IsGeology == geology)
                return i;
        }

        return -1;
    }

    private void OnValidate()
    {
        manualOccupancyIndices = null;
        width = Mathf.Clamp(width, GameDataConfig.MinLevelGridSize, GameDataConfig.MaxLevelGridSize);
        height = Mathf.Clamp(height, GameDataConfig.MinLevelGridSize, GameDataConfig.MaxLevelGridSize);
        if (width == 1 && height == 1) width = 2;
    }

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        manualOccupancyIndices = null;
    }
}
