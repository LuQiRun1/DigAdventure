using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class MonsterEditor : EditorWindow
{
    private const string MonsterFolder = "Assets/Res/Monster";
    private const string MonsterPrefix = "Monster_";

    [SerializeField] private MonsterData selectedMonster;
    private readonly List<MonsterData> monsters = new List<MonsterData>();
    private Vector2 listScroll;
    private Vector2 detailsScroll;

    [MenuItem("Tools/怪物编辑器")]
    private static void Open()
    {
        GetWindow<MonsterEditor>("怪物编辑器");
    }

    private void OnEnable()
    {
        minSize = new Vector2(520f, 280f);
        ReloadMonsters();
        Undo.undoRedoPerformed += Repaint;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
    }

    private void OnProjectChange()
    {
        ReloadMonsters();
        Repaint();
    }

    private void ReloadMonsters()
    {
        monsters.Clear();
        if (AssetDatabase.IsValidFolder(MonsterFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MonsterData", new[] { MonsterFolder }))
            {
                var monster = AssetDatabase.LoadAssetAtPath<MonsterData>(AssetDatabase.GUIDToAssetPath(guid));
                if (monster != null)
                    monsters.Add(monster);
            }
        }

        monsters.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        if (!monsters.Contains(selectedMonster))
            SelectMonster(null);
    }

    private void SelectMonster(MonsterData monster)
    {
        selectedMonster = monster;
        detailsScroll = Vector2.zero;
    }

    private void CreateMonster()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Res"))
            AssetDatabase.CreateFolder("Assets", "Res");
        if (!AssetDatabase.IsValidFolder(MonsterFolder))
            AssetDatabase.CreateFolder("Assets/Res", "Monster");

        long nextNumber = 1;
        foreach (string guid in AssetDatabase.FindAssets("", new[] { MonsterFolder }))
        {
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
            if (name.StartsWith(MonsterPrefix, StringComparison.Ordinal) &&
                int.TryParse(name.Substring(MonsterPrefix.Length), out int number))
            {
                nextNumber = Math.Max(nextNumber, (long)number + 1);
            }
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{MonsterFolder}/{MonsterPrefix}{nextNumber}.asset");
        var monster = CreateInstance<MonsterData>();
        AssetDatabase.CreateAsset(monster, path);
        AssetDatabase.SaveAssetIfDirty(monster);
        ReloadMonsters();
        SelectMonster(monster);
        listScroll.y = float.MaxValue;
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(192f), GUILayout.ExpandHeight(true)))
                DrawMonsterList();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                DrawMonsterDetails();
        }
    }

    private void DrawMonsterList()
    {
        EditorGUILayout.LabelField("怪物编辑", EditorStyles.boldLabel);
        if (GUILayout.Button("新建怪物"))
        {
            CreateMonster();
            GUIUtility.ExitGUI();
        }

        using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.ExpandHeight(true)))
        {
            listScroll = scroll.scrollPosition;
            foreach (MonsterData monster in monsters)
            {
                bool selected = monster == selectedMonster;
                if (GUILayout.Toggle(selected, monster.name, "Button") && !selected)
                {
                    SelectMonster(monster);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

    private void DrawMonsterDetails()
    {
        if (selectedMonster == null)
        {
            EditorGUILayout.HelpBox(monsters.Count == 0 ? "请先新建怪物。" : "请从左侧选择怪物。", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(selectedMonster.name, EditorStyles.boldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(detailsScroll, GUILayout.ExpandHeight(true)))
        {
            detailsScroll = scroll.scrollPosition;
            using (var data = new SerializedObject(selectedMonster))
            {
                EditorGUILayout.PropertyField(data.FindProperty("previewSprite"), new GUIContent("预览图"));
                EditorGUILayout.PropertyField(data.FindProperty("description"), new GUIContent("描述"));
                EditorGUILayout.PropertyField(data.FindProperty("speed"), new GUIContent("速度"));
                EditorGUILayout.PropertyField(data.FindProperty("traits"), new GUIContent("特性"), true);
                if (data.ApplyModifiedProperties())
                    AssetDatabase.SaveAssetIfDirty(selectedMonster);
            }
        }
    }
}
