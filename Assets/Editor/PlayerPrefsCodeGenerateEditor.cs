#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UFrame
{
    /// <summary>
    /// 根据规则生成 PlayerPrefs保存的数据类
    /// </summary>
    public class PlayerPrefsCodeGenerateEditor
    {
        [MenuItem("ZNG/PlayerPrefsCode")]
        public static void PlayerPrefsCode()
        {
			PlayerPrefsCodeGenerate.PlayerPrefsCode(Application.dataPath, Application.dataPath);
			AssetDatabase.Refresh();
        }
	}
}
#endif
