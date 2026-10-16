using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 收藏存档读写：<see cref="CollectionProgress"/> ↔ <see cref="CollectionSaveData"/> ↔ PlayerPrefs JSON。
///
/// 刻意使用**独立存档键**（<see cref="PrefsKey"/>），与仓库的 <c>PlayerSave</c> 分开：
/// 收藏是长期进度，不该因仓库存档损坏而一起清零。
///
/// 键按账号隔离（规则见 <see cref="LocalCacheKeys"/>）：未登录用全局键，登录后用账号键。
/// 云存档落地时直接走 <see cref="SaveData"/>，避免进度对象与数据对象来回转换。
/// </summary>
public static class CollectionStore
{
    public const string PrefsKey = "CollectionSave";

    /// <summary>兼容旧调用的全局键读取判断；登录后请用 <see cref="HasSaveFor"/>。</summary>
    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey);

    public static bool HasSaveFor(string playerId = null) => PlayerPrefs.HasKey(KeyFor(playerId));

    /// <summary>账号隔离的缓存键：未登录为全局键，登录后为账号键。</summary>
    public static string KeyFor(string playerId = null) => LocalCacheKeys.For(PrefsKey, playerId);

    /// <summary>读取原始收藏数据。缺失或解析失败时返回 null，不抛异常。</summary>
    public static CollectionSaveData LoadData(string playerId = null)
    {
        var key = KeyFor(playerId);
        if (!PlayerPrefs.HasKey(key))
            return null;

        var json = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;

        CollectionSaveData data;
        try
        {
            data = JsonUtility.FromJson<CollectionSaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CollectionStore] 收藏存档解析失败，已回退为空进度：{e.Message}");
            return null;
        }

        if (data == null)
        {
            Debug.LogWarning("[CollectionStore] 收藏存档为空，已回退为空进度。");
            return null;
        }

        data.Validate();
        return data;
    }

    /// <summary>读取存档并还原成运行时进度。缺失或损坏时返回空进度。</summary>
    public static CollectionProgress Load(string playerId = null)
    {
        var progress = new CollectionProgress();
        var data = LoadData(playerId);
        if (data == null)
            return progress;

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

    /// <summary>把运行时进度转成可序列化数据。</summary>
    public static CollectionSaveData Capture(CollectionProgress progress)
    {
        return new CollectionSaveData
        {
            version = CollectionSaveData.CurrentVersion,
            obtainedIds = new List<string>(progress.ObtainedIds),
            litIds = new List<string>(progress.LitIds),
            allCollectedRewarded = progress.AllCollectedRewarded,
        };
    }

    /// <summary>写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(CollectionProgress progress, string playerId = null)
    {
        if (progress == null)
            return;

        SaveData(Capture(progress), playerId);
    }

    /// <summary>直接写入原始数据（云存档落地路径使用）。</summary>
    public static void SaveData(CollectionSaveData data, string playerId = null)
    {
        if (data == null)
            return;

        PlayerPrefs.SetString(KeyFor(playerId), JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete(string playerId = null)
    {
        PlayerPrefs.DeleteKey(KeyFor(playerId));
        PlayerPrefs.Save();
    }
}
