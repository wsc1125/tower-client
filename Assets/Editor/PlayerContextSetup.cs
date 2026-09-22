using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一配置玩家库存宿主：主菜单与战斗场景各有一个 InventoryHolder，
/// 两者都从同一份存档读写，并共享同一套首次建档的初始装备。
/// </summary>
public static class PlayerContextSetup
{
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string BattleScenePath = "Assets/Scenes/BattleScene.unity";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";

    private static readonly string[] InitialItemPaths =
    {
        "Assets/Inventory/Items/TrainingSword.asset",
        "Assets/Inventory/Items/TrainingBow.asset",
    };

    private static readonly string[] InitialEquippedPaths =
    {
        "Assets/Inventory/Items/Rifle.asset",
    };

    [MenuItem("Tools/仓库/配置玩家库存宿主")]
    public static void Configure()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
        if (database == null)
            Debug.LogError($"[PlayerContextSetup] 找不到 ItemDatabase：{ItemDatabasePath}");

        ConfigureMainMenu(database);
        ConfigureBattleScene(database);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerContextSetup] 玩家库存宿主配置完成。");
    }

    private static void ConfigureMainMenu(ItemDatabase database)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var holder = contents.GetComponentInChildren<InventoryHolder>(true);
            if (holder == null)
            {
                Debug.LogError("[PlayerContextSetup] MainMenu 中找不到 InventoryHolder。");
                return;
            }

            Apply(holder, database);
            PrefabUtility.SaveAsPrefabAsset(contents, MainMenuPrefabPath);
            saved = true;
        }
        finally
        {
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ConfigureBattleScene(ItemDatabase database)
    {
        var scene = SceneManager.GetSceneByPath(BattleScenePath);
        var openedHere = !scene.isLoaded;

        // 以叠加方式打开，避免丢掉当前已打开且可能未保存的场景。
        if (openedHere)
            scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Additive);

        try
        {
            var holder = FindHolder(scene);
            if (holder == null)
            {
                Debug.LogError("[PlayerContextSetup] BattleScene 中找不到 InventoryHolder。");
                return;
            }

            Apply(holder, database);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static InventoryHolder FindHolder(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var holder = root.GetComponentInChildren<InventoryHolder>(true);
            if (holder != null)
                return holder;
        }

        return null;
    }

    /// <summary>写入存档开关、物品登记表与首次建档用的初始装备。</summary>
    private static void Apply(InventoryHolder holder, ItemDatabase database)
    {
        var serialized = new SerializedObject(holder);
        serialized.FindProperty("loadFromSave").boolValue = true;
        serialized.FindProperty("database").objectReferenceValue = database;

        var items = serialized.FindProperty("initialItems");
        items.arraySize = InitialItemPaths.Length;
        for (var i = 0; i < InitialItemPaths.Length; i++)
            items.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(InitialItemPaths[i]);

        var equipped = serialized.FindProperty("initialEquippedItems");
        equipped.arraySize = InitialEquippedPaths.Length;
        for (var i = 0; i < InitialEquippedPaths.Length; i++)
            equipped.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>(InitialEquippedPaths[i]);

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(holder);
    }
}
