using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using SceneElement = GameDataConfig.SceneElement;
using GeologyElement = GameDataConfig.GeologyElement;

public sealed class LevelEditor : EditorWindow
{
    private const string LegacyLevelFolder = "Assets/Res/Level";
    private const string LevelFolder = "Assets/Resources/Levels";
    private const string LevelPrefix = "Level_";
    private const string SceneElementsAtlas = "Assets/Res/Art/Sprites/all_scene_props.png";
    private const string TreesAtlas = "Assets/Res/Art/Sprites/trees.png";
    private const string GeologyAtlas = "Assets/Res/Art/Sprites/geology.png";
    private const float BaseCellSize = 24f;
    private const float DefaultSidebarWidth = 244f;
    private const float MinSidebarWidth = 192f;
    private const float MinGridPanelWidth = 280f;
    private const float SplitterWidth = 8f;
    private const float MinGridZoom = 0.5f;
    private const float MaxGridZoom = 4f;
    private const float MinElementScale = 0.25f;
    private const float MaxElementScale = 4f;
    private const float PaletteCardSize = 52f;
    private const float PaletteCardGap = 5f;
    private const float PaletteCardPadding = 5f;
    private const float PaletteScrollGutter = 30f;
    [SerializeField] private LevelData selectedLevel;
    [SerializeField] private string selectedLevelName;
    [SerializeField] private bool gridEditingExpanded = true;
    [SerializeField] private bool monstersExpanded;
    [SerializeField] private bool chestsExpanded;
    private ChestCatalog chestCatalog;
    [SerializeField] private bool sceneElementsExpanded;
    [SerializeField] private bool geologyElementsExpanded;
    [SerializeField] private int selectedElement;
    [SerializeField] private float brushScale = 1f;
    [SerializeField] private float gridZoom = 1f;
    [SerializeField] private bool showAllOccupiedCells;
    [SerializeField] private bool editingGridOccupancy = true;
    [SerializeField] private float sidebarWidth = DefaultSidebarWidth;
    private readonly List<LevelData> levels = new List<LevelData>();
    private readonly Dictionary<string, string> levelSnapshots = new Dictionary<string, string>();
    private readonly Dictionary<string, string> loadedJson = new Dictionary<string, string>();
    private readonly Dictionary<int, Sprite> elementSprites = new Dictionary<int, Sprite>();
    private readonly List<int> scenePalette = new List<int>();
    private readonly List<int> geologyPalette = new List<int>();
    private readonly Dictionary<int, SceneElementFootprint> footprints = new Dictionary<int, SceneElementFootprint>();
    private readonly Dictionary<string, Hash128> footprintAtlasHashes = new Dictionary<string, Hash128>();
    private Vector2 listScroll;
    private Vector2 detailsScroll;
    private Vector2 gridScroll;
    private Vector2 paletteScroll;
    private Vector2 geologyPaletteScroll;
    private float dragStartX;
    private float dragStartWidth;
    private readonly HashSet<Vector2Int> strokeCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> highlightedOccupancy = new HashSet<Vector2Int>();
    private LevelData strokeLevel;
    private Vector2Int? lastStrokeCell;
    private int strokeElement;
    private float strokeScale;
    private Vector2Int[] strokeOccupancy;
    private int editingPlacementIndex = -1;
    private int strokeButton;
    private int strokeControlId;
    private int strokeUndoGroup;
    private bool strokeEditsOccupancy;
    private string strokeUndoName;
    private bool strokeChanged;
    private int moveControlId;
    private LevelData moveLevel;
    private Vector2 moveStartPointer;
    private Vector2Int moveStartCell;
    private bool moveChanged;

    private float GridCellSize => BaseCellSize * gridZoom;
    private float MaxSidebarWidth => Mathf.Max(MinSidebarWidth,
        (position.width - MinGridPanelWidth - SplitterWidth - 24f) * 0.5f);

    [MenuItem("Tools/关卡编辑器")]
    private static void Open()
    {
        var window = GetWindow<LevelEditor>("关卡编辑器");
        window.minSize = new Vector2(696, 300);
    }

    private void OnEnable()
    {
        chestCatalog = Resources.Load<ChestCatalog>("ChestCatalog");
        ReloadSceneElements();
        ReloadLevels();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        FinishElementMove();
        FinishGridStroke();
        Undo.undoRedoPerformed -= OnUndoRedo;
        foreach (var level in levels)
            DestroyImmediate(level);
        levels.Clear();
        levelSnapshots.Clear();
        loadedJson.Clear();
        selectedLevel = null;
    }

    private void OnLostFocus()
    {
        FinishElementMove();
        FinishGridStroke();
    }

    private void OnUndoRedo()
    {
        editingPlacementIndex = -1;
        foreach (var level in levels)
            if (levelSnapshots.TryGetValue(level.name, out string snapshot) &&
                snapshot != JsonUtility.ToJson(level, true))
                SaveLevel(level);
        Repaint();
    }

    private void OnProjectChange()
    {
        chestCatalog = Resources.Load<ChestCatalog>("ChestCatalog");
        ReloadSceneElements();
        ReloadLevels();
        Repaint();
    }

    private void ReloadSceneElements()
    {
        elementSprites.Clear();
        scenePalette.Clear();
        geologyPalette.Clear();
        LoadElementSprites(elementSprites, scenePalette, geologyPalette);
        scenePalette.Sort();
        geologyPalette.Sort();
        InvalidateFootprints(SceneElementsAtlas);
        InvalidateFootprints(TreesAtlas);
    }

    private void InvalidateFootprints(string path)
    {
        Hash128 hash = AssetDatabase.GetAssetDependencyHash(path);
        if (!footprintAtlasHashes.TryGetValue(path, out Hash128 previous) || previous == hash)
            return;

        footprints.Clear();
        footprintAtlasHashes.Clear();
    }

    private Vector2Int[] CalculateOccupancy(int element, float scale)
    {
        if (!elementSprites.TryGetValue(element, out Sprite sprite) || GameDataConfig.IsGeology(element))
            return null;

        if (!footprints.ContainsKey(element))
            LoadAtlasFootprints(sprite);

        if (!footprints.TryGetValue(element, out SceneElementFootprint footprint))
            return null;

        Rect source = sprite.textureRect;
        return footprint.GetOccupiedCells(scale, source.width / source.height);
    }

    private void LoadAtlasFootprints(Sprite sprite)
    {
        string path = AssetDatabase.GetAssetPath(sprite);
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(image, File.ReadAllBytes(path)))
                return;

            Color32[] pixels = image.GetPixels32();
            Texture2D imported = sprite.texture;
            float sourceScaleX = (float)image.width / imported.width;
            float sourceScaleY = (float)image.height / imported.height;
            foreach (int element in scenePalette)
            {
                Sprite candidate = elementSprites[element];
                if (candidate.texture != imported)
                    continue;

                Rect rect = candidate.textureRect;
                int left = Mathf.RoundToInt(rect.xMin * sourceScaleX);
                int right = Mathf.RoundToInt(rect.xMax * sourceScaleX);
                int top = Mathf.RoundToInt(rect.yMax * sourceScaleY);
                int bottom = Mathf.RoundToInt(rect.yMin * sourceScaleY);
                int width = right - left;
                int height = top - bottom;
                var alpha = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = (top - 1 - y) * image.width + left;
                    for (int x = 0; x < width; x++)
                        alpha[y * width + x] = pixels[row + x].a;
                }

                footprints[element] = new SceneElementFootprint(alpha, width, height);
            }
            footprintAtlasHashes[path] = AssetDatabase.GetAssetDependencyHash(path);
        }
        finally
        {
            DestroyImmediate(image);
        }
    }

    internal static void LoadElementSprites(Dictionary<int, Sprite> sprites, List<int> scenes = null, List<int> geology = null)
    {
        LoadSceneElementSprites(SceneElementsAtlas, (int)SceneElement.FloweringBush, (int)SceneElement.BrokenTreeStump, scenes, sprites);
        LoadSceneElementSprites(TreesAtlas, (int)SceneElement.GreenBroadleafTree, (int)SceneElement.GlowingBlueTree, scenes, sprites);
        LoadSceneElementSprites(GeologyAtlas, (int)GeologyElement.GrassTile1, (int)GeologyElement.MossyDirtTile4, geology, sprites);
    }

    private static void LoadSceneElementSprites(string assetPath, int firstElement, int lastElement,
        List<int> palette, Dictionary<int, Sprite> elementSprites)
    {
        string spritePrefix = Path.GetFileNameWithoutExtension(assetPath) + "_";
        int lastIndex = lastElement - firstElement;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is Sprite sprite &&
                sprite.name.StartsWith(spritePrefix, StringComparison.Ordinal) &&
                int.TryParse(sprite.name.Substring(spritePrefix.Length), out int index) &&
                index >= 0 && index <= lastIndex)
            {
                int element = firstElement + index;
                if (!elementSprites.ContainsKey(element))
                    palette?.Add(element);
                elementSprites[element] = sprite;
            }
        }
    }

    private void ReloadLevels()
    {
        var previous = new Dictionary<string, LevelData>();
        foreach (var level in levels)
            previous.Add(level.name, level);
        levels.Clear();
        if (Directory.Exists(LevelFolder))
            foreach (string path in Directory.GetFiles(LevelFolder, LevelPrefix + "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    string json = File.ReadAllText(path);
                    if (previous.TryGetValue(name, out var existing) &&
                        loadedJson.TryGetValue(name, out string snapshot) && snapshot == json)
                    {
                        levels.Add(existing);
                        previous.Remove(name);
                    }
                    else
                    {
                        var loaded = LevelData.FromJson(json, name);
                        BakeMissingOccupancy(loaded);
                        levels.Add(loaded);
                        loadedJson[name] = json;
                        levelSnapshots[name] = JsonUtility.ToJson(loaded, true);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException ||
                                                   exception is UnauthorizedAccessException)
                {
                    Debug.LogError($"无法读取关卡 {path}：{exception.Message}");
                }
            }

        if (AssetDatabase.IsValidFolder(LegacyLevelFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:LevelData", new[] { LegacyLevelFolder }))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || File.Exists(GetLevelPath(level.name))) continue;
                if (!loadedJson.ContainsKey(level.name) && previous.TryGetValue(level.name, out var existing))
                {
                    levels.Add(existing);
                    previous.Remove(level.name);
                    continue;
                }

                var copy = Instantiate(level);
                copy.name = level.name;
                BakeMissingOccupancy(copy);
                levels.Add(copy);
                loadedJson.Remove(copy.name);
                levelSnapshots[copy.name] = JsonUtility.ToJson(copy, true);
            }
        }

        foreach (var level in previous.Values)
        {
            if (!levels.Exists(current => current.name == level.name))
            {
                levelSnapshots.Remove(level.name);
                loadedJson.Remove(level.name);
            }
            DestroyImmediate(level);
        }
        levels.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        if (!levels.Contains(selectedLevel))
            SelectLevel(levels.Find(level => level.name == selectedLevelName));
    }

    private void BakeMissingOccupancy(LevelData level)
    {
        for (int i = 0; i < level.SceneElements.Count; i++)
        {
            var placement = level.SceneElements[i];
            if (placement.Element == SceneElement.None || placement.HasBakedOccupancy)
                continue;

            Vector2Int[] cells = CalculateOccupancy(placement.ElementId, placement.Scale);
            if (cells != null)
                level.SetSceneElementTransform(i, placement.Scale, cells);
        }
    }

    private static string GetLevelPath(string name) => $"{LevelFolder}/{name}.json";

    private bool SaveLevel(LevelData level)
    {
        string path = GetLevelPath(level.name);
        string json = JsonUtility.ToJson(level, true);
        try
        {
            EnsureFolder(LevelFolder);
            File.WriteAllText(path, json);
            levelSnapshots[level.name] = json;
            loadedJson[level.name] = json;
            AssetDatabase.ImportAsset(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"无法保存关卡 {path}：{exception.Message}");
            ShowNotification(new GUIContent("关卡 JSON 保存失败，请查看 Console。"));
            return false;
        }
    }

    private void SelectLevel(LevelData level)
    {
        selectedLevel = level;
        selectedLevelName = level ? level.name : null;
        gridScroll = Vector2.zero;
        detailsScroll = Vector2.zero;
        editingPlacementIndex = -1;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }

    private void CreateLevel()
    {
        long nextNumber = 1;
        foreach (var existing in levels)
        {
            string name = existing.name;
            if (name.StartsWith(LevelPrefix, StringComparison.Ordinal) &&
                int.TryParse(name.Substring(LevelPrefix.Length), out int number))
            {
                nextNumber = Math.Max(nextNumber, (long)number + 1);
            }
        }

        while (File.Exists(GetLevelPath($"{LevelPrefix}{nextNumber}")))
            nextNumber++;

        var level = CreateInstance<LevelData>();
        level.name = $"{LevelPrefix}{nextNumber}";
        if (!SaveLevel(level))
        {
            DestroyImmediate(level);
            return;
        }
        levels.Add(level);
        SelectLevel(level);
        listScroll.y = float.MaxValue;
    }

    private void OnGUI()
    {
        int splitterControlId = GUIUtility.GetControlID(FocusType.Passive);
        int gridControlId = GUIUtility.GetControlID(FocusType.Passive);
        if (moveControlId != 0 && (GUIUtility.hotControl != moveControlId || moveLevel != selectedLevel))
            FinishElementMove();
        if (strokeControlId != 0 && (GUIUtility.hotControl != strokeControlId || strokeLevel != selectedLevel))
            FinishGridStroke();

        using (new EditorGUILayout.HorizontalScope())
        {
            float currentSidebarWidth = Mathf.Clamp(sidebarWidth, MinSidebarWidth, MaxSidebarWidth);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(currentSidebarWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("关卡列表", EditorStyles.boldLabel);
                if (GUILayout.Button("新建关卡"))
                {
                    CreateLevel();
                    GUIUtility.ExitGUI();
                }

                using (var scrollView = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Height(Mathf.Max(72f, position.height * 0.3f))))
                {
                    listScroll = scrollView.scrollPosition;
                    foreach (LevelData level in levels)
                    {
                        bool isSelected = level == selectedLevel;
                        if (GUILayout.Toggle(isSelected, level.name, "Button") && !isSelected)
                        {
                            SelectLevel(level);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (selectedLevel != null)
                {
                    EditorGUILayout.Space();
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        DrawGridEditing();
                    monstersExpanded = EditorGUILayout.Foldout(monstersExpanded, "怪物", true);
                    if (monstersExpanded)
                    {
                        using (var data = new SerializedObject(selectedLevel))
                        {
                            EditorGUILayout.PropertyField(data.FindProperty("slimeCount"), new GUIContent("史莱姆数量"));
                            EditorGUILayout.PropertyField(data.FindProperty("wolfCount"), new GUIContent("狼数量"));
                            if (data.ApplyModifiedProperties()) SaveLevel(selectedLevel);
                        }
                    }
                    chestsExpanded = EditorGUILayout.Foldout(chestsExpanded, "宝箱", true);
                    if (chestsExpanded) DrawChestCounts();
                }

                DrawElementPalette("场景元素", scenePalette, ref sceneElementsExpanded, ref paletteScroll, currentSidebarWidth);
                DrawElementPalette("地质元素", geologyPalette, ref geologyElementsExpanded, ref geologyPaletteScroll, currentSidebarWidth);
            }

            DrawSidebarSplitter(currentSidebarWidth, splitterControlId);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if (selectedLevel == null)
                {
                    EditorGUILayout.HelpBox(levels.Count == 0 ? "请先新建关卡。" : "请从左侧选择关卡。", MessageType.Info);
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var previewLabel = editingGridOccupancy
                            ? new GUIContent("网格类型编辑", "左键循环切换类型，右键设为畅通。")
                            : new GUIContent("网格预览", "Ctrl＋左键选中场景元素，Ctrl＋Shift＋左键切换重叠元素。");
                        EditorGUILayout.LabelField(previewLabel, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                        GUILayout.FlexibleSpace();
                        showAllOccupiedCells = GUILayout.Toggle(showAllOccupiedCells,
                            new GUIContent("显示网格类型", "橙色阻移动，蓝色阻视野，紫色两者都阻。"), GUILayout.ExpandWidth(false));
                    }
                    Rect viewport = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    DrawGrid(viewport, selectedLevel.Width, selectedLevel.Height, gridControlId);
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
                       GUILayout.Width(currentSidebarWidth), GUILayout.ExpandHeight(true)))
                DrawLevelDetails();
        }
    }

    private void DrawLevelDetails()
    {
        EditorGUILayout.LabelField("关卡详情", EditorStyles.boldLabel);
        if (!selectedLevel)
        {
            EditorGUILayout.LabelField("请先选择关卡。");
            return;
        }

        var chests = selectedLevel.ChestPlacements;
        EditorGUILayout.LabelField($"宝箱 · {chests.Count} 个");
        if (chests.Count == 0)
        {
            EditorGUILayout.LabelField("在左侧宝箱栏目点击「生成」。");
            return;
        }

        var details = new StringBuilder("网格坐标从左上角 (0, 0) 开始。");
        foreach (var chest in chests)
        {
            int quality = (int)chest.Quality;
            string qualityName = quality >= 0 && quality < DesignInfoEditor.QualityNames.Length
                ? DesignInfoEditor.QualityNames[quality] : chest.Quality.ToString();
            details.AppendLine().Append($"{qualityName}  ({chest.Cell.x}, {chest.Cell.y})  深度 {chest.Depth} 层");
        }
        string result = details.ToString();
        if (GUILayout.Button("复制结果"))
            EditorGUIUtility.systemCopyBuffer = result;

        using (var scroll = new EditorGUILayout.ScrollViewScope(detailsScroll))
        {
            detailsScroll = scroll.scrollPosition;
            EditorGUILayout.TextArea(result, EditorStyles.wordWrappedLabel);
        }
    }

    private void DrawSidebarSplitter(float currentSidebarWidth, int controlId)
    {
        Rect handle = GUILayoutUtility.GetRect(SplitterWidth, 0f, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal, controlId);
        Event current = Event.current;
        bool dragging = GUIUtility.hotControl == controlId;

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when current.button == 0 && handle.Contains(current.mousePosition):
                GUIUtility.hotControl = controlId;
                dragStartX = current.mousePosition.x;
                dragStartWidth = currentSidebarWidth;
                current.Use();
                Repaint();
                break;

            case EventType.MouseDrag when dragging:
                sidebarWidth = Mathf.Clamp(dragStartWidth + current.mousePosition.x - dragStartX,
                    MinSidebarWidth, MaxSidebarWidth);
                current.Use();
                Repaint();
                break;

            case EventType.MouseUp when dragging && current.button == 0:
                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
                break;

            case EventType.Repaint:
                bool hovered = handle.Contains(current.mousePosition);
                Color color = dragging || hovered ? new Color32(80, 150, 205, 255) : new Color32(115, 115, 115, 255);
                EditorGUI.DrawRect(new Rect(handle.center.x - 1f, handle.center.y - 14f, 2f, 28f), color);
                break;
        }
    }

    private void DrawElementPalette(string label, List<int> palette, ref bool expanded,
        ref Vector2 scroll, float currentSidebarWidth)
    {
        bool isOpen = EditorGUILayout.Foldout(expanded, label, true);
        if (isOpen != expanded)
        {
            expanded = isOpen;
            GUIUtility.ExitGUI();
        }

        if (!expanded)
            return;

        if (palette == scenePalette && selectedLevel != null &&
            selectedElement != (int)SceneElement.None && !GameDataConfig.IsGeology(selectedElement))
        {
            DrawSceneElementTransform();
        }

        if (palette.Count == 0)
        {
            EditorGUILayout.HelpBox($"{label}图集中没有可用的 Sprite。", MessageType.Warning);
            return;
        }

        float stride = PaletteCardSize + PaletteCardGap;
        int columns = Mathf.Max(1, Mathf.FloorToInt((currentSidebarWidth - PaletteScrollGutter + PaletteCardGap) / stride));
        int rows = Mathf.CeilToInt((float)palette.Count / columns);
        int cardsPerRow = Mathf.Min(columns, palette.Count);
        float contentWidth = cardsPerRow * stride - PaletteCardGap;
        float contentHeight = rows * stride - PaletteCardGap;

        using (var scrollView = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
        {
            scroll = scrollView.scrollPosition;
            Rect content = GUILayoutUtility.GetRect(contentWidth, contentHeight, GUILayout.ExpandWidth(true));
            for (int index = 0; index < palette.Count; index++)
            {
                var card = new Rect(content.x + index % columns * stride, content.y + index / columns * stride,
                    PaletteCardSize, PaletteCardSize);
                DrawPaletteCard(card, palette[index]);
            }
        }
    }

    private void DrawPaletteCard(Rect card, int element)
    {
        var tooltip = new GUIContent("", ObjectNames.NicifyVariableName(GameDataConfig.GetElementName(element)));
        if (GUI.Button(card, tooltip, GUIStyle.none))
        {
            selectedElement = element;
            editingGridOccupancy = false;
            editingPlacementIndex = -1;
            Repaint();
        }

        if (Event.current.type != EventType.Repaint)
            return;

        bool selected = selectedElement == element;
        Color border = selected ? new Color32(102, 173, 228, 255) : new Color32(96, 99, 104, 255);
        Color fill = selected ? new Color32(51, 76, 98, 255) : new Color32(46, 48, 51, 255);
        float borderWidth = selected ? 2f : 1f;
        EditorGUI.DrawRect(card, border);
        EditorGUI.DrawRect(new Rect(card.x + borderWidth, card.y + borderWidth,
            card.width - 2f * borderWidth, card.height - 2f * borderWidth), fill);

        var image = new Rect(card.x + PaletteCardPadding, card.y + PaletteCardPadding,
            card.width - 2f * PaletteCardPadding, card.height - 2f * PaletteCardPadding);
        DrawElementSprite(image, element);
    }

    private void DrawElementSprite(Rect target, int element)
    {
        if (Event.current.type != EventType.Repaint || !elementSprites.TryGetValue(element, out Sprite sprite))
            return;

        DrawSpritePreview(target, sprite);
    }

    internal static void DrawSpritePreview(Rect target, Sprite sprite)
    {
        Texture2D texture = sprite.texture;
        Rect source = sprite.textureRect;
        float scale = Mathf.Min(target.width / source.width, target.height / source.height);
        Vector2 size = source.size * scale;
        Rect destination = new Rect(target.center - size * 0.5f, size);
        var uv = new Rect(source.x / texture.width, source.y / texture.height,
            source.width / texture.width, source.height / texture.height);
        GUI.DrawTextureWithTexCoords(destination, texture, uv);
    }

    private void DrawChestCounts()
    {
        using (var data = new SerializedObject(selectedLevel))
        using (new EditorGUI.IndentLevelScope())
        {
            var counts = data.FindProperty("chestCounts");
            var names = DesignInfoEditor.QualityNames;
            if (counts.arraySize < names.Length) counts.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var count = counts.GetArrayElementAtIndex(i);
                count.intValue = Mathf.Max(0, EditorGUILayout.DelayedIntField(names[i] + "宝箱数量", count.intValue));
            }
            if (data.ApplyModifiedProperties()) SaveLevel(selectedLevel);

            if (GUILayout.Button("生成"))
                GenerateChests();
            EditorGUILayout.LabelField($"已生成 {selectedLevel.ChestPlacements.Count} 个宝箱");
        }
    }

    private void GenerateChests()
    {
        var catalog = Resources.Load<ChestCatalog>("ChestCatalog");
        if (!catalog)
        {
            ShowNotification(new GUIContent("缺少 ChestCatalog 宝箱配置。"));
            return;
        }

        BakeMissingOccupancy(selectedLevel);
        Undo.RecordObject(selectedLevel, "生成宝箱");
        if (!selectedLevel.TryGenerateChestPlacements(catalog, out long requested, out int available))
        {
            ShowNotification(new GUIContent($"需要 {requested} 格，当前只有 {available} 格可放置宝箱。"));
            return;
        }

        SaveLevel(selectedLevel);
        Repaint();
    }

    private void DrawGridEditing()
    {
        EditorGUILayout.LabelField(selectedLevel.name, EditorStyles.boldLabel);
        bool expanded = EditorGUILayout.Foldout(gridEditingExpanded, "网格编辑", true);
        if (expanded != gridEditingExpanded)
        {
            gridEditingExpanded = expanded;
            GUIUtility.ExitGUI();
        }
        if (!gridEditingExpanded)
            return;

        var serializedLevel = new SerializedObject(selectedLevel);
        SerializedProperty width = serializedLevel.FindProperty("width");
        SerializedProperty height = serializedLevel.FindProperty("height");

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 72f;
        EditorGUI.BeginChangeCheck();
        int newWidth = EditorGUILayout.DelayedIntField("网格宽度", width.intValue);
        int newHeight = EditorGUILayout.DelayedIntField("网格高度", height.intValue);
        EditorGUIUtility.labelWidth = previousLabelWidth;
        if (EditorGUI.EndChangeCheck())
        {
            width.intValue = Mathf.Clamp(newWidth, GameDataConfig.MinLevelGridSize, GameDataConfig.MaxLevelGridSize);
            height.intValue = Mathf.Clamp(newHeight, GameDataConfig.MinLevelGridSize, GameDataConfig.MaxLevelGridSize);
            if (serializedLevel.ApplyModifiedProperties())
                SaveLevel(selectedLevel);
        }

        DrawGridResizeButtons(true);
        DrawGridResizeButtons(false);
        var occupancyButton = new GUIContent(editingGridOccupancy ? "网格类型编辑（开启）" : "网格类型编辑",
            "左键循环切换格子类型，右键设为畅通。手动设置优先。");
        editingGridOccupancy = GUILayout.Toggle(editingGridOccupancy, occupancyButton, GUI.skin.button);
        if (editingGridOccupancy)
            EditorGUILayout.HelpBox("左键循环：畅通 → 阻移动（橙）→ 阻视野（蓝）→ 两者都阻（紫）。右键设为畅通。", MessageType.None);
    }

    private void DrawGridResizeButtons(bool expand)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(expand ? "扩展" : "减少", GUILayout.Width(42f));
            bool canResizeWidth = expand ? selectedLevel.Width < GameDataConfig.MaxLevelGridSize
                : selectedLevel.Width > GameDataConfig.MinLevelGridSize;
            bool canResizeHeight = expand ? selectedLevel.Height < GameDataConfig.MaxLevelGridSize
                : selectedLevel.Height > GameDataConfig.MinLevelGridSize;
            string action = expand ? "新增" : "删除";
            DrawGridResizeButton("↑", $"从上边{action}一行", new Vector2Int(0, -1), expand, canResizeHeight);
            DrawGridResizeButton("↓", $"从下边{action}一行", new Vector2Int(0, 1), expand, canResizeHeight);
            DrawGridResizeButton("←", $"从左边{action}一列", new Vector2Int(-1, 0), expand, canResizeWidth);
            DrawGridResizeButton("→", $"从右边{action}一列", new Vector2Int(1, 0), expand, canResizeWidth);
        }
    }

    private void DrawGridResizeButton(string label, string tooltip, Vector2Int direction, bool expand, bool enabled)
    {
        using (new EditorGUI.DisabledScope(!enabled))
        {
            if (!GUILayout.Button(new GUIContent(label, tooltip)))
                return;
        }

        Undo.RecordObject(selectedLevel, expand ? "扩展网格" : "减少网格");
        if (expand ? selectedLevel.ExpandGrid(direction) : selectedLevel.ShrinkGrid(direction))
        {
            if (!expand)
                editingPlacementIndex = -1;
            EditorUtility.SetDirty(selectedLevel);
            SaveLevel(selectedLevel);
            Repaint();
            GUIUtility.ExitGUI();
        }
    }

    private void DrawSceneElementTransform()
    {
        bool editing = TryGetSelectedPlacement(out var placement);
        EditorGUILayout.LabelField(editing ? "已放置元素" : "场景元素放置设置", EditorStyles.boldLabel);
        if (editing)
        {
            int occupiedCount = 0;
            foreach (var cell in selectedLevel.GetOccupiedCells(placement))
                occupiedCount++;
            EditorGUILayout.LabelField($"占用 {occupiedCount} 格", EditorStyles.miniLabel);
        }

        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 72f;
        EditorGUI.BeginChangeCheck();
        float scale = EditorGUILayout.Slider("缩放", editing ? placement.Scale : brushScale, MinElementScale, MaxElementScale);
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUIUtility.labelWidth = previousLabelWidth;

        if (!changed)
            return;

        if (!editing)
        {
            brushScale = scale;
            return;
        }

        Undo.RecordObject(selectedLevel, "调整场景元素");
        if (ApplyElementScale(placement.ElementId, scale))
            SaveLevel(selectedLevel);
    }

    private static void GetGridRects(Rect viewport, Vector2 gridSize, out Rect grid, out Rect visibleArea)
    {
        float contentWidth = gridSize.x + 1;
        float contentHeight = gridSize.y + 1;
        GUIStyle vertical = GUI.skin.verticalScrollbar;
        GUIStyle horizontal = GUI.skin.horizontalScrollbar;
        float verticalInset = vertical.fixedWidth + vertical.margin.left;
        float horizontalInset = horizontal.fixedHeight + horizontal.margin.top;
        bool needsHorizontal = contentWidth > viewport.width;
        bool needsVertical = contentHeight > viewport.height - (needsHorizontal ? horizontalInset : 0f);
        needsHorizontal |= contentWidth > viewport.width - (needsVertical ? verticalInset : 0f);
        float visibleWidth = viewport.width - (needsVertical ? verticalInset : 0f);
        float visibleHeight = viewport.height - (needsHorizontal ? horizontalInset : 0f);
        float gridX = Mathf.Round(Mathf.Max(0f, (visibleWidth - contentWidth) * 0.5f));
        float gridY = Mathf.Round(Mathf.Max(0f, (visibleHeight - contentHeight) * 0.5f));
        grid = new Rect(gridX, gridY, gridSize.x, gridSize.y);
        visibleArea = new Rect(viewport.position, new Vector2(visibleWidth, visibleHeight));
    }

    private void DrawGrid(Rect viewport, int width, int height, int controlId)
    {
        var dimensions = new Vector2(width, height);
        GetGridRects(viewport, dimensions * GridCellSize, out Rect grid, out Rect visibleArea);
        Event current = Event.current;
        if (current.type == EventType.ScrollWheel && viewport.Contains(current.mousePosition))
        {
            if (strokeControlId == 0 && moveControlId == 0)
            {
                Vector2 cursor = current.mousePosition - viewport.position;
                float scaleFactor = Mathf.Pow(1.12f, -current.delta.y / 3f);
                if (editingGridOccupancy || !visibleArea.Contains(current.mousePosition) ||
                    !TryScaleSelectedElement(grid, cursor + gridScroll, scaleFactor, current.control))
                {
                    Vector2 cellPosition = (cursor + gridScroll - grid.position) / GridCellSize;
                    gridZoom = Mathf.Clamp(gridZoom * scaleFactor, MinGridZoom, MaxGridZoom);
                    GetGridRects(viewport, dimensions * GridCellSize, out grid, out visibleArea);
                    Vector2 scroll = grid.position + cellPosition * GridCellSize - cursor;
                    Vector2 maxScroll = Vector2.Max(Vector2.zero, grid.size + Vector2.one - visibleArea.size);
                    gridScroll = Vector2.Min(Vector2.Max(Vector2.zero, scroll), maxScroll);
                }
            }
            current.Use();
            Repaint();
        }

        float cellSize = GridCellSize;
        float gridX = grid.x;
        float gridY = grid.y;
        float contentWidth = grid.width + 1;
        float contentHeight = grid.height + 1;
        bool mouseInsideViewport = visibleArea.Contains(Event.current.mousePosition);
        gridScroll = GUI.BeginScrollView(viewport, gridScroll, new Rect(0, 0, contentWidth, contentHeight));

        if (Event.current.type == EventType.Repaint)
        {
            var background = new Color32(38, 43, 49, 255);
            var lineColor = new Color32(91, 102, 114, 255);
            EditorGUI.DrawRect(grid, background);

            int firstColumn = Mathf.Max(0, Mathf.FloorToInt((gridScroll.x - gridX) / cellSize));
            int lastColumn = Mathf.Min(width, Mathf.CeilToInt((gridScroll.x + visibleArea.width - gridX) / cellSize));
            for (int column = firstColumn; column <= lastColumn; column++)
                EditorGUI.DrawRect(new Rect(gridX + column * cellSize, gridY, 1, contentHeight), lineColor);

            int firstRow = Mathf.Max(0, Mathf.FloorToInt((gridScroll.y - gridY) / cellSize));
            int lastRow = Mathf.Min(height, Mathf.CeilToInt((gridScroll.y + visibleArea.height - gridY) / cellSize));
            for (int row = firstRow; row <= lastRow; row++)
                EditorGUI.DrawRect(new Rect(gridX, gridY + row * cellSize, contentWidth, 1), lineColor);

            var visibleContent = new Rect(gridScroll, visibleArea.size);
            DrawGridLayer(grid.position, visibleContent, true);
            DrawGridLayer(grid.position, visibleContent, false);
            DrawGridOccupancy(grid.position, visibleContent);
            if (chestsExpanded)
                DrawGridChests(grid.position, visibleContent);
        }

        HandleGridPointer(grid, mouseInsideViewport, controlId);

        GUI.EndScrollView();
    }

    private void DrawGridLayer(Vector2 gridOrigin, Rect visibleContent, bool geology)
    {
        foreach (var placement in selectedLevel.SceneElements)
        {
            if (placement.IsGeology != geology || !selectedLevel.ContainsCell(placement.Cell))
                continue;

            Rect target = GetPlacementRect(gridOrigin, placement);
            if (target.Overlaps(visibleContent))
                DrawElementSprite(target, placement.ElementId);
        }
    }

    private void DrawGridChests(Vector2 gridOrigin, Rect visibleContent)
    {
        foreach (var chest in selectedLevel.ChestPlacements)
        {
            var rect = new Rect(gridOrigin + (Vector2)chest.Cell * GridCellSize, Vector2.one * GridCellSize);
            if (!rect.Overlaps(visibleContent)) continue;

            float size = GridCellSize - 2f;
            var badge = new Rect(rect.center - Vector2.one * (size * 0.5f), Vector2.one * size);
            Sprite icon = chestCatalog ? chestCatalog.Get(chest.Quality)?.PreviewSprite : null;
            if (icon)
                DrawSpritePreview(badge, icon);
            else
                EditorGUI.DrawRect(badge, new Color32(117, 77, 29, 255));
            GUI.Label(badge, new GUIContent(icon ? "" : "箱", DesignInfoEditor.QualityNames[(int)chest.Quality] + "宝箱"),
                EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawGridOccupancy(Vector2 gridOrigin, Rect visibleContent)
    {
        highlightedOccupancy.Clear();
        bool hasSelection = TryGetSelectedPlacement(out var selected);
        if (showAllOccupiedCells || editingGridOccupancy)
        {
            foreach (var placement in selectedLevel.SceneElements)
            {
                if (!placement.IsGeology)
                    DrawOccupiedCells(placement, gridOrigin, visibleContent);
            }
            foreach (Vector2Int cell in selectedLevel.GetManuallyBlockedCells())
                DrawOccupiedCell(cell, gridOrigin, visibleContent);
            foreach (Vector2Int cell in selectedLevel.GetSightBlockingCells())
                DrawOccupiedCell(cell, gridOrigin, visibleContent);
        }
        else if (hasSelection)
        {
            DrawOccupiedCells(selected, gridOrigin, visibleContent);
        }

        if (!hasSelection || editingGridOccupancy)
            return;

        Vector2 anchor = gridOrigin + (Vector2)selected.Cell * GridCellSize;
        var anchorRect = new Rect(anchor, Vector2.one * GridCellSize);
        Handles.DrawSolidRectangleWithOutline(anchorRect, Color.clear, new Color32(102, 173, 228, 255));
    }

    private void DrawOccupiedCells(LevelData.ElementPlacement placement, Vector2 gridOrigin, Rect visibleContent)
    {
        foreach (Vector2Int cell in selectedLevel.GetOccupiedCells(placement))
            DrawOccupiedCell(cell, gridOrigin, visibleContent);
    }

    private void DrawOccupiedCell(Vector2Int cell, Vector2 gridOrigin, Rect visibleContent)
    {
        var rect = new Rect(gridOrigin + (Vector2)cell * GridCellSize, Vector2.one * GridCellSize);
        if (!rect.Overlaps(visibleContent) || !highlightedOccupancy.Add(cell)) return;
        var cellType = selectedLevel.GetCellType(cell);
        if (cellType == LevelData.CellType.Open) return;
        Color32 color = new Color32(244, 155, 52, 230);
        if ((cellType & LevelData.CellType.BlockSight) != 0)
            color = cellType == LevelData.CellType.BlockSight
                ? new Color32(55, 160, 245, 230) : new Color32(174, 90, 220, 230);
        Handles.DrawSolidRectangleWithOutline(rect, new Color32(color.r, color.g, color.b, 72), color);
    }

    private Rect GetPlacementRect(Vector2 gridOrigin, LevelData.ElementPlacement placement)
    {
        float cellSize = GridCellSize;
        Vector2 anchor = gridOrigin + (Vector2)placement.Cell * cellSize;
        if (placement.IsGeology)
            return new Rect(anchor + Vector2.one, Vector2.one * (cellSize - 1f));

        Rect bounds = placement.BoundsInCells;
        return new Rect(gridOrigin + bounds.position * cellSize, bounds.size * cellSize);
    }

    private void SelectPlacedElement(Rect grid, Vector2 pointerPosition, bool cycle = false)
    {
        editingPlacementIndex = FindPlacedElementIndex(grid, pointerPosition, cycle);
        if (TryGetSelectedPlacement(out var placement))
        {
            selectedElement = placement.ElementId;
            brushScale = placement.Scale;
            sceneElementsExpanded = true;
        }
        Repaint();
    }

    private bool TryGetSelectedPlacement(out LevelData.ElementPlacement placement)
    {
        placement = default;
        if (selectedLevel == null || editingPlacementIndex < 0 || editingPlacementIndex >= selectedLevel.SceneElements.Count)
            return false;

        placement = selectedLevel.SceneElements[editingPlacementIndex];
        return !placement.IsGeology && selectedLevel.ContainsCell(placement.Cell);
    }

    private bool ApplyElementScale(int element, float scale)
    {
        Vector2Int[] cells = CalculateOccupancy(element, scale);
        if (!selectedLevel.SetSceneElementTransform(editingPlacementIndex, scale, cells))
            return false;

        brushScale = scale;
        EditorUtility.SetDirty(selectedLevel);
        Repaint();
        return true;
    }

    private bool TryScaleSelectedElement(Rect grid, Vector2 pointerPosition, float factor, bool controlHeld)
    {
        if (!TryGetSelectedPlacement(out var placement) ||
            (!controlHeld && !GetPlacementRect(grid.position, placement).Contains(pointerPosition)))
            return false;

        float scale = Mathf.Clamp(placement.Scale * factor, MinElementScale, MaxElementScale);
        if (!Mathf.Approximately(scale, placement.Scale))
        {
            Undo.RecordObject(selectedLevel, "缩放场景元素");
            if (ApplyElementScale(placement.ElementId, scale))
                SaveLevel(selectedLevel);
        }

        return true;
    }

    private int FindPlacedElementIndex(Rect grid, Vector2 pointerPosition, bool cycle)
    {
        var placements = selectedLevel.SceneElements;
        int count = placements.Count;
        int start = cycle && editingPlacementIndex >= 0 && editingPlacementIndex < count
            ? editingPlacementIndex - 1
            : count - 1;
        for (int step = 0; step < count; step++)
        {
            int index = (start + count - step) % count;
            var placement = placements[index];
            if (placement.IsGeology || !selectedLevel.ContainsCell(placement.Cell) ||
                !GetPlacementRect(grid.position, placement).Contains(pointerPosition))
                continue;

            return index;
        }

        return -1;
    }

    private void HandleGridPointer(Rect grid, bool insideViewport, int controlId)
    {
        Event pointer = Event.current;
        EventType eventType = pointer.GetTypeForControl(controlId);
        bool canStart = eventType == EventType.MouseDown && insideViewport && GUIUtility.hotControl == 0;
        if (!editingGridOccupancy && HandleElementMove(grid, pointer, eventType, canStart, controlId))
            return;

        bool insideGrid = insideViewport && grid.Contains(pointer.mousePosition);
        bool canPaint = editingGridOccupancy || elementSprites.ContainsKey(selectedElement);
        if (!editingGridOccupancy && canStart && pointer.button == 0 && (pointer.shift || !canPaint || !insideGrid))
        {
            SelectPlacedElement(grid, pointer.mousePosition);
            pointer.Use();
            return;
        }

        if (canStart && insideGrid)
        {
            bool erase = pointer.button == 1;
            if (!erase && (pointer.button != 0 || !canPaint))
                return;

            GUIUtility.hotControl = controlId;
            strokeControlId = controlId;
            strokeLevel = selectedLevel;
            strokeButton = pointer.button;
            strokeEditsOccupancy = editingGridOccupancy;
            strokeElement = erase ? (int)SceneElement.None : selectedElement;
            strokeScale = brushScale;
            strokeOccupancy = strokeEditsOccupancy ? null : CalculateOccupancy(strokeElement, strokeScale);
            strokeUndoName = strokeEditsOccupancy
                ? (erase ? "设为畅通" : "切换网格类型")
                : (erase ? "删除网格元素" : "放置网格元素");
            Undo.IncrementCurrentGroup();
            strokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(strokeUndoName);
            if (!strokeEditsOccupancy)
                editingPlacementIndex = -1;
            ApplyGridStroke(grid, pointer.mousePosition);
            pointer.Use();
            return;
        }

        if (strokeControlId != controlId)
            return;

        bool released = eventType == EventType.MouseUp && pointer.button == strokeButton;
        if (eventType == EventType.MouseDrag || released)
        {
            if (insideGrid)
                ApplyGridStroke(grid, pointer.mousePosition);
            else
                lastStrokeCell = null;

            if (released)
                FinishGridStroke();

            pointer.Use();
        }
    }

    private bool HandleElementMove(Rect grid, Event pointer, EventType eventType, bool canStart, int controlId)
    {
        if (canStart && pointer.button == 0)
        {
            if (pointer.control)
                SelectPlacedElement(grid, pointer.mousePosition, pointer.shift);

            bool canMove = TryGetSelectedPlacement(out var placement) && !pointer.shift &&
                GetPlacementRect(grid.position, placement).Contains(pointer.mousePosition);
            if (canMove)
            {
                GUIUtility.hotControl = controlId;
                moveControlId = controlId;
                moveLevel = selectedLevel;
                moveStartPointer = pointer.mousePosition;
                moveStartCell = placement.Cell;
            }

            if (canMove || pointer.control)
            {
                pointer.Use();
                return true;
            }
        }

        if (moveControlId != controlId)
            return false;

        bool released = eventType == EventType.MouseUp && pointer.button == 0;
        if (eventType == EventType.MouseDrag || released)
        {
            if ((!released || moveChanged) && TryGetSelectedPlacement(out var placement))
            {
                Vector2 delta = (pointer.mousePosition - moveStartPointer) / GridCellSize;
                var cell = new Vector2Int(
                    Mathf.Clamp(moveStartCell.x + Mathf.RoundToInt(delta.x), 0, moveLevel.Width - 1),
                    Mathf.Clamp(moveStartCell.y + Mathf.RoundToInt(delta.y), 0, moveLevel.Height - 1));
                if (cell != placement.Cell)
                {
                    if (!moveChanged)
                    {
                        Undo.IncrementCurrentGroup();
                        Undo.RegisterCompleteObjectUndo(moveLevel, "移动场景元素");
                    }

                    if (moveLevel.MoveSceneElement(editingPlacementIndex, cell))
                    {
                        moveChanged = true;
                        EditorUtility.SetDirty(moveLevel);
                        Repaint();
                    }
                }
            }

            if (released)
                FinishElementMove();

            pointer.Use();
        }
        return true;
    }

    private void FinishElementMove()
    {
        if (moveControlId == 0)
            return;

        if (GUIUtility.hotControl == moveControlId)
            GUIUtility.hotControl = 0;
        if (moveChanged && moveLevel != null)
            SaveLevel(moveLevel);

        moveControlId = 0;
        moveLevel = null;
        moveChanged = false;
    }

    private void ApplyGridStroke(Rect grid, Vector2 pointerPosition)
    {
        Vector2 offset = (pointerPosition - grid.position) / GridCellSize;
        var end = new Vector2Int(Mathf.FloorToInt(offset.x), Mathf.FloorToInt(offset.y));
        Vector2Int cell = lastStrokeCell ?? end;
        lastStrokeCell = end;
        if (cell == end && strokeCells.Contains(cell))
            return;

        Undo.RecordObject(strokeLevel, strokeUndoName);
        int dx = Mathf.Abs(end.x - cell.x);
        int dy = -Mathf.Abs(end.y - cell.y);
        int stepX = cell.x < end.x ? 1 : -1;
        int stepY = cell.y < end.y ? 1 : -1;
        int error = dx + dy;
        bool changed = false;

        // Rasterize between mouse events so fast motion still visits the intervening cells.
        while (true)
        {
            if (strokeCells.Add(cell))
            {
                changed |= strokeEditsOccupancy
                    ? strokeLevel.SetCellType(cell, strokeButton == 1 ? LevelData.CellType.Open
                        : (LevelData.CellType)(((int)strokeLevel.GetCellType(cell) + 1) % 4))
                    : GameDataConfig.IsGeology(strokeElement)
                        ? strokeLevel.SetGeologyElement(cell, (GeologyElement)strokeElement)
                        : strokeLevel.SetSceneElement(cell, (SceneElement)strokeElement, strokeScale, strokeOccupancy);
            }
            if (cell == end)
                break;

            int doubledError = error * 2;
            if (doubledError >= dy)
            {
                error += dy;
                cell.x += stepX;
            }
            if (doubledError <= dx)
            {
                error += dx;
                cell.y += stepY;
            }
        }

        if (changed)
        {
            strokeChanged = true;
            EditorUtility.SetDirty(strokeLevel);
            Repaint();
        }
    }

    private void FinishGridStroke()
    {
        if (strokeControlId == 0)
            return;

        if (GUIUtility.hotControl == strokeControlId)
            GUIUtility.hotControl = 0;
        strokeControlId = 0;

        if (strokeChanged && strokeLevel != null)
        {
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(strokeUndoGroup);
            SaveLevel(strokeLevel);
        }

        strokeChanged = false;
        strokeLevel = null;
        strokeOccupancy = null;
        lastStrokeCell = null;
        strokeCells.Clear();
    }
}
