using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 联机房间面板：创建房间 / 通过房间码加入。
///
/// 只做两件事，不做房间列表 —— 房间码本身就是分享凭证，
/// 玩家口头或聊天里把码给对方即可，列表反而增加无效房间的曝光。
///
/// 本组件只负责「收集输入 + 展示结果」，不直接引用 Network 程序集：
/// 提交与取消通过事件抛给 <see cref="MainMenuRoomBinder"/>，
/// 由它去调 NetworkSession。这样 UI 层与网络层保持解耦。
/// </summary>
[DisallowMultipleComponent]
public sealed class RoomPanelController : MonoBehaviour
{
    /// <summary>
    /// 成员展示数据。由 Binder 从 RoomDto 转换而来 ——
    /// 本组件不引用 Network 程序集，因此不直接用 RoomMemberDto。
    /// </summary>
    public struct MemberView
    {
        public string DisplayName;
        public bool IsHost;
        public bool IsLocal;

        /// <summary>是否已准备。房主恒为 true（房主点出发即开始，无需准备）。</summary>
        public bool IsReady;

        /// <summary>头像贴图。未加载或加载失败时为 null，此时显示占位色块。</summary>
        public Texture2D Avatar;
    }

    [Header("面板")]
    [SerializeField] private GameObject panelRoot;

    [Header("页签")]
    [SerializeField] private Button createTabButton;
    [SerializeField] private Button joinTabButton;
    [SerializeField] private RectTransform createTabHighlight;
    [SerializeField] private RectTransform joinTabHighlight;

    [Header("创建房间")]
    [SerializeField] private GameObject createPage;
    [SerializeField] private TMP_InputField roomNameInput;

    [Tooltip("人数档位按钮，顺序对应 2 / 3 / 4 人。")]
    [SerializeField] private Button[] maxPlayersButtons;

    [Tooltip("人数档位按钮上的数字文本，与 maxPlayersButtons 一一对应。")]
    [SerializeField] private TMP_Text[] maxPlayersLabels;

    [SerializeField] private Button createSubmitButton;
    [SerializeField] private TMP_Text createSubmitLabel;

    [Header("加入房间")]
    [SerializeField] private GameObject joinPage;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Button joinSubmitButton;
    [SerializeField] private TMP_Text joinSubmitLabel;

    [Header("结果")]
    [SerializeField] private GameObject resultGroup;
    [SerializeField] private TMP_Text resultCodeText;
    [SerializeField] private TMP_Text resultHintText;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private Button leaveButton;

    [Tooltip("出发按钮。仅房主、且房间人数大于 1 时可点击。")]
    [SerializeField] private Button departButton;

    [Tooltip("出发按钮上的文字。")]
    [SerializeField] private TMP_Text departLabel;

    [Tooltip("准备按钮。仅成员可见，点击切换准备状态。")]
    [SerializeField] private Button readyButton;

    [Tooltip("准备按钮上的文字。")]
    [SerializeField] private TMP_Text readyLabel;

    [Header("成员列表")]
    [Tooltip("成员条目容器。条目按加入顺序纵向排列。")]
    [SerializeField] private RectTransform memberListRoot;

    [Tooltip("成员条目模板。运行时克隆，模板本身隐藏。")]
    [SerializeField] private GameObject memberEntryTemplate;

    [Tooltip("成员条目里头像的尺寸。")]
    [SerializeField] private float memberAvatarSize = 72f;

    [Tooltip("成员条目之间的纵向间距。")]
    [SerializeField] private float memberEntrySpacing = 12f;

    [Header("通用")]
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private Button closeButton;

    [Header("配色")]
    [SerializeField] private Color accentColor = new Color(0.35f, 0.85f, 0.95f, 1f);
    [SerializeField] private Color inactiveColor = new Color(0.55f, 0.6f, 0.65f, 1f);

    /// <summary>可选人数档位。与服务端约束（2~4）一致。</summary>
    private static readonly int[] MaxPlayersOptions = { 2, 3, 4 };

    /// <summary>当前选中的人数档位索引。默认 4 人（最后一项）。</summary>
    private int maxPlayersIndex = MaxPlayersOptions.Length - 1;

    /// <summary>当前是否在「创建房间」页签。</summary>
    private bool isCreateTab = true;

    /// <summary>本端（成员）当前准备状态。房主不使用。</summary>
    private bool localReady;

    /// <summary>提交创建房间。参数：房间名、人数上限。</summary>
    public event Action<string, int> CreateRequested;

    /// <summary>提交加入房间。参数：房间码。</summary>
    public event Action<string> JoinRequested;

    /// <summary>请求离开当前房间。</summary>
    public event Action LeaveRequested;

    /// <summary>房主请求出发（开始游戏）。</summary>
    public event Action DepartRequested;

    /// <summary>成员请求切换准备状态。参数为期望的准备状态。</summary>
    public event Action<bool> ReadyRequested;

    /// <summary>面板关闭。</summary>
    public event Action Closed;

    private void Awake()
    {
        if (createTabButton != null)
            createTabButton.onClick.AddListener(() => SelectTab(true));

        if (joinTabButton != null)
            joinTabButton.onClick.AddListener(() => SelectTab(false));

        if (maxPlayersButtons != null)
        {
            for (var i = 0; i < maxPlayersButtons.Length; i++)
            {
                if (maxPlayersButtons[i] == null)
                    continue;

                // 闭包捕获：必须用局部变量，直接用 i 会让所有回调都指向最后一个值。
                var index = i;
                maxPlayersButtons[i].onClick.AddListener(() => SelectMaxPlayers(index));
            }
        }

        if (createSubmitButton != null)
            createSubmitButton.onClick.AddListener(OnCreateClicked);

        if (joinSubmitButton != null)
            joinSubmitButton.onClick.AddListener(OnJoinClicked);

        if (copyCodeButton != null)
            copyCodeButton.onClick.AddListener(OnCopyClicked);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(() => LeaveRequested?.Invoke());

        if (departButton != null)
            departButton.onClick.AddListener(() => DepartRequested?.Invoke());

        if (readyButton != null)
            readyButton.onClick.AddListener(OnReadyClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        // 房间码统一大写：服务端字符集是大写，玩家小写输入也能进。
        if (roomCodeInput != null)
            roomCodeInput.onValueChanged.AddListener(OnRoomCodeChanged);
    }

    private void OnDestroy()
    {
        if (roomCodeInput != null)
            roomCodeInput.onValueChanged.RemoveListener(OnRoomCodeChanged);
    }

    /// <summary>打开面板，回到创建页。</summary>
    public void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        SelectTab(true);
        ClearError();
        HideResult();
        SetBusy(false);
        RefreshMaxPlayersHighlight();
    }

    /// <summary>关闭面板。</summary>
    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        Closed?.Invoke();
    }

    /// <summary>是否已打开。</summary>
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    /// <summary>切换创建 / 加入页。</summary>
    public void SelectTab(bool isCreate)
    {
        isCreateTab = isCreate;

        if (createPage != null)
            createPage.SetActive(isCreate);

        if (joinPage != null)
            joinPage.SetActive(!isCreate);

        if (createTabHighlight != null)
            createTabHighlight.gameObject.SetActive(isCreate);

        if (joinTabHighlight != null)
            joinTabHighlight.gameObject.SetActive(!isCreate);

        ClearError();
    }

    /// <summary>显示错误信息。</summary>
    public void ShowError(string message)
    {
        if (errorText != null)
            errorText.text = message ?? string.Empty;

        SetBusy(false);
    }

    /// <summary>清空错误信息。</summary>
    public void ClearError()
    {
        if (errorText != null)
            errorText.text = string.Empty;
    }

    /// <summary>请求进行中：禁用按钮，避免重复提交。</summary>
    public void SetBusy(bool busy)
    {
        if (createSubmitButton != null)
            createSubmitButton.interactable = !busy;

        if (joinSubmitButton != null)
            joinSubmitButton.interactable = !busy;

        if (createSubmitLabel != null)
            createSubmitLabel.text = busy ? "处理中…" : "创建房间";

        if (joinSubmitLabel != null)
            joinSubmitLabel.text = busy ? "处理中…" : "加入房间";
    }

    /// <summary>展示创建成功后的房间码。</summary>
    public void ShowRoomCode(string roomCode)
    {
        if (resultGroup != null)
            resultGroup.SetActive(true);

        // 结果区与输入区互斥：内容区高度有限，两者同时显示会溢出面板。
        SetInputAreaVisible(false);

        if (resultCodeText != null)
            resultCodeText.text = roomCode;

        if (resultHintText != null)
            resultHintText.text = "把房间码发给好友，对方在「加入房间」里输入即可";

        SetBusy(false);
    }

    /// <summary>隐藏结果区，恢复输入区。</summary>
    public void HideResult()
    {
        if (resultGroup != null)
            resultGroup.SetActive(false);

        SetInputAreaVisible(true);
    }

    /// <summary>
    /// 刷新出发 / 准备按钮状态。
    ///
    /// 房主：显示「出发」，仅人数 > 1 时可点；准备按钮隐藏。
    /// 成员：出发按钮隐藏，显示「准备 / 已准备」切换按钮。
    /// </summary>
    /// <param name="isHost">本端是否为房主。</param>
    /// <param name="memberCount">房间当前人数。</param>
    public void SetDepartState(bool isHost, int memberCount)
    {
        // 房主：出发按钮。
        if (departButton != null)
        {
            departButton.gameObject.SetActive(isHost);

            var canDepart = isHost && memberCount > 1;
            departButton.interactable = canDepart;

            if (departLabel != null)
            {
                if (memberCount <= 1)
                    departLabel.text = "等待队友加入";
                else
                    departLabel.text = "出发";
            }
        }

        // 成员：准备按钮。
        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(!isHost);

            if (readyLabel != null)
                readyLabel.text = localReady ? "已准备" : "准备";
        }
    }

    /// <summary>成员点击准备按钮：切换准备状态并上报。</summary>
    private void OnReadyClicked()
    {
        localReady = !localReady;

        if (readyLabel != null)
            readyLabel.text = localReady ? "已准备" : "准备";

        ReadyRequested?.Invoke(localReady);
    }

    /// <summary>重置本端准备状态（离开房间 / 重新进房时调用）。</summary>
    public void ResetReady()
    {
        localReady = false;

        if (readyLabel != null)
            readyLabel.text = "准备";
    }

    /// <summary>显示 / 隐藏输入区（页签 + 两页内容）。</summary>
    private void SetInputAreaVisible(bool visible)
    {
        if (createPage != null)
            createPage.SetActive(visible && isCreateTab);

        if (joinPage != null)
            joinPage.SetActive(visible && !isCreateTab);

        if (createTabButton != null)
            createTabButton.gameObject.SetActive(visible);

        if (joinTabButton != null)
            joinTabButton.gameObject.SetActive(visible);

        if (createTabHighlight != null)
            createTabHighlight.gameObject.SetActive(visible && isCreateTab);

        if (joinTabHighlight != null)
            joinTabHighlight.gameObject.SetActive(visible && !isCreateTab);
    }

    /// <summary>
    /// 刷新成员列表（头像 + 昵称 + 房主标记）。
    ///
    /// 每次全量重建而非增量更新：房间人数上限只有 4，
    /// 重建成本可忽略，而增量更新要处理「谁进谁出」的差集，容易出错。
    /// </summary>
    public void SetMembers(IReadOnlyList<MemberView> members)
    {
        if (memberListRoot == null || memberEntryTemplate == null)
            return;

        // 清掉旧条目（模板本身保留，只隐藏）。
        for (var i = memberListRoot.childCount - 1; i >= 0; i--)
        {
            var child = memberListRoot.GetChild(i).gameObject;
            if (child == memberEntryTemplate)
                continue;

            Destroy(child);
        }

        memberEntryTemplate.SetActive(false);

        if (members == null || members.Count == 0)
            return;

        var entryHeight = memberAvatarSize + memberEntrySpacing;

        for (var i = 0; i < members.Count; i++)
        {
            var entry = Instantiate(memberEntryTemplate, memberListRoot);
            entry.name = $"Member{i}";
            entry.SetActive(true);

            var rect = (RectTransform)entry.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -i * entryHeight);

            ApplyMemberEntry(entry, members[i]);
        }
    }

    /// <summary>把单个成员数据填进条目（头像 / 昵称 / 房主标记）。</summary>
    private void ApplyMemberEntry(GameObject entry, MemberView member)
    {
        var avatarImage = FindChildComponent<Image>(entry.transform, "Avatar");
        if (avatarImage != null)
        {
            if (member.Avatar != null)
            {
                avatarImage.sprite = Sprite.Create(
                    member.Avatar,
                    new Rect(0f, 0f, member.Avatar.width, member.Avatar.height),
                    new Vector2(0.5f, 0.5f));
                avatarImage.color = Color.white;
            }
            else
            {
                // 无头像时保留底图色块，避免条目塌陷成空白。
                avatarImage.sprite = null;
                avatarImage.color = inactiveColor;
            }
        }

        var nameText = FindChildComponent<TMP_Text>(entry.transform, "Name");
        if (nameText != null)
        {
            var suffix = member.IsHost ? "（房主）" : string.Empty;
            var self = member.IsLocal ? "（我）" : string.Empty;
            // 不用 ✓：simhei 动态字体没有该字形，会渲染成方框。用纯文字最稳。
            var ready = member.IsHost || member.IsReady ? "  [已准备]" : "  [未准备]";
            nameText.text = (member.DisplayName ?? "玩家") + suffix + self + ready;
            nameText.color = member.IsLocal ? accentColor : Color.white;
        }
    }

    /// <summary>在子层级里按名字找组件（含未激活对象）。</summary>
    private static T FindChildComponent<T>(Transform root, string name) where T : Component
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child.GetComponent<T>();
        }

        return null;
    }

    // ---- 内部 ----

    private void OnCreateClicked()
    {
        ClearError();

        var roomName = roomNameInput != null ? roomNameInput.text : string.Empty;
        roomName = string.IsNullOrWhiteSpace(roomName) ? "未命名房间" : roomName.Trim();

        SetBusy(true);
        CreateRequested?.Invoke(roomName, MaxPlayersOptions[maxPlayersIndex]);
    }

    private void OnJoinClicked()
    {
        ClearError();

        var code = roomCodeInput != null ? roomCodeInput.text : string.Empty;
        code = code.Trim().ToUpperInvariant();

        if (code.Length != 8)
        {
            ShowError("房间码为 8 位字符");
            return;
        }

        SetBusy(true);
        JoinRequested?.Invoke(code);
    }

    private void OnCopyClicked()
    {
        var code = resultCodeText != null ? resultCodeText.text : string.Empty;
        if (string.IsNullOrEmpty(code))
            return;

        GUIUtility.systemCopyBuffer = code;

        if (resultHintText != null)
            resultHintText.text = "已复制到剪贴板";
    }

    /// <summary>输入时实时转大写，避免玩家输入小写后提交失败。</summary>
    private void OnRoomCodeChanged(string value)
    {
        var upper = value.ToUpperInvariant();
        if (upper != value)
            roomCodeInput.SetTextWithoutNotify(upper);
    }

    /// <summary>选中人数档位，并刷新高亮。</summary>
    private void SelectMaxPlayers(int index)
    {
        if (index < 0 || index >= MaxPlayersOptions.Length)
            return;

        maxPlayersIndex = index;
        RefreshMaxPlayersHighlight();
    }

    /// <summary>刷新人数档位的高亮：选中项用强调色，其余用次要色。</summary>
    private void RefreshMaxPlayersHighlight()
    {
        if (maxPlayersLabels == null)
            return;

        for (var i = 0; i < maxPlayersLabels.Length; i++)
        {
            if (maxPlayersLabels[i] == null)
                continue;

            var selected = i == maxPlayersIndex;
            maxPlayersLabels[i].color = selected ? accentColor : inactiveColor;
            maxPlayersLabels[i].fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
        }
    }
}
