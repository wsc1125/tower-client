using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TowerResultPanelControllerTests
{
    private GameObject root;
    private TowerResultPanelController panel;
    private GameObject panelRoot;
    private TowerResultRowView[] rows;
    private Text totalValueText;
    private TextMeshProUGUI floorText;
    private Button confirmButton;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("TowerResultPanel");
        panel = root.AddComponent<TowerResultPanelController>();

        panelRoot = new GameObject("PanelRoot");
        panelRoot.transform.SetParent(root.transform);

        rows = new TowerResultRowView[3];
        for (var i = 0; i < rows.Length; i++)
        {
            var rowObject = new GameObject("Row" + i);
            rowObject.transform.SetParent(panelRoot.transform);
            rows[i] = rowObject.AddComponent<TowerResultRowView>();
        }

        var totalObject = new GameObject("TotalValue");
        totalObject.transform.SetParent(panelRoot.transform);
        totalValueText = totalObject.AddComponent<Text>();

        var floorObject = new GameObject("Floor");
        floorObject.transform.SetParent(panelRoot.transform);
        floorText = floorObject.AddComponent<TextMeshProUGUI>();

        var buttonObject = new GameObject("Confirm");
        buttonObject.transform.SetParent(panelRoot.transform);
        confirmButton = buttonObject.AddComponent<Button>();

        var serialized = new UnityEditor.SerializedObject(panel);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        var rowsProperty = serialized.FindProperty("rows");
        rowsProperty.arraySize = rows.Length;
        for (var i = 0; i < rows.Length; i++)
            rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
        serialized.FindProperty("totalValueText").objectReferenceValue = totalValueText;
        serialized.FindProperty("floorText").objectReferenceValue = floorText;
        serialized.FindProperty("confirmButton").objectReferenceValue = confirmButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Awake 跑在 AddComponent 那一刻，此时引用还没赋值，需要补绑一次。
        panel.BindConfirmButton();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    private static TowerResultEntry Entry(string name, int value)
    {
        return new TowerResultEntry { DisplayName = name, Value = value, Icon = null };
    }

    [Test]
    public void Show_ActivatesPanelAndFillsRows()
    {
        panel.Show(new List<TowerResultEntry> { Entry("长剑", 60), Entry("狗奶", 30) }, 90, 5);

        Assert.That(panel.IsVisible, Is.True);
        Assert.That(rows[0].gameObject.activeSelf, Is.True);
        Assert.That(rows[1].gameObject.activeSelf, Is.True);
        Assert.That(rows[2].gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Show_SetsTotalValueAndFloorText()
    {
        panel.Show(new List<TowerResultEntry> { Entry("长剑", 60) }, 60, 7);

        Assert.That(totalValueText.text, Is.EqualTo("总价值 60"));
        Assert.That(floorText.text, Is.EqualTo("抵达第 7 层"));
    }

    [Test]
    public void Show_HandlesEmptyList()
    {
        panel.Show(new List<TowerResultEntry>(), 0, 1);

        Assert.That(panel.IsVisible, Is.True);
        Assert.That(rows[0].gameObject.activeSelf, Is.False);
        Assert.That(totalValueText.text, Is.EqualTo("总价值 0"));
    }

    [Test]
    public void Show_HandlesNullList()
    {
        Assert.DoesNotThrow(() => panel.Show(null, 0, 1));
        Assert.That(rows[0].gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Hide_DeactivatesPanel()
    {
        panel.Show(new List<TowerResultEntry> { Entry("长剑", 60) }, 60, 5);

        panel.Hide();

        Assert.That(panel.IsVisible, Is.False);
    }

    [Test]
    public void ConfirmButton_RaisesConfirmed()
    {
        var raised = false;
        panel.Confirmed += () => raised = true;

        confirmButton.onClick.Invoke();

        Assert.That(raised, Is.True);
    }
}
