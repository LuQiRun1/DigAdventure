
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class UniversalSpriteAnimationTool : EditorWindow
{
    private enum ImportMode
    {
        SpriteSheet,
        MultiplePNG
    }

    private enum ActionType { Idle, Move, Run, Open }
    private enum Direction { None, Top, Down, Left, Right }
    private enum ExistingAssetMode { Create, Merge, Replace }
    [Serializable] private class ClipGroup
    {
        public Direction direction;
        public string customName = "";
    }

    private static readonly Direction[] DefaultDirectionOrder =
    {
        Direction.Down,
        Direction.Left,
        Direction.Right,
        Direction.Top
    };

    private ImportMode importMode = ImportMode.SpriteSheet;

    // Sprite Sheet
    private Texture2D spriteSheet;
    private int rows = 3;
    private int columns = 3;

    // Multiple PNG
    private DefaultAsset pngFolder;

    // Animation settings
    private string animationName = "NewAnimation";
    private ActionType action = ActionType.Idle;
    private int frameRate = 12;
    private bool loopAnimation = false;
    private int sortingOrder = 10;
    private int framesPerClip = 4;
    private readonly List<ClipGroup> groups = new List<ClipGroup>();
    private UnityEngine.Object previousSource;
    private Vector2 scrollPosition;

    // Pivot
    private Vector2 pivot = new Vector2(0.5f, 0.5f);

    // Output
    private DefaultAsset outputFolder;
    private DefaultAsset spriteOutputFolder;
    private DefaultAsset prefabOutputFolder;

    private const string DefaultOutput =
        "Assets/GeneratedAnimations";

    [MenuItem("Tools/Animation/Universal Sprite Animation Tool")]
    public static void OpenWindow()
    {
        UniversalSpriteAnimationTool window =
            GetWindow<UniversalSpriteAnimationTool>();

        window.titleContent =
            new GUIContent("Sprite Animation");

        window.minSize = new Vector2(420, 620);
        window.Show();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space(10);

        GUILayout.Label(
            "Universal Sprite Animation Tool",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "通用序列帧动画生成工具\n" +
            "支持 Sprite Sheet 和多张 PNG 批量导入。",
            MessageType.Info
        );

        EditorGUILayout.Space(10);

        DrawImportSettings();

        UnityEngine.Object source = importMode == ImportMode.SpriteSheet ? (UnityEngine.Object)spriteSheet : pngFolder;
        if (source != previousSource)
        {
            previousSource = source;
            groups.Clear();
            if (source != null)
            {
                string sourceName = source.name;
                int separator = sourceName.IndexOf('_');
                animationName = separator > 0 ? sourceName.Substring(0, separator) : sourceName;
                if (separator >= 0 && TryGetActionFromName(sourceName.Substring(separator + 1), out ActionType detectedAction))
                    action = detectedAction;
            }
        }

        EditorGUILayout.Space(12);

        DrawAnimationSettings();

        EditorGUILayout.Space(12);

        DrawOutputSettings();

        EditorGUILayout.Space(20);

        GUI.backgroundColor = new Color(0.3f, 0.75f, 0.4f);

        if (GUILayout.Button(
            "Generate Animation + Controller + Prefab",
            GUILayout.Height(42)))
        {
            Generate();
        }

        GUI.backgroundColor = Color.white;
        if (importMode == ImportMode.SpriteSheet)
        {
            using (new EditorGUI.DisabledScope(!spriteSheet))
                if (GUILayout.Button("删除当前图集及关联资源…"))
                    DeleteSheetAssets();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DeleteSheetAssets()
    {
        string sheet = AssetDatabase.GetAssetPath(spriteSheet);
        if (!sheet.StartsWith("Assets/", StringComparison.Ordinal) ||
            !(AssetImporter.GetAtPath(sheet) is TextureImporter))
        {
            EditorUtility.DisplayDialog("无法删除", "请选择 Assets 目录中的图集纹理。", "确定");
            return;
        }

        var clips = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings.Length != 1 || bindings[0].type != typeof(SpriteRenderer) ||
                bindings[0].propertyName != "m_Sprite" || bindings[0].path != "" ||
                AnimationUtility.GetCurveBindings(clip).Length != 0 || clip.events.Length != 0) continue;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            if (keys.Length > 0 && keys.All(k => k.value is Sprite && AssetDatabase.GetAssetPath(k.value) == sheet))
                clips.Add(path);
        }

        var controllers = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!controller || !path.EndsWith("Animator.controller", StringComparison.OrdinalIgnoreCase)) continue;
            var motions = controller.animationClips;
            if (motions.Length > 0 && motions.All(c => clips.Contains(AssetDatabase.GetAssetPath(c))))
                controllers.Add(path);
        }

        var prefabs = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab || !prefab.TryGetComponent(out Animator animator) || !prefab.GetComponent<SpriteRenderer>()) continue;
            string controller = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
            if (controllers.Contains(controller) && Path.GetFileNameWithoutExtension(controller) ==
                Path.GetFileNameWithoutExtension(path) + "Animator") prefabs.Add(path);
        }

        var paths = prefabs.Concat(controllers).Concat(clips).Append(sheet).ToArray();
        var targets = new HashSet<string>(paths);
        var blockers = new List<string>();
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || targets.Contains(path) ||
                AssetDatabase.IsValidFolder(path)) continue;
            if (AssetDatabase.GetDependencies(path, false).Any(targets.Contains)) blockers.Add(path);
        }
        if (blockers.Count > 0)
        {
            EditorUtility.DisplayDialog("有其他资源仍在使用", "请先解除以下引用，再删除图集：\n" +
                string.Join("\n", blockers), "确定");
            return;
        }
        if (!EditorUtility.DisplayDialog("删除图集及关联资源", "以下资源将移至回收站：\n" +
            string.Join("\n", paths), "移至回收站", "取消")) return;
        foreach (string path in paths)
        {
            if (AssetDatabase.MoveAssetToTrash(path)) continue;
            EditorUtility.DisplayDialog("删除已停止", "无法移至回收站：" + path +
                "\n此前列出的已处理资源可从回收站恢复。", "确定");
            return;
        }
        spriteSheet = null;
        groups.Clear();
        GUIUtility.ExitGUI();
    }

    // =========================
    // Import Settings
    // =========================

    private void DrawImportSettings()
    {
        GUILayout.Label("01. Import Settings",
            EditorStyles.boldLabel);

        importMode = (ImportMode)EditorGUILayout.EnumPopup(
            "Import Mode", importMode);

        EditorGUILayout.Space(5);

        if (importMode == ImportMode.SpriteSheet)
        {
            spriteSheet =
                (Texture2D)EditorGUILayout.ObjectField(
                    "Sprite Sheet",
                    spriteSheet,
                    typeof(Texture2D),
                    false
                );

            rows = Mathf.Max(1,
                EditorGUILayout.IntField("Rows", rows));

            columns = Mathf.Max(1,
                EditorGUILayout.IntField("Columns", columns));

            EditorGUILayout.HelpBox(
                "按从左到右、从上到下的顺序切割。",
                MessageType.None
            );
            string path = AssetDatabase.GetAssetPath(spriteSheet);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            int slicedCount = importer != null && importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Multiple
                ? importer.spritesheet.Length : 0;
            EditorGUILayout.LabelField(slicedCount > 0
                ? $"已切割：{slicedCount} 帧（保留现有切片）"
                : $"尚未切割；按当前行列将生成 {rows * columns} 帧");

            using (new EditorGUI.DisabledScope(spriteSheet == null || importer == null))
            {
                if (GUILayout.Button(slicedCount > 0 ? "按行列重新切片 Sprite Sheet" : "按行列切片 Sprite Sheet"))
                {
                    bool confirmed = slicedCount == 0 || EditorUtility.DisplayDialog(
                        "重新切片",
                        $"当前图集已有 {slicedCount} 个切片。重新切片会替换现有切片数据，可能影响已有 Sprite 引用。是否继续？",
                        "重新切片", "取消");
                    if (confirmed)
                    {
                    try
                    {
                        PrepareSpriteSource();
                        Sprite[] sliced = ImportSpriteSheet(true);
                        if (sliced != null && sliced.Length > 0)
                        {
                            Debug.Log($"图集切片完成：{spriteSheet.name}，共 {sliced.Length} 帧。", spriteSheet);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("图集切片失败：\n" + e);
                    }
                    }
                }
            }
        }
        else
        {
            pngFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "PNG Folder",
                pngFolder,
                typeof(DefaultAsset),
                false
            );

            EditorGUILayout.HelpBox(
                "读取文件夹内的 PNG 图片，按文件名中的数字自然排序。",
                MessageType.None
            );
        }
    }

    // =========================
    // Animation Settings
    // =========================

    private void DrawAnimationSettings()
    {
        GUILayout.Label("02. Animation Settings",
            EditorStyles.boldLabel);

        animationName = EditorGUILayout.TextField(
            "Resource Name", animationName);

        action = (ActionType)EditorGUILayout.EnumPopup(
            "Action", action);

        framesPerClip = Mathf.Max(1, EditorGUILayout.IntField("Frames Per Clip", framesPerClip));
        int total = GetFrameCount();
        int count = total > 0 ? (total + framesPerClip - 1) / framesPerClip : 0;
        EditorGUILayout.LabelField($"总帧数：{total}，动画数量：{count}");
        while (groups.Count < count)
        {
            int groupIndex = groups.Count;
            groups.Add(new ClipGroup
            {
                direction = groupIndex < DefaultDirectionOrder.Length
                    ? DefaultDirectionOrder[groupIndex]
                    : Direction.None
            });
        }
        while (groups.Count > count)
        {
            groups.RemoveAt(groups.Count - 1);
        }
        for (int i = 0; i < count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Clip {i + 1}", GUILayout.Width(55));
            groups[i].direction = (Direction)EditorGUILayout.EnumPopup(groups[i].direction);
            EditorGUILayout.EndHorizontal();
            groups[i].customName = EditorGUILayout.TextField("自定义名称", groups[i].customName);
            EditorGUILayout.LabelField("实际名称", GetClipName(i));
        }
        if (count > 0)
            EditorGUILayout.HelpBox(
                "一个图集共用一个动作；方向默认按下、左、右、上排列。自定义名称留空时，使用资源名、动作和方向自动命名。",
                MessageType.None);
        if (importMode == ImportMode.SpriteSheet && spriteSheet != null)
            EditorGUILayout.LabelField("当前动画帧来源", AssetDatabase.GetAssetPath(spriteSheet));

        frameRate = Mathf.Clamp(
            EditorGUILayout.IntField("Frame Rate (FPS)", frameRate),
            1, 120
        );

        loopAnimation = EditorGUILayout.Toggle(
            "Loop Animation", loopAnimation);

        sortingOrder = EditorGUILayout.IntField(
            "Sorting Order", sortingOrder);

        EditorGUILayout.Space(5);

        GUILayout.Label("Sprite Pivot", EditorStyles.boldLabel);

        pivot = EditorGUILayout.Vector2Field(
            "Pivot (X / Y)", pivot);

        pivot.x = Mathf.Clamp01(pivot.x);
        pivot.y = Mathf.Clamp01(pivot.y);
    }

    // =========================
    // Output Settings
    // =========================

    private void DrawOutputSettings()
    {
        GUILayout.Label("03. Output Settings",
            EditorStyles.boldLabel);

        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Animation / Controller Folder",
            outputFolder,
            typeof(DefaultAsset),
            false
        );

        prefabOutputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Prefab Folder", prefabOutputFolder, typeof(DefaultAsset), false);
        spriteOutputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Sprite Resource Folder", spriteOutputFolder, typeof(DefaultAsset), false);
        EditorGUILayout.HelpBox("不选择素材目录时直接使用原始图片；选择后会复制素材，不移动原文件。预制体目录为空时使用动画目录。", MessageType.None);

        if (outputFolder == null)
        {
            EditorGUILayout.LabelField(
                "默认输出路径：",
                DefaultOutput
            );
        }
    }

    // =========================
    // Main Generator
    // =========================

    private void Generate()
    {
        if (string.IsNullOrWhiteSpace(animationName))
        {
            Debug.LogError("请输入动画名称！");
            return;
        }

        animationName = animationName.Trim();
        animationName = char.ToUpperInvariant(animationName[0]) + animationName.Substring(1);

        string outputPath = GetOutputPath();

        if (string.IsNullOrEmpty(outputPath))
        {
            Debug.LogError("输出文件夹无效！");
            return;
        }

        EnsureFolder(outputPath);

        string prefabPath = prefabOutputFolder == null ? outputPath : AssetDatabase.GetAssetPath(prefabOutputFolder);
        if (!AssetDatabase.IsValidFolder(prefabPath)
            || (spriteOutputFolder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(spriteOutputFolder))))
        {
            Debug.LogError("素材或预制体输出文件夹无效！");
            return;
        }

        string controllerAssetPath = outputPath + "/" + animationName + "Animator.controller";
        string prefabAssetPath = prefabPath + "/" + animationName + ".prefab";
        bool controllerExists = AssetDatabase.LoadMainAssetAtPath(controllerAssetPath) != null;
        bool prefabExists = AssetDatabase.LoadMainAssetAtPath(prefabAssetPath) != null;
        ExistingAssetMode assetMode = ExistingAssetMode.Create;
        if (controllerExists || prefabExists)
        {
            int choice = EditorUtility.DisplayDialogComplex("角色资源已存在",
                "发现同名预制体或动画控制器。\n合并：保留现有角色并添加新动作；覆盖：替换控制器状态、预制体和本次同名动画。",
                "合并", "取消", "覆盖");
            if (choice == 1) return;
            assetMode = choice == 0 ? ExistingAssetMode.Merge : ExistingAssetMode.Replace;
        }

        Sprite[] sprites = null;

        try
        {
            if (importMode == ImportMode.SpriteSheet)
            {
                PrepareSpriteSource();
                sprites = ImportSpriteSheet();
            }
            else
            {
                sprites = ImportMultiplePNG();
            }

            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogError("没有成功读取任何 Sprite！");
                return;
            }

            if (!CreateAnimationAssets(sprites, outputPath, prefabPath, assetMode)) return;

            Debug.Log(
                "动画资源生成成功！\n" +
                "动画名称：" + animationName + "\n" +
                "帧数：" + sprites.Length + "\n" +
                "FPS：" + frameRate
            );

            AssetDatabase.Refresh();

        }
        catch (Exception e)
        {
            Debug.LogError(
                "动画生成失败：\n" + e
            );
        }
    }

    // =========================
    // Sprite Sheet Import
    // =========================

    private Sprite[] ImportSpriteSheet(bool reslice = false)
    {
        if (spriteSheet == null)
        {
            Debug.LogError("请先选择 Sprite Sheet！");
            return null;
        }

        string path = AssetDatabase.GetAssetPath(spriteSheet);

        TextureImporter importer =
            AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError("无法获取图片导入设置！");
            return null;
        }

        if (!reslice && importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Multiple
            && importer.spritesheet.Length > 0)
        {
            return LoadSprites(path);
        }

        importer.GetSourceTextureWidthAndHeight(out int width, out int height);

        if (columns > width || rows > height)
        {
            Debug.LogError("行数或列数超过图片像素尺寸！");
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null)
            throw new InvalidOperationException("无法获取图集切片数据提供器：" + path);
        provider.InitSpriteEditorDataProvider();

        List<SpriteRect> metadata = new List<SpriteRect>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int xMin = Mathf.RoundToInt(
                    c * width / (float)columns);

                int xMax = Mathf.RoundToInt(
                    (c + 1) * width / (float)columns);

                int yMin = Mathf.RoundToInt(
                    (rows - 1 - r) * height / (float)rows);

                int yMax = Mathf.RoundToInt(
                    (rows - r) * height / (float)rows);

                SpriteRect data = new SpriteRect();

                data.name = animationName + "_" +
                    (r * columns + c + 1).ToString("D3");

                data.rect = new Rect(
                    xMin,
                    yMin,
                    xMax - xMin,
                    yMax - yMin
                );

                data.alignment = SpriteAlignment.Custom;
                data.pivot = pivot;
                data.spriteID = GUID.Generate();

                metadata.Add(data);
            }
        }

        provider.SetSpriteRects(metadata.ToArray());
        var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameProvider != null)
            nameProvider.SetNameFileIdPairs(metadata.Select(data => new SpriteNameFileIdPair(data.name, data.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        Sprite[] sliced = LoadSprites(path);
        if (sliced.Length != metadata.Count)
            throw new InvalidOperationException($"图集切片未完整导入：预期 {metadata.Count} 帧，实际 {sliced.Length} 帧。路径：{path}");
        return sliced;
    }

    // =========================
    // Multiple PNG Import
    // =========================

    private Sprite[] ImportMultiplePNG()
    {
        if (pngFolder == null)
        {
            Debug.LogError("请选择 PNG 文件夹！");
            return null;
        }

        string folderPath =
            AssetDatabase.GetAssetPath(pngFolder);

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("选择的路径不是有效文件夹！");
            return null;
        }

        string[] guids =
            AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });

        List<string> paths = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetExtension(path).ToLower() == ".png")
            {
                paths.Add(path);
            }
        }

        paths.Sort(NaturalCompare);

        if (spriteOutputFolder != null)
        {
            string targetFolder = AssetDatabase.GetAssetPath(spriteOutputFolder);
            if (targetFolder.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("素材输出文件夹不能位于输入文件夹内。");
            var targets = paths.Select(p => targetFolder + "/" + Path.GetFileName(p)).ToArray();
            if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
                throw new InvalidOperationException("输入文件夹中存在同名图片，无法复制到同一素材目录。");
            for (int i = 0; i < paths.Count; i++)
                if (!string.Equals(paths[i], targets[i], StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.LoadMainAssetAtPath(targets[i]) != null)
                    throw new InvalidOperationException("素材目标已存在：" + targets[i]);
            for (int i = 0; i < paths.Count; i++)
            {
                paths[i] = CopySpriteAsset(paths[i], targetFolder);
            }
        }

        List<Sprite> sprites = new List<Sprite>();

        foreach (string path in paths)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
                continue;

            
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;

        // Unity 2021.2 使用 TextureImporterSettings 设置 Pivot
        TextureImporterSettings settings =
            new TextureImporterSettings();

        importer.ReadTextureSettings(settings);

        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;

        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();


            Sprite sprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite != null)
                sprites.Add(sprite);
        }

        return sprites.ToArray();
    }

    // =========================
    // Load Sprites
    // =========================

    private Sprite[] LoadSprites(string path)
    {
        UnityEngine.Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(path);

        List<Sprite> sprites = new List<Sprite>();

        foreach (UnityEngine.Object asset in assets)
        {
            Sprite sprite = asset as Sprite;

            if (sprite != null)
                sprites.Add(sprite);
        }

        sprites.Sort((a, b) => {
            int row = b.rect.y.CompareTo(a.rect.y);
            return row != 0 ? row : a.rect.x.CompareTo(b.rect.x);
        });

        return sprites.ToArray();
    }

    // =========================
    // Natural Sorting
    // =========================

    private static int NaturalCompare(string a, string b)
    {
        string[] partsA = Regex.Split(a, @"(\d+)");
        string[] partsB = Regex.Split(b, @"(\d+)");

        int count = Mathf.Min(partsA.Length, partsB.Length);

        for (int i = 0; i < count; i++)
        {
            long numberA, numberB;

            if (long.TryParse(partsA[i], out numberA) &&
                long.TryParse(partsB[i], out numberB))
            {
                int result = numberA.CompareTo(numberB);

                if (result != 0)
                    return result;
            }
            else
            {
                int result = string.Compare(
                    partsA[i],
                    partsB[i],
                    StringComparison.OrdinalIgnoreCase
                );

                if (result != 0)
                    return result;
            }
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    // =========================
    // Create Animation Assets
    // =========================

    private bool CreateAnimationAssets(
        Sprite[] sprites,
        string outputPath,
        string prefabOutputPath,
        ExistingAssetMode assetMode)
    {
        string controllerPath =
            outputPath + "/" + animationName + "Animator.controller";

        string prefabPath =
            prefabOutputPath + "/" + animationName + ".prefab";

        int clipCount = (sprites.Length + framesPerClip - 1) / framesPerClip;
        var names = Enumerable.Range(0, clipCount).Select(GetClipName).ToArray();
        if (names.Any(name => string.IsNullOrWhiteSpace(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOfAny(new[] { '/', '\\' }) >= 0
            || name == "." || name == ".."))
            throw new InvalidOperationException("动画名称无效，请检查自定义名称是否包含文件名不允许的字符。");
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidOperationException("每组动画名称必须不同，请调整自定义名称、动作或方向。");
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (AssetDatabase.LoadMainAssetAtPath(controllerPath) != null && controller == null
            || AssetDatabase.LoadMainAssetAtPath(prefabPath) != null && prefab == null)
            throw new InvalidOperationException("角色输出路径存在类型不匹配的资源。");
        if (assetMode == ExistingAssetMode.Merge && prefab != null)
        {
            Animator existingAnimator = prefab.GetComponent<Animator>();
            if (existingAnimator == null || existingAnimator.runtimeAnimatorController != controller || controller == null)
                throw new InvalidOperationException("预制体未引用目标动画控制器，无法安全合并。请检查输出目录和角色名称。");
        }
        bool hasMergeConflicts = false;
        foreach (string name in names)
        {
            string path = outputPath + "/" + name + ".anim";
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && (!(existing is AnimationClip) || assetMode == ExistingAssetMode.Create))
                throw new InvalidOperationException("动画已存在或类型不匹配：" + path);
            AnimatorState state = controller != null
                ? controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                : null;
            if (assetMode == ExistingAssetMode.Merge && state != null && state.motion != null
                && state.motion != existing)
                throw new InvalidOperationException("同名状态引用了其他动画，无法安全更新：" + name);
            if (assetMode == ExistingAssetMode.Merge && (existing != null || state != null))
                hasMergeConflicts = true;
        }
        if (hasMergeConflicts && !EditorUtility.DisplayDialog("更新同名动画",
            "合并时发现同名动画或状态。是否用当前素材更新这些动画？其他动作、控制器状态和预制体将保留。",
            "更新", "取消")) return false;
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        if (assetMode == ExistingAssetMode.Replace)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState oldState in machine.states)
                machine.RemoveState(oldState.state);
        }
        for (int group = 0; group < clipCount; group++)
        {
        string clipName = names[group];
        Sprite[] clipSprites = sprites.Skip(group * framesPerClip).Take(framesPerClip).ToArray();
        AnimationClip clip = new AnimationClip();

        clip.name = clipName;
        clip.frameRate = frameRate;

        EditorCurveBinding binding =
            EditorCurveBinding.PPtrCurve(
                "",
                typeof(SpriteRenderer),
                "m_Sprite"
            );

        // 最后一帧增加结束时间，保证播放完整
        ObjectReferenceKeyframe[] keys =
            new ObjectReferenceKeyframe[clipSprites.Length + 1];

        for (int i = 0; i <= clipSprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe();

            keys[i].time = i / (float)frameRate;

            keys[i].value = clipSprites[
                Mathf.Min(i, clipSprites.Length - 1)
            ];
        }

        AnimationUtility.SetObjectReferenceCurve(
            clip, binding, keys);

        AnimationClipSettings settings =
            AnimationUtility.GetAnimationClipSettings(clip);

        settings.loopTime = loopAnimation;

        AnimationUtility.SetAnimationClipSettings(
            clip, settings);

        string clipPath = outputPath + "/" + clipName + ".anim";
        AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (existingClip != null)
        {
            EditorUtility.CopySerialized(clip, existingClip);
            DestroyImmediate(clip);
            clip = existingClip;
            EditorUtility.SetDirty(clip);
        }
        else AssetDatabase.CreateAsset(clip, clipPath);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState state = assetMode == ExistingAssetMode.Merge
            ? stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == clipName)
            : null;
        if (state == null) state = stateMachine.AddState(clipName);

        state.motion = clip;

        if (controller.layers[0].stateMachine.defaultState == null)
            controller.layers[0].stateMachine.defaultState = state;
        }

        // Create GameObject
        if (assetMode != ExistingAssetMode.Merge || prefab == null)
        {
        GameObject obj = new GameObject(animationName);

        SpriteRenderer renderer =
            obj.AddComponent<SpriteRenderer>();

        renderer.sprite = sprites[0];
        renderer.sortingOrder = sortingOrder;

        Animator animator =
            obj.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;

        // Save Prefab
        PrefabUtility.SaveAsPrefabAsset(
            obj, prefabPath);

        DestroyImmediate(obj);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }

    private int GetFrameCount()
    {
        if (importMode == ImportMode.SpriteSheet)
        {
            if (spriteSheet == null) return 0;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(spriteSheet)) as TextureImporter;
            return importer != null && importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Multiple && importer.spritesheet.Length > 0
                ? importer.spritesheet.Length : rows * columns;
        }
        string path = AssetDatabase.GetAssetPath(pngFolder);
        if (!AssetDatabase.IsValidFolder(path)) return 0;
        return AssetDatabase.FindAssets("t:Texture2D", new[] { path })
            .Count(g => string.Equals(Path.GetExtension(AssetDatabase.GUIDToAssetPath(g)), ".png", StringComparison.OrdinalIgnoreCase));
    }

    private void PrepareSpriteSource()
    {
        if (spriteOutputFolder == null || spriteSheet == null) return;
        string path = CopySpriteAsset(AssetDatabase.GetAssetPath(spriteSheet), AssetDatabase.GetAssetPath(spriteOutputFolder));
        spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        previousSource = spriteSheet;
    }

    private static string CopySpriteAsset(string sourcePath, string targetFolder)
    {
        if (!AssetDatabase.IsValidFolder(targetFolder))
            throw new InvalidOperationException("素材输出文件夹无效：" + targetFolder);
        string targetPath = targetFolder + "/" + Path.GetFileName(sourcePath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)) return sourcePath;
        if (AssetDatabase.LoadMainAssetAtPath(targetPath) != null)
            throw new InvalidOperationException("素材目标已存在：" + targetPath);
        if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            throw new InvalidOperationException("无法复制素材：" + sourcePath);
        return targetPath;
    }

    private string GetClipName(int index)
    {
        ClipGroup group = groups[index];
        if (!string.IsNullOrWhiteSpace(group.customName)) return group.customName.Trim();
        string direction = group.direction == Direction.None ? "" : "-" +
            (group.direction == Direction.Top ? "T" : group.direction == Direction.Down ? "D" : group.direction == Direction.Left ? "L" : "R");
        string name = animationName.Trim();
        if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
        return name + action + direction;
    }

    private static bool TryGetActionFromName(string suffix, out ActionType detectedAction)
    {
        foreach (string token in Regex.Split(suffix, "[^a-zA-Z]+"))
        {
            if (string.Equals(token, "open", StringComparison.OrdinalIgnoreCase))
            {
                detectedAction = ActionType.Open;
                return true;
            }
            if (string.Equals(token, "idle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "idea", StringComparison.OrdinalIgnoreCase))
            {
                detectedAction = ActionType.Idle;
                return true;
            }
            if (string.Equals(token, "move", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "mov", StringComparison.OrdinalIgnoreCase))
            {
                detectedAction = ActionType.Move;
                return true;
            }
            if (string.Equals(token, "run", StringComparison.OrdinalIgnoreCase))
            {
                detectedAction = ActionType.Run;
                return true;
            }
        }
        detectedAction = default;
        return false;
    }

    // =========================
    // Folder Utilities
    // =========================

    private string GetOutputPath()
    {
        if (outputFolder == null)
            return DefaultOutput;

        string path = AssetDatabase.GetAssetPath(outputFolder);

        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');

        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
