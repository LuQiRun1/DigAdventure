using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class BackpackPanel : MonoBehaviour
{
    private readonly List<Image> cells = new List<Image>();
    private readonly List<BackpackPiece> pieces = new List<BackpackPiece>();
    private readonly Dictionary<Image, Color> highlighted = new Dictionary<Image, Color>();
    private RectTransform content, objects, viewport;
    private GridLayoutGroup grid;
    [SerializeField] private Image detailIcon;
    private Vector3 detailIconBaseScale;
    private Coroutine detailIconAnimation;
    [SerializeField] private Text detailName, detailQuantity, detailDescription;
    private bool seeded;
    [SerializeField, Min(0), Tooltip("测试用初始金币数量，设为 0 可关闭。")]
    private int startingTestCoins = 100;
    private bool coinsSeeded;
    public static BackpackPanel OpenPanel { get; private set; }
    internal Vector2 Pitch => grid.cellSize + grid.spacing;
    internal Vector2 CellSize => grid.cellSize;

    private void Awake()
    {
        detailIcon.preserveAspect = true;
        detailIconBaseScale = detailIcon.rectTransform.localScale;
        ShowDetails(null);
        var scroll = GetComponentInChildren<ScrollRect>(true);
        content = scroll.content;
        viewport = scroll.viewport;
        grid = content.GetComponent<GridLayoutGroup>();
        foreach (RectTransform child in content)
            cells.Add(child.GetComponentInChildren<Image>(true));
        objects = (RectTransform)transform.Find("object");
        objects.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        objects.SetParent(content, false);
        objects.anchorMin = Vector2.zero;
        objects.anchorMax = Vector2.one;
        objects.offsetMin = objects.offsetMax = Vector2.zero;
        transform.Find("closeBtn").GetComponent<Button>().onClick.AddListener(Close);
    }

    public void Open(ChestCatalog.Shovel shovel)
    {
        gameObject.SetActive(true);
        OpenPanel = this;
        int rows = Mathf.CeilToInt((float)cells.Count / grid.constraintCount);
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(viewport.rect.height, grid.padding.vertical + rows * Pitch.y - grid.spacing.y));
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (!seeded && shovel != null) seeded = TryAdd(shovel, shovel.DisplayName);
        if (!coinsSeeded && startingTestCoins > 0)
        {
            var catalog = Resources.Load<ChestCatalog>("ChestCatalog");
            if (catalog)
            {
                var coin = catalog.GetItem(GameDataConfig.ItemType.Coin);
                int remaining = startingTestCoins;
                while (remaining > 0)
                {
                    int stack = Mathf.Min(remaining, coin.StackLimit);
                    if (!TryAdd(coin, "金币", stack)) break;
                    remaining -= stack;
                }
                coinsSeeded = true;
                if (remaining > 0) Debug.LogWarning($"背包空间不足，未放入 {remaining} 个测试金币。", this);
            }
        }
    }

    public void Close() => gameObject.SetActive(false);
    private void OnDisable()
    {
        if (OpenPanel == this) OpenPanel = null;
        ResetDetailIconAnimation();
        ClearPreview();
    }

    public bool TryAdd(ChestCatalog.Item item, string label, int quantity = 1)
    {
        if (item == null || quantity < 1 || quantity > item.StackLimit) return false;
        var shape = new List<Vector2Int>();
        for (int y = 0; y < ChestCatalog.ShapeSize; y++)
            for (int x = 0; x < ChestCatalog.ShapeSize; x++)
                if (item.Occupies(x, y)) shape.Add(new Vector2Int(x, y));
        if (shape.Count == 0) return false;
        Vector2Int min = shape[0];
        foreach (var offset in shape) min = Vector2Int.Min(min, offset);
        for (int i = 0; i < shape.Count; i++) shape[i] -= min;
        for (int i = 0; i < cells.Count; i++)
        {
            var anchor = new Vector2Int(i % grid.constraintCount, i / grid.constraintCount);
            if (!Fits(shape, anchor, null)) continue;
            var obj = new GameObject(label, typeof(RectTransform), typeof(CanvasGroup), typeof(BackpackPiece));
            obj.transform.SetParent(objects, false);
            var piece = obj.GetComponent<BackpackPiece>();
            piece.Build(this, shape, anchor, label, item, quantity);
            pieces.Add(piece);
            return true;
        }
        return false;
    }

    internal void ShowDetails(BackpackPiece piece)
    {
        detailIcon.sprite = piece ? piece.Item.PreviewSprite : null;
        detailIcon.enabled = detailIcon.sprite != null;
        detailName.text = piece ? piece.DisplayName : "";
        detailQuantity.text = piece ? piece.Quantity.ToString() : "";
        detailDescription.text = piece ? piece.Item.Description : "";
        ResetDetailIconAnimation();
        if (detailIcon.enabled)
            detailIconAnimation = StartCoroutine(AnimateDetailIcon());
    }

    private IEnumerator AnimateDetailIcon()
    {
        const float duration = 0.16f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = 1f - elapsed / duration;
            detailIcon.rectTransform.localScale = detailIconBaseScale * (1f - 0.14f * t * t);
            yield return null;
        }
        detailIcon.rectTransform.localScale = detailIconBaseScale;
        detailIconAnimation = null;
    }

    private void ResetDetailIconAnimation()
    {
        if (detailIconAnimation == null) return;
        StopCoroutine(detailIconAnimation);
        detailIconAnimation = null;
        detailIcon.rectTransform.localScale = detailIconBaseScale;
    }

    private bool Fits(List<Vector2Int> shape, Vector2Int anchor, BackpackPiece self)
    {
        foreach (var offset in shape)
        {
            var pos = anchor + offset;
            if (pos.x < 0 || pos.x >= grid.constraintCount || pos.y < 0 ||
                pos.y * grid.constraintCount + pos.x >= cells.Count) return false;
            foreach (var piece in pieces)
                if (piece != self && piece.Shape.Contains(pos - piece.Anchor)) return false;
        }
        return true;
    }

    internal Vector2 Position(Vector2Int anchor)
    {
        var first = (RectTransform)content.GetChild(0);
        Vector2 center = objects.InverseTransformPoint(first.TransformPoint(first.rect.center));
        return center + new Vector2(anchor.x * Pitch.x, -anchor.y * Pitch.y);
    }

    internal bool Pointer(Vector2 screen, Camera camera, out Vector2 local)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(objects, screen, camera, out local);
        return RectTransformUtility.RectangleContainsScreenPoint(viewport, screen, camera);
    }

    internal bool Preview(BackpackPiece piece, Vector2 local, bool inside, out Vector2Int anchor)
    {
        ClearPreview();
        Vector2 delta = local - Position(Vector2Int.zero);
        anchor = new Vector2Int(Mathf.RoundToInt(delta.x / Pitch.x), Mathf.RoundToInt(-delta.y / Pitch.y));
        if (!inside || !Fits(piece.Shape, anchor, piece)) return false;
        foreach (var offset in piece.Shape)
        {
            var p = anchor + offset;
            var image = cells[p.y * grid.constraintCount + p.x];
            highlighted.Add(image, image.color);
            image.color = new Color(0.55f, 0.83f, 1f);
        }
        return true;
    }

    internal void ClearPreview()
    {
        foreach (var pair in highlighted) if (pair.Key) pair.Key.color = pair.Value;
        highlighted.Clear();
    }
}
