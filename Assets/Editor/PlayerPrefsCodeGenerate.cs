using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
#if UNITY_EDITOR
using UnityEngine;
#endif

/*5. 设计一个生成Data保存类的程序(小游戏设计)
字段:
实物字段(金币 等级 所在关卡):默认值
虚拟字段(道具是否存在过)

设计生成器:
int levelNum entity 1
string levelKeyDisappear virtual 1
string item virtualMore 0
stringAry itemAry entity 0            //解析成List操作, 存储的时候存在一个字符串中 0_1_1_1_1
*/

/*
 *
 *
 * */

namespace UFrame
{
    /// <summary>
    /// 根据规则生成 PlayerPrefs保存的数据类
    /// </summary>
    public class PlayerPrefsCodeGenerate
    {
        public static string path = "BawAsset/PlayerPrefs/";
        public static string outPath = "Scripts/";

        public static void PlayerPrefsCode(string base1, string base2)
        {
            var comPath = Path.Combine(base1, path);
            string classPath = Path.Combine(base2, outPath);

            Debug.Log("路径:" + comPath);


            if (!Directory.Exists(comPath))
            {
                Debug.LogError("生成代码路径不存在 " + comPath);
                return;
            }

            DirectoryInfo d = new DirectoryInfo(comPath);
            var files = d.GetFiles("*.txt");

            foreach (var item in files)
            {
                string className = Path.GetFileNameWithoutExtension(item.Name);

                string s = File.ReadAllText(item.FullName, Encoding.UTF8);
                var allLine = s.Split(new string[] {"\r\n", "\n"}, StringSplitOptions.None);

                var classInfoSB = GenerateClass(allLine);

                var templateClass = DefineScript.templateClass.Replace("${className}", className);
                templateClass = templateClass.Replace("${classContent}", classInfoSB.GetClassContent());
                templateClass = templateClass.Replace("${classLoad}",
                    DefineScript.loadMethod.Replace("${classLoad}", classInfoSB.initMethod.ToString()));
                templateClass = templateClass.Replace("${classSave}",
                    DefineScript.saveMethod.Replace("${classSave}", classInfoSB.saveMethod.ToString()));
                templateClass = templateClass.Replace("${classDeleteAll}",
                    DefineScript.deleteAllMethod.Replace("${classDeleteAll}", classInfoSB.deleteAllMethod.ToString()));
                templateClass = templateClass.Replace("${classClearAction}",
                    DefineScript.clearActionMethod.Replace("${classClearAction}", classInfoSB.clearActionMethod.ToString()));

                var fileOut = Path.Combine(classPath, className);
                File.WriteAllText(fileOut + ".cs", templateClass);
                Debug.Log(fileOut + ".cs");
            }
        }

        public static string[] SplitSpacing(string line)
        {
            string[] mm = Regex.Split(line, "\\s+", RegexOptions.IgnoreCase);
            return mm;
        }

        private static PlayerRefsSB GenerateClass(string[] allLine)
        {
            PlayerRefsSB p = new PlayerRefsSB();
            foreach (var line in allLine)
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

                StringComparison ion = StringComparison.OrdinalIgnoreCase;
                string[] charAry = SplitSpacing(line2);
                string fieldType = charAry[0];
                string fieldName = charAry[1];
                string fieldDes = charAry[2];
                bool isEntity = fieldDes.Equals("entity", ion);
                bool isVirtual = fieldDes.Equals("virtual", ion);
                bool isVirtualMore = fieldDes.Equals("virtualMore", ion);
                bool isVirtualOne = fieldDes.Equals("virtualOne", ion);
                bool isList = false;
				bool isDict = false;
                string defaultVal = string.Empty;
				if(charAry.Length > 3) {
					defaultVal = charAry[3];
				}
				bool isSec = false;
				if(charAry.Length > 4) {
					isSec = (charAry[4].Trim().ToLower() == "[enc]");
				}

                string unName = fieldName + "Un";
                p.fieldUn.AppendLine(string.Format("private string {0} = \"{1}\";", unName, unName));
                p.fieldUn.AppendLine();

                if (fieldType.EndsWith("Ary")) {
                    isList = true;
                    fieldType = fieldType.Substring(0, fieldType.Length - 3);
                } else if (fieldType.EndsWith("Dict")) {
                    isDict = true;
                    fieldType = fieldType.Substring(0, fieldType.Length - 4);
				}

                string maxFieldType = fieldType.Substring(0, 1).ToUpper() + fieldType.Substring(1);
                string maxFieldName = fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);

                GenerateInfo info = new GenerateInfo();
                info.fieldType = fieldType;
                info.fieldName = fieldName;
                info.fieldDes = fieldDes;
                info.isList = isList;
                info.defaultVal = defaultVal;
                info.unName = unName;
                info.p = p;
                info.maxFieldType = maxFieldType;
                info.maxFieldName = maxFieldName;
				info.isSec = isSec;

                if (isList) 
				{
					GenerateAry(info);
				} 
				else if (isDict)
                {
					GenerateDict(info);
                }
                else if (isEntity)
                {
                    GenerateEntity(info);
                }
                else if (isVirtual)
                {
                    GenerateVirtual(info);
                }
                else if (isVirtualMore)
                {
                    GenerateVirtualMore(info);
                }
            }


            return p;
        }

        public static void GenerateAry(GenerateInfo info)
        {
            string fieldType = info.fieldType;
            string fieldName = info.fieldName;
            string defaultVal = info.defaultVal;
            string unName = info.unName;
            PlayerRefsSB p = info.p;
            string maxFieldName = info.maxFieldName;
            string maxFieldType = info.maxFieldType;

            if (defaultVal.StartsWith("\"") && defaultVal.EndsWith("\""))
            {
            }
            else
            {
                defaultVal = "\"" + defaultVal + "\"";
            }

            p.fieldCall.AppendLine(string.Format("public Action<int,{0}> On{1};", fieldType, maxFieldName));
            p.field.AppendLine(string.Format("private string {1} = {2};", fieldType, fieldName, defaultVal));

            string setMethod = "public void Set{0}(int index,{1} value)"
                               + "{{\n"
                               + "string [] ary = {2}.Split('_'); \n"
                               + "ary[index] = value.ToString(); \n"
                               + "string s = string.Join(\"_\",ary); \n"
                               + "PlayerPrefs.SetString({3},s); \n"
                               + "if(On{4} != null) \n"
                               + "    On{4}(index,value); \n"
                               + "}}\n";
            p.virtualMethod.AppendLine(string.Format(setMethod, maxFieldName, fieldType, fieldName, unName,
                maxFieldName,
                defaultVal));


            string getMethod = string.Empty;
            if (fieldType == "string")
            {
                getMethod = "public {0} Get{1}(int index)"
                            + "{{\n"
                            + "string [] ary = {2}.Split('_') ;\n"
                            + "string num = ary[index]; \n"
                            + "return num; \n"
                            + "}}\n";
                p.virtualMethod.AppendLine(string.Format(getMethod, fieldType, maxFieldName, fieldName,
                    defaultVal));
            }
            else
            {
                getMethod = "public {0} Get{1}(int index)"
                            + "{{\n"
                            + "string [] ary = {2}.Split('_'); \n"
                            + "var num = System.Convert.ToInt32(ary[index]); \n"
                            + "return num; \n"
                            + "}}\n";
                p.virtualMethod.AppendLine(string.Format(getMethod, fieldType, maxFieldName, fieldName, defaultVal));
            }

            p.initMethod.AppendLine(string.Format("{0} = PlayerPrefs.GetString({1}, {2});\n", fieldName, unName,
                defaultVal, maxFieldType));

            p.saveMethod.AppendLine(string.Format("PlayerPrefs.SetString({0}, {1});", unName, fieldName,
                maxFieldType));
            p.deleteAllMethod.AppendLine(string.Format("PlayerPrefs.DeleteKey({0});", unName));

            p.clearActionMethod.AppendLine(string.Format("On{0} = null;", maxFieldName));
        }

		public static void GenerateDict(GenerateInfo info)
        {
            string fieldType = info.fieldType;
            string fieldName = info.fieldName;
            string defaultVal = info.defaultVal;
            string unName = info.unName;
            PlayerRefsSB p = info.p;
            string maxFieldName = info.maxFieldName;
            string maxFieldType = info.maxFieldType;

			//Debug.LogWarning("注意：使用 "+fieldType+"Dict 类型时, key, value 不能包含 ; 和 =");

			bool isWrapped = defaultVal.StartsWith("\"") && defaultVal.EndsWith("\"");
			if (fieldType != "string") {
				if (isWrapped) {
					defaultVal = defaultVal.Substring(1, defaultVal.Length - 2);
				}
				if (defaultVal == "") {
					defaultVal = "0";
				}
            } else if (!isWrapped) {
               defaultVal = "\"" + defaultVal + "\"";
            }

            p.fieldCall.AppendLine(string.Format("public Action<string,{0}> On{1};", fieldType, maxFieldName));
            p.field.AppendLine(string.Format(@"private string {0} = """";", fieldName));

			string setMethod = @"public void Set{0}({1} key) {{
	Set{0}(key.ToString(), {5});
}}
public void Set{0}(string key, {1} val) {{
	{2} = {2}.AddVal(key, val.ToString());
	PlayerPrefs.SetString({3}, {2});
	if (On{0} != null) {{
		On{0}(key, val);
	}}
}}
public void Add{0}(string key, {1} val) {{
	Set{0}(key, Get{0}(key) + val);
}}
public string {0} {{
	get {{ return {2}; }}
	set {{
		if({2} == value)
			return;
		{2} = value;
		PlayerPrefs.SetString({3}, {2});
	}}
}}
";
			string defKeyVal;
            if (fieldType == "string") {
				defKeyVal = @"""1""";
			} else {
				defKeyVal = "1";
			}
            p.virtualMethod.AppendLine(string.Format(setMethod, maxFieldName, fieldType, fieldName, unName, defaultVal, defKeyVal));

			string getMethod = @"public {0} Get{1}<T>(T key, {0} defVal = {3}) {{
	string s = {2}.GetVal(key);
	if (s == null) {{
		return defVal;
	}}
	{4}
}}";

			string returnStr;
            if (fieldType == "string") {
				returnStr = "return s;";
			} else if (fieldType == "float") {
				returnStr = "return float.Parse(s);";
			} else if (fieldType == "long") {
				returnStr = "return long.Parse(s);";
			} else {
				returnStr = "return int.Parse(s);";
			}
            p.virtualMethod.AppendLine(string.Format(getMethod, fieldType, maxFieldName, fieldName, defaultVal, returnStr));

			string clearMethod = @"public void Clear{0}() {{
	{1} = """";
	PlayerPrefs.SetString({2}, {1});
}}";
            p.virtualMethod.AppendLine(string.Format(clearMethod, maxFieldName, fieldName, unName));

            p.initMethod.AppendLine(string.Format(@"{0} = PlayerPrefs.GetString({1}, """");", fieldName, unName, defaultVal, maxFieldType));

            p.saveMethod.AppendLine(string.Format("PlayerPrefs.SetString({0}, {1});", unName, fieldName, maxFieldType));

            p.deleteAllMethod.AppendLine(string.Format("PlayerPrefs.DeleteKey({0});", unName));

            p.clearActionMethod.AppendLine(string.Format("On{0} = null;", maxFieldName));
        }

        public static void GenerateVirtual(GenerateInfo info)
        {
            string fieldType = info.fieldType;
            string defaultVal = info.defaultVal;
            string unName = info.unName;
            PlayerRefsSB p = info.p;
            string maxFieldName = info.maxFieldName;

            string setMethod = "public void Set{0}(string valStr)"
                               + "{{\n"
                               + "PlayerPrefs.SetString({1}, valStr);\n"
                               + "}}\n";

            string getMethod = "public {0} Get{1}()"
                               + "{{\n"
                               + "return PlayerPrefs.GetString({2}, \"\");\n"
                               + "}}\n";

            p.virtualMethod.AppendLine(string.Format(setMethod, maxFieldName, unName, defaultVal));
            p.virtualMethod.AppendLine(string.Format(getMethod, fieldType, maxFieldName, unName, defaultVal));
            //p.field.AppendLine(string.Format("private {0} {1} = {2}", fieldType, fieldName, defaultVal));
        }

        public static void GenerateEntity(GenerateInfo info)
        {
            string fieldType = info.fieldType;
            string fieldName = info.fieldName;
            string defaultVal = info.defaultVal;
            string unName = info.unName;
            PlayerRefsSB p = info.p;
            string maxFieldName = info.maxFieldName;
            string maxFieldType = info.maxFieldType;

            p.fieldCall.AppendLine(string.Format("public Action<{0}> On{1};", fieldType, maxFieldName));
			var fieldType2 = fieldType;
			if (info.isSec) {
				if (fieldType == "long") {
					fieldType2 = "zlong";
				} else if (fieldType == "int") {
					fieldType2 = "zint";
				} else if (fieldType == "float") {
					fieldType2 = "zfloat";
				}
			}
            p.field.AppendLine(string.Format("private {0} {1} = {2};", fieldType2, fieldName, defaultVal));

			if (fieldType == "long") {
				p.property.AppendLine(string.Format("public {0} {1} \n" +
													"{{\n" +
													"get {{ return {2}; }}\n" +
													"set{{\n" +
													"if({2} == value)\n" +
													"return;\n" +
													"{2} = value;\n" +
													"PlayerPrefs.SetString({3}, {2}.ToString());\n" +
													"if(On{1} != null)\n" +
													"On{1}({2});\n" +
													"}}\n" +
													"}}", fieldType, maxFieldName, fieldName, unName,
					maxFieldType));

				p.initMethod.AppendLine(string.Format("string tmp{0} = PlayerPrefs.GetString({1}, \"\");\n{0} = string.IsNullOrEmpty(tmp{0}) ? {2} : long.Parse(tmp{0});\n", 
							fieldName, unName, defaultVal, maxFieldType));

				p.saveMethod.AppendLine(string.Format("PlayerPrefs.SetString({0}, {1}.ToString());", unName, fieldName,
					maxFieldType));
				p.deleteAllMethod.AppendLine(string.Format("PlayerPrefs.DeleteKey({0});", unName));
			} else {
				p.property.AppendLine(string.Format("public {0} {1} \n" +
													"{{\n" +
													"get {{ return {2}; }}\n" +
													"set{{\n" +
													"if({2} == value)\n" +
													"return;\n" +
													"{2} = value;\n" +
													"PlayerPrefs.Set{4}({3}, {2});\n" +
													"if(On{1} != null)\n" +
													"On{1}({2});\n" +
													"}}\n" +
													"}}", fieldType, maxFieldName, fieldName, unName,
					maxFieldType));

				p.initMethod.AppendLine(string.Format("{0} = PlayerPrefs.Get{3}({1}, {2});\n", fieldName, unName,
					defaultVal, maxFieldType));

				p.saveMethod.AppendLine(string.Format("PlayerPrefs.Set{2}({0}, {1});", unName, fieldName,
					maxFieldType));
				p.deleteAllMethod.AppendLine(string.Format("PlayerPrefs.DeleteKey({0});", unName));
			}

			p.property.AppendLine(string.Format("public void Clear{0}() {{ {0} = {1}; }}\n",
				maxFieldName, defaultVal));

            p.clearActionMethod.AppendLine(string.Format("On{0} = null;", maxFieldName));
        }

        public static void GenerateVirtualMore(GenerateInfo info)
        {
            string fieldType = info.fieldType;
            string defaultVal = info.defaultVal;
            string unName = info.unName;
            PlayerRefsSB p = info.p;
            string maxFieldName = info.maxFieldName;
            string maxFieldType = info.maxFieldType;

            string isMethod = string.Empty;
            string setMethod = string.Empty;
            string getMethod = string.Empty;
            if (fieldType == "int")
            {
                setMethod = "public void Set{0}(string name,int val)"
                            + "{{\n"
                            + "PlayerPrefs.SetInt(\"{0}_\" + name, val);\n"
                            + "}}\n";

                getMethod = "public {0} Get{1}(string name)"
                            + "{{\n"
                            + "return PlayerPrefs.GetInt(\"{1}_\"  + name, 0);\n"
                            + "}}\n";

                isMethod = "public bool Is{0}(string name)"
                           + "{{\n"
                           + "return PlayerPrefs.GetInt(\"{1}_\"  + name, 0) > 0;\n"
                           + "}}\n";
            }

            if (fieldType == "string")
            {
                setMethod = "public void Set{0}(string name,string valStr)"
                            + "{{\n"
                            + "PlayerPrefs.SetString(\"{0}_\" + name, valStr);\n"
                            + "}}\n";

                getMethod = "public {0} Get{1}(string name)"
                            + "{{\n"
                            + "return PlayerPrefs.GetString(\"{1}_\"  + name, \"\");\n"
                            + "}}\n";

                isMethod = "public bool Is{0}(string name)"
                           + "{{\n"
                           + "return PlayerPrefs.GetString(\"{1}_\"  + name, \"\") == \"1\";\n"
                           + "}}\n";
            }

            p.virtualMethod.AppendLine(string.Format(isMethod, maxFieldName, maxFieldName, defaultVal));
            p.virtualMethod.AppendLine(string.Format(setMethod, maxFieldName, unName, defaultVal));
            p.virtualMethod.AppendLine(string.Format(getMethod, fieldType, maxFieldName, unName, defaultVal));
        }

        public class GenerateInfo
        {
            public PlayerRefsSB p;
            public string fieldType;
            public string fieldDes;
            public string maxFieldName;
            public string fieldName;
            public string defaultVal;
            public string unName;
            public string maxFieldType;
            public bool isList;
            public bool isSec;
        }

        public class PlayerRefsSB
        {
            public StringBuilder fieldCall = new StringBuilder();
            public StringBuilder field = new StringBuilder();
            public StringBuilder fieldUn = new StringBuilder();
            public StringBuilder property = new StringBuilder();
            public StringBuilder initMethod = new StringBuilder();
            public StringBuilder saveMethod = new StringBuilder();
            public StringBuilder deleteAllMethod = new StringBuilder();
            public StringBuilder clearActionMethod = new StringBuilder();
            public StringBuilder virtualMethod = new StringBuilder();

            public string GetClassContent()
            {
                return field.ToString() + fieldCall + fieldUn + property + virtualMethod;
            }
        }


        public class DefineScript
        {
            public static string boolProperty = "private {0} {1} \n" +
                                                "{{\n" +
                                                "get {{ return {2} > 0; }}\n" +
                                                "set{{\n" +
                                                "if({2} == value)\n" +
                                                "return;\n" +
                                                "{2} = value\n" +
                                                "PlayerPrefs.SetInt({3}, {2});\n" +
                                                "}}\n" +
                                                "}}\n";


            public static string templateClass = "using System.Collections;\n" +
                                                 "using System.Collections.Generic;\n" +
                                                 "using System;\n" +
                                                 "using UnityEngine;\n" +
                                                 "using System.IO;\n" +
                                                 //"public class ${className} : Singleton<${className}>\n" +
                                                 "public class ${className} \n" +
                                                 "{\n" +
                                                 "private static ${className} i;" +
                                                 "public static ${className} I\n" +
                                                 "{\n" +
                                                 "get\n" +
                                                 "{\n" +
                                                 "if (i == null) {\n" +
                                                 "i = new ${className}();\n" +
                                                 "i.Load();\n}\n" +
                                                 "return i;\n" +
                                                 "}\n" +
                                                 "}\n" +
                                                 "${classContent}\n" +
                                                 "${classLoad}\n" +
                                                 "${classSave}\n" +
                                                 "${classDeleteAll}\n" +
                                                 "${classClearAction}\n" +
                                                 "}\n";

            public static string loadMethod = "public void Load()\n" +
                                              "{\n" +
                                              "${classLoad}\n" +
                                              "}\n";

            public static string saveMethod = "public void Save()\n" +
                                              "{\n" +
                                              "${classSave}" +
                                              "PlayerPrefs.Save();\n\n" +
                                              "}\n";
            public static string deleteAllMethod = "public void DeleteAll()\n" +
                                              "{\n" +
                                              "${classDeleteAll}" +
                                              "PlayerPrefs.Save();\n" +
                                              "Load();\n\n" +
                                              "}\n";


            public static string clearActionMethod = "public void ClearAllAction()\n" +
                                              "{\n" +
                                              "${classClearAction}\n" +
                                              "}\n";
        }
    }
}
