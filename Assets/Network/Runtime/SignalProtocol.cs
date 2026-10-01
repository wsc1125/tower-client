using System;
using System.Text;

namespace Tower.Network
{
    /// <summary>信令消息类型。与服务端 signal 包的定义一一对应。</summary>
    public static class SignalType
    {
        // 客户端 → 服务端
        public const string JoinRoom = "join_room";
        public const string Signal = "signal";
        public const string Ready = "ready";
        public const string StartGame = "start_game";
        public const string Ping = "ping";

        // 服务端 → 客户端
        public const string RoomState = "room_state";
        public const string MemberJoined = "member_joined";
        public const string MemberLeft = "member_left";
        public const string GameStarting = "game_starting";
        public const string GameSession = "game_session";
        public const string HostChanged = "host_changed";
        public const string RoomClosed = "room_closed";
        public const string Error = "error";
        public const string Pong = "pong";
    }

    /// <summary>
    /// 信令消息信封。
    ///
    /// payload 在线上是**嵌套 JSON 对象**，但 JsonUtility 无法把嵌套对象
    /// 反序列化进 string 字段（会得到空串）。因此这里不直接反序列化信封，
    /// 而是用 <see cref="SignalEnvelopeCodec"/> 手工切出 type 与 payload 两段。
    /// </summary>
    [Serializable]
    public sealed class SignalEnvelope
    {
        public string type;
        public long seq;

        /// <summary>payload 的 JSON 原文（不含外层大括号之外的任何东西）。</summary>
        public string payload;
    }

    /// <summary>
    /// 信封编解码器。
    ///
    /// 存在的唯一理由：JsonUtility 不支持「嵌套对象 → string」。
    /// 手工扫描顶层字段，把 payload 的 JSON 原文原样取出，
    /// 再交给 JsonUtility 二次解析成具体类型。
    /// </summary>
    public static class SignalEnvelopeCodec
    {
        /// <summary>解析信封，取出 type / seq / payload 原文。</summary>
        public static SignalEnvelope Decode(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            var envelope = new SignalEnvelope
            {
                type = ExtractString(raw, "type"),
                seq = ExtractLong(raw, "seq"),
                payload = ExtractRawObject(raw, "payload"),
            };

            return string.IsNullOrEmpty(envelope.type) ? null : envelope;
        }

        /// <summary>编码信封。payload 为 null 时不输出该字段。</summary>
        public static string Encode(string type, long seq, string payloadJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"").Append(Escape(type)).Append('"');

            if (seq != 0)
                sb.Append(",\"seq\":").Append(seq);

            if (!string.IsNullOrEmpty(payloadJson))
                sb.Append(",\"payload\":").Append(payloadJson);

            sb.Append('}');

            return sb.ToString();
        }

        /// <summary>取出顶层字符串字段的值。</summary>
        private static string ExtractString(string json, string key)
        {
            var start = FindValueStart(json, key);
            if (start < 0 || start >= json.Length || json[start] != '"')
                return null;

            var sb = new StringBuilder();
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];

                if (c == '\\' && i + 1 < json.Length)
                {
                    // 处理转义：\" \\ \/ \n \r \t \uXXXX
                    var next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 5 < json.Length &&
                                int.TryParse(json.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(next); break;
                    }

                    i++;
                    continue;
                }

                if (c == '"')
                    return sb.ToString();

                sb.Append(c);
            }

            return null;
        }

        /// <summary>取出顶层整数字段的值。</summary>
        private static long ExtractLong(string json, string key)
        {
            var start = FindValueStart(json, key);
            if (start < 0)
                return 0;

            var end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;

            return long.TryParse(json.Substring(start, end - start), out var value) ? value : 0;
        }

        /// <summary>
        /// 取出顶层对象字段的 JSON 原文（含大括号）。
        ///
        /// 用括号配对而非找下一个逗号：payload 内部可能嵌套对象与数组，
        /// 简单按逗号切会截断。
        /// </summary>
        private static string ExtractRawObject(string json, string key)
        {
            var start = FindValueStart(json, key);
            if (start < 0 || start >= json.Length)
                return null;

            var open = json[start];

            // 非对象（如字符串、数字、null）直接返回其原文。
            if (open != '{' && open != '[')
            {
                var end = start;
                while (end < json.Length && json[end] != ',' && json[end] != '}')
                    end++;

                return json.Substring(start, end - start).Trim();
            }

            var close = open == '{' ? '}' : ']';
            var depth = 0;
            var inString = false;

            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];

                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(start, i - start + 1);
                }
            }

            return null;
        }

        /// <summary>定位 "key": 之后第一个非空白字符的位置。</summary>
        private static int FindValueStart(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var keyIndex = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIndex < 0)
                return -1;

            var colon = json.IndexOf(':', keyIndex + needle.Length);
            if (colon < 0)
                return -1;

            var i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;

            return i < json.Length ? i : -1;
        }

        /// <summary>转义字符串中的特殊字符。</summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>加入房间。</summary>
    [Serializable]
    public sealed class JoinRoomPayload
    {
        public string room_code;
    }

    /// <summary>转发信令数据。data 是不透明字符串，内容由客户端定义。</summary>
    [Serializable]
    public sealed class SignalPayload
    {
        public long target_account_id;
        public string data;
    }

    /// <summary>准备状态。</summary>
    [Serializable]
    public sealed class ReadyPayload
    {
        public bool ready;
    }

    /// <summary>房间成员（信令侧）。</summary>
    [Serializable]
    public sealed class SignalMemberDto
    {
        public long account_id;
        public string display_name;
        public string avatar_url;
        public bool is_host;
        public bool ready;
    }

    /// <summary>房间状态。</summary>
    [Serializable]
    public sealed class RoomStatePayload
    {
        public string room_code;
        public SignalMemberDto[] members;
        public long host_account_id;
    }

    /// <summary>有玩家加入。</summary>
    [Serializable]
    public sealed class MemberJoinedPayload
    {
        public long account_id;
        public string display_name;
        public string avatar_url;
    }

    /// <summary>有玩家离开。</summary>
    [Serializable]
    public sealed class MemberLeftPayload
    {
        public long account_id;
    }

    /// <summary>转发来的信令数据。</summary>
    [Serializable]
    public sealed class SignalForwardPayload
    {
        public long from_account_id;
        public string data;
    }

    /// <summary>开始游戏。</summary>
    [Serializable]
    public sealed class GameStartingPayload
    {
        public long host_account_id;
        public string session_id;
    }

    /// <summary>本局游戏会话信息。</summary>
    [Serializable]
    public sealed class GameSessionPayload
    {
        public string session_id;
        public string room_code;
        public string transport_mode;
        public long host_account_id;
        public string host_address;
        public int host_port;
        public string status;
    }

    /// <summary>房主迁移。客户端收到后需执行权威接管。</summary>
    [Serializable]
    public sealed class HostChangedPayload
    {
        public long new_host_account_id;
        public string new_host_display_name;
    }

    /// <summary>房间关闭。</summary>
    [Serializable]
    public sealed class RoomClosedPayload
    {
        public string reason;
    }

    /// <summary>信令错误。</summary>
    [Serializable]
    public sealed class SignalErrorPayload
    {
        public string code;
        public string message;
    }
}
