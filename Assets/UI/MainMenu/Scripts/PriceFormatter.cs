using System.Globalization;
using UnityEngine;

/// <summary>
/// 价格等数值的短格式：一万以下按原值显示，一万及以上缩写为 k，一百万及以上缩写为 M。
/// 卡片价格框在金币右侧只留出有限宽度，长数字会顶到金币图标上，故用缩写控制字符数。
/// </summary>
public static class PriceFormatter
{
    private const int KiloThreshold = 10000;
    private const int MegaThreshold = 1000000;

    /// <summary>把价格格式化为可直接显示的文本：120、9999、1.2k、12k、1.2M。</summary>
    public static string Format(int value)
    {
        if (value < KiloThreshold)
            return value.ToString(CultureInfo.InvariantCulture);

        if (value < MegaThreshold)
        {
            var thousands = Round1(value / 1000f);

            // 999950 以上四舍五入会得到 1000k，此时改用它不足 1 的 M 更简洁。
            if (thousands < 1000f)
                return thousands.ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        return Round1(value / 1000000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
    }

    /// <summary>四舍五入到一位小数，整数则省略小数点（12k 而非 12.0k）。</summary>
    private static float Round1(float value)
    {
        return Mathf.Floor(value * 10f + 0.5f) / 10f;
    }
}
