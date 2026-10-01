using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录 / 注册面板：全屏背景 + 面板底图（自带六边形标题牌）+ 页签底图 + 用户名 / 密码输入框
/// + 主按钮 + 密码可见切换。
///
/// 只负责展示与把输入转成事件，不直接依赖 <c>Tower.Network</c>（Assembly-CSharp 不引用 Network 程序集）：
/// 提交时抛 <see cref="SubmitRequested"/>，由 <see cref="MainMenuAccountBinder"/> 调用 NetworkSession 执行请求，
/// 再把结果通过 <see cref="ShowError"/> / <see cref="Close"/> 回灌。
///
/// 页签是一整张底图（中间有斜线分隔），因此用两个透明热区叠在上面，而不是两个独立按钮。
/// </summary>
public sealed class LoginPanelController : MonoBehaviour
{
    /// <summary>登录页签索引。</summary>
    public const int LoginTab = 0;

    /// <summary>注册页签索引。</summary>
    public const int RegisterTab = 1;

    [Header("面板引用")]
    [SerializeField] private GameObject panelRoot;

    [Header("页签")]
    [Tooltip("页签底图上的两个透明热区。")]
    [SerializeField] private Button loginTabButton;
    [SerializeField] private Button registerTabButton;

    [Tooltip("选中态高亮：叠在选中页签上的青色下划线。")]
    [SerializeField] private RectTransform loginTabHighlight;
    [SerializeField] private RectTransform registerTabHighlight;

    [Header("输入")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField displayNameInput;
    [SerializeField] private GameObject displayNameRow;

    [Header("头像（仅注册页）")]
    [Tooltip("头像行容器，随注册页签显示 / 隐藏。")]
    [SerializeField] private GameObject avatarRow;

    [Tooltip("点击选择本地图片的按钮。")]
    [SerializeField] private Button avatarButton;

    [Tooltip("头像预览图。未选择时显示占位图。")]
    [SerializeField] private Image avatarPreview;

    [Tooltip("未选择头像时的占位图。")]
    [SerializeField] private Sprite avatarPlaceholderSprite;

    [Tooltip("头像预览尺寸（像素）。用于把选中的图压到服务端友好的大小。")]
    [SerializeField] private int avatarPreviewSize = 256;

    [Header("密码可见切换")]
    [SerializeField] private Button passwordToggleButton;
    [SerializeField] private Image passwordToggleIcon;
    [SerializeField] private Sprite eyeOpenSprite;
    [SerializeField] private Sprite eyeClosedSprite;

    [Header("提交")]
    [SerializeField] private Button submitButton;
    [SerializeField] private Image submitBackground;
    [SerializeField] private Sprite submitNormalSprite;
    [SerializeField] private Sprite submitPressedSprite;
    [SerializeField] private TMP_Text submitLabel;
    [SerializeField] private TMP_Text errorText;

    [Header("文案")]
    [SerializeField] private string loginSubmitText = "登 录";
    [SerializeField] private string registerSubmitText = "注 册";
    [SerializeField] private string usernamePlaceholder = "用户名";
    [SerializeField] private string passwordPlaceholder = "密码";
    [SerializeField] private string displayNamePlaceholder = "昵称";

    /// <summary>提交请求。参数为 (是否注册, 用户名, 密码, 昵称, 头像字节)。</summary>
    public event Action<bool, string, string, string, byte[]> SubmitRequested;

    /// <summary>当前页签，0 登录 / 1 注册。</summary>
    public int CurrentTab { get; private set; } = LoginTab;

    /// <summary>是否正在等待服务端响应。等待期间禁止重复提交。</summary>
    public bool IsBusy { get; private set; }

    /// <summary>密码是否以明文显示。</summary>
    public bool PasswordVisible { get; private set; }

    /// <summary>已选择的头像图片字节。未选择时为 null。</summary>
    public byte[] AvatarBytes { get; private set; }

    /// <summary>已选择的头像贴图。未选择时为 null。</summary>
    public Texture2D AvatarTexture { get; private set; }

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        BindButtons();
        SetTab(LoginTab);
        SetPasswordVisible(false);
        ClearError();
    }

    private void OnDestroy()
    {
        if (loginTabButton != null)
            loginTabButton.onClick.RemoveListener(SelectLoginTab);

        if (registerTabButton != null)
            registerTabButton.onClick.RemoveListener(SelectRegisterTab);

        if (submitButton != null)
            submitButton.onClick.RemoveListener(Submit);

        if (passwordToggleButton != null)
            passwordToggleButton.onClick.RemoveListener(TogglePasswordVisible);

        if (avatarButton != null)
            avatarButton.onClick.RemoveListener(PickAvatar);
    }

    /// <summary>
    /// 幂等绑定按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值（测试与运行时接线都会遇到），需要能补绑一次。
    /// </summary>
    public void BindButtons()
    {
        if (loginTabButton != null)
        {
            loginTabButton.onClick.RemoveListener(SelectLoginTab);
            loginTabButton.onClick.AddListener(SelectLoginTab);
        }

        if (registerTabButton != null)
        {
            registerTabButton.onClick.RemoveListener(SelectRegisterTab);
            registerTabButton.onClick.AddListener(SelectRegisterTab);
        }

        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(Submit);
            submitButton.onClick.AddListener(Submit);
        }

        if (passwordToggleButton != null)
        {
            passwordToggleButton.onClick.RemoveListener(TogglePasswordVisible);
            passwordToggleButton.onClick.AddListener(TogglePasswordVisible);
        }

        if (avatarButton != null)
        {
            avatarButton.onClick.RemoveListener(PickAvatar);
            avatarButton.onClick.AddListener(PickAvatar);
        }
    }

    /// <summary>打开面板并清空上一次的输入与错误。</summary>
    public void Open()
    {
        if (panelRoot == null)
            return;

        BindButtons();
        SetBusy(false);
        ClearError();
        SetTab(LoginTab);
        SetPasswordVisible(false);
        ClearAvatar();
        panelRoot.SetActive(true);

        if (usernameInput != null)
            usernameInput.ActivateInputField();
    }

    /// <summary>关闭面板。</summary>
    public void Close()
    {
        SetBusy(false);

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>切换页签。0 登录 / 1 注册。</summary>
    public void SetTab(int tab)
    {
        CurrentTab = tab == RegisterTab ? RegisterTab : LoginTab;
        var isRegister = CurrentTab == RegisterTab;

        if (displayNameRow != null)
            displayNameRow.SetActive(isRegister);

        if (avatarRow != null)
            avatarRow.SetActive(isRegister);

        if (submitLabel != null)
            submitLabel.text = isRegister ? registerSubmitText : loginSubmitText;

        if (loginTabHighlight != null)
            loginTabHighlight.gameObject.SetActive(!isRegister);

        if (registerTabHighlight != null)
            registerTabHighlight.gameObject.SetActive(isRegister);

        ClearError();
    }

    /// <summary>登录页签入口，供 Button.onClick 调用。</summary>
    public void SelectLoginTab()
    {
        SetTab(LoginTab);
    }

    /// <summary>注册页签入口，供 Button.onClick 调用。</summary>
    public void SelectRegisterTab()
    {
        SetTab(RegisterTab);
    }

    /// <summary>切换密码明文 / 密文显示。</summary>
    public void TogglePasswordVisible()
    {
        SetPasswordVisible(!PasswordVisible);
    }

    /// <summary>设置密码可见性，并同步图标。</summary>
    public void SetPasswordVisible(bool visible)
    {
        PasswordVisible = visible;

        if (passwordInput != null)
            passwordInput.contentType = visible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;

        // 改 contentType 后必须强制刷新，否则已输入的字符不会重新排版。
        if (passwordInput != null)
            passwordInput.ForceLabelUpdate();

        if (passwordToggleIcon != null)
        {
            var sprite = visible ? eyeOpenSprite : eyeClosedSprite;
            if (sprite != null)
                passwordToggleIcon.sprite = sprite;
        }
    }

    /// <summary>
    /// 选择本地图片作为头像。
    ///
    /// 用 <see cref="GUIUtility.systemCopyBuffer"/> 之外的原生文件对话框：
    /// 编辑器与桌面平台走 EditorUtility / 系统对话框，移动端走相册。
    /// 这里只做「拿到图片字节」这一件事，压缩与格式转换交给 <see cref="ApplyAvatar"/>。
    /// </summary>
    public void PickAvatar()
    {
#if UNITY_EDITOR
        var path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(
            "选择头像", string.Empty, new[] { "图片", "png,jpg,jpeg,webp" });
        if (string.IsNullOrEmpty(path))
            return;

        byte[] raw;
        try
        {
            raw = System.IO.File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            ShowError("读取图片失败：" + ex.Message);
            return;
        }

        ApplyAvatar(raw);
#elif UNITY_ANDROID || UNITY_IOS
        // 移动端走原生相册选择器。NativeGallery 之类的插件不在本期依赖里，
        // 这里留出接入点：拿到图片字节后调用 ApplyAvatar 即可。
        ShowError("当前平台暂不支持选择头像");
#else
        ShowError("当前平台暂不支持选择头像");
#endif
    }

    /// <summary>
    /// 接收一张图片的原始字节，解码 → 缩放 → 重新编码为 JPEG，并刷新预览。
    ///
    /// 之所以重新编码而不是原样上传：手机拍的照片动辄 3~5MB，
    /// 直接传会撞服务端 2MB 上限，也没必要为一张头像付这个带宽。
    /// </summary>
    public void ApplyAvatar(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            ShowError("图片内容为空");
            return;
        }

        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(raw))
        {
            Destroy(source);
            ShowError("无法识别的图片格式");
            return;
        }

        var size = Mathf.Max(32, avatarPreviewSize);
        var scaled = ScaleToSquare(source, size);
        Destroy(source);

        AvatarTexture = scaled;
        AvatarBytes = scaled.EncodeToJPG(85);

        if (avatarPreview != null)
        {
            avatarPreview.sprite = Sprite.Create(
                scaled, new Rect(0f, 0f, scaled.width, scaled.height), new Vector2(0.5f, 0.5f));
            avatarPreview.preserveAspect = true;
        }

        ClearError();
    }

    /// <summary>清空已选头像，预览恢复为占位图。</summary>
    public void ClearAvatar()
    {
        AvatarBytes = null;

        if (AvatarTexture != null)
        {
            Destroy(AvatarTexture);
            AvatarTexture = null;
        }

        if (avatarPreview != null)
        {
            avatarPreview.sprite = avatarPlaceholderSprite;
            avatarPreview.preserveAspect = true;
        }
    }

    /// <summary>把任意尺寸的贴图等比缩放到正方形画布中央（多余部分留透明）。</summary>
    private static Texture2D ScaleToSquare(Texture2D source, int size)
    {
        var target = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // 先铺一层透明底，避免非正方形图片缩放后边缘出现拉伸的杂色。
        var clear = new Color32[size * size];
        target.SetPixels32(clear);

        var scale = Mathf.Min((float)size / source.width, (float)size / source.height);
        var w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
        var h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
        var x = (size - w) / 2;
        var y = (size - h) / 2;

        // 逐像素双线性采样。用 GetPixelBilinear 而非 GPU Blit：
        // Blit 需要 RenderTexture 与可读的源贴图，在 IL2CPP 下更容易踩坑。
        var pixels = new Color[size * size];
        for (var ty = 0; ty < h; ty++)
        {
            var v = (ty + 0.5f) / h;
            for (var tx = 0; tx < w; tx++)
            {
                var u = (tx + 0.5f) / w;
                pixels[(y + ty) * size + (x + tx)] = source.GetPixelBilinear(u, v);
            }
        }

        target.SetPixels(pixels);
        target.Apply();
        return target;
    }

    /// <summary>提交当前表单。校验通过后抛 <see cref="SubmitRequested"/>。</summary>
    public void Submit()
    {
        if (IsBusy)
            return;

        var username = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
        var password = passwordInput != null ? passwordInput.text : string.Empty;
        var displayName = displayNameInput != null ? displayNameInput.text.Trim() : string.Empty;

        if (string.IsNullOrEmpty(username))
        {
            ShowError("请输入用户名");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("请输入密码");
            return;
        }

        // 昵称仅注册页需要，且不可为空。
        if (CurrentTab == RegisterTab && string.IsNullOrEmpty(displayName))
        {
            ShowError("请输入昵称");
            return;
        }

        ClearError();
        SetBusy(true);
        SubmitRequested?.Invoke(CurrentTab == RegisterTab, username, password, displayName,
            CurrentTab == RegisterTab ? AvatarBytes : null);
    }

    /// <summary>显示错误提示并解除等待态。</summary>
    public void ShowError(string message)
    {
        SetBusy(false);

        if (errorText != null)
            errorText.text = message ?? string.Empty;
    }

    /// <summary>清空错误提示。</summary>
    public void ClearError()
    {
        if (errorText != null)
            errorText.text = string.Empty;
    }

    /// <summary>设置等待态：等待期间禁用提交按钮，避免重复请求。</summary>
    public void SetBusy(bool busy)
    {
        IsBusy = busy;

        if (submitButton != null)
            submitButton.interactable = !busy;

        if (submitBackground != null && submitNormalSprite != null)
            submitBackground.sprite = busy && submitPressedSprite != null ? submitPressedSprite : submitNormalSprite;

        if (submitLabel != null)
            submitLabel.text = busy
                ? "请稍候…"
                : (CurrentTab == RegisterTab ? registerSubmitText : loginSubmitText);
    }
}
