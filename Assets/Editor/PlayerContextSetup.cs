using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一配置玩家数据入口：把 <see cref="ItemDatabase"/> 接到主菜单与战斗场景里所有需要它的组件上。
/// 玩家数据本身由 <see cref="GameSession"/> 在进程内单例持有，不再需要场景里的库存宿主。
/// </summary>
public static class PlayerContextSetup
{
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string BattleScenePath = "Assets/Scenes/BattleScene.unity";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";

    /// <summary>首次建档时穿在身上的初始装备。</summary>
    private static readonly string[] InitialEquippedPaths =
    {
        "Assets/Inventory/Items/Rifle.asset",
    };

    [MenuItem("Tools/仓库/配置玩家数据入口")]
    public static void Configure()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
        if (database == null)
            Debug.LogError($"[PlayerContextSetup] 找不到 ItemDatabase：{ItemDatabasePath}");

        ConfigureMainMenu(database);
        ConfigureBattleScene(database);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PlayerContextSetup] 玩家数据入口配置完成。");
    }

    private static void ConfigureMainMenu(ItemDatabase database)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var wired = 0;
            foreach (var controller in contents.GetComponentsInChildren<LoadoutPanelController>(true))
            {
                SetDatabase(controller, database);
                wired++;
            }

            foreach (var controller in contents.GetComponentsInChildren<ShopPanelController>(true))
            {
                SetDatabase(controller, database);
                wired++;
            }

            if (wired == 0)
                Debug.LogWarning("[PlayerContextSetup] MainMenu 中未找到需要 ItemDatabase 的面板。");

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
            var wired = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var controller in root.GetComponentsInChildren<BagPanelController>(true))
                {
                    SetDatabase(controller, database);
                    wired++;
                }
            }

            if (wired == 0)
                Debug.LogWarning("[PlayerContextSetup] BattleScene 中未找到需要 ItemDatabase 的面板。");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>把登记表写入组件的 database 字段。</summary>
    private static void SetDatabase(Object target, ItemDatabase database)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty("database");
        if (property == null)
        {
            Debug.LogWarning($"[PlayerContextSetup] {target.GetType().Name} 上没有 database 字段，已跳过。");
            return;
        }

        property.objectReferenceValue = database;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
