using System.Collections.Generic;
using UnityEngine;

public class DigAdventureGameBoard : MonoBehaviour
{
    private const int MinSpawnDistance = 5;
    private static readonly Vector2Int[] NeighborOffsets =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    [SerializeField] private LevelData level;
    [SerializeField, Range(0f, 1f), Tooltip("地图中心的水平屏幕比例，从左向右。")]
    private float screenX = 0.5f;
    [SerializeField, Range(0f, 1f), Tooltip("地图中心的垂直屏幕比例，从下向上。")]
    private float screenY = 0.5f;
    [SerializeField] private Sprite[] elementSprites;
    [SerializeField] private Transform geologyRoot;
    [SerializeField] private Transform sceneryRoot;
    [SerializeField] private Transform monsterRoot;
    [SerializeField] private SlimeView slimePrefab;
    [SerializeField] private WolfView wolfPrefab;
    [SerializeField] private AdventurerView adventurerPrefab;

    private void Start()
    {
        var camera = Camera.main;
        if (camera == null || level == null)
        {
            Debug.LogWarning("生成地图需要主摄像机和关卡数据。", this);
            return;
        }

        if (!FitGrid(camera)) return;
        if (!geologyRoot || !sceneryRoot || !monsterRoot || !slimePrefab || !wolfPrefab || !adventurerPrefab)
        {
            Debug.LogError("请绑定 BoardRoot 下的三个容器、Slime、Wolf 和 Adventurer 预制体。", this);
            return;
        }
        Vector2 center = new Vector2(level.Width - 1, level.Height - 1) * 0.5f;
        for (int i = 0; i < level.SceneElements.Count; i++)
        {
            var element = level.SceneElements[i];
            int index = element.ElementId;
            if (elementSprites == null || index >= elementSprites.Length || index < 0 || !elementSprites[index])
                continue;
            Sprite sprite = elementSprites[index];
            float size = element.IsGeology ? 1f : element.BoundsInCells.width;
            float scale = size / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var tile = new GameObject(GameDataConfig.GetElementName(index));
            tile.transform.SetParent(element.IsGeology ? geologyRoot : sceneryRoot, false);
            tile.transform.localScale = Vector3.one * scale;
            tile.transform.localPosition = new Vector3(element.Cell.x - center.x, center.y - element.Cell.y)
                - sprite.bounds.center * scale;
            var renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = element.IsGeology ? -1 : i;
        }
        SpawnCharacters(center);
    }

    private bool FitGrid(Camera camera)
    {
        var plane = new Plane(Vector3.forward, transform.position);
        Ray bottomLeft = camera.ViewportPointToRay(Vector3.zero);
        Ray topRight = camera.ViewportPointToRay(Vector3.one);
        if (!plane.Raycast(bottomLeft, out float near) || !plane.Raycast(topRight, out float far))
        {
            Debug.LogWarning("摄像机没有朝向地图平面。", this);
            return false;
        }
        Vector3 min = bottomLeft.GetPoint(near);
        Vector3 max = topRight.GetPoint(far);
        float width = max.x - min.x;
        float height = max.y - min.y;
        // 在目标中心两侧各留出半张地图；端点比例则贴边显示一个完整网格。
        float cellSize = Mathf.Min(width / level.Width, height / level.Height);
        if (screenX > 0f && screenX < 1f)
            cellSize = Mathf.Min(cellSize, 2f * width * Mathf.Min(screenX, 1f - screenX) / level.Width);
        if (screenY > 0f && screenY < 1f)
            cellSize = Mathf.Min(cellSize, 2f * height * Mathf.Min(screenY, 1f - screenY) / level.Height);
        float halfWidth = level.Width * cellSize / 2f;
        float halfHeight = level.Height * cellSize / 2f;
        transform.position = new Vector3(
            Mathf.Clamp(Mathf.Lerp(min.x, max.x, screenX), min.x + halfWidth, max.x - halfWidth),
            Mathf.Clamp(Mathf.Lerp(min.y, max.y, screenY), min.y + halfHeight, max.y - halfHeight),
            transform.position.z);
        transform.localScale = Vector3.one * cellSize;
        return true;
    }

    private void SpawnCharacters(Vector2 center)
    {
        int count = level.Width * level.Height;
        int start = Random.Range(0, count);
        Vector2Int? adventurerCell = null;
        long requested = (long)level.SlimeCount + level.WolfCount;
        int needed = (int)System.Math.Min(count - 1, requested);
        var spawnCells = new List<Vector2Int>();
        var candidates = new List<Vector2Int>();
        var frontier = new Queue<(Vector2Int cell, int distance)>();
        var visited = new HashSet<Vector2Int>();

        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            var cell = new Vector2Int(index % level.Width, index / level.Width);
            if (!level.IsCellWalkable(cell)) continue;

            if (!adventurerCell.HasValue)
                adventurerCell = cell;

            if (needed == 0) break;
            FindMonsterSpawns(cell, needed, frontier, visited, candidates);
            if (candidates.Count > spawnCells.Count)
            {
                adventurerCell = cell;
                var previous = spawnCells;
                spawnCells = candidates;
                candidates = previous;
                if (spawnCells.Count == needed) break;
            }
        }

        if (!adventurerCell.HasValue)
        {
            Debug.LogWarning("当前关卡没有可供 Adventurer 出生的通行格。", this);
            return;
        }

        var origin = monsterRoot.TransformPoint(new Vector3(-center.x, center.y));
        var adventurer = Instantiate(adventurerPrefab, SpawnPosition(adventurerCell.Value, center),
            monsterRoot.rotation, monsterRoot);
        adventurer.Initialize(level, origin, monsterRoot.lossyScale.x);
        adventurer.GetComponent<SpriteRenderer>().sortingOrder = level.SceneElements.Count + 1;

        for (int i = 0; i < spawnCells.Count; i++)
        {
            MonsterPatrolView prefab = i < level.SlimeCount ? (MonsterPatrolView)slimePrefab : wolfPrefab;
            var monster = Instantiate(prefab, SpawnPosition(spawnCells[i], center), monsterRoot.rotation, monsterRoot);
            monster.Initialize(level, origin, monsterRoot.lossyScale.x, adventurer);
            monster.GetComponent<SpriteRenderer>().sortingOrder = level.SceneElements.Count;
        }
        if (spawnCells.Count < requested)
            Debug.LogWarning($"怪物出生格不足：配置 {requested} 只，实际生成 {spawnCells.Count} 只。", this);
    }

    private void FindMonsterSpawns(Vector2Int start, int needed,
        Queue<(Vector2Int cell, int distance)> frontier, HashSet<Vector2Int> visited, List<Vector2Int> result)
    {
        result.Clear();
        frontier.Clear();
        visited.Clear();
        frontier.Enqueue((start, 0));
        visited.Add(start);

        while (frontier.Count > 0)
        {
            var (cell, distance) = frontier.Dequeue();
            if (distance >= MinSpawnDistance)
            {
                result.Add(cell);
                if (result.Count == needed) return;
            }

            foreach (Vector2Int offset in NeighborOffsets)
            {
                Vector2Int next = cell + offset;
                if (visited.Add(next) && level.IsCellWalkable(next))
                    frontier.Enqueue((next, distance + 1));
            }
        }

    }

    private Vector3 SpawnPosition(Vector2Int cell, Vector2 center)
    {
        return monsterRoot.TransformPoint(new Vector3(cell.x - center.x, center.y - cell.y));
    }
}
