using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class CanvasBackgroundSetup : MonoBehaviour
{
    [SerializeField] private RectTransform background;

    private void Start()
    {
        var canvas = GetComponent<Canvas>();
        var renderCamera = canvas.worldCamera ? canvas.worldCamera : Camera.main;
        if (!renderCamera)
        {
            Debug.LogError("战斗 Canvas 需要绑定渲染相机，或场景中需要启用 Main Camera。", this);
            return;
        }

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = renderCamera;

        if (!background) return;
        ResetDepth(background);
        foreach (Transform child in background)
            ResetDepth(child);
    }

    private static void ResetDepth(Transform item)
    {
        Vector3 position = item.localPosition;
        position.z = 0f;
        item.localPosition = position;
    }
}
