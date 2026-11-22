using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class BackpackPiece : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal List<Vector2Int> Shape { get; private set; }
    internal Vector2Int Anchor { get; private set; }
    public int Quantity { get; private set; }
    internal ChestCatalog.Item Item { get; private set; }
    internal string DisplayName { get; private set; }
    private BackpackPanel panel;
    private Vector2 grab;
    private readonly List<Image> iconBackplates = new List<Image>();
    private bool dragging;

    internal void Build(BackpackPanel owner, List<Vector2Int> shape, Vector2Int anchor, string label, ChestCatalog.Item item, int quantity)
    {
        panel = owner;
        Shape = shape;
        Anchor = anchor;
        Quantity = quantity;
        Item = item;
        DisplayName = label;
        Sprite sprite = item.PreviewSprite;
        transform.localPosition = panel.Position(anchor);
        foreach (var offset in shape)
        {
            var block = new GameObject("占格", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)block.transform;
            rect.SetParent(transform, false);
            rect.sizeDelta = panel.CellSize - Vector2.one * 4;
            rect.localPosition = new Vector2(offset.x * panel.Pitch.x, -offset.y * panel.Pitch.y);
            var backplate = block.GetComponent<Image>();
            backplate.color = new Color(0.32f, 0.44f, 0.52f);
            if (sprite) iconBackplates.Add(backplate);
            if (sprite || offset != shape[0]) continue;
            var text = new GameObject("名称", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(rect, false);
            text.rectTransform.sizeDelta = rect.sizeDelta;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
        }
        if (sprite) AddIcon(sprite);
        if (quantity > 1)
        {
            var count = new GameObject("堆叠数量", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            count.transform.SetParent(transform, false);
            count.rectTransform.sizeDelta = panel.CellSize - Vector2.one * 6;
            count.rectTransform.localPosition = new Vector2(shape[0].x * panel.Pitch.x, -shape[0].y * panel.Pitch.y);
            count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            count.fontSize = 20;
            count.alignment = TextAnchor.LowerRight;
            count.text = quantity.ToString();
            count.raycastTarget = false;
        }
    }

    private void AddIcon(Sprite sprite)
    {
        // 在完全被物品占用的矩形中，选择能完整容纳最大等比图标的一个。
        float bestScale = 0f;
        Vector2 center = Vector2.zero;
        foreach (var start in Shape)
        {
            int width = ChestCatalog.ShapeSize;
            for (int height = 1; height <= ChestCatalog.ShapeSize; height++)
            {
                int rowWidth = 0;
                while (rowWidth < width && Shape.Contains(start + new Vector2Int(rowWidth, height - 1)))
                    rowWidth++;
                width = rowWidth;
                if (width == 0) break;
                Vector2 extra = Vector2.Scale(panel.Pitch, new Vector2(width - 1, height - 1));
                Vector2 available = panel.CellSize + extra - Vector2.one * 4f;
                float scale = Mathf.Min(available.x / sprite.rect.width, available.y / sprite.rect.height);
                if (scale <= bestScale) continue;
                bestScale = scale;
                center = new Vector2(start.x * panel.Pitch.x + extra.x / 2f,
                    -start.y * panel.Pitch.y - extra.y / 2f);
            }
        }
        var icon = new GameObject("物品图标", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(transform, false);
        icon.rectTransform.localPosition = center;
        icon.rectTransform.sizeDelta = sprite.rect.size * bestScale;
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && !e.dragging)
            panel.ShowDetails(this);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || dragging) return;
        panel.Pointer(e.pressPosition, e.pressEventCamera, out Vector2 point);
        grab = point - (Vector2)transform.localPosition;
        transform.SetAsLastSibling();
        dragging = true;
        foreach (var backplate in iconBackplates) backplate.canvasRenderer.SetAlpha(0f);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging) return;
        bool inside = panel.Pointer(e.position, e.pressEventCamera, out Vector2 point);
        transform.localPosition = point - grab;
        panel.Preview(this, point - grab, inside, out _);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        bool inside = panel.Pointer(e.position, e.pressEventCamera, out Vector2 point);
        if (panel.Preview(this, point - grab, inside, out var candidate)) Anchor = candidate;
        Finish();
    }

    private void OnDisable() { if (dragging) Finish(); }
    private void Finish()
    {
        dragging = false;
        foreach (var backplate in iconBackplates) backplate.canvasRenderer.SetAlpha(1f);
        panel.ClearPreview();
        transform.localPosition = panel.Position(Anchor);
    }
}
