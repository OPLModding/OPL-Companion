using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal static class MessageBoardUI
{
    private const float UiScale = 1.15f;

    private static readonly Color AccentBlue = new Color(0.4f, 0.7f, 1f);
    private static readonly Color AccentGreen = new Color(0.3f, 0.8f, 0.4f);
    private static readonly Color AccentOrange = new Color(0.88f, 0.62f, 0.18f);
    private static readonly Color AccentRed = new Color(0.9f, 0.2f, 0.2f);
    private static readonly Color AccentNeutral = new Color(0.6f, 0.67f, 0.75f);
    private static readonly Color BgDark = new Color(0.12f, 0.12f, 0.12f, 0.97f);
    private static readonly Color BgHeader = new Color(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color BgRow = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color BgButton = new Color(0.2f, 0.2f, 0.2f, 1f);
    private static readonly Color BorderGray = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color BorderDark = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color TextPrimary = Color.white;
    private static readonly Color TextSecondary = new Color(0.78f, 0.78f, 0.78f, 1f);
    private static readonly Color TextMuted = new Color(0.62f, 0.62f, 0.62f, 1f);

    private static readonly FieldInfo? ViewsField = typeof(UIManager).GetField("views", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Dictionary<string, Button> TabButtons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Button> HoveredTabButtons = new();

    private static bool isSetup;
    private static bool isVisible;
    private static int hideFrame = -1;

    private static VisualElement? overlay;
    private static VisualElement? panel;
    private static VisualElement? tabRail;
    private static VisualElement? tabBar;
    private static ScrollView? tabScrollView;
    private static ScrollView? scrollView;
    private static VisualElement? contentRoot;
    private static Label? titleLabel;
    private static Label? subtitleLabel;
    private static Label? footerNoteLabel;
    private static Label? footerHintLabel;

    private static MessageBoardDocument? currentDocument;
    private static string currentTabId = string.Empty;

    public static bool IsVisible => isVisible;

    public static void EnsureSetup()
    {
        if (isSetup && overlay?.panel != null)
        {
            return;
        }

        VisualElement? root = GetRoot();
        if (root == null)
        {
            return;
        }

        Destroy();

        overlay = new VisualElement
        {
            name = "MessageBoardOverlay",
            pickingMode = PickingMode.Position
        };
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.top = 0f;
        overlay.style.right = 0f;
        overlay.style.bottom = 0f;
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.52f);
        overlay.style.display = DisplayStyle.None;
        overlay.RegisterCallback<ClickEvent>(evt =>
        {
            if (ReferenceEquals(evt.target, overlay))
            {
                Hide();
            }
        });
        root.Add(overlay);

        panel = new VisualElement
        {
            name = "MessageBoardPanel"
        };
        panel.style.width = Length.Percent(65f);
        panel.style.minWidth = 950f;
        panel.style.maxWidth = 1300f;
        panel.style.height = Length.Percent(72f);
        panel.style.minHeight = 0f;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = BgDark;
        panel.style.borderTopLeftRadius = 8f;
        panel.style.borderTopRightRadius = 8f;
        panel.style.borderBottomLeftRadius = 8f;
        panel.style.borderBottomRightRadius = 8f;
        panel.style.overflow = Overflow.Hidden;
        overlay.Add(panel);

        BuildHeader(panel);
        BuildBody(panel);
        BuildFooter(panel);

        isSetup = true;
        if (currentDocument != null)
        {
            ApplyDocument(currentDocument);
        }
    }

    public static bool Show()
    {
        EnsureSetup();
        if (overlay == null)
        {
            return false;
        }

        overlay.style.display = DisplayStyle.Flex;
        overlay.BringToFront();
        isVisible = true;
        SetGameInputSuppressed(true);
        GlobalStateManager.SetUIState(new Dictionary<string, object> { { "isMouseRequired", true } });
        ResetLocalPlayerInputs();
        return true;
    }

    public static void Hide()
    {
        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.None;
        }

        isVisible = false;
        try
        {
            UIPauseMenu? pauseMenu = MonoBehaviourSingleton<UIManager>.Instance?.PauseMenu;
            if (pauseMenu == null || !pauseMenu.IsVisible)
            {
                hideFrame = Time.frameCount;
            }
        }
        catch
        {
            hideFrame = Time.frameCount;
        }

        SetGameInputSuppressed(false);
        if (!AnyGameViewRequiresMouse())
        {
            GlobalStateManager.SetUIState(new Dictionary<string, object> { { "isMouseRequired", false } });
        }
    }

    public static bool ShouldBlockPauseAction()
    {
        return isVisible || hideFrame == Time.frameCount;
    }

    public static void ApplyDocument(MessageBoardDocument document)
    {
        currentDocument = document;
        EnsureSetup();
        if (!isSetup || titleLabel == null || subtitleLabel == null || footerNoteLabel == null || tabRail == null || tabBar == null || contentRoot == null)
        {
            return;
        }

        titleLabel.text = string.IsNullOrWhiteSpace(document.Title) ? "Server Dashboard" : document.Title;
        subtitleLabel.text = document.Subtitle ?? string.Empty;
        subtitleLabel.style.display = string.IsNullOrWhiteSpace(subtitleLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
        footerNoteLabel.text = document.FooterNote ?? string.Empty;
        footerNoteLabel.style.display = string.IsNullOrWhiteSpace(footerNoteLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
        tabRail.style.display = DisplayStyle.Flex;
        tabBar.style.display = DisplayStyle.Flex;

        RebuildTabs(document);
    }

    public static void ShowLoadingStatus(string message)
    {
        RenderStatusState("Server Dashboard", "Loading message board", MessageBoardTones.Info, message);
    }

    public static void ShowErrorStatus(string message)
    {
        RenderStatusState("Server Dashboard", "Message board unavailable", MessageBoardTones.Error, message);
    }

    public static void ShowInfoStatus(string message)
    {
        RenderStatusState("Server Dashboard", "Message board", MessageBoardTones.Info, message);
    }

    public static void Schedule(Action action, long delayMs)
    {
        VisualElement? root = GetRoot();
        if (root == null)
        {
            action();
            return;
        }

        root.schedule.Execute(action).ExecuteLater(delayMs);
    }

    public static void Destroy()
    {
        overlay?.RemoveFromHierarchy();

        overlay = null;
        panel = null;
        tabRail = null;
        tabBar = null;
        tabScrollView = null;
        scrollView = null;
        contentRoot = null;
        titleLabel = null;
        subtitleLabel = null;
        footerNoteLabel = null;
        footerHintLabel = null;
        currentTabId = string.Empty;
        TabButtons.Clear();
        HoveredTabButtons.Clear();

        isSetup = false;
        isVisible = false;
        hideFrame = -1;
    }

    private static void BuildHeader(VisualElement parent)
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = Scale(18f);
        header.style.paddingRight = Scale(14f);
        header.style.paddingTop = Scale(13f);
        header.style.paddingBottom = Scale(13f);
        header.style.backgroundColor = BgHeader;
        parent.Add(header);

        VisualElement titleWrap = new VisualElement();
        titleWrap.style.flexDirection = FlexDirection.Column;
        titleWrap.style.alignItems = Align.FlexStart;
        titleWrap.style.flexGrow = 1f;
        header.Add(titleWrap);

        titleLabel = new Label("Server Dashboard");
        titleLabel.style.fontSize = Scale(22f);
        titleLabel.style.color = TextPrimary;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleWrap.Add(titleLabel);

        subtitleLabel = new Label();
        subtitleLabel.style.fontSize = Scale(12f);
        subtitleLabel.style.color = TextSecondary;
        subtitleLabel.style.marginTop = Scale(2f);
        subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
        titleWrap.Add(subtitleLabel);

        Button closeButton = CreateHeaderButton("X", Hide);
        header.Add(closeButton);

        VisualElement accentBar = new VisualElement();
        accentBar.style.height = 2f;
        accentBar.style.backgroundColor = AccentBlue;
        accentBar.style.marginLeft = Scale(16f);
        accentBar.style.marginRight = Scale(16f);
        parent.Add(accentBar);
    }

    private static void BuildTabBar(VisualElement parent)
    {
        tabScrollView = new ScrollView(ScrollViewMode.Vertical);
        tabScrollView.style.flexGrow = 1f;
        tabScrollView.style.flexShrink = 0f;
        tabScrollView.style.minHeight = 0f;
        parent.Add(tabScrollView);

        tabBar = tabScrollView.contentContainer;
        tabBar.style.flexDirection = FlexDirection.Column;
        tabBar.style.alignItems = Align.Stretch;
        tabBar.style.paddingTop = Scale(4f);
        tabBar.style.paddingBottom = Scale(4f);
    }

    private static void BuildBody(VisualElement parent)
    {
        VisualElement bodyLayout = new VisualElement();
        bodyLayout.style.flexDirection = FlexDirection.Row;
        bodyLayout.style.flexGrow = 1f;
        bodyLayout.style.flexShrink = 1f;
        bodyLayout.style.minHeight = 0f;
        parent.Add(bodyLayout);

        tabRail = new VisualElement();
        tabRail.style.width = Scale(190f);
        tabRail.style.minWidth = Scale(180f);
        tabRail.style.maxWidth = Scale(230f);
        tabRail.style.flexShrink = 0f;
        tabRail.style.backgroundColor = BgHeader;
        tabRail.style.paddingLeft = Scale(10f);
        tabRail.style.paddingRight = Scale(10f);
        tabRail.style.paddingTop = Scale(12f);
        tabRail.style.paddingBottom = Scale(12f);
        tabRail.style.borderRightWidth = 1f;
        tabRail.style.borderRightColor = BorderDark;
        bodyLayout.Add(tabRail);

        BuildTabBar(tabRail);

        VisualElement contentPanel = new VisualElement();
        contentPanel.style.flexGrow = 1f;
        contentPanel.style.flexShrink = 1f;
        contentPanel.style.minWidth = 0f;
        contentPanel.style.minHeight = 0f;
        bodyLayout.Add(contentPanel);

        scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.style.flexGrow = 1f;
        scrollView.style.flexShrink = 1f;
        scrollView.style.minHeight = 0f;
        contentPanel.Add(scrollView);

        contentRoot = scrollView.contentContainer;
        contentRoot.style.paddingLeft = Scale(22f);
        contentRoot.style.paddingRight = Scale(26f);
        contentRoot.style.paddingTop = Scale(18f);
        contentRoot.style.paddingBottom = Scale(22f);
    }

    private static void BuildFooter(VisualElement parent)
    {
        VisualElement footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.SpaceBetween;
        footer.style.alignItems = Align.Center;
        footer.style.paddingLeft = Scale(16f);
        footer.style.paddingRight = Scale(16f);
        footer.style.paddingTop = Scale(8f);
        footer.style.paddingBottom = Scale(10f);
        footer.style.backgroundColor = BgHeader;
        footer.style.flexWrap = Wrap.Wrap;
        parent.Add(footer);

        footerNoteLabel = new Label();
        footerNoteLabel.style.flexGrow = 1f;
        footerNoteLabel.style.color = TextMuted;
        footerNoteLabel.style.fontSize = Scale(11f);
        footerNoteLabel.style.whiteSpace = WhiteSpace.Normal;
        footer.Add(footerNoteLabel);

        footerHintLabel = new Label(MessageBoardHotkeys.ToggleHint);
        footerHintLabel.style.color = TextMuted;
        footerHintLabel.style.fontSize = Scale(11f);
        footerHintLabel.style.marginLeft = Scale(12f);
        footer.Add(footerHintLabel);
    }

    private static void RenderStatusState(string title, string subtitle, string tone, string message)
    {
        EnsureSetup();
        if (titleLabel == null || subtitleLabel == null || footerNoteLabel == null || tabRail == null || tabBar == null || contentRoot == null)
        {
            return;
        }

        titleLabel.text = title;
        subtitleLabel.text = subtitle;
        subtitleLabel.style.display = string.IsNullOrWhiteSpace(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
        footerNoteLabel.text = MessageBoardHotkeys.RetryFooterNote;
        footerNoteLabel.style.display = DisplayStyle.Flex;
        tabRail.style.display = DisplayStyle.None;

        contentRoot.Clear();
        AddAnnouncementBox(
            contentRoot,
            new MessageBoardBlock
            {
                Type = MessageBoardBlockTypes.AnnouncementBox,
                Tone = tone,
                Title = subtitle,
                Text = message
            });
    }

    private static void RebuildTabs(MessageBoardDocument document)
    {
        if (tabBar == null)
        {
            return;
        }

        tabBar.Clear();
        TabButtons.Clear();
        HoveredTabButtons.Clear();

        if (document.Tabs.Count == 0)
        {
            currentTabId = string.Empty;
            RenderStatusState(document.Title, document.Subtitle, MessageBoardTones.Info, "This message board does not contain any tabs.");
            return;
        }

        bool tabExists = false;
        foreach (MessageBoardTab tab in document.Tabs)
        {
            if (string.Equals(tab.Id, currentTabId, StringComparison.OrdinalIgnoreCase))
            {
                tabExists = true;
                break;
            }
        }

        if (!tabExists)
        {
            currentTabId = document.Tabs[0].Id;
        }

        foreach (MessageBoardTab tab in document.Tabs)
        {
            MessageBoardTab capturedTab = tab;
            Button tabButton = new Button(() =>
            {
                currentTabId = capturedTab.Id;
                RenderSelectedTab();
            })
            {
                text = capturedTab.Label
            };
            tabButton.userData = capturedTab.Id;
            RegisterHoverCallbacks(
                tabButton,
                () =>
                {
                    HoveredTabButtons.Add(tabButton);
                    StyleTabButton(tabButton, IsTabButtonActive(tabButton));
                },
                () =>
                {
                    HoveredTabButtons.Remove(tabButton);
                    StyleTabButton(tabButton, IsTabButtonActive(tabButton));
                });
            StyleTabButton(tabButton, active: false);
            tabBar.Add(tabButton);
            TabButtons[capturedTab.Id] = tabButton;
        }

        RenderSelectedTab();
    }

    private static void RenderSelectedTab()
    {
        if (currentDocument == null || contentRoot == null)
        {
            return;
        }

        MessageBoardTab selectedTab = currentDocument.Tabs[0];
        foreach (MessageBoardTab tab in currentDocument.Tabs)
        {
            if (string.Equals(tab.Id, currentTabId, StringComparison.OrdinalIgnoreCase))
            {
                selectedTab = tab;
                break;
            }
        }

        currentTabId = selectedTab.Id;
        foreach (KeyValuePair<string, Button> entry in TabButtons)
        {
            StyleTabButton(entry.Value, string.Equals(entry.Key, currentTabId, StringComparison.OrdinalIgnoreCase));
        }

        contentRoot.Clear();
        RenderBlocks(contentRoot, selectedTab.Blocks);
        if (scrollView != null)
        {
            scrollView.scrollOffset = Vector2.zero;
        }
    }

    private static void RenderBlocks(VisualElement parent, IEnumerable<MessageBoardBlock> blocks)
    {
        foreach (MessageBoardBlock block in blocks)
        {
            switch (block.Type)
            {
                case MessageBoardBlockTypes.Heading:
                    AddHeading(parent, block);
                    break;
                case MessageBoardBlockTypes.Paragraph:
                    AddParagraph(parent, block);
                    break;
                case MessageBoardBlockTypes.BulletList:
                    AddBulletList(parent, block);
                    break;
                case MessageBoardBlockTypes.RuleList:
                    AddRuleList(parent, block);
                    break;
                case MessageBoardBlockTypes.ButtonRow:
                    AddButtonRow(parent, block);
                    break;
                case MessageBoardBlockTypes.Divider:
                    AddDivider(parent);
                    break;
                case MessageBoardBlockTypes.Spacer:
                    AddSpacer(parent, block.Height);
                    break;
                case MessageBoardBlockTypes.CollapsibleSection:
                    AddCollapsibleSection(parent, block);
                    break;
                case MessageBoardBlockTypes.AnnouncementBox:
                    AddAnnouncementBox(parent, block);
                    break;
                default:
                    AddAnnouncementBox(
                        parent,
                        new MessageBoardBlock
                        {
                            Type = MessageBoardBlockTypes.AnnouncementBox,
                            Tone = MessageBoardTones.Warning,
                            Title = "Unsupported block type",
                            Text = $"The block type '{block.Type}' is not supported by this client."
                        });
                    break;
            }
        }
    }

    private static void AddHeading(VisualElement parent, MessageBoardBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Text))
        {
            return;
        }

        string headingTone = HasTone(block.TextTone)
            ? block.TextTone
            : HasTone(block.TitleTone)
                ? block.TitleTone
                : ResolveTone(block.Tone, MessageBoardTones.Info);

        Label label = new Label(block.Text);
        label.enableRichText = true;
        label.style.fontSize = Scale(18f);
        label.style.color = GetToneAccentColor(headingTone);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = Scale(4f);
        label.style.marginBottom = Scale(10f);
        label.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(label);
    }

    private static void AddParagraph(VisualElement parent, MessageBoardBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Text))
        {
            return;
        }

        Label label = new Label(block.Text);
        label.enableRichText = true;
        label.style.fontSize = Scale(14f);
        label.style.color = ResolveTextColor(block, TextSecondary, inheritBlockTone: true);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginBottom = Scale(10f);
        parent.Add(label);
    }

    private static void AddBulletList(VisualElement parent, MessageBoardBlock block)
    {
        Color accentColor = GetToneAccentColor(ResolveTone(block.Tone, MessageBoardTones.Info));
        Color titleColor = ResolveTitleColor(block, TextPrimary);
        Color itemTextColor = ResolveTextColor(block, TextSecondary);
        AddMinorHeading(parent, block.Title, titleColor);

        foreach (string item in block.Items)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = Scale(6f);
            parent.Add(row);

            Label bullet = new Label("- ");
            bullet.style.fontSize = Scale(14f);
            bullet.style.color = accentColor;
            bullet.style.unityFontStyleAndWeight = FontStyle.Bold;
            bullet.style.minWidth = Scale(16f);
            row.Add(bullet);

            Label text = new Label(item);
            text.enableRichText = true;
            text.style.flexGrow = 1f;
            text.style.fontSize = Scale(14f);
            text.style.color = itemTextColor;
            text.style.whiteSpace = WhiteSpace.Normal;
            row.Add(text);
        }
    }

    private static void AddRuleList(VisualElement parent, MessageBoardBlock block)
    {
        Color accentColor = GetToneAccentColor(ResolveTone(block.Tone, MessageBoardTones.Info));
        Color titleColor = ResolveTitleColor(block, TextPrimary);
        Color bodyTextColor = ResolveTextColor(block, TextSecondary);
        AddMinorHeading(parent, block.Title, titleColor);

        int ruleNumber = 1;
        foreach (MessageBoardRuleItem ruleItem in block.RuleItems)
        {
            VisualElement row = CreateCardContainer();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = Scale(8f);
            parent.Add(row);

            Label number = new Label($"{ruleNumber}.");
            number.style.fontSize = Scale(14f);
            number.style.color = accentColor;
            number.style.unityFontStyleAndWeight = FontStyle.Bold;
            number.style.minWidth = Scale(28f);
            row.Add(number);

            VisualElement textWrap = new VisualElement();
            textWrap.style.flexGrow = 1f;
            row.Add(textWrap);

            Label title = new Label(ruleItem.Title);
            title.style.fontSize = Scale(14f);
            title.style.color = titleColor;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = Scale(2f);
            textWrap.Add(title);

            Label text = new Label(ruleItem.Text);
            text.enableRichText = true;
            text.style.fontSize = Scale(13f);
            text.style.color = bodyTextColor;
            text.style.whiteSpace = WhiteSpace.Normal;
            textWrap.Add(text);

            ruleNumber++;
        }
    }

    private static void AddButtonRow(VisualElement parent, MessageBoardBlock block)
    {
        Color titleColor = ResolveTitleColor(block, TextPrimary);
        string rowTone = ResolveTone(block.Tone, MessageBoardTones.Info);
        AddMinorHeading(parent, block.Title, titleColor);

        VisualElement wrap = new VisualElement();
        wrap.style.flexDirection = FlexDirection.Row;
        wrap.style.flexWrap = Wrap.Wrap;
        wrap.style.marginBottom = Scale(12f);
        parent.Add(wrap);

        foreach (MessageBoardActionButton actionButton in block.Buttons)
        {
            Button button = new Button(() =>
            {
                if (!string.IsNullOrWhiteSpace(actionButton.Url))
                {
                    Application.OpenURL(actionButton.Url);
                }
            })
            {
                text = actionButton.Label
            };
            button.style.marginRight = Scale(8f);
            button.style.marginBottom = Scale(8f);
            button.style.minHeight = Scale(40f);
            button.style.minWidth = Scale(132f);
            string buttonAccentTone = HasTone(actionButton.Tone) ? actionButton.Tone : rowTone;
            Color buttonAccent = GetToneAccentColor(buttonAccentTone);
            bool hasBackgroundTone = HasTone(actionButton.BackgroundTone);
            Color buttonBackgroundColor = hasBackgroundTone
                ? GetToneAccentColor(actionButton.BackgroundTone)
                : BgButton;
            Color buttonBorderColor = HasTone(actionButton.Tone)
                ? buttonAccent
                : hasBackgroundTone
                    ? GetToneAccentColor(actionButton.BackgroundTone)
                    : buttonAccent;
            Color buttonTextColor = HasTone(actionButton.TextTone)
                ? GetToneAccentColor(actionButton.TextTone)
                : hasBackgroundTone
                    ? GetContrastingTextColor(buttonBackgroundColor)
                    : buttonAccent;
            Color hoverBackgroundColor = hasBackgroundTone
                ? BlendToward(buttonBackgroundColor, TextPrimary, 0.22f)
                : BlendToward(buttonBackgroundColor, buttonBorderColor, 0.48f);
            Color hoverBorderColor = BlendToward(buttonBorderColor, TextPrimary, 0.34f);
            Color hoverTextColor = HasTone(actionButton.TextTone)
                ? BlendToward(buttonTextColor, TextPrimary, 0.22f)
                : hasBackgroundTone
                    ? GetContrastingTextColor(hoverBackgroundColor)
                    : TextPrimary;

            ApplyButtonColors(button, buttonBackgroundColor, buttonTextColor, buttonBorderColor);
            button.style.fontSize = Scale(13f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.paddingLeft = Scale(16f);
            button.style.paddingRight = Scale(16f);
            button.style.paddingTop = Scale(6f);
            button.style.paddingBottom = Scale(6f);
            button.style.borderTopLeftRadius = 4f;
            button.style.borderTopRightRadius = 4f;
            button.style.borderBottomLeftRadius = 4f;
            button.style.borderBottomRightRadius = 4f;
            button.style.borderTopWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            RegisterHoverCallbacks(
                button,
                () => ApplyButtonColors(button, hoverBackgroundColor, hoverTextColor, hoverBorderColor),
                () => ApplyButtonColors(button, buttonBackgroundColor, buttonTextColor, buttonBorderColor));
            wrap.Add(button);
        }
    }

    private static void AddDivider(VisualElement parent)
    {
        VisualElement divider = new VisualElement();
        divider.style.height = 1f;
        divider.style.marginTop = Scale(10f);
        divider.style.marginBottom = Scale(10f);
        divider.style.backgroundColor = BorderGray;
        parent.Add(divider);
    }

    private static void AddSpacer(VisualElement parent, float height)
    {
        VisualElement spacer = new VisualElement();
        spacer.style.height = height;
        parent.Add(spacer);
    }

    private static void AddAnnouncementBox(VisualElement parent, MessageBoardBlock block)
    {
        string accentTone = ResolveTone(block.Tone, MessageBoardTones.Info);
        (Color accentColor, Color surfaceColor) = GetToneSurface(accentTone);
        Color titleColor = HasTone(block.TitleTone) ? GetToneAccentColor(block.TitleTone) : accentColor;
        Color textColor = ResolveTextColor(block, TextPrimary);

        VisualElement container = CreateCardContainer();
        container.style.marginBottom = Scale(12f);
        container.style.backgroundColor = surfaceColor;
        container.style.borderLeftWidth = 3f;
        container.style.borderLeftColor = accentColor;
        parent.Add(container);

        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            Label title = new Label(block.Title);
            title.enableRichText = true;
            title.style.fontSize = Scale(14f);
            title.style.color = titleColor;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = string.IsNullOrWhiteSpace(block.Text) ? 0f : Scale(4f);
            title.style.whiteSpace = WhiteSpace.Normal;
            container.Add(title);
        }

        if (!string.IsNullOrWhiteSpace(block.Text))
        {
            Label text = new Label(block.Text);
            text.enableRichText = true;
            text.style.fontSize = Scale(13f);
            text.style.color = textColor;
            text.style.whiteSpace = WhiteSpace.Normal;
            container.Add(text);
        }

        if (block.Blocks.Count > 0)
        {
            VisualElement nestedContent = new VisualElement();
            if (!string.IsNullOrWhiteSpace(block.Title) || !string.IsNullOrWhiteSpace(block.Text))
            {
                nestedContent.style.marginTop = Scale(8f);
            }

            container.Add(nestedContent);
            RenderBlocks(nestedContent, block.Blocks);
        }
    }

    private static void AddCollapsibleSection(VisualElement parent, MessageBoardBlock block)
    {
        bool expanded = block.DefaultExpanded;
        string accentTone = ResolveTone(block.Tone, MessageBoardTones.Info);
        Color accentColor = GetToneAccentColor(accentTone);
        Color titleColor = HasTone(block.TitleTone) ? GetToneAccentColor(block.TitleTone) : accentColor;

        VisualElement container = CreateCardContainer();
        container.style.marginBottom = Scale(12f);
        parent.Add(container);

        Button headerButton = new Button();
        Color headerBackgroundColor = BgButton;
        Color headerBorderColor = accentColor;
        Color headerTextColor = titleColor;
        Color hoverHeaderBackgroundColor = BlendToward(headerBackgroundColor, accentColor, 0.45f);
        Color hoverHeaderBorderColor = BlendToward(headerBorderColor, TextPrimary, 0.34f);
        Color hoverHeaderTextColor = TextPrimary;
        headerButton.style.minHeight = Scale(40f);
        ApplyButtonColors(headerButton, headerBackgroundColor, headerTextColor, headerBorderColor);
        headerButton.style.fontSize = Scale(13f);
        headerButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        headerButton.style.unityTextAlign = TextAnchor.MiddleLeft;
        headerButton.style.paddingLeft = Scale(12f);
        headerButton.style.paddingRight = Scale(12f);
        headerButton.style.paddingTop = Scale(6f);
        headerButton.style.paddingBottom = Scale(6f);
        headerButton.style.borderTopLeftRadius = 4f;
        headerButton.style.borderTopRightRadius = 4f;
        headerButton.style.borderBottomLeftRadius = 4f;
        headerButton.style.borderBottomRightRadius = 4f;
        headerButton.style.borderTopWidth = 1f;
        headerButton.style.borderRightWidth = 1f;
        headerButton.style.borderBottomWidth = 1f;
        headerButton.style.borderLeftWidth = 1f;
        RegisterHoverCallbacks(
            headerButton,
            () => ApplyButtonColors(headerButton, hoverHeaderBackgroundColor, hoverHeaderTextColor, hoverHeaderBorderColor),
            () => ApplyButtonColors(headerButton, headerBackgroundColor, headerTextColor, headerBorderColor));
        container.Add(headerButton);

        VisualElement body = new VisualElement();
        body.style.marginTop = Scale(8f);
        body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        container.Add(body);

        void RefreshHeaderText()
        {
            string title = string.IsNullOrWhiteSpace(block.Title) ? "Section" : block.Title;
            headerButton.text = $"{(expanded ? "[-]" : "[+]")} {title}";
        }

        headerButton.clicked += () =>
        {
            expanded = !expanded;
            body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshHeaderText();
        };

        RefreshHeaderText();

        if (block.Blocks.Count == 0)
        {
            AddParagraph(
                body,
                new MessageBoardBlock
                {
                    Text = "This section is empty.",
                    TextTone = block.TextTone
                });
            return;
        }

        RenderBlocks(body, block.Blocks);
    }

    private static void AddMinorHeading(VisualElement parent, string text, Color color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Label label = new Label(text);
        label.enableRichText = true;
        label.style.fontSize = Scale(15f);
        label.style.color = color;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = Scale(2f);
        label.style.marginBottom = Scale(8f);
        label.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(label);
    }

    private static Button CreateHeaderButton(string text, Action onClick)
    {
        Button button = new Button(onClick)
        {
            text = text
        };
        Color backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        Color borderColor = BorderGray;
        Color hoverBackgroundColor = BlendToward(backgroundColor, AccentBlue, 0.45f);
        Color hoverBorderColor = BlendToward(BorderGray, AccentBlue, 0.6f);

        ApplyButtonColors(button, backgroundColor, TextPrimary, borderColor);
        button.style.fontSize = Scale(16f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.width = Scale(36f);
        button.style.height = Scale(36f);
        button.style.paddingLeft = Scale(8f);
        button.style.paddingRight = Scale(8f);
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopLeftRadius = 0f;
        button.style.borderTopRightRadius = 0f;
        button.style.borderBottomLeftRadius = 0f;
        button.style.borderBottomRightRadius = 0f;
        RegisterHoverCallbacks(
            button,
            () => ApplyButtonColors(button, hoverBackgroundColor, TextPrimary, hoverBorderColor),
            () => ApplyButtonColors(button, backgroundColor, TextPrimary, borderColor));
        return button;
    }

    private static VisualElement CreateCardContainer()
    {
        VisualElement container = new VisualElement();
        container.style.backgroundColor = BgRow;
        container.style.paddingLeft = Scale(14f);
        container.style.paddingRight = Scale(14f);
        container.style.paddingTop = Scale(12f);
        container.style.paddingBottom = Scale(12f);
        container.style.borderTopLeftRadius = 4f;
        container.style.borderTopRightRadius = 4f;
        container.style.borderBottomLeftRadius = 4f;
        container.style.borderBottomRightRadius = 4f;
        container.style.borderTopWidth = 1f;
        container.style.borderRightWidth = 1f;
        container.style.borderBottomWidth = 1f;
        container.style.borderLeftWidth = 1f;
        container.style.borderTopColor = BorderDark;
        container.style.borderRightColor = BorderDark;
        container.style.borderBottomColor = BorderDark;
        container.style.borderLeftColor = BorderDark;
        return container;
    }

    private static void StyleTabButton(Button button, bool active)
    {
        bool hovered = HoveredTabButtons.Contains(button);
        Color backgroundColor = active
            ? hovered ? BlendToward(BgRow, AccentBlue, 0.42f) : BgRow
            : hovered ? BlendToward(BgButton, AccentBlue, 0.34f) : BgButton;
        Color textColor = active || hovered ? TextPrimary : TextSecondary;
        Color sideAccentColor = active || hovered ? AccentBlue : BorderGray;
        Color borderColor = hovered ? BlendToward(BorderGray, AccentBlue, 0.55f) : BorderGray;

        button.style.minHeight = Scale(40f);
        button.style.marginRight = 0f;
        button.style.marginBottom = Scale(8f);
        button.style.paddingLeft = Scale(14f);
        button.style.paddingRight = Scale(14f);
        button.style.paddingTop = Scale(6f);
        button.style.paddingBottom = Scale(6f);
        button.style.backgroundColor = backgroundColor;
        button.style.color = textColor;
        button.style.fontSize = Scale(13f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderLeftWidth = 3f;
        button.style.borderBottomWidth = 1f;
        button.style.borderTopColor = borderColor;
        button.style.borderRightColor = borderColor;
        button.style.borderLeftColor = sideAccentColor;
        button.style.borderBottomColor = borderColor;
        button.style.borderTopLeftRadius = 4f;
        button.style.borderTopRightRadius = 4f;
        button.style.borderBottomLeftRadius = 4f;
        button.style.borderBottomRightRadius = 4f;
    }

    private static bool IsTabButtonActive(Button button)
    {
        string? tabId = button.userData as string;
        return !string.IsNullOrWhiteSpace(tabId)
            && string.Equals(tabId, currentTabId, StringComparison.OrdinalIgnoreCase);
    }

    private static float Scale(float value)
    {
        return value * UiScale;
    }

    private static bool HasTone(string? tone)
    {
        return !string.IsNullOrWhiteSpace(tone);
    }

    private static string ResolveTone(string? tone, string fallbackTone)
    {
        return HasTone(tone) ? tone!.Trim() : fallbackTone;
    }

    private static Color ResolveTitleColor(MessageBoardBlock block, Color defaultColor)
    {
        if (HasTone(block.TitleTone))
        {
            return GetToneAccentColor(block.TitleTone);
        }

        if (HasTone(block.Tone))
        {
            return GetToneAccentColor(block.Tone);
        }

        return defaultColor;
    }

    private static Color ResolveTextColor(MessageBoardBlock block, Color defaultColor, bool inheritBlockTone = false)
    {
        if (HasTone(block.TextTone))
        {
            return GetToneAccentColor(block.TextTone);
        }

        if (inheritBlockTone && HasTone(block.Tone))
        {
            return GetToneAccentColor(block.Tone);
        }

        return defaultColor;
    }

    private static Color GetToneAccentColor(string tone)
    {
        if (TryParseHexTone(tone, out Color customColor))
        {
            return customColor;
        }

        return NormalizeTone(tone) switch
        {
            MessageBoardTones.Success => AccentGreen,
            MessageBoardTones.Warning => AccentOrange,
            MessageBoardTones.Error => AccentRed,
            MessageBoardTones.Neutral => AccentNeutral,
            _ => AccentBlue
        };
    }

    private static Color GetContrastingTextColor(Color backgroundColor)
    {
        float luminance = (0.299f * backgroundColor.r) + (0.587f * backgroundColor.g) + (0.114f * backgroundColor.b);
        return luminance >= 0.62f ? BgHeader : TextPrimary;
    }

    private static Color BlendToward(Color source, Color target, float amount)
    {
        Color blended = Color.Lerp(source, target, Mathf.Clamp01(amount));
        blended.a = 1f;
        return blended;
    }

    private static void RegisterHoverCallbacks(VisualElement element, Action onEnter, Action onLeave)
    {
        element.RegisterCallback<PointerEnterEvent>(_ => onEnter());
        element.RegisterCallback<PointerLeaveEvent>(_ => onLeave());
        element.RegisterCallback<MouseEnterEvent>(_ => onEnter());
        element.RegisterCallback<MouseLeaveEvent>(_ => onLeave());
    }

    private static void ApplyButtonColors(Button button, Color backgroundColor, Color textColor, Color borderColor)
    {
        button.style.backgroundColor = backgroundColor;
        button.style.color = textColor;
        button.style.borderTopColor = borderColor;
        button.style.borderRightColor = borderColor;
        button.style.borderBottomColor = borderColor;
        button.style.borderLeftColor = borderColor;
    }

    private static (Color AccentColor, Color SurfaceColor) GetToneSurface(string tone)
    {
        if (TryParseHexTone(tone, out Color customColor))
        {
            Color opaqueAccent = new Color(customColor.r, customColor.g, customColor.b, 1f);
            Color surfaceColor = Color.Lerp(BgDark, opaqueAccent, 0.16f);
            surfaceColor.a = 1f;
            return (customColor, surfaceColor);
        }

        return NormalizeTone(tone) switch
        {
            MessageBoardTones.Success => (AccentGreen, new Color(0.12f, 0.21f, 0.14f, 1f)),
            MessageBoardTones.Warning => (AccentOrange, new Color(0.24f, 0.17f, 0.08f, 1f)),
            MessageBoardTones.Error => (AccentRed, new Color(0.23f, 0.10f, 0.10f, 1f)),
            MessageBoardTones.Neutral => (AccentNeutral, new Color(0.17f, 0.17f, 0.19f, 1f)),
            _ => (AccentBlue, new Color(0.11f, 0.17f, 0.23f, 1f))
        };
    }

    private static bool TryParseHexTone(string? tone, out Color color)
    {
        string value = (tone ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("#", StringComparison.Ordinal))
        {
            color = default;
            return false;
        }

        return ColorUtility.TryParseHtmlString(value, out color);
    }

    private static string NormalizeTone(string? tone)
    {
        return (tone ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            MessageBoardTones.Success => MessageBoardTones.Success,
            MessageBoardTones.Warning => MessageBoardTones.Warning,
            MessageBoardTones.Error => MessageBoardTones.Error,
            MessageBoardTones.Neutral => MessageBoardTones.Neutral,
            _ => MessageBoardTones.Info
        };
    }

    private static bool AnyGameViewRequiresMouse()
    {
        try
        {
            UIManager? uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null || ViewsField == null)
            {
                return false;
            }

            if (!(ViewsField.GetValue(uiManager) is List<UIView> views))
            {
                return false;
            }

            foreach (UIView view in views)
            {
                if ((view.VisibilityRequiresMouse && view.IsVisible) || (view.FocusRequiresMouse && view.IsFocused))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static void SetGameInputSuppressed(bool suppressed)
    {
        try
        {
            if (suppressed)
            {
                UIChat? chat = MonoBehaviourSingleton<UIManager>.Instance?.Chat;
                if (chat != null)
                {
                    GlobalStateManager.SetUIState(new Dictionary<string, object>
                    {
                        { "interactingViews", new List<UIView> { chat } }
                    });
                }

                return;
            }

            UIManager? uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null || ViewsField == null)
            {
                return;
            }

            List<UIView> interactingViews = new();
            if (ViewsField.GetValue(uiManager) is List<UIView> views)
            {
                foreach (UIView view in views)
                {
                    if ((view.VisibilityIsInteractive && view.IsVisible) || (view.FocusIsInteractive && view.IsFocused))
                    {
                        interactingViews.Add(view);
                    }
                }
            }

            GlobalStateManager.SetUIState(new Dictionary<string, object> { { "interactingViews", interactingViews } });
        }
        catch
        {
        }
    }

    private static void ResetLocalPlayerInputs()
    {
        try
        {
            Player? localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
            if (localPlayer?.PlayerInput != null)
            {
                localPlayer.PlayerInput.ResetInputs(localPlayer.Handedness.Value);
            }
        }
        catch
        {
        }
    }

    private static VisualElement? GetRoot()
    {
        return MonoBehaviourSingleton<UIManager>.Instance?.RootVisualElement;
    }
}
