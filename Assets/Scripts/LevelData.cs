using System;
using System.Collections.Generic;
using UnityEngine;
using SceneElement = GameDataConfig.SceneElement;

public sealed class LevelData : ScriptableObject, ISerializationCallbackReceiver
{
    [SerializeField, Min(GameDataConfig.MinLevelGridSize)] private int width = GameDataConfig.DefaultLevelWidth;
    [SerializeField, Min(GameDataConfig.MinLevelGridSize)] private int height = GameDataConfig.DefaultLevelHeight;
    [SerializeField] private List<ElementPlacement> sceneElements = new List<ElementPlacement>();
    [SerializeField] private List<ManualOccupancy> manualOccupancy = new List<ManualOccupancy>();
    private Dictionary<Vector2Int, int> manualOccupancyIndices;

    public int Width => width;
    public int Height => height;
    public IReadOnlyList<ElementPlacement> SceneElements => sceneElements;

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
        [SerializeField] private SceneElement element;
        [SerializeField] private float scale;
        [SerializeField] private bool hasBakedOccupancy;
        [SerializeField] private Vector2Int[] occupiedCells;

        public Vector2Int Cell => cell;
        public SceneElement Element => element;
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

        public ElementPlacement(Vector2Int cell, SceneElement element, float scale, Vector2Int[] occupiedCells)
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

        bool geology = GameDataConfig.IsGeology(element);
        if (!geology && occupiedCells == null)
            return false;

        var placement = new ElementPlacement(cell, element, geology ? 1f : scale, geology ? null : occupiedCells);
        int index = geology ? FindPlacement(cell, true) : -1;
        if (index < 0)
        {
            sceneElements.Add(placement);
            return true;
        }

        ElementPlacement existing = sceneElements[index];
        if (existing.Element == element)
            return false;

        sceneElements[index] = placement;
        return true;
    }

    public bool SetSceneElementTransform(int placementIndex, float scale, Vector2Int[] occupiedCells)
    {
        if (placementIndex < 0 || placementIndex >= sceneElements.Count)
            return false;

        ElementPlacement existing = sceneElements[placementIndex];
        if (existing.Element == SceneElement.None || existing.IsGeology || occupiedCells == null ||
            (existing.HasBakedOccupancy && existing.Scale.Equals(scale)))
            return false;

        sceneElements[placementIndex] = new ElementPlacement(existing.Cell, existing.Element, scale, occupiedCells);
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
        return !ContainsCell(cell) || (GetManualOccupancyIndices().TryGetValue(cell, out int index)
            && manualOccupancy[index].blocksSight);
    }

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
        if (!ContainsCell(cell))
            return false;

        if (TryGetManualOccupancy(cell, out bool blocked))
            return !blocked;

        foreach (var placement in sceneElements)
        {
            if (!CanOccupyCells(placement))
                continue;

            Vector2Int relativeCell = cell - placement.Cell;
            var occupiedCells = placement.OccupiedCellsFromAnchor;
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                if (occupiedCells[i] == relativeCell && placement.Element != ignoredElement)
                    return false;
            }
        }

        return true;
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
        return placement.Element != SceneElement.None && !placement.IsGeology && ContainsCell(placement.Cell);
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
    }

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        manualOccupancyIndices = null;
    }
}
