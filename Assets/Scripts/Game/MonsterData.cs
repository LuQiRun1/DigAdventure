using System.Collections.Generic;
using UnityEngine;

public sealed class MonsterData : ScriptableObject
{
    [SerializeField, TextArea(3, 8)] private string description = "";
    [SerializeField, Min(0f)] private float speed = GameDataConfig.DefaultMonsterSpeed;
    [SerializeField] private List<string> traits = new List<string>();

    public string Description => description;
    public float Speed => speed;
    public IReadOnlyList<string> Traits => traits;

    private void OnValidate()
    {
        speed = float.IsNaN(speed) || float.IsInfinity(speed)
            ? GameDataConfig.DefaultMonsterSpeed
            : Mathf.Max(0f, speed);
    }
}
