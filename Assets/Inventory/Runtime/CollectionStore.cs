using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 收藏存档读写：<see cref="CollectionProgress"/> ↔ <see cref="CollectionSaveData"/> ↔ PlayerPrefs JSON。
///
/// 刻意使用**独立存档键**（<see cref="PrefsKey"/>），与仓库的 <c>PlayerSave</c> 分开：
/// 收藏是长期进度，不该因仓库存档损坏而一起清零。
/// </summary>
public static class CollectionStore
{
    public const string PrefsKey = "CollectionSave";

    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey);

    /// <summary>读取存档。缺失或解析失败时返回空进度，不抛异常。</summary>
    public static CollectionProgress Load()
    {
        var progress = new CollectionProgress();

        if (!PlayerPrefs.HasKey(PrefsKey))
            return progress;

        var json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return progress;

        CollectionSaveData data;
        try
        {
            data = JsonUtility.FromJson<CollectionSaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CollectionStore] 收藏存档解析失败，已回退为空进度：{e.Message}");
            return progress;
        }

        if (data == null)
        {
            Debug.LogWarning("[CollectionStore] 收藏存档为空，已回退为空进度。");
            return progress;
        }

        data.Validate();

        foreach (var id in data.obtainedIds)
            progress.Obtain(id);

        foreach (var id in data.litIds)
        {
            // 先补获得资格再点亮，避免脏数据导致点亮丢失。
            progress.Obtain(id);
            progress.TryLight(id);
        }

        progress.AllCollectedRewarded = data.allCollectedRewarded;
        progress.Reconcile();
        return progress;
    }

    /// <summary>写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(CollectionProgress progress)
    {
        if (progress == null)
            return;

        var data = new CollectionSaveData
        {
            version = CollectionSaveData.CurrentVersion,
            obtainedIds = new List<string>(progress.ObtainedIds),
            litIds = new List<string>(progress.LitIds),
            allCollectedRewarded = progress.AllCollectedRewarded,
        };

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }
}
