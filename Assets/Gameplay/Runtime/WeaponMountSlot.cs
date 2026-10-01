using UnityEngine;

/// <summary>
/// 角色的一只手上的武器挂点。只负责「把当前装备且被选中的武器显示出来」——
/// 未被选中的那只手会关掉渲染，攻击逻辑读的 <see cref="HasWeapon"/> 也随之变假，
/// 于是「同一时间只有一把武器显示、也只有它能攻击」由这一个开关统一保证。
///
/// 每次刷新都会按 <see cref="targetWorldWidth"/> 归一武器贴图宽度，
/// 并把枪口放到贴图右缘——各武器素材分辨率与轴心差异大，写死局部值换枪就会错位。
/// 瞄准旋转由 <see cref="RifleShooter2D"/> 驱动 <see cref="aimPivot"/> 完成，本组件不自己转。
/// </summary>
public sealed class WeaponMountSlot : MonoBehaviour
{
    [SerializeField] private SpriteRenderer weaponRenderer;
    [SerializeField] private PlayerWeaponSelector selector;
    [SerializeField] private EquipmentSlotType mountedSlot = EquipmentSlotType.MainHand;

    [Tooltip("瞄准旋转轴。武器与枪口挂在它下面，开火方向由它的旋转决定。")]
    [SerializeField] private Transform aimPivot;

    [Tooltip("枪口位置。弹丸从这里生成，并沿瞄准方向飞出。")]
    [SerializeField] private Transform muzzle;

    [Tooltip("枪口火光渲染器。开火时短暂显示。")]
    [SerializeField] private SpriteRenderer muzzleFlash;

    [Tooltip("武器贴图的目标世界宽度。各武器素材按此归一，0 表示不缩放。")]
    [SerializeField] private float targetWorldWidth;

    private PlayerWeaponSelector subscribedSelector;

    /// <summary>手上当前可见、可攻击的武器；被切走或未装备时为 false。</summary>
    public bool HasWeapon => weaponRenderer != null
                          && weaponRenderer.enabled
                          && weaponRenderer.sprite != null;

    /// <summary>
    /// 该槽位是否是当前使用的武器。没接 selector 时视为始终选中（单武器场景）；
    /// 两把武器都没装备时也视为选中，否则手上会残留上一把武器的贴图。
    /// </summary>
    public bool IsSelected => selector == null
                           || selector.ActiveSlot == EquipmentSlotType.None
                           || selector.IsActive(mountedSlot);

    public SpriteRenderer WeaponRenderer => weaponRenderer;

    /// <summary>本挂点对应的装备槽（主手 / 副手）。攻击逻辑据此读取手上这把武器的定义。</summary>
    public EquipmentSlotType MountedSlot => mountedSlot;

    /// <summary>枪口位置。弹丸从这里生成，并沿瞄准方向飞出。</summary>
    public Transform Muzzle => muzzle;

    /// <summary>瞄准旋转轴。武器与枪口挂在它下面，开火方向由它的旋转决定。</summary>
    public Transform AimPivot => aimPivot;

    /// <summary>枪口火光渲染器。开火时短暂显示。</summary>
    public SpriteRenderer MuzzleFlash => muzzleFlash;

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribedSelector == null && selector != null)
        {
            subscribedSelector = selector;
            subscribedSelector.Changed += Refresh;
        }

        // 武器读的是装备槽，而装备槽与局外是同一份，因此订阅 LoadoutChanged。
        GameSession.LoadoutChanged += Refresh;
    }

    private void Unsubscribe()
    {
        if (subscribedSelector != null)
        {
            subscribedSelector.Changed -= Refresh;
            subscribedSelector = null;
        }

        GameSession.LoadoutChanged -= Refresh;
    }

    private void Refresh()
    {
        if (weaponRenderer == null)
            return;

        var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(mountedSlot);
        var sprite = instance != null && instance.Definition != null ? instance.Definition.WorldSprite : null;

        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = sprite != null && IsSelected;

        ApplySpriteScale(sprite);
    }

    /// <summary>
    /// 按目标世界宽度归一武器缩放，并把枪口放到贴图右缘（枪管朝 +x）。
    /// sprite.bounds 以贴图轴心（握把）为原点：max.x 就是轴心到枪口端的距离。
    /// 挂点整体在 Visual（局部缩放 0.29）之下，因此局部缩放必须除以父级 lossyScale，
    /// 否则武器实际显示宽度只有目标值的 29%（表现为「武器小到几乎看不见」）。
    /// 场景/预制体上残留的手调缩放与枪口偏移会在每次刷新时被覆盖回正确值。
    /// </summary>
    private void ApplySpriteScale(Sprite sprite)
    {
        if (sprite == null || targetWorldWidth <= 0f)
            return;

        var width = sprite.bounds.size.x;
        if (width <= 0f)
            return;

        var weaponTransform = weaponRenderer.transform;
        var parentScale = weaponTransform.parent != null ? weaponTransform.parent.lossyScale.x : 1f;
        if (Mathf.Abs(parentScale) < 0.0001f)
            parentScale = 1f;

        var scale = targetWorldWidth / (width * Mathf.Abs(parentScale));
        weaponTransform.localScale = new Vector3(scale, scale, 1f);

        if (muzzle != null)
            muzzle.localPosition = new Vector3(sprite.bounds.max.x / scale, 0f, 0f);
    }
}
