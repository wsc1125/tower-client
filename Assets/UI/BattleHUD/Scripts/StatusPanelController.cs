using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image hpFillImage;
    [SerializeField] private Image mpFillImage;
    [SerializeField] private TMP_Text levelText;

    [Header("Preview Values")]
    [SerializeField] private float currentHealth = 85f;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentMana = 70f;
    [SerializeField] private float maxMana = 100f;
    [SerializeField] private int level = 1;

    public void SetHealth(float current, float max)
    {
        maxHealth = Mathf.Max(1f, max);
        currentHealth = Mathf.Clamp(current, 0f, maxHealth);
        Refresh();
    }

    public void SetMana(float current, float max)
    {
        maxMana = Mathf.Max(1f, max);
        currentMana = Mathf.Clamp(current, 0f, maxMana);
        Refresh();
    }

    public void SetLevel(int value)
    {
        level = Mathf.Max(1, value);
        Refresh();
    }

    private void Awake()
    {
        Refresh();
    }

    private void OnValidate()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (hpFillImage != null)
        {
            hpFillImage.fillAmount = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
        }

        if (mpFillImage != null)
        {
            mpFillImage.fillAmount = Mathf.Clamp01(currentMana / Mathf.Max(1f, maxMana));
        }

        if (levelText != null)
        {
            levelText.text = $"Lv.{level}";
        }
    }
}
