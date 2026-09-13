using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BattleHUDTouchTargetTests
{
    private const string PrefabPath = "Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab";

    [Test]
    public void BattleHudPrefab_UsesComfortableTouchTargetSizes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);

        var joystick = prefab.transform.Find("Joystick") as RectTransform;
        var attackButton = prefab.transform.Find("AttackButton") as RectTransform;
        var attackSwitchButton = prefab.transform.Find("AttackSwitchButton") as RectTransform;
        var bagButton = prefab.transform.Find("BagButton") as RectTransform;

        Assert.That(joystick, Is.Not.Null);
        Assert.That(joystick.sizeDelta.x, Is.GreaterThanOrEqualTo(360f));

        Assert.That(attackButton, Is.Not.Null);
        Assert.That(attackButton.sizeDelta.x, Is.GreaterThanOrEqualTo(280f));

        Assert.That(attackSwitchButton, Is.Not.Null);
        Assert.That(attackSwitchButton.sizeDelta.x, Is.GreaterThanOrEqualTo(156f));

        Assert.That(bagButton, Is.Not.Null);
        Assert.That(bagButton.sizeDelta.x, Is.GreaterThanOrEqualTo(144f));
    }

    [Test]
    public void BattleHudPrefab_DefaultJoystickPositionKeepsBaseFullyVisible()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);

        var joystick = prefab.transform.Find("Joystick") as RectTransform;
        var baseRect = joystick != null ? joystick.Find("Base") as RectTransform : null;

        Assert.That(joystick, Is.Not.Null);
        Assert.That(baseRect, Is.Not.Null);

        var halfWidth = baseRect.sizeDelta.x * 0.5f;
        var halfHeight = baseRect.sizeDelta.y * 0.5f;

        Assert.That(baseRect.anchoredPosition.x, Is.GreaterThanOrEqualTo(halfWidth));
        Assert.That(baseRect.anchoredPosition.y, Is.GreaterThanOrEqualTo(halfHeight));
        Assert.That(baseRect.anchoredPosition.x + halfWidth, Is.LessThanOrEqualTo(joystick.sizeDelta.x));
        Assert.That(baseRect.anchoredPosition.y + halfHeight, Is.LessThanOrEqualTo(joystick.sizeDelta.y));
    }
}
