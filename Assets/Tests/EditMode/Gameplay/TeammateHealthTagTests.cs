using NUnit.Framework;
using UnityEngine;
using Tower.Netcode;

public class TeammateHealthTagTests
{
    [Test]
    public void SetState_ShowsNameAndClampedAuthoritativeHealth()
    {
        var player = new GameObject("Teammate");
        try
        {
            var tag = player.AddComponent<TeammateHealthTag>();
            tag.SetState("队友", 35f, 100f, true);

            Assert.That(tag.DisplayedName, Is.EqualTo("队友"));
            Assert.That(tag.HealthFill.fillAmount, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(tag.HealthFill.rectTransform.rect.width, Is.EqualTo(39.2f).Within(0.01f));
            Assert.That(tag.IsVisible, Is.True);

            tag.SetState("队友", 140f, 100f, true);
            Assert.That(tag.HealthFill.fillAmount, Is.EqualTo(1f));
            Assert.That(tag.HealthFill.rectTransform.rect.width, Is.EqualTo(112f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void SetState_HidesOnDeathAndReturnsOnRevive()
    {
        var player = new GameObject("Teammate");
        try
        {
            var tag = player.AddComponent<TeammateHealthTag>();
            tag.SetState("队友", 0f, 100f, false);
            Assert.That(tag.IsVisible, Is.False);

            tag.SetState("队友", 40f, 100f, true);
            Assert.That(tag.IsVisible, Is.True);
            Assert.That(tag.HealthFill.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }
}
