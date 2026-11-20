using System;
using UnityEngine;

public sealed class SceneElementRules : ScriptableObject
{
    [Flags]
    public enum Traits { None = 0, Passable = 1, BlocksSight = 2, BlocksSmell = 4, BlocksHearing = 8 }

    [SerializeField] private Traits[] byElement = Array.Empty<Traits>();
    private static SceneElementRules shared;
    public static SceneElementRules Shared => shared ? shared : shared = Resources.Load<SceneElementRules>("SceneElementRules");

    public Traits Get(GameDataConfig.SceneElement element)
    {
        int index = (int)element;
        return index >= 0 && index < byElement.Length ? byElement[index] : Traits.None;
    }

#if UNITY_EDITOR
    public void Set(GameDataConfig.SceneElement element, Traits traits)
    {
        int index = (int)element;
        if (index >= byElement.Length) Array.Resize(ref byElement, index + 1);
        byElement[index] = traits;
    }
#endif
}
