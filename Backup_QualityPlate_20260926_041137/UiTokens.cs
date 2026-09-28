using UnityEngine;

/// <summary>
/// 扁平路线的统一视觉令牌：色板与几何尺寸都收敛在这里。
/// 所有面板只引用令牌、不写字面值，改这一处即可让仓库、商店、背包、装备配置同时生效。
/// 放在 Inventory 程序集内，供局外（Assembly-CSharp）与局内（BattleHUD）共同引用。
/// </summary>
public static class UiTokens
{
    // ---- 色板（扁平 / 低饱和，无渐变、无发光、无纹理） ----
    // 面板与格子统一带透明度：弹窗是叠在界面之上的，半透明才能看出层级。

    /// <summary>面板底色（半透明）。</summary>
    public static readonly Color PanelBackground = Hex("171B1F", 0.90f);

    /// <summary>次级容器底色（详情框等，半透明）。</summary>
    public static readonly Color SurfaceBackground = Hex("12161A", 0.86f);

    /// <summary>普通格子底色（半透明）。</summary>
    public static readonly Color SlotBackground = Hex("0E1216", 0.80f);

    /// <summary>强描边（面板外框、分区线）。</summary>
    public static readonly Color LineStrong = Hex("3A4148");

    /// <summary>弱描边（普通格子）。</summary>
    public static readonly Color LineSoft = Hex("333B42");

    /// <summary>强调色·金：防具槽（头盔 / 护甲 / 靴）与关键数值。不用于品质，留给拖拽悬停。</summary>
    public static readonly Color AccentGold = Hex("C7A34A");

    /// <summary>防具槽底色（金色系深底，半透明）。</summary>
    public static readonly Color AccentGoldSurface = Hex("181307", 0.88f);

    /// <summary>强调色·青蓝：武器槽（近战 / 远程），也是「稀有」品质色。</summary>
    public static readonly Color AccentWeapon = Hex("4FA8C4");

    /// <summary>武器槽底色（青蓝系深底，半透明）。</summary>
    public static readonly Color AccentWeaponSurface = Hex("0C1A20", 0.88f);

    /// <summary>强调色·青：当前选中的格。</summary>
    public static readonly Color AccentCyan = Hex("5FD3E8");

    /// <summary>主文字。</summary>
    public static readonly Color TextPrimary = Hex("D5DCE1");

    /// <summary>次级文字（分区标题）。</summary>
    public static readonly Color TextSecondary = Hex("8B959C");

    /// <summary>弱化文字（计数、翻页、未选中详情）。</summary>
    public static readonly Color TextMuted = Hex("69737A");

    // ---- 品质（物品底板） ----

    /// <summary>品质底板底色：统一深底，品质只体现在描边与内部微染上。</summary>
    public static readonly Color RarityPlateBackground = Hex("0B1116", 0.94f);

    /// <summary>品质色在底板内部混入的比例（0~1）。太低看不出区别，太高会吃掉图标可读性。</summary>
    public const float RarityPlateTintAlpha = 0.18f;

    /// <summary>品质描边宽度（像素）。</summary>
    public const float RarityPlateBorderWidth = 4f;

    /// <summary>品质对应的强调色。传说刻意避开金色，把金色留给拖拽悬停。</summary>
    public static Color RarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Fine:
                return Hex("6FBF73");
            case ItemRarity.Rare:
                return AccentWeapon;
            case ItemRarity.Epic:
                return Hex("A97BD6");
            case ItemRarity.Legendary:
                return Hex("D94A4A");
            default:
                return Hex("C8D2D8");
        }
    }

    /// <summary>品质中文名，用于详情区展示。</summary>
    public static string RarityName(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Fine:
                return "精良";
            case ItemRarity.Rare:
                return "稀有";
            case ItemRarity.Epic:
                return "史诗";
            case ItemRarity.Legendary:
                return "传说";
            default:
                return "普通";
        }
    }

    // ---- 加载页 ----

    /// <summary>加载页背景：比弹窗更实，避免透出底下的主菜单。</summary>
    public static readonly Color LoadingBackdrop = Hex("0B0F13", 0.98f);

    /// <summary>进度条轨道底色。</summary>
    public static readonly Color LoadingTrack = Hex("232A31");

    // ---- 几何 ----

    /// <summary>描边统一宽度。格子与面板都用它，保证所有界面边框粗细一致。</summary>
    public const float BorderWidth = 2f;

    /// <summary>设计尺寸，用于整体等比缩放适配。</summary>
    public static readonly Vector2 DesignSize = new Vector2(1603f, 938f);

    private static Color Hex(string rgb)
    {
        return ColorUtility.TryParseHtmlString("#" + rgb, out var color) ? color : Color.magenta;
    }

    private static Color Hex(string rgb, float alpha)
    {
        var color = Hex(rgb);
        color.a = alpha;
        return color;
    }
}
