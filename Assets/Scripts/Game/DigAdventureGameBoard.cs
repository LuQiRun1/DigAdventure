using UnityEngine;

public class DigAdventureGameBoard : MonoBehaviour
{
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

    private void Start()
    {
        var camera = Camera.main;
        if (camera == null || level == null)
        {
            Debug.LogWarning("生成地图需要主摄像机和关卡数据。", this);
            return;
        }

        if (!FitGrid(camera)) return;
        if (!geologyRoot || !sceneryRoot || !monsterRoot || !slimePrefab)
        {
            Debug.LogError("请绑定 BoardRoot 下的三个容器和 Slime 预制体。", this);
            return;
        }
        Vector2 center = new Vector2(level.Width - 1, level.Height - 1) * 0.5f;
        for (int i = 0; i < level.SceneElements.Count; i++)
        {
            var element = level.SceneElements[i];
            int index = (int)element.Element;
            if (elementSprites == null || index >= elementSprites.Length || index < 0 || !elementSprites[index])
                continue;
            Sprite sprite = elementSprites[index];
            float size = element.IsGeology ? 1f : element.BoundsInCells.width;
            float scale = size / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var tile = new GameObject(element.Element.ToString());
            tile.transform.SetParent(element.IsGeology ? geologyRoot : sceneryRoot, false);
            tile.transform.localScale = Vector3.one * scale;
            tile.transform.localPosition = new Vector3(element.Cell.x - center.x, center.y - element.Cell.y)
                - sprite.bounds.center * scale;
            var renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = element.IsGeology ? -1 : i;
        }
        SpawnSlime(center);
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

    private void SpawnSlime(Vector2 center)
    {
        int count = level.Width * level.Height;
        int start = (level.Height / 2) * level.Width + level.Width / 2;
        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            var cell = new Vector2Int(index % level.Width, index / level.Width);
            if (!level.IsCellWalkable(cell)) continue;

            var origin = monsterRoot.TransformPoint(new Vector3(-center.x, center.y));
            var position = monsterRoot.TransformPoint(new Vector3(cell.x - center.x, center.y - cell.y));
            var slime = Instantiate(slimePrefab, position, monsterRoot.rotation, monsterRoot);
            slime.Initialize(level, origin, monsterRoot.lossyScale.x);
            slime.GetComponent<SpriteRenderer>().sortingOrder = level.SceneElements.Count;
            return;
        }
        Debug.LogWarning("当前关卡没有可供 Slime 出生的通行格。", this);
    }
}
