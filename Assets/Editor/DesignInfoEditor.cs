using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class DesignInfoEditor : EditorWindow
{
    private static string Folder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "DesignNotes");
    private static readonly string[] Defaults = { "场景元素", "怪物", "地质", "物品" };
    private readonly List<string> categories = new List<string>();
    private readonly Dictionary<string, List<KeyValuePair<string, string>>> entries = new Dictionary<string, List<KeyValuePair<string, string>>>();
    [SerializeField] private List<string> expanded = new List<string> { "场景元素" };
    [SerializeField] private string selected;
    [SerializeField] private string draft = "";
    [SerializeField] private string original = "";
    private string newName = "";
    private Vector2 listScroll, textScroll;
    private GUIStyle noteStyle;
    private static readonly string ChestKey = Path.Combine("物品", nameof(GameDataConfig.ItemType.TreasureChest));
    internal static readonly string[] QualityNames = { "白色", "绿色", "蓝色", "紫色", "红色" };
    [SerializeField] private bool chestsExpanded;
    private ChestCatalog chestCatalog;
    private readonly Dictionary<string, MonsterPatrolView> monsterPrefabs = new Dictionary<string, MonsterPatrolView>();
    private readonly Dictionary<string, Sprite> previews = new Dictionary<string, Sprite>();

    [MenuItem("Tools/信息编辑器")]
    private static void Open() => GetWindow<DesignInfoEditor>("信息编辑器");

    private void OnEnable()
    {
        minSize = new Vector2(470, 260);
        saveChangesMessage = "保存当前类型的设计笔记？";
        RefreshEntries();
        chestCatalog = Resources.Load<ChestCatalog>("ChestCatalog");
        hasUnsavedChanges = draft != original;
    }

    private void OnProjectChange()
    {
        RefreshEntries();
        Repaint();
    }

    private void RefreshEntries()
    {
        categories.Clear();
        categories.AddRange(Defaults);
        if (Directory.Exists(Folder))
            categories.AddRange(Directory.GetFiles(Folder, "*.txt").Select(Path.GetFileNameWithoutExtension)
                .Except(Defaults, StringComparer.OrdinalIgnoreCase).OrderBy(name => name));
        entries.Clear();
        monsterPrefabs.Clear();
        previews.Clear();
        var sprites = new Dictionary<int, Sprite>();
        LevelEditor.LoadElementSprites(sprites);
        foreach (string category in categories)
        {
            entries[category] = new List<KeyValuePair<string, string>>();
            if (File.Exists(NotePath(category)) || selected == category || !Defaults.Contains(category))
                entries[category].Add(new KeyValuePair<string, string>(category, "栏目笔记"));
        }
        foreach (GameDataConfig.SceneElement element in Enum.GetValues(typeof(GameDataConfig.SceneElement)))
        {
            if (element == GameDataConfig.SceneElement.None) continue;
            sprites.TryGetValue((int)element, out Sprite sprite);
            AddEntry("场景元素", element.ToString(), ObjectNames.NicifyVariableName(element.ToString()), sprite);
        }
        foreach (GameDataConfig.GeologyElement element in Enum.GetValues(typeof(GameDataConfig.GeologyElement)))
        {
            if (element == GameDataConfig.GeologyElement.None) continue;
            sprites.TryGetValue((int)element, out Sprite sprite);
            AddEntry("地质", element.ToString(), ObjectNames.NicifyVariableName(element.ToString()), sprite);
        }
        AddEntry("物品", nameof(GameDataConfig.ItemType.Key), "钥匙", null);
        AddEntry("物品", nameof(GameDataConfig.ItemType.Coin), "金币", null);
        AddEntry("物品", nameof(GameDataConfig.ItemType.TreasureChest), "宝箱", null);
        AddEntry("物品", nameof(GameDataConfig.ItemType.Shovel), "铲子", null);
        foreach (string guid in AssetDatabase.FindAssets("t:MonsterData", new[] { "Assets" }))
        {
            var monster = AssetDatabase.LoadAssetAtPath<MonsterData>(AssetDatabase.GUIDToAssetPath(guid));
            if (monster != null) AddEntry("怪物", guid, monster.name, monster.PreviewSprite);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Res/Prefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null && prefab.TryGetComponent(out MonsterPatrolView monster))
            {
                monsterPrefabs[Path.Combine("怪物", guid)] = monster;
                AddEntry("怪物", guid, prefab.name, prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite);
            }
        }
    }

    private void AddEntry(string category, string id, string label, Sprite sprite)
    {
        string key = Path.Combine(category, id);
        entries[category].Add(new KeyValuePair<string, string>(key, label));
        previews[key] = sprite;
    }

    private static string NotePath(string name) => Path.Combine(Folder, name + ".txt");

    private void Select(string name)
    {
        if (name == selected) return;
        if (hasUnsavedChanges && !WriteNote()) return;
        string path = NotePath(name);
        try
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            GUI.FocusControl(null);
            selected = name;
            original = draft = text;
            textScroll = Vector2.zero;
        }
        catch (IOException e) { ShowNotification(new GUIContent(e.Message)); }
    }

    private bool WriteNote()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NotePath(selected)));
            File.WriteAllText(NotePath(selected), draft);
            original = draft;
            base.SaveChanges();
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            EditorUtility.DisplayDialog("保存失败", e.Message, "确定");
            return false;
        }
    }

    public override void SaveChanges() => WriteNote();

    private void AddCategory()
    {
        string name = newName.Trim();
        if (name.Length == 0 || name.EndsWith(".", StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || categories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ShowNotification(new GUIContent("请输入有效且不重复的类型名称"));
            return;
        }
        if (hasUnsavedChanges && !WriteNote()) return;
        try
        {
            Directory.CreateDirectory(Folder);
            using (File.Open(NotePath(name), FileMode.CreateNew, FileAccess.Write)) { }
            RefreshEntries();
            expanded.Add(name);
            Select(name);
            newName = "";
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            EditorUtility.DisplayDialog("新增类型失败", e.Message, "确定");
        }
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(200)))
            {
                GUILayout.Label("信息类型", EditorStyles.boldLabel);
                using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll))
                {
                    listScroll = scroll.scrollPosition;
                    foreach (string name in categories)
                    {
                        bool open = expanded.Contains(name);
                        Rect categoryRow = TreeRow(0, false);
                        if (EditorGUI.Foldout(categoryRow, open, name, true, EditorStyles.foldoutHeader) != open)
                        {
                            if (open) expanded.Remove(name); else expanded.Add(name);
                            open = !open;
                        }
                        if (!open) continue;
                        foreach (var entry in entries[name])
                        {
                            if (entry.Key == ChestKey)
                            {
                                Rect chestRow = TreeRow(1, selected == ChestKey || (!chestsExpanded && Path.GetDirectoryName(selected ?? "") == ChestKey));
                                bool openChest = EditorGUI.Foldout(chestRow, chestsExpanded, "宝箱", true);
                                if (openChest && !chestsExpanded) Select(ChestKey);
                                chestsExpanded = openChest;
                                if (openChest)
                                {
                                    foreach (GameDataConfig.ItemQuality quality in Enum.GetValues(typeof(GameDataConfig.ItemQuality)))
                                    {
                                        string key = Path.Combine(ChestKey, quality.ToString());
                                        DrawTreeItem(key, QualityNames[(int)quality] + "宝箱", 2);
                                    }
                                }
                                continue;
                            }
                            DrawTreeItem(entry.Key, entry.Value, 1);
                        }
                    }
                }
                newName = EditorGUILayout.TextField(newName);
                if (GUILayout.Button("新增类型")) AddCategory();
            }
            using (new EditorGUILayout.VerticalScope())
            {
                if (string.IsNullOrEmpty(selected))
                {
                    EditorGUILayout.HelpBox("展开左侧栏目，选择要编辑的条目。", MessageType.Info);
                    return;
                }
                bool isChestTier = Enum.TryParse(Path.GetFileName(selected), out GameDataConfig.ItemQuality chestQuality)
                    && Path.GetDirectoryName(selected) == ChestKey;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string category = Path.GetDirectoryName(selected);
                    string title = selected;
                    if (!string.IsNullOrEmpty(category) && entries.TryGetValue(category, out var items))
                        title = category + " / " + (items.Find(item => item.Key == selected).Value ?? Path.GetFileName(selected));
                    GUILayout.Label(isChestTier ? "物品 / 宝箱 / " + QualityNames[(int)chestQuality] : title, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                        if (GUILayout.Button("保存")) SaveChanges();
                }
                if (previews.TryGetValue(selected, out Sprite preview))
                {
                    if (preview != null)
                    {
                        Rect area = GUILayoutUtility.GetRect(1f, Mathf.Min(150f, position.height * 0.3f), GUILayout.ExpandWidth(true));
                        if (Event.current.type == EventType.Repaint) LevelEditor.DrawSpritePreview(area, preview);
                    }
                    else EditorGUILayout.LabelField("暂无预览图", EditorStyles.centeredGreyMiniLabel);
                }
                if (Path.GetDirectoryName(selected) == "场景元素" &&
                    Enum.TryParse(Path.GetFileName(selected), out GameDataConfig.SceneElement element))
                    DrawElementRules(element);
                if (monsterPrefabs.TryGetValue(selected, out var monster) && monster)
                    DrawMonsterSenses(monster);
                using (var scroll = new EditorGUILayout.ScrollViewScope(textScroll))
                {
                    textScroll = scroll.scrollPosition;
                    if (isChestTier) DrawChestSettings(chestQuality);
                    else if (Path.GetDirectoryName(selected) == "物品" && selected != ChestKey &&
                        Enum.TryParse(Path.GetFileName(selected), out GameDataConfig.ItemType itemType))
                        DrawOrdinaryItem(itemType);
                    if (noteStyle == null) noteStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    draft = EditorGUILayout.TextArea(draft, noteStyle, GUILayout.ExpandHeight(true));
                    hasUnsavedChanges = draft != original;
                }
            }
        }
    }

    private static Rect TreeRow(int depth, bool active)
    {
        Rect row = EditorGUILayout.GetControlRect(false, depth == 0 ? 23f : 20f);
        if (active) EditorGUI.DrawRect(row, new Color(0.24f, 0.51f, 0.85f, 0.35f));
        for (int i = 0; i < depth; i++)
            EditorGUI.DrawRect(new Rect(row.x + 7f + i * 16f, row.y, 1f, row.height),
                new Color(0.5f, 0.5f, 0.5f, 0.32f));
        row.xMin += depth * 16f;
        return row;
    }

    private void DrawTreeItem(string key, string label, int depth)
    {
        Rect row = TreeRow(depth, selected == key);
        Rect text = row;
        text.xMin += 14f; // 为折叠箭头保留空间，与同级分支的文字对齐。
        if (GUI.Button(row, GUIContent.none, GUIStyle.none)) Select(key);
        GUI.Label(text, label, selected == key ? EditorStyles.boldLabel : EditorStyles.label);
    }

    private void DrawChestSettings(GameDataConfig.ItemQuality quality)
    {
        if (!chestCatalog)
        {
            EditorGUILayout.HelpBox("缺少 ChestCatalog 宝箱配置。", MessageType.Error);
            return;
        }
        using (var data = new SerializedObject(chestCatalog))
        {
            var tier = data.FindProperty("tiers").GetArrayElementAtIndex((int)quality);
            DrawItemSettings(tier, quality);
            EditorGUILayout.LabelField("出现的地下深度（含两端）", EditorStyles.boldLabel);
            var minDepth = tier.FindPropertyRelative("minDepth");
            var maxDepth = tier.FindPropertyRelative("maxDepth");
            minDepth.intValue = Mathf.Max(1, EditorGUILayout.DelayedIntField("最浅层数", minDepth.intValue));
            int deepest = Mathf.Max(0, EditorGUILayout.DelayedIntField("最深层数（0 为不限）", maxDepth.intValue));
            maxDepth.intValue = deepest == 0 ? 0 : Mathf.Max(minDepth.intValue, deepest);

            var capacity = tier.FindPropertyRelative("capacity");
            Vector2Int size = capacity.vector2IntValue;
            EditorGUILayout.LabelField("宝箱内部容量", EditorStyles.boldLabel);
            size.x = Mathf.Max(1, EditorGUILayout.DelayedIntField("列数", size.x));
            size.y = Mathf.Max(1, EditorGUILayout.DelayedIntField("行数", size.y));
            capacity.vector2IntValue = size;
            DrawCapacityPreview(size);

            EditorGUILayout.LabelField("掉落品质（权重 / 实际概率）", EditorStyles.boldLabel);
            var weights = tier.FindPropertyRelative("weights");
            long total = 0;
            for (int i = 0; i < weights.arraySize; i++) total += weights.GetArrayElementAtIndex(i).intValue;
            for (int i = 0; i < QualityNames.Length; i++)
            {
                var weight = weights.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(QualityNames[i], GUILayout.Width(42));
                    int next = Mathf.Max(0, EditorGUILayout.DelayedIntField(weight.intValue));
                    if (next != weight.intValue && total - weight.intValue + next > 0) weight.intValue = next;
                    double chance = chestCatalog.Get(quality).Probability((GameDataConfig.ItemQuality)i);
                    GUILayout.Label(chance.ToString("P1"), GUILayout.Width(64));
                }
            }
            if (data.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(chestCatalog);
        }
        EditorGUILayout.LabelField("设计笔记", EditorStyles.boldLabel);
    }

    private void DrawOrdinaryItem(GameDataConfig.ItemType type)
    {
        if (!chestCatalog) return;
        using (var data = new SerializedObject(chestCatalog))
        {
            DrawItemSettings(data.FindProperty(type.ToString().ToLowerInvariant()), null);
            if (data.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(chestCatalog);
        }
        EditorGUILayout.LabelField("设计笔记", EditorStyles.boldLabel);
    }

    private static void DrawItemSettings(SerializedProperty item, GameDataConfig.ItemQuality? fixedQuality)
    {
        var quality = item.FindPropertyRelative("quality");
        if (fixedQuality.HasValue)
            EditorGUILayout.LabelField("物品品质", QualityNames[(int)fixedQuality.Value]);
        else
            quality.enumValueIndex = EditorGUILayout.Popup("物品品质", quality.enumValueIndex, QualityNames);
        var description = item.FindPropertyRelative("description");
        EditorGUILayout.PropertyField(description, new GUIContent("物品描述"));
        var shape = item.FindPropertyRelative("shape");
        EditorGUILayout.LabelField("背包占格（点击切换）", EditorStyles.boldLabel);
        for (int y = 0; y < ChestCatalog.ShapeSize; y++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int x = 0; x < ChestCatalog.ShapeSize; x++)
                {
                    int bit = 1 << (y * ChestCatalog.ShapeSize + x);
                    bool used = (shape.intValue & bit) != 0;
                    bool next = GUILayout.Toggle(used, used ? "■" : "", "Button", GUILayout.Width(24), GUILayout.Height(24));
                    if (next != used && (next || shape.intValue != bit)) shape.intValue ^= bit;
                }
            }
        }
        int cells = 0;
        for (int bits = shape.intValue; bits != 0; bits &= bits - 1) cells++;
        if (cells == 1)
        {
            var cost = item.FindPropertyRelative("singleCellCost");
            float next = EditorGUILayout.DelayedFloatField("单件占用（格）", cost.floatValue);
            if (next > 0f && next <= 1f) cost.floatValue = next;
            EditorGUILayout.LabelField($"每格可装 {ChestCatalog.Item.StackLimitFor(cost.floatValue)} 个");
        }
        else EditorGUILayout.LabelField($"单件占用 {cells} 格");
    }

    private static void DrawCapacityPreview(Vector2Int size)
    {
        float step = Mathf.Min(18f, 180f / size.x, 120f / size.y);
        Rect rect = GUILayoutUtility.GetRect(size.x * step, size.y * step, GUILayout.ExpandWidth(false));
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 0.65f));
        // 格线密于一个像素时只绘制底色，避免超大容量拖慢窗口。
        if (step < 1f) return;
        for (int x = 0; x <= size.x; x++)
            EditorGUI.DrawRect(new Rect(rect.x + x * step, rect.y, 1, rect.height), Color.gray);
        for (int y = 0; y <= size.y; y++)
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + y * step, rect.width, 1), Color.gray);
    }

    private static void DrawElementRules(GameDataConfig.SceneElement element)
    {
        var rules = SceneElementRules.Shared;
        if (!rules) return;
        var previous = rules.Get(element);
        var updated = SceneElementRules.Traits.None;
        void Toggle(string label, SceneElementRules.Traits trait)
        {
            bool enabled = (previous & trait) != 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.ExpandWidth(true));
                if (GUILayout.Toggle(enabled, "是", EditorStyles.radioButton, GUILayout.Width(44)))
                    enabled = true;
                if (GUILayout.Toggle(!enabled, "否", EditorStyles.radioButton, GUILayout.Width(44)))
                    enabled = false;
            }
            if (enabled) updated |= trait;
        }
        Toggle("可穿过", SceneElementRules.Traits.Passable);
        Toggle("阻挡视觉", SceneElementRules.Traits.BlocksSight);
        Toggle("阻挡嗅觉", SceneElementRules.Traits.BlocksSmell);
        Toggle("阻挡听觉", SceneElementRules.Traits.BlocksHearing);
        if (updated == previous) return;
        Undo.RecordObject(rules, "修改场景元素参数");
        rules.Set(element, updated);
        EditorUtility.SetDirty(rules);
        AssetDatabase.SaveAssetIfDirty(rules);
    }

    private static void DrawMonsterSenses(MonsterPatrolView monster)
    {
        using (var data = new SerializedObject(monster))
        {
            EditorGUILayout.PropertyField(data.FindProperty("visionRange"), new GUIContent("视觉距离（格）", "按曼哈顿距离检查前方及左右两侧，背后不可见。视线可被遮挡，0 表示关闭。"));
            EditorGUILayout.PropertyField(data.FindProperty("hearingRange"), new GUIContent("听觉距离（格）", "只能听到正在移动的冒险者。0 表示关闭。"));
            EditorGUILayout.PropertyField(data.FindProperty("smellRange"), new GUIContent("嗅觉距离（格）", "搜索冒险者最近 3 秒留下的气味，仅靠嗅觉时沿有气味的格子追踪。0 表示关闭。"));
            EditorGUILayout.PropertyField(data.FindProperty("patrolAllowsPassableElements"), new GUIContent("巡逻可进入可穿过元素"));
            EditorGUILayout.PropertyField(data.FindProperty("chaseAllowsPassableElements"), new GUIContent("追击可进入可穿过元素"));
            if (data.ApplyModifiedProperties()) PrefabUtility.SavePrefabAsset(monster.gameObject);
        }
    }
}
