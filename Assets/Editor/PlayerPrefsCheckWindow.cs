#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GL = UnityEngine.GUILayout;
using EGL = UnityEditor.EditorGUILayout;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;


namespace UFrame
{
    /// <summary>
    /// 查看游戏存档信息界面
    /// </summary>
    public class PlayerPrefsCheckWindow : EditorWindow
    {
        public static string path = "BawAsset/PlayerPrefs/";
        
        public enum PrefType
        {
            Int,
            String,
            Float,
        }

        public class PreTypeEntity
        {
            public string name;
            public PrefType type;
            public string valStr = "";
            public float valFloat = 0f;
            public int valInt = 0;

            public float checkTime = 1f;
            public bool hasValue = false;
        }

        public Dictionary<string, string> allProperty = new Dictionary<string, string>();
        public Dictionary<string, PreTypeEntity> propertyDic = new Dictionary<string, PreTypeEntity>();

        public string id = "";
        public string value = "";
        public PrefType valType = PrefType.Int;

        [MenuItem("ZNG/PlayerCheck")]
        public static void OpenPlayerPrefsCheck()
        {
            PlayerPrefsCheckWindow myWindow = GetWindow<PlayerPrefsCheckWindow>("小游戏存档", true);

            Analysis(myWindow);
            //myWindow.mSize = new Vector2(300,800);

            myWindow.Show();
        }


        public static void Analysis(PlayerPrefsCheckWindow myWindow)
        {
            myWindow.allProperty.Clear();

            var comPath = Path.Combine(Application.dataPath, path);
            //Debug.Log("路径:" + comPath);

            if (!Directory.Exists(comPath))
            {
                Debug.LogError("存档配置文件路径不存在 " + comPath);
                return;
            }

            DirectoryInfo d = new DirectoryInfo(comPath);
            var files = d.GetFiles("PlayerData.txt");
            //var configVar = Resources.Load<TextAsset>("ConfigVar");
            //if (configVar == null)
            //{
            //    Debug.LogError("Resources文件夹下没有存在ConfigVar");
            //    return;
            //}
            foreach (var file in files) {
            string s = File.ReadAllText(file.FullName, Encoding.UTF8);
            string[] lines = s.Split(new string[] {"\r\n", "\n"}, StringSplitOptions.None);
            foreach (var line in lines)
            {
				if (string.IsNullOrEmpty(line))
                    continue;
				var line2 = line;
				int pos = line2.IndexOf('#');
				if (pos >= 0) {
                    line2 = line2.Substring(0, pos);
				}
				line2 = line2.Trim();
                if (string.IsNullOrEmpty(line2))
                    continue;

                var itemAry = Regex.Split(line2, "\\s+", RegexOptions.IgnoreCase);
                if (string.IsNullOrEmpty(itemAry[0]))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(itemAry[1]))
                {
                    continue;
                }

                if (itemAry.Length > 2)
                {
                    if (itemAry[2].Contains("-"))
                    {
                        var numberAry = itemAry[2].Split('-');
                        int startIndex = Convert.ToInt32(numberAry[0]);
                        int endIndex = Convert.ToInt32(numberAry[1]);

                        for (int i = startIndex; i < endIndex; i++)
                        {
                            myWindow.allProperty.Add(itemAry[1].Trim() + i, itemAry[0].Trim());        
                        }
                        continue;
                    }
                }
                myWindow.allProperty.Add(itemAry[1].Trim(), itemAry[0].Trim());
            }
            }


            myWindow.Init(myWindow.allProperty);
        }

        public void OnFocus()
        {
            Analysis(this);
            this.name = "小游戏存档设置";
            this.minSize = new Vector2(300, 200);
        }

        public void OnSelectionChange()
        {
            this.name = "小游戏存档设置";
            this.minSize = new Vector2(300, 200);
        }

        public void Awake()
        {
            this.name = "小游戏存档设置";
            this.minSize = new Vector2(300, 200);
        }

        public void Reset()
        {
            Analysis(this);
            
            this.name = "小游戏存档设置";
            this.minSize = new Vector2(300, 200);
        }

        public void OnEnable()
        {
            this.name = "小游戏存档设置";
            this.minSize = new Vector2(300, 200);
        }


        public void Init(Dictionary<string, string> allProperty)
        {
            propertyDic.Clear();
            //var sc = StringComparison.OrdinalIgnoreCase;
            foreach (var item in allProperty)
            {
                PreTypeEntity e = new PreTypeEntity();
                propertyDic.Add(item.Key, e);
                e.name = item.Key + "Un";
                e.type = Get(item.Value);

                if (PlayerPrefs.HasKey(e.name))
                {
                    if (e.type == PrefType.String)
                        e.valStr = PlayerPrefs.GetString(e.name, "");
                    if (e.type == PrefType.Float)
                        e.valFloat = PlayerPrefs.GetFloat(e.name, 0f);
                    if (e.type == PrefType.Int)
                        e.valInt = PlayerPrefs.GetInt(e.name, 0);
                }
                else
                {
                }
            }
        }

        public PrefType Get(string name)
        {
            var sc = StringComparison.OrdinalIgnoreCase;
            if (name.Equals("int", sc))
            {
                return PrefType.Int;
            }
            else if (name.Equals("float", sc))
            {
                return PrefType.Float;
            }
            else
            {
                return PrefType.String;
            }
        }

        public void SetValue(PreTypeEntity entity)
        {
            if (entity.type == PrefType.Float)
            {
                PlayerPrefs.SetFloat(entity.name, entity.valFloat);
            }
            else if (entity.type == PrefType.Int)
            {
                PlayerPrefs.SetInt(entity.name, entity.valInt);
            }
            else
            {
                PlayerPrefs.SetString(entity.name, entity.valStr);
            }
        }

        public void ReadValue(PreTypeEntity entity)
        {
            var type = entity.type;
            var name = entity.name;

            if (type == PrefType.String)
                entity.valStr = PlayerPrefs.GetString(name, "");
            if (type == PrefType.Float)
                entity.valFloat = PlayerPrefs.GetFloat(name, 0f);
            if (type == PrefType.Int)
                entity.valInt = PlayerPrefs.GetInt(name, 0);
        }


        public void OnGUI()
        {
            ListKeyUI();
            AddKeyUI();
        }

        //private float scrollPos = 0f;
        private Vector2 v = new Vector2(0,0);
		private string searchStr = "";
        public void ListKeyUI()
        {
            v = EGL.BeginScrollView(v);

			GL.BeginHorizontal();
			GL.Label("search: ", GL.Width(60));
            searchStr = EGL.TextField(searchStr, GL.Width(160)).ToLower();
            if (GL.Button("删除", GUILayout.Width(60))) {
                searchStr = "";
                GUI.FocusControl(null); // 清除当前焦点
            }
			GL.EndHorizontal();

            foreach (var item in propertyDic)
            {
                var entity = item.Value;
				if (!string.IsNullOrEmpty(searchStr) && entity.name.ToLower().IndexOf(searchStr) < 0) {
					continue;
				}

                GL.BeginHorizontal();
                GL.Label(entity.name, GL.Width(110));

                if (Time.realtimeSinceStartup - entity.checkTime >= 1f)
                {
                    if (PlayerPrefs.HasKey(entity.name))
                    {
                        entity.hasValue = true;
                        entity.checkTime = 0;
                    }
                    else
                    {
                        entity.hasValue = false;
                        entity.checkTime = 0;
                    }
                }

                if (entity.hasValue)
                {
                    GL.Label("有", GL.Width(30));
                }
                else
                {
                    GL.Label("无", GL.Width(30));
                }

                entity.type = (PrefType) EGL.EnumPopup(entity.type, GL.Width(50));

                if (item.Value.type == PrefType.Int)
                {
                    entity.valInt = EGL.IntField(entity.valInt);
                }
                else if (item.Value.type == PrefType.Float)
                {
                    entity.valFloat = EGL.FloatField(entity.valFloat);
                }
                else
                {
                    entity.valStr = EGL.TextField(entity.valStr);
                }

                if (GL.Button("设置"))
                {
                    SetValue(entity);
                    PlayerPrefs.Save();
                    PlayerData.I.Load();
                }

                if (GL.Button("重读"))
                {
                    ReadValue(entity);
                }

                if (GL.Button("删除"))
                {
                    entity.valFloat = 0f;
                    entity.valInt = 0;
                    entity.valStr = "";
                    entity.hasValue = false;
                    PlayerPrefs.DeleteKey(entity.name);
                    PlayerPrefs.Save();
                    PlayerData.I.Load();
                }

                GL.EndHorizontal();
            }
            EGL.EndScrollView();
        }

        public void AddKeyUI()
        {
            using (new EGL.HorizontalScope("box"))
            {
                if (GL.Button("重新读取"))
                {
                    PlayerData.I.Load();
                    Analysis(this);
                }

                if (GL.Button("删除关卡存档") &&
                    EditorUtility.DisplayDialog("删除关卡存档", "确定要删除已保存的关卡进度？", "删除", "取消"))
                {
                    PlayerData.I.DeleteAll();
                    Analysis(this);
                }
            }
        }
    }
}

#endif
