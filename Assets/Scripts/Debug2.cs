using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Debug2 : Object {
	public static void Log(params object[] v){
		string s = "";
		for (int i = 0; i < v.Length; i++) {
			if (i > 0)
				s += ", ";
			if (v[i] == null) {
				s += "null";
			} else {
				s += v[i].ToString();
			}
		}
		Debug.Log(s);
		//Debug.Log("[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s);
	}

	public static void Log2(params object[] v){
		string s = "";
		for (int i = 0; i < v.Length; i++) {
			if (i > 0)
				s += ", ";
			if (v[i] == null) {
				s += "null";
			} else {
				s += v[i].ToString();
			}
		}
		//Debug.Log("[" + Time.time + "] " + s);
		Debug.Log("[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s);
	}

	public static void LogWarning(params object[] v){
		string s = "";
		for (int i = 0; i < v.Length; i++) {
			if (i > 0)
				s += ", ";
			if (v[i] == null) {
				s += "null";
			} else {
				s += v[i].ToString();
			}
		}
		//Debug.LogWarning(s);
		Debug.LogWarning("[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s);
		//Debug.Log("[" + Time.time + "] " + s);
		//Debug.Log("[" + Time.time + "] " + "<color=yellow>" + s + "</color>");
		//Debug.Log("<color=yellow>" + s + "</color>");
	}

	public static void LogError(params object[] v){
		string s = "";
		for (int i = 0; i < v.Length; i++) {
			if (i > 0)
				s += ", ";
			if (v[i] == null) {
				s += "null";
			} else {
				s += v[i].ToString();
			}
		}
		Debug.LogError(s);
	}

	public static void Assert(object expect, object ret) {
		if (expect.ToString() != ret.ToString()) {
			LogError(expect, ret);
		} else {
			Log(expect, ret);
		}
	}

	public static string Dump(IDictionary dict, bool compact = false) {
		var s = "";
		foreach (var item in dict.Keys) {
			if (!string.IsNullOrEmpty(s))
				s += (compact ? "; " : "\n");
			s += item + ": " + ((dict[item] is System.Collections.IDictionary) ? ('{'+ Debug2.Dump((IDictionary)dict[item], true) +'}') : dict[item].ToString());
		}
		return s;
	}
}
