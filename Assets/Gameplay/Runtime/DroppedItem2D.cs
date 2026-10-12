using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 地面掉落物。物品实例（含耐久）随对象走，拾取时整个实例进背包。
///
/// 联机：由主机生成并 <c>Spawn</c>，物品定义 ID / 实例 ID / 耐久通过 NetworkVariable 广播，
/// 客户端按 ID 从 <see cref="ItemDatabase"/> 反查定义并渲染。
/// 拾取走 <see cref="RequestPickupServerRpc"/>，由主机裁决归属（先到先得），
/// 裁决通过后主机 <c>Despawn</c>，所有端同步移除。
///
/// 单机模式下 prefab 上没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
public sealed class DroppedItem2D : NetworkBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private CircleCollider2D pickupZone;

    /// <summary>光圈宽度 ÷ 物品本体实际宽度。光圈素材是「宽 > 高」的地面椭圆，所以按宽定尺寸。</summary>
    [SerializeField] private float glowWidthRatio = 1.6f;

    /// <summary>权威物品定义 ID。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> syncedDefinitionId =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(
            default, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威实例 ID。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> syncedInstanceId =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(
            default, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威剩余耐久。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<int> syncedDurability =
        new NetworkVariable<int>(0, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>是否已被拾取。主机裁决后置 true，客户端据此隐藏。</summary>
    private readonly NetworkVariable<bool> syncedPickedUp =
        new NetworkVariable<bool>(false, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>地面上的这件物品实例。拾取时整个实例进背包，耐久随之带走。</summary>
    public ItemInstance Item { get; private set; }

    /// <summary>物品模板。便捷访问，等价于 <c>Item.Definition</c>。</summary>
    public ItemDefinition Definition => Item != null ? Item.Definition : null;

    private void Awake()
    {
        WorldRegistry.Register(this);
    }

    private void OnEnable()
    {
        WorldRegistry.Register(this);
    }

    private void OnDisable()
    {
        WorldRegistry.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        syncedPickedUp.OnValueChanged += HandlePickedUpChanged;

        // 客户端：按权威 ID 反查定义并渲染。
        // 主机不需要 —— 物品实例已在 Initialize() 里设置好。
        if (!IsServer)
            RebuildFromNetwork();
    }

    public override void OnNetworkDespawn()
    {
        syncedPickedUp.OnValueChanged -= HandlePickedUpChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// 渲染贴图取 worldSprite，缺失时回退 icon；体量按「最大边」等比缩放到 item.WorldSize。
    /// 只调整体量、绝不改变比例 —— 细长的剑保持细长，竖立的箱盒保持竖立。
    /// </summary>
    public void Initialize(ItemInstance item)
    {
        Item = item;

        var definition = Definition;
        if (definition == null)
        {
            DisableVisual();
            return;
        }

        var sprite = definition.WorldSprite != null ? definition.WorldSprite : definition.Icon;
        if (visual == null || sprite == null)
        {
            DisableVisual();
            return;
        }

        visual.sprite = sprite;
        visual.enabled = true;

        var bounds = sprite.bounds.size;
        var maxSide = Mathf.Max(bounds.x, bounds.y);
        var scale = 1f;
        if (maxSide > 0f && definition.WorldSize > 0f)
        {
            scale = definition.WorldSize / maxSide;
            visual.transform.localScale = Vector3.one * scale;
        }

        if (pickupZone != null)
            pickupZone.radius = definition.WorldSize * 0.5f;

        // 物品贴图可能用自定义轴点（如武器 pivot.x = 0.15），缩放后视觉中心会偏离 transform 原点。
        // 光圈自身是中心轴点，必须按物品贴图的真实中心与底边对齐，否则会「套不住」物品。
        var pivot = sprite.pivot;
        var rect = sprite.rect.size;
        var centerOffset = new Vector2(
            (0.5f - pivot.x / rect.x) * bounds.x * scale,
            (0.5f - pivot.y / rect.y) * bounds.y * scale);

        // 光圈宽度按物品本体「实际宽度」算，而非最大边 worldSize —— 竖长贴图（剑、立箱）的
        // 本体宽度小于 worldSize，若按 worldSize 算光圈会明显宽于物品。
        ApplyGlow(bounds.x * scale, centerOffset, bounds.y * scale);
    }

    /// <summary>
    /// 联机主机：在 <c>Spawn()</c> 之前把物品信息写入权威字段。
    /// 必须在 Spawn 前调用 —— 否则客户端 OnNetworkSpawn 时读到的还是默认值。
    /// </summary>
    public void PublishItemBeforeSpawn()
    {
        if (Item == null || Item.Definition == null)
            return;

        syncedDefinitionId.Value = ToFixedString(Item.Definition.Id);
        syncedInstanceId.Value = ToFixedString(Item.InstanceId);
        syncedDurability.Value = Item.CurrentDurability;
    }

    /// <summary>
    /// 把字符串安全地转成 <see cref="Unity.Collections.FixedString64Bytes"/>。
    ///
    /// 不能直接用隐式转换：FixedString64Bytes 在源串超过 61 字节时会**抛异常**（不是静默截断），
    /// 一旦有超长物品 ID 就会让掉落生成崩溃。这里显式截断，保证不抛。
    /// 当前物品 ID 最长 19 字符、实例 ID 固定 32 字符，均远低于上限。
    /// </summary>
    private static Unity.Collections.FixedString64Bytes ToFixedString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return default;

        const int maxBytes = 61;
        if (System.Text.Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        // 超长时按字符逐个截断，保证不切断多字节字符。
        var builder = new System.Text.StringBuilder();
        var bytes = 0;
        foreach (var ch in value)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(ch.ToString());
            if (bytes + size > maxBytes)
                break;

            builder.Append(ch);
            bytes += size;
        }

        return builder.ToString();
    }

    /// <summary>客户端按权威字段重建物品实例并渲染。</summary>
    private void RebuildFromNetwork()
    {
        var definitionId = syncedDefinitionId.Value.ToString();
        var database = GameSession.Database;
        var definition = database != null ? database.Find(definitionId) : null;

        if (definition == null)
        {
            // 定义缺失（客户端未登记该物品）时只隐藏视觉，不销毁对象 ——
            // 否则拾取裁决会因对象不存在而失败。
            DisableVisual();
            return;
        }

        var instanceId = syncedInstanceId.Value.ToString();
        var instance = new ItemInstance(instanceId, definition);
        instance.RestoreDurability(syncedDurability.Value);

        Initialize(instance);
    }

    private void HandlePickedUpChanged(bool previous, bool current)
    {
        if (IsServer || !current)
            return;

        // 已被他人拾取：客户端隐藏并禁用拾取判定。
        DisableVisual();
        if (pickupZone != null)
            pickupZone.enabled = false;
    }

    /// <summary>
    /// 客户端请求拾取本件掉落物。主机校验后裁决归属（先到先得）。
    ///
    /// 为什么必须走主机：掉落物是全局共享的，若各端本地各自拾取，
    /// 同一件物品会被多个玩家同时拿走（物品复制）。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        // 已被拾取：拒绝（先到先得）。
        if (syncedPickedUp.Value)
            return;

        var requesterId = rpcParams.Receive.SenderClientId;
        var context = ResolvePlayerContext(requesterId);
        if (context == null)
            return;

        if (Item == null)
            return;

        // 主机校验：背包容量。
        if (!context.Raid.TryAddToCarry(Item))
            return;

        context.Raid.RecordLoot(Item);

        if (requesterId != NetworkManager.LocalClientId)
        {
            PickupResultClientRpc(
                ToFixedString(Item.Definition.Id), ToFixedString(Item.InstanceId), Item.CurrentDurability,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { requesterId } }
                });
        }

        // 标记已拾取并销毁，NGO 会自动同步到所有客户端。
        syncedPickedUp.Value = true;
        DespawnSelf();
    }

    [ClientRpc]
    private void PickupResultClientRpc(Unity.Collections.FixedString64Bytes definitionId,
        Unity.Collections.FixedString64Bytes instanceId, int durability, ClientRpcParams rpcParams = default)
    {
        if (IsServer)
            return;

        ApplyPickupResult(definitionId.ToString(), instanceId.ToString(), durability);
    }

    private static void ApplyPickupResult(string definitionId, string instanceId, int durability)
    {
        if (string.IsNullOrEmpty(instanceId))
            return;

        var database = GameSession.Database;
        var definition = database != null ? database.Find(definitionId) : null;
        if (definition == null)
            return;

        var bag = GameSession.LocalPlayer.Raid;
        for (var i = 0; i < RaidBag.CarryCapacity; i++)
        {
            var existing = bag.GetCarry(i);
            if (existing != null && existing.InstanceId == instanceId)
                return;
        }
        foreach (var existing in bag.Loot)
        {
            if (existing.InstanceId == instanceId)
                return;
        }

        var item = new ItemInstance(instanceId, definition);
        item.RestoreDurability(durability);
        if (bag.TryAddToCarry(item))
            bag.RecordLoot(item);
    }

    /// <summary>
    /// 把 clientId 解析为对应的玩家上下文。
    /// 主机自己用本机上下文；成员通过 MultiplayerCoordinator 的账号映射解析。
    /// </summary>
    private static IPlayerContext ResolvePlayerContext(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return null;

        // 主机自己：直接用本机玩家上下文。
        if (clientId == nm.LocalClientId)
            return GameSession.LocalPlayer;

        var coordinator = Tower.Netcode.MultiplayerCoordinator.Instance;
        var accountId = coordinator != null ? coordinator.AccountIdOf(clientId) : 0L;
        if (accountId == 0L)
            return null;

        return GameSession.GetPlayer(accountId.ToString());
    }

    /// <summary>销毁本对象。联机时走 Despawn（NGO 同步），单机时直接 Destroy。</summary>
    private void DespawnSelf()
    {
        if (IsSpawned && IsServer)
        {
            NetworkObject.Despawn(true);
            return;
        }

        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    /// <summary>光圈贴在物品底边（读起来像「地上的一滩光」），宽度跟随物品本体宽度，自身比例不变。</summary>
    private void ApplyGlow(float itemWorldWidth, Vector2 itemCenterOffset, float itemWorldHeight)
    {
        if (glow == null)
            return;

        var glowSprite = glow.sprite;
        if (glowSprite == null || itemWorldWidth <= 0f)
        {
            glow.enabled = false;
            return;
        }

        glow.enabled = true;

        var glowBounds = glowSprite.bounds.size;
        if (glowBounds.x > 0f)
            glow.transform.localScale = Vector3.one * (itemWorldWidth * glowWidthRatio / glowBounds.x);

        // 光圈中心对齐物品贴图的水平中心，纵向贴到物品底边。
        glow.transform.localPosition = new Vector3(
            itemCenterOffset.x,
            itemCenterOffset.y - itemWorldHeight * 0.5f,
            0f);
    }

    private void DisableVisual()
    {
        if (visual != null)
            visual.enabled = false;

        if (glow != null)
            glow.enabled = false;
    }
}
