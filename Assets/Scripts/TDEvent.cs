using System.Collections.Generic;
using UnityEngine;

public enum TDEventType
{
	MemError = 255,
}

public class TDEvent
{
	private const string EventPrefsKey = "evts";
	private static readonly Dictionary<int, object> Evts = new Dictionary<int, object>();

	public static void Init()
	{
		Evts.Clear();
	}

	public static void Set(TDEventType type, int val)
	{
		SetValue((int)type, val);
	}

	public static void Set(TDEventType type, uint val)
	{
		SetValue((int)type, val);
	}

	public static void Set(TDEventType type, long val)
	{
		SetValue((int)type, val);
	}

	public static void Set(int targetId, TDEventType type, int val)
	{
		SetValue(((int)type << 24) | targetId, val);
	}

	public static void SetString(TDEventType type, string val)
	{
		SetValue((int)type, val);
	}

	public static void SetIncr(TDEventType type, int val)
	{
		int key = 1000 + (int)type;
		if (Evts.TryGetValue(key, out object oldVal) && oldVal is int oldInt)
		{
			Evts[key] = oldInt + val;
		}
		else
		{
			Evts[key] = val;
		}
		Save();
	}

	public static bool TrySync(bool noAnyDelayOrFilter = false)
	{
		Save();
		return Evts.Count > 0;
	}

	public static void StoreTxid(int txid)
	{
		PlayerPrefs.SetInt("evts_syncing_txid", txid);
	}

	public static void ParseParams(string evtsStr, Dictionary<string, string> dict, char sepItem = '\n')
	{
		var arr = evtsStr.Split(sepItem);
		for (int i = 0; i < arr.Length; ++i)
		{
			var item = arr[i].Trim();
			if (string.IsNullOrEmpty(item))
			{
				continue;
			}

			var arr2 = item.Split('=');
			if (arr2.Length >= 2)
			{
				dict[arr2[0]] = arr2[1];
			}
		}
	}

	public static void ParseEvts(string evtsStr, Dictionary<int, long> dict, Dictionary<int, string> dict2 = null)
	{
		var arr = evtsStr.Split('\n');
		for (int i = 0; i < arr.Length; ++i)
		{
			var item = arr[i].Trim();
			if (string.IsNullOrEmpty(item))
			{
				continue;
			}

			var arr2 = item.Split('=');
			if (arr2.Length < 2 || !int.TryParse(arr2[0], out int key))
			{
				continue;
			}

			if (long.TryParse(arr2[1], out long val))
			{
				dict[key] = val;
			}
			else if (dict2 != null)
			{
				dict2[key] = arr2[1];
			}
		}
	}

	public static void MemError(int type, string orig, string result)
	{
		var str = $"{type},{orig},{result}";
		Debug2.LogWarning($"memory error, {str}");
		SetString(TDEventType.MemError, str);
	}

	private static void SetValue(int key, object val)
	{
		Evts[key] = val;
		Save();
	}

	private static void Save()
	{
		string evtsStr = "";
		int i = 0;
		foreach (var item in Evts)
		{
			if (i++ > 0)
			{
				evtsStr += "\n";
			}
			evtsStr += item.Key + "=" + item.Value;
		}
		PlayerPrefs.SetString(EventPrefsKey, evtsStr);
	}
}
