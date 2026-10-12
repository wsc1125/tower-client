using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Tower.Network
{
    /// <summary>
    /// 服务端 HTTP 客户端。
    ///
    /// 用协程而非 async/await：Unity 的 <see cref="UnityWebRequest"/> 必须在主线程驱动，
    /// 协程天然满足；且回调直接在主线程执行，调用方不必再 Marshal 回主线程。
    /// </summary>
    public sealed class ApiClient
    {
        private readonly NetworkConfig config;

        /// <summary>当前 access token。为空表示未登录。</summary>
        private string accessToken;

        /// <summary>当前 refresh token。</summary>
        private string refreshToken;

        /// <summary>当前登录账号。未登录时为 null。</summary>
        private AccountDto currentAccount;

        public ApiClient(NetworkConfig config)
        {
            this.config = config ?? NetworkConfig.Local;
        }

        /// <summary>当前登录账号。未登录时为 null。</summary>
        public AccountDto CurrentAccount => currentAccount;

        /// <summary>是否已登录。</summary>
        public bool IsLoggedIn => !string.IsNullOrEmpty(accessToken);

        /// <summary>当前 access token。</summary>
        public string AccessToken => accessToken;

        /// <summary>当前 refresh token。</summary>
        public string RefreshToken => refreshToken;

        /// <summary>清空登录态。</summary>
        public void Logout()
        {
            accessToken = null;
            refreshToken = null;
            currentAccount = null;
        }

        // ---- 账号 ----

        /// <summary>注册。成功时自动保存登录态。</summary>
        /// <param name="avatar">头像图片字节。为 null 或空数组时走 JSON 请求体，不带头像。</param>
        public IEnumerator Register(string username, string password, string displayName, byte[] avatar,
            Action<AuthResponseDto> onSuccess, Action<ApiError> onError)
        {
            // 不带头像时仍走 JSON：multipart 会多一层 boundary 解析开销，
            // 且服务端两种格式都支持，没必要为「没图」的情况付这个成本。
            if (avatar == null || avatar.Length == 0)
            {
                var body = new RegisterRequestDto
                {
                    username = username,
                    password = password,
                    display_name = displayName,
                };

                return Send<AuthResponseDto>(UnityWebRequest.kHttpVerbPOST, "/auth/register", body, false,
                    response =>
                    {
                        StoreAuth(response);
                        onSuccess?.Invoke(response);
                    },
                    onError);
            }

            return SendRegisterMultipart(username, password, displayName, avatar, onSuccess, onError);
        }

        /// <summary>以 multipart/form-data 提交注册（含头像文件）。</summary>
        private IEnumerator SendRegisterMultipart(string username, string password, string displayName,
            byte[] avatar, Action<AuthResponseDto> onSuccess, Action<ApiError> onError)
        {
            var form = new WWWForm();
            form.AddField("username", username);
            form.AddField("password", password);
            form.AddField("display_name", displayName ?? string.Empty);

            // 文件名固定为 avatar.jpg：服务端按文件头判断真实格式，不信任这里的扩展名。
            form.AddBinaryData("avatar", avatar, "avatar.jpg", "image/jpeg");

            using var request = UnityWebRequest.Post(config.ApiBase + "/auth/register", form);
            request.timeout = config.httpTimeoutSeconds;

            yield return request.SendWebRequest();

            var status = request.responseCode;
            var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(ParseError(status, text, request.error));
                yield break;
            }

            AuthResponseDto parsed;
            try
            {
                parsed = JsonUtility.FromJson<AuthResponseDto>(text);
            }
            catch (Exception ex)
            {
                onError?.Invoke(new ApiError("PARSE_ERROR", "响应解析失败: " + ex.Message));
                yield break;
            }

            if (parsed == null)
            {
                onError?.Invoke(new ApiError("PARSE_ERROR", "响应为空"));
                yield break;
            }

            StoreAuth(parsed);
            onSuccess?.Invoke(parsed);
        }

        /// <summary>登录。成功时自动保存登录态。</summary>
        public IEnumerator Login(string username, string password,
            Action<AuthResponseDto> onSuccess, Action<ApiError> onError)
        {
            var body = new LoginRequestDto { username = username, password = password };

            return Send<AuthResponseDto>(UnityWebRequest.kHttpVerbPOST, "/auth/login", body, false,
                response =>
                {
                    StoreAuth(response);
                    onSuccess?.Invoke(response);
                },
                onError);
        }

        /// <summary>用 refresh token 换新令牌。</summary>
        public IEnumerator Refresh(Action<AuthResponseDto> onSuccess, Action<ApiError> onError)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                onError?.Invoke(new ApiError("TOKEN_INVALID", "尚未登录"));
                yield break;
            }

            var body = new RefreshRequestDto { refresh_token = refreshToken };

            yield return Send<AuthResponseDto>(UnityWebRequest.kHttpVerbPOST, "/auth/refresh", body, false,
                response =>
                {
                    StoreAuth(response);
                    onSuccess?.Invoke(response);
                },
                onError);
        }

        /// <summary>查询当前账号资料。</summary>
        public IEnumerator GetMe(Action<AccountDto> onSuccess, Action<ApiError> onError)
        {
            return Send<AccountResponseDto>(UnityWebRequest.kHttpVerbGET, "/auth/me", null, true,
                response =>
                {
                    currentAccount = response.account;
                    onSuccess?.Invoke(response.account);
                },
                onError);
        }

        // ---- 房间 ----

        /// <summary>创建房间。</summary>
        public IEnumerator CreateRoom(string name, int maxPlayers, bool isPublic,
            Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            var body = new CreateRoomRequestDto
            {
                name = name,
                max_players = maxPlayers,
                is_public = isPublic,
            };

            return Send<RoomDto>(UnityWebRequest.kHttpVerbPOST, "/rooms", body, true,
                onSuccess, onError);
        }

        /// <summary>房间列表（仅公开且等待中的房间）。</summary>
        public IEnumerator ListRooms(Action<RoomListEntryDto[]> onSuccess, Action<ApiError> onError)
        {
            return Send<RoomListResponseDto>(UnityWebRequest.kHttpVerbGET, "/rooms", null, true,
                response => onSuccess?.Invoke(response.rooms ?? Array.Empty<RoomListEntryDto>()),
                onError);
        }

        /// <summary>查询房间详情。</summary>
        public IEnumerator GetRoom(string roomCode, Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            return Send<RoomDto>(UnityWebRequest.kHttpVerbGET, "/rooms/" + roomCode, null, true,
                onSuccess, onError);
        }

        /// <summary>加入房间。</summary>
        public IEnumerator JoinRoom(string roomCode, Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            return Send<RoomDto>(UnityWebRequest.kHttpVerbPOST, "/rooms/" + roomCode + "/join", null, true,
                onSuccess, onError);
        }

        /// <summary>离开房间。房间因此关闭时 <c>closed</c> 为 true。</summary>
        public IEnumerator LeaveRoom(string roomCode, Action<LeaveRoomResponseDto> onSuccess, Action<ApiError> onError)
        {
            return Send<LeaveRoomResponseDto>(UnityWebRequest.kHttpVerbPOST, "/rooms/" + roomCode + "/leave", null, true,
                onSuccess, onError);
        }

        /// <summary>关闭房间（仅房主）。</summary>
        public IEnumerator CloseRoom(string roomCode, Action onSuccess, Action<ApiError> onError)
        {
            return SendRaw(UnityWebRequest.kHttpVerbDELETE, "/rooms/" + roomCode, null, true,
                _ => onSuccess?.Invoke(), onError);
        }

        // ---- 中继 ----

        /// <summary>获取 STUN/TURN 地址与临时凭证。</summary>
        public IEnumerator GetRelayCredentials(Action<RelayCredentialsDto> onSuccess, Action<ApiError> onError)
        {
            return Send<RelayCredentialsDto>(UnityWebRequest.kHttpVerbGET, "/relay/credentials", null, true,
                onSuccess, onError);
        }

        // ---- 撤离结算 ----

        /// <summary>
        /// 提交本局撤离战利品清单。服务端直接落库（本期不做校验）。
        /// 响应体为空，因此用 SendRaw 而非 Send。
        /// </summary>
        public IEnumerator SubmitExtraction(ExtractionRequestDto body,
            Action onSuccess, Action<ApiError> onError)
        {
            return SendRaw(UnityWebRequest.kHttpVerbPOST, "/raid/extract", body, true,
                _ => onSuccess?.Invoke(), onError);
        }

        // ---- 头像 ----

        /// <summary>
        /// 下载头像图片。url 为服务端下发的完整地址（avatar_url）。
        ///
        /// 失败时回调 null 而非报错：头像缺失只影响观感，
        /// 不该让整个成员列表刷新流程中断。
        /// </summary>
        public IEnumerator DownloadAvatar(string url, Action<Texture2D> onDone)
        {
            if (string.IsNullOrEmpty(url))
            {
                onDone?.Invoke(null);
                yield break;
            }

            using var request = UnityWebRequestTexture.GetTexture(url);
            request.timeout = config.httpTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(null);
                yield break;
            }

            onDone?.Invoke(DownloadHandlerTexture.GetContent(request));
        }

        // ---- 内部实现 ----

        /// <summary>发送请求并反序列化响应体。</summary>
        private IEnumerator Send<T>(string method, string path, object body, bool authenticated,
            Action<T> onSuccess, Action<ApiError> onError) where T : class
        {
            yield return SendRaw(method, path, body, authenticated, raw =>
            {
                T parsed;
                try
                {
                    parsed = JsonUtility.FromJson<T>(raw);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(new ApiError("PARSE_ERROR", "响应解析失败: " + ex.Message));
                    return;
                }

                if (parsed == null)
                {
                    onError?.Invoke(new ApiError("PARSE_ERROR", "响应为空"));
                    return;
                }

                onSuccess?.Invoke(parsed);
            }, onError);
        }

        /// <summary>发送请求，把响应体原文交给回调。</summary>
        private IEnumerator SendRaw(string method, string path, object body, bool authenticated,
            Action<string> onSuccess, Action<ApiError> onError)
        {
            var url = config.ApiBase + path;

            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = config.httpTimeoutSeconds;

            if (body != null)
            {
                var json = JsonUtility.ToJson(body);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (authenticated)
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    onError?.Invoke(new ApiError("TOKEN_INVALID", "尚未登录"));
                    yield break;
                }

                request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            }

            yield return request.SendWebRequest();

            var status = request.responseCode;
            var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(text);
                yield break;
            }

            onError?.Invoke(ParseError(status, text, request.error));
        }

        /// <summary>把服务端错误响应解析为 <see cref="ApiError"/>。</summary>
        private static ApiError ParseError(long status, string body, string transportError)
        {
            // 服务端错误格式统一为 {"error":{"code":"...","message":"..."}}。
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<ErrorResponseDto>(body);
                    if (parsed?.error != null && !string.IsNullOrEmpty(parsed.error.code))
                        return new ApiError(parsed.error.code, parsed.error.message, status);
                }
                catch
                {
                    // 响应体不是预期格式（如网关返回的 HTML），落到下面的兜底分支。
                }
            }

            // 网络层失败（连不上、超时）时 responseCode 为 0。
            if (status == 0)
                return new ApiError("NETWORK_ERROR", transportError ?? "无法连接服务器", 0);

            return new ApiError("HTTP_" + status, transportError ?? "请求失败", status);
        }

        /// <summary>保存登录态。</summary>
        private void StoreAuth(AuthResponseDto response)
        {
            if (response?.tokens == null)
                return;

            accessToken = response.tokens.access_token;
            refreshToken = response.tokens.refresh_token;
            currentAccount = response.account;
        }
    }

    /// <summary>API 错误。</summary>
    public sealed class ApiError
    {
        /// <summary>服务端错误码，如 INVALID_CREDENTIALS；网络层失败时为 NETWORK_ERROR。</summary>
        public string Code { get; }

        /// <summary>可展示给用户的错误信息。</summary>
        public string Message { get; }

        /// <summary>HTTP 状态码。网络层失败时为 0。</summary>
        public long StatusCode { get; }

        public ApiError(string code, string message, long statusCode = 0)
        {
            Code = code;
            Message = message;
            StatusCode = statusCode;
        }

        /// <summary>是否为网络层失败（连不上 / 超时），而非业务错误。</summary>
        public bool IsNetworkFailure => StatusCode == 0;

        public override string ToString() => $"[{Code}] {Message}";
    }
}
