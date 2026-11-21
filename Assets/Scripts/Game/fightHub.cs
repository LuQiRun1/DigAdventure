using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Globalization;

[DisallowMultipleComponent]
public class fightHub : MonoBehaviour
{
    private (Button button, UnityAction callback)[] bindings;
    private DigAdventureGameBoard board;
    private Text detectTimeText;
    private Text digTimeText;
    private AdventurerView Adventurer => board ? board.Adventurer : null;

    private void Awake()
    {
        // 路径以当前挂载脚本的 FightUI 为起点。
        bindings = new (Button, UnityAction)[]
        {
            (FindButton("down/backpackBtn"), OnBackpackClick),
            (FindButton("Info/pauseBtn"), OnPauseClick),
            (FindButton("prop/detectBtn"), OnDetectClick),
            (FindButton("prop/digBtn"), OnDigClick)
        };
        detectTimeText = FindTimeText("prop/detectBtn/timeText");
        digTimeText = FindTimeText("prop/digBtn/timeText");
        board = FindObjectOfType<DigAdventureGameBoard>();
    }

    private Text FindTimeText(string path)
    {
        var target = transform.Find(path);
        if (target && target.TryGetComponent<Text>(out var label))
            return label;

        Debug.LogError($"FightUI/{path} 找不到 Text 组件。", this);
        return null;
    }

    private Button FindButton(string path)
    {
        var target = transform.Find(path);
        if (target && target.TryGetComponent<Button>(out var button))
            return button;

        Debug.LogError($"FightUI/{path} 找不到 Button 组件。", this);
        return null;
    }

    private void OnEnable() => SetListening(true);

    private void OnDisable() => SetListening(false);

    private void SetListening(bool listen)
    {
        foreach (var (button, callback) in bindings)
        {
            if (!button) continue;

            if (listen)
                button.onClick.AddListener(callback);
            else
                button.onClick.RemoveListener(callback);
        }
    }

    public void OnBackpackClick()
    {
        var adventurer = Adventurer;
        if (adventurer) adventurer.CancelShovelAction();
    }

    public void OnPauseClick()
    {
        var adventurer = Adventurer;
        if (adventurer) adventurer.CancelShovelAction();
    }

    public void OnDetectClick()
    {
        var adventurer = Adventurer;
        if (adventurer) adventurer.TryStartScan();
        RefreshTimers();
    }

    public void OnDigClick()
    {
        var adventurer = Adventurer;
        if (adventurer) adventurer.TryDigCurrentCell();
        RefreshTimers();
    }

    private void LateUpdate() => RefreshTimers();

    private void RefreshTimers()
    {
        var adventurer = Adventurer;
        if (!adventurer || !adventurer.HasShovel) return;

        if (detectTimeText)
            detectTimeText.text = FormatSeconds(adventurer.ScanSecondsRemaining);
        if (digTimeText)
            digTimeText.text = adventurer.UsesManualDig
                ? $"{adventurer.DigClicksRemaining}次"
                : FormatSeconds(adventurer.DigSecondsRemaining);
    }

    private static string FormatSeconds(float seconds) =>
        (Mathf.Ceil(seconds * 10f) / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "s";
}
