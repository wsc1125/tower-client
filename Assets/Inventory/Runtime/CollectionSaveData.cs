using System;
using System.Collections.Generic;

/// <summary>
/// 收藏存档载体。只存 ID 字符串，不存物品引用。
/// </summary>
[Serializable]
public sealed class CollectionSaveData
{
    /// <summary>当前存档格式版本。</summary>
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public List<string> obtainedIds = new List<string>();
    public List<string> litIds = new List<string>();
    public bool allCollectedRewarded;

    /// <summary>补齐可能为 null 的列表（JsonUtility 对 JSON 中显式 null 的列表字段会置 null）。</summary>
    public void Validate()
    {
        obtainedIds ??= new List<string>();
        litIds ??= new List<string>();
    }
}
