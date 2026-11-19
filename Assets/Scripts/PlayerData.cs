using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using System.IO;
public class PlayerData 
{
private static PlayerData i;public static PlayerData I
{
get
{
if (i == null) {
i = new PlayerData();
i.Load();
}
return i;
}
}
private zint curLevel = GameDataConfig.DefaultPlayerLevel;
public Action<int> OnCurLevel;
private string curLevelUn = "curLevelUn";

public int CurLevel 
{
get { return curLevel; }
set{
if(curLevel == value)
return;
curLevel = value;
PlayerPrefs.SetInt(curLevelUn, curLevel);
if(OnCurLevel != null)
OnCurLevel(curLevel);
}
}
public void ClearCurLevel() { CurLevel = GameDataConfig.DefaultPlayerLevel; }


public void Load()
{
curLevel = PlayerPrefs.GetInt(curLevelUn, GameDataConfig.DefaultPlayerLevel);


}

public void Save()
{
PlayerPrefs.SetInt(curLevelUn, curLevel);
PlayerPrefs.Save();

}

public void DeleteAll()
{
PlayerPrefs.DeleteKey(curLevelUn);
PlayerPrefs.Save();
Load();

}

public void ClearAllAction()
{
OnCurLevel = null;

}

}
