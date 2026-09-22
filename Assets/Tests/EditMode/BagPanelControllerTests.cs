using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class BagPanelControllerTests
{
    [Test]
    public void Toggle_DoesNotChangeTimeScale()
    {
        var root = new GameObject("Controller");
        var content = new GameObject("Content");
        content.transform.SetParent(root.transform);
        var controller = root.AddComponent<BagPanelController>();
        controller.Configure(content, null, null, null);
        var originalTimeScale = Time.timeScale;

        controller.Close();
        Assert.That(controller.IsOpen, Is.False);
        controller.Toggle();
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));

        Object.DestroyImmediate(root);
    }

    [Test]
    public void TryDrop_MovesMatchingBagItemToEquipment()
    {
        var root = new GameObject("Controller");
        var controller = root.AddComponent<BagPanelController>();
        controller.Configure(root, null, null, null);
        var sword = CreateItem(EquipmentSlotType.MeleeWeapon);
        controller.Model.TryAdd(sword);

        var result = controller.TryDrop(
            BagDragSource.FromBag(0, sword),
            BagDropTarget.Equipment(EquipmentSlotType.MeleeWeapon));

        Assert.That(result, Is.True);
        Assert.That(controller.Model.GetEquippedItem(EquipmentSlotType.MeleeWeapon), Is.SameAs(sword));
        Object.DestroyImmediate(root);
    }

    [Test]
    public void EndDrag_ClearsTargetHighlight()
    {
        var root = new GameObject("Controller");
        var slotObject = new GameObject("Slot");
        slotObject.transform.SetParent(root.transform);
        var frame = slotObject.AddComponent<Image>();
        var slot = slotObject.AddComponent<BagItemView>();
        var serializedSlot = new SerializedObject(slot);
        serializedSlot.FindProperty("frame").objectReferenceValue = frame;
        serializedSlot.ApplyModifiedPropertiesWithoutUndo();

        var controller = root.AddComponent<BagPanelController>();
        controller.Configure(root, new[] { slot }, null, null);
        var item = CreateItem(EquipmentSlotType.MeleeWeapon);
        controller.BeginDrag(BagDragSource.FromBag(0, item), null);
        controller.SetTargetHighlight(frame, BagDropTarget.Bag(0), true);
        Assert.That(frame.color, Is.Not.EqualTo(Color.white));

        controller.EndDrag();

        Assert.That(frame.color, Is.EqualTo(Color.white));
        Object.DestroyImmediate(root);
    }

    [Test]
    public void BagPanelPrefab_ContainsExpectedSlotsAndSections()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/BattleHUD/Prefabs/BagPanel.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<BagPanelController>(), Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<BagItemView>(true).Length, Is.EqualTo(20));
        Assert.That(prefab.GetComponentsInChildren<EquipmentSlotView>(true).Length, Is.EqualTo(5));
        Assert.That(prefab.transform.Find("PanelContent/LeftColumn"), Is.Not.Null);
        Assert.That(prefab.transform.Find("PanelContent/RightColumn/Grid"), Is.Not.Null);
        Assert.That(prefab.transform.Find("DragLayer"), Is.Not.Null);
    }

    [Test]
    public void TrainingItems_HaveCorrectTypesAndIcons()
    {
        var sword = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/TrainingSword.asset");
        var bow = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/TrainingBow.asset");

        Assert.That(sword, Is.Not.Null);
        Assert.That(sword.EquipSlotType, Is.EqualTo(EquipmentSlotType.MeleeWeapon));
        Assert.That(sword.Icon, Is.Not.Null);
        Assert.That(bow, Is.Not.Null);
        Assert.That(bow.EquipSlotType, Is.EqualTo(EquipmentSlotType.RangedWeapon));
        Assert.That(bow.Icon, Is.Not.Null);
    }

    [Test]
    public void EnsureModel_UsesWiredInventoryHolder()
    {
        var holderRoot = new GameObject("Holder");
        var holder = holderRoot.AddComponent<InventoryHolder>();

        var controllerRoot = new GameObject("Controller");
        var controller = controllerRoot.AddComponent<BagPanelController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("holder").objectReferenceValue = holder;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        controller.Configure(controllerRoot, null, null, null);

        Assert.That(controller.Model, Is.SameAs(holder.Model));

        Object.DestroyImmediate(controllerRoot);
        Object.DestroyImmediate(holderRoot);
    }

    private static ItemDefinition CreateItem(EquipmentSlotType slot)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("equipSlotType").enumValueIndex = (int)slot;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }
}
