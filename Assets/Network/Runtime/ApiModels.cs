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
        public bool is_host;
        public string joined_at;
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
}
