using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image hpFillImage;
    [SerializeField] private TMP_Text levelText;

    [Header("Preview Values")]
    [SerializeField] private float currentHealth = 85f;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private int level = 1;

    public void SetHealth(float current, float max)
    {
        maxHealth = Mathf.Max(1f, max);
        currentHealth = Mathf.Clamp(current, 0f, maxHealth);
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

        if (levelText != null)
        {
            levelText.text = $"Lv.{level}";
        }
    }
}
