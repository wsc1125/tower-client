using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class StatusPanelControllerTests
{
    private const string PrefabPath = "Assets/UI/BattleHUD/Prefabs/Modules/StatusPanel.prefab";

    [Test]
    public void StatusPanelPrefab_HasRequiredStructure()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<StatusPanelController>(), Is.Not.Null);
        Assert.That(prefab.transform.Find("Frame"), Is.Not.Null);
        Assert.That(prefab.transform.Find("HpFill"), Is.Not.Null);
        Assert.That(prefab.transform.Find("MpFill"), Is.Not.Null);
        Assert.That(prefab.transform.Find("Badge"), Is.Not.Null);
    }

    [Test]
    public void StatusPanelController_UpdatesFillAmounts()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = Object.Instantiate(prefab);

        try
        {
            var controller = instance.GetComponent<StatusPanelController>();
            var hpFill = instance.transform.Find("HpFill").GetComponent<Image>();
            var mpFill = instance.transform.Find("MpFill").GetComponent<Image>();

            controller.SetHealth(50f, 100f);
            controller.SetMana(25f, 100f);

            Assert.That(hpFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(mpFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void StatusPanelPrefab_PlacesBarsInSeparateSlotsAboveFrame()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);

        var hpFill = prefab.transform.Find("HpFill") as RectTransform;
        var mpFill = prefab.transform.Find("MpFill") as RectTransform;
        var frame = prefab.transform.Find("Frame") as RectTransform;

        Assert.That(hpFill, Is.Not.Null);
        Assert.That(mpFill, Is.Not.Null);
        Assert.That(frame, Is.Not.Null);

        Assert.That(hpFill.GetSiblingIndex(), Is.GreaterThan(frame.GetSiblingIndex()));
        Assert.That(mpFill.GetSiblingIndex(), Is.GreaterThan(frame.GetSiblingIndex()));
        Assert.That(hpFill.anchoredPosition.y, Is.GreaterThan(mpFill.anchoredPosition.y));
        Assert.That(Mathf.Abs(hpFill.anchoredPosition.y - mpFill.anchoredPosition.y), Is.GreaterThan(10f));
    }
}
