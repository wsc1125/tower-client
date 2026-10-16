using System;

/// <summary>
/// 服务端 API 的数据模型。
///
/// 全部用 <see cref="SerializableAttribute"/> + 公共字段：
/// JsonUtility 只认这种形式，不支持属性、Dictionary、可空值类型。
/// 因此「可选字段」用哨兵值（如空串、0）表示，而非 null。
/// </summary>
namespace Tower.Network
{
    /// <summary>账号信息。对应服务端 account.Account。</summary>
    [Serializable]
    public sealed class AccountDto
    {
        public long account_id;
        public string username;
        public string display_name;

        /// <summary>头像完整 URL。未设置头像时为空串。</summary>
        public string avatar_url;

        public string created_at;
        public string updated_at;
        public string last_login_at;
    }

    /// <summary>双令牌。对应服务端 auth.TokenPair。</summary>
    [Serializable]
    public sealed class TokenPairDto
    {
        public string access_token;
        public string refresh_token;
        public string token_type;
        public long expires_in;
    }

    /// <summary>注册 / 登录 / 刷新的响应。</summary>
    [Serializable]
    public sealed class AuthResponseDto
    {
        public AccountDto account;
        public TokenPairDto tokens;
    }

    /// <summary>仅含账号的响应（/me）。</summary>
    [Serializable]
    public sealed class AccountResponseDto
    {
        public AccountDto account;
    }

    /// <summary>刷新令牌请求。</summary>
    [Serializable]
    public sealed class RefreshRequestDto
    {
        public string refresh_token;
    }

    /// <summary>注册请求。仅在不带头像（JSON 请求体）时使用。</summary>
    [Serializable]
    public sealed class RegisterRequestDto
    {
        public string username;
        public string password;
        public string display_name;
    }

    /// <summary>登录请求。</summary>
    [Serializable]
    public sealed class LoginRequestDto
    {
        public string username;
        public string password;
    }

    /// <summary>房间成员。对应服务端 room.MemberBrief。</summary>
    [Serializable]
    public sealed class RoomMemberDto
    {
        public long account_id;
        public string display_name;

        /// <summary>头像完整 URL。未设置头像时为空串。</summary>
        public string avatar_url;

        public bool is_host;
        public string joined_at;

        /// <summary>是否已准备。仅信令侧 room_state 会填充，HTTP 房间详情里恒为 false。</summary>
        public bool ready;
    }

    /// <summary>房间详情。对应服务端 room.Snapshot。</summary>
    [Serializable]
    public sealed class RoomDto
    {
        public string room_code;
        public string name;
        public long host_account;
        public int max_players;
        public bool is_public;
        public string status;
        public RoomMemberDto[] members;
        public string created_at;
    }

    /// <summary>房间列表项。对应服务端 room.ListEntry。</summary>
    [Serializable]
    public sealed class RoomListEntryDto
    {
        public string room_code;
        public string name;
        public string host_display_name;
        public int current_players;
        public int max_players;
        public string status;
    }

    /// <summary>房间列表响应。</summary>
    [Serializable]
    public sealed class RoomListResponseDto
    {
        public RoomListEntryDto[] rooms;
    }

    /// <summary>创建房间请求。</summary>
    [Serializable]
    public sealed class CreateRoomRequestDto
    {
        public string name;
        public int max_players;
        public bool is_public;
    }

    /// <summary>离开房间响应。</summary>
    [Serializable]
    public sealed class LeaveRoomResponseDto
    {
        public bool closed;
        public RoomDto room;
    }

    /// <summary>ICE 服务器。对应服务端 relay.ICEServer。</summary>
    [Serializable]
    public sealed class IceServerDto
    {
        public string[] urls;
        public string username;
        public string credential;
    }

    /// <summary>中继凭证响应。对应服务端 relay.Response。</summary>
    [Serializable]
    public sealed class RelayCredentialsDto
    {
        public IceServerDto[] ice_servers;
        public long ttl;
    }

    /// <summary>撤离结算请求。客户端提交本局战利品清单，服务端直接落库（本期不做校验）。</summary>
    [Serializable]
    public sealed class ExtractionRequestDto
    {
        /// <summary>撤离时的层数。</summary>
        public int floor;

        /// <summary>本局拾取的战利品清单。</summary>
        public ExtractionItemDto[] items;
    }

    /// <summary>撤离结算中的单件战利品。</summary>
    [Serializable]
    public sealed class ExtractionItemDto
    {
        /// <summary>物品定义 ID（对应 ItemDefinition.Id）。</summary>
        public string definition_id;

        /// <summary>剩余耐久。非耐久物品为 0。</summary>
        public int durability;
    }

    /// <summary>服务端统一错误响应体。</summary>
    [Serializable]
    public sealed class ErrorResponseDto
    {
        public ErrorBodyDto error;
    }

    /// <summary>错误详情。</summary>
    [Serializable]
    public sealed class ErrorBodyDto
    {
        public string code;
        public string message;
    }

    // ---- 资产快照 ----

    /// <summary>资产快照协议常量。</summary>
    public static class AssetSnapshotProtocol
    {
        /// <summary>当前客户端快照信封版本。服务端只透传，高于自身已知版本时返回 400。</summary>
        public const int SchemaVersion = 1;

        /// <summary>快照请求体大小上限（字节）。当前快照约几 KB，仅防滥用。</summary>
        public const int MaxPayloadBytes = 256 * 1024;
    }

    /// <summary>
    /// 资产快照信封：仓库/装备（复用玩家存档 v3 结构）+ 收藏 + 星空币，
    /// 整包原子上下传。对服务端不透明。
    /// </summary>
    [Serializable]
    public sealed class AssetSnapshotEnvelopeDto
    {
        /// <summary>仓库 + 装备槽。直接复用客户端存档结构，零转换。</summary>
        public PlayerSaveData save = new PlayerSaveData();

        /// <summary>收藏进度。</summary>
        public CollectionSaveData collection = new CollectionSaveData();

        /// <summary>星空币余额。</summary>
        public int currency;
    }

    /// <summary>GET /assets/snapshot 响应。data 为 null 表示服务端尚无快照（首次登录）。</summary>
    [Serializable]
    public sealed class AssetSnapshotResponseDto
    {
        public int schema_version;

        /// <summary>服务端自增版本号，每次 PUT +1。用于排查「谁覆盖了谁」。</summary>
        public long revision;

        public string updated_at;
        public AssetSnapshotEnvelopeDto data;
    }

    /// <summary>PUT /assets/snapshot 请求。</summary>
    [Serializable]
    public sealed class AssetSnapshotPutRequestDto
    {
        public int schema_version;
        public AssetSnapshotEnvelopeDto data;
    }

    /// <summary>PUT /assets/snapshot 响应。</summary>
    [Serializable]
    public sealed class AssetSnapshotPutResponseDto
    {
        public long revision;
        public string updated_at;
    }
}
