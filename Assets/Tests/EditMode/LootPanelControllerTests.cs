using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class LootPanelControllerTests
{
    private GameObject root;
    private GameObject panelRoot;
    private LootPanelController controller;
    private LootRowView[] rows;
    private GameObject[] rowObjects;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("LootPanel");
        panelRoot = new GameObject("PanelRoot");
        panelRoot.transform.SetParent(root.transform);

        rowObjects = new GameObject[4];
        rows = new LootRowView[4];
        for (var i = 0; i < rows.Length; i++)
        {
            rowObjects[i] = new GameObject($"Row{i}");
            rowObjects[i].transform.SetParent(panelRoot.transform);
            rowObjects[i].AddComponent<Image>();
            rowObjects[i].AddComponent<Button>();
            rows[i] = rowObjects[i].AddComponent<LootRowView>();
        }

        controller = root.AddComponent<LootPanelController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        var rowsProperty = serialized.FindProperty("rows");
        rowsProperty.arraySize = rows.Length;
        for (var i = 0; i < rows.Length; i++)
            rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void Show_ActivatesPanelAndOnlyFillsProvidedRows()
    {
        controller.Show(new List<LootEntry> { new LootEntry { DisplayName = "甲", Value = 10 } });

        Assert.That(controller.IsVisible, Is.True);
        Assert.That(rowObjects[0].activeSelf, Is.True);
        Assert.That(rowObjects[1].activeSelf, Is.False);
        Assert.That(rowObjects[3].activeSelf, Is.False);
    }

    [Test]
    public void Hide_DeactivatesPanel()
    {
        controller.Show(new List<LootEntry> { new LootEntry { DisplayName = "甲", Value = 10 } });

        controller.Hide();

        Assert.That(controller.IsVisible, Is.False);
    }

    [Test]
    public void Show_WithEmptyListHidesAllRows()
    {
        controller.Show(new List<LootEntry>());

        foreach (var rowObject in rowObjects)
            Assert.That(rowObject.activeSelf, Is.False);
    }

    [Test]
    public void RowClick_RaisesItemSelectedWithRowIndex()
    {
        var selected = -1;
        controller.ItemSelected += index => selected = index;
        controller.Show(new List<LootEntry>
        {
            new LootEntry { DisplayName = "甲", Value = 10 },
            new LootEntry { DisplayName = "乙", Value = 20 },
        });

        rows[1].GetComponent<Button>().onClick.Invoke();

        Assert.That(selected, Is.EqualTo(1));
    }

    [Test]
    public void ShowHint_SetsHintText()
    {
        var hintObject = new GameObject("Hint");
        hintObject.transform.SetParent(root.transform);
        var hint = hintObject.AddComponent<TMPro.TextMeshProUGUI>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("hintText").objectReferenceValue = hint;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        controller.ShowHint("背包已满");

        Assert.That(hint.text, Is.EqualTo("背包已满"));
    }

    [Test]
    public void SetEntry_MissingIconDisablesIconImage()
    {
        var iconObject = new GameObject("Icon");
        iconObject.transform.SetParent(rowObjects[0].transform);
        var icon = iconObject.AddComponent<Image>();
        var serialized = new SerializedObject(rows[0]);
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        rows[0].SetEntry(new LootEntry { DisplayName = "甲", Value = 10, Icon = null }, true);

        Assert.That(icon.enabled, Is.False);
    }
}
