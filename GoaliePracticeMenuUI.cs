using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal static class GoaliePracticeMenuUI
{
    private static readonly Color AccentBlue = new Color(0.4f, 0.7f, 1f);
    private static readonly Color ActiveGreen = new Color(0.3f, 0.8f, 0.4f);
    private static readonly Color WarningOrange = new Color(0.88f, 0.62f, 0.18f);
    private static readonly Color ErrorRed = new Color(0.9f, 0.2f, 0.2f);
    private static readonly Color BgDark = new Color(0.12f, 0.12f, 0.12f, 0.97f);
    private static readonly Color BgHeader = new Color(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color BgRow = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color BgButton = new Color(0.2f, 0.2f, 0.2f, 1f);
    private static readonly Color BgButtonDisabled = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color BorderGray = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color BorderDark = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color TextPrimary = Color.white;
    private static readonly Color TextSecondary = new Color(0.67f, 0.67f, 0.67f, 1f);
    private static readonly Color ToastHintGrey = new Color(0.45f, 0.45f, 0.45f, 1f);

    private static readonly FieldInfo ViewsField = typeof(UIManager).GetField("views", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool isSetup;
    private static bool isVisible;
    private static bool suppressControlEvents;
    private static int hideFrame = -1;

    private static VisualElement? overlay;
    private static VisualElement? panel;
    private static ScrollView? scrollView;
    private static VisualElement? contentRoot;
    private static VisualElement? footer;
    private static VisualElement? tooltip;
    private static Label? tooltipLabel;
    private static VisualElement? openHint;
    private static Label? statusLabel;
    private static Button? toggleSessionButton;
    private static Button? resetDefaultsButton;

    private static readonly Dictionary<string, Button> ShotSelectionButtons = new Dictionary<string, Button>();
    private static readonly Dictionary<string, Button> ShotDistanceButtons = new Dictionary<string, Button>();
    private static readonly Dictionary<string, Button> ShotAreaButtons = new Dictionary<string, Button>();
    private static readonly Dictionary<string, Button> ConstrainPassButtons = new Dictionary<string, Button>();

    private static Slider? passCountSlider;
    private static Slider? passLateralRangeSlider;
    private static Slider? minShotSpeedSlider;
    private static Slider? maxShotSpeedSlider;
    private static Slider? maxShotReleaseHeightSlider;
    private static Slider? minTimeBetweenShotsSlider;
    private static Slider? maxTimeBetweenShotsSlider;

    private static GoaliePracticeMenuProtocol.State? currentState;

    public static bool IsVisible => isVisible;

    public static void EnsureSetup()
    {
        if (isSetup && overlay?.panel != null && openHint?.panel != null)
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
            name = "GoaliePracticeMenuOverlay",
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
            name = "GoaliePracticeMenuPanel"
        };
        panel.style.width = Length.Percent(46f);
        panel.style.minWidth = 700f;
        panel.style.maxWidth = 980f;
        panel.style.height = StyleKeyword.Auto;
        panel.style.minHeight = 0f;
        panel.style.maxHeight = Length.Percent(87f);
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

        tooltip = new VisualElement
        {
            name = "GoaliePracticeMenuTooltip",
            pickingMode = PickingMode.Ignore
        };
        tooltip.style.position = Position.Absolute;
        tooltip.style.display = DisplayStyle.None;
        tooltip.style.backgroundColor = new Color(0.07f, 0.07f, 0.07f, 0.96f);
        tooltip.style.borderTopWidth = 1f;
        tooltip.style.borderRightWidth = 1f;
        tooltip.style.borderBottomWidth = 1f;
        tooltip.style.borderLeftWidth = 1f;
        tooltip.style.borderTopColor = AccentBlue;
        tooltip.style.borderRightColor = AccentBlue;
        tooltip.style.borderBottomColor = AccentBlue;
        tooltip.style.borderLeftColor = AccentBlue;
        tooltip.style.paddingTop = 8f;
        tooltip.style.paddingBottom = 8f;
        tooltip.style.paddingLeft = 10f;
        tooltip.style.paddingRight = 10f;
        tooltip.style.maxWidth = 320f;
        tooltip.style.borderTopLeftRadius = 4f;
        tooltip.style.borderTopRightRadius = 4f;
        tooltip.style.borderBottomLeftRadius = 4f;
        tooltip.style.borderBottomRightRadius = 4f;
        tooltipLabel = new Label();
        tooltipLabel.style.whiteSpace = WhiteSpace.Normal;
        tooltipLabel.style.color = TextPrimary;
        tooltipLabel.style.fontSize = 12f;
        tooltip.Add(tooltipLabel);
        root.Add(tooltip);

        openHint = new VisualElement
        {
            name = "GoaliePracticeMenuHint",
            pickingMode = PickingMode.Ignore
        };
        openHint.style.position = Position.Absolute;
        openHint.style.left = 16f;
        openHint.style.bottom = 74f;
        openHint.style.display = DisplayStyle.None;

        Label hintLabel = new Label("F6 - Goalie Prac Menu");
        hintLabel.style.color = ToastHintGrey;
        hintLabel.style.fontSize = 18f;
        openHint.Add(hintLabel);
        root.Add(openHint);

        isSetup = true;
        if (currentState != null)
        {
            ApplyServerState(currentState);
        }
    }

    public static void Show()
    {
        EnsureSetup();
        if (overlay == null)
        {
            return;
        }

        overlay.style.display = DisplayStyle.Flex;
        isVisible = true;
        UpdateOpenHint(false);
        SetGameInputSuppressed(true);
        GlobalStateManager.SetUIState(new Dictionary<string, object> { { "isMouseRequired", true } });
        ResetLocalPlayerInputs();
        if (currentState != null)
        {
            ApplyServerState(currentState);
        }
    }

    public static void Hide()
    {
        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.None;
        }

        if (tooltip != null)
        {
            tooltip.style.display = DisplayStyle.None;
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

    public static void UpdateOpenHint(bool shouldShow)
    {
        EnsureSetup();
        if (openHint == null)
        {
            return;
        }

        openHint.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public static void ApplyServerState(GoaliePracticeMenuProtocol.State state)
    {
        currentState = state;
        EnsureSetup();
        if (!isSetup)
        {
            return;
        }

        ApplyStatus(state.StatusKind, state.StatusMessage);

        suppressControlEvents = true;
        ApplySegmentSelection(ShotSelectionButtons, NormalizeValue(state.ShotSelection, "all"));
        ApplySegmentSelection(ShotDistanceButtons, NormalizeValue(state.ShotDistance, "all"));
        ApplySegmentSelection(ShotAreaButtons, NormalizeValue(state.ShotArea, "all"));
        ApplySegmentSelection(ConstrainPassButtons, state.ConstrainPasses ? "on" : "off");

        passCountSlider?.SetValueWithoutNotify(Mathf.Clamp(state.PassCount, 0, GoaliePracticeMenuProtocol.MaxPassCount));
        passLateralRangeSlider?.SetValueWithoutNotify(ClampStep(state.PassLateralRange, 0f, GoaliePracticeMenuProtocol.MaxPassLateralRange, 0.1f));
        minShotSpeedSlider?.SetValueWithoutNotify(ClampStep(state.MinShotSpeedKph, 0f, GoaliePracticeMenuProtocol.MaxShotSpeedKph, 1f));
        maxShotSpeedSlider?.SetValueWithoutNotify(ClampStep(state.MaxShotSpeedKph, 0f, GoaliePracticeMenuProtocol.MaxShotSpeedKph, 1f));
        maxShotReleaseHeightSlider?.SetValueWithoutNotify(ClampStep(state.MaxShotReleaseHeight, 0f, GoaliePracticeMenuProtocol.MaxShotReleaseHeight, 0.1f));
        minTimeBetweenShotsSlider?.SetValueWithoutNotify(ClampStep(state.MinTimeBetweenShots, 0f, GoaliePracticeMenuProtocol.MaxTimeBetweenShots, 0.1f));
        maxTimeBetweenShotsSlider?.SetValueWithoutNotify(ClampStep(state.MaxTimeBetweenShots, 0f, GoaliePracticeMenuProtocol.MaxTimeBetweenShots, 0.1f));
        suppressControlEvents = false;

        SetInteractiveState(state.CanEditSettings);

        if (toggleSessionButton != null)
        {
            toggleSessionButton.text = state.IsSessionActive ? "Stop Practice" : "Start Practice";
            toggleSessionButton.SetEnabled(state.IsSessionActive || state.CanToggleSession);
            toggleSessionButton.style.backgroundColor = state.IsSessionActive
                ? new StyleColor(new Color(0.45f, 0.18f, 0.18f))
                : new StyleColor(AccentBlue);
        }

        if (resetDefaultsButton != null)
        {
            resetDefaultsButton.SetEnabled(state.CanEditSettings);
        }
    }

    public static void ShowLoadingStatus(string message)
    {
        EnsureSetup();
        ApplyStatus("info", message);
        SetInteractiveState(false);
        if (toggleSessionButton != null)
        {
            toggleSessionButton.SetEnabled(false);
        }

        if (resetDefaultsButton != null)
        {
            resetDefaultsButton.SetEnabled(false);
        }
    }

    public static void ShowOfflineStatus(string message)
    {
        EnsureSetup();
        ApplyStatus("error", message);
        SetInteractiveState(false);
        if (toggleSessionButton != null)
        {
            toggleSessionButton.SetEnabled(false);
        }

        if (resetDefaultsButton != null)
        {
            resetDefaultsButton.SetEnabled(false);
        }
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
        tooltip?.RemoveFromHierarchy();
        openHint?.RemoveFromHierarchy();

        overlay = null;
        panel = null;
        scrollView = null;
        contentRoot = null;
        footer = null;
        tooltip = null;
        tooltipLabel = null;
        openHint = null;
        statusLabel = null;
        toggleSessionButton = null;
        resetDefaultsButton = null;

        ShotSelectionButtons.Clear();
        ShotDistanceButtons.Clear();
        ShotAreaButtons.Clear();
        ConstrainPassButtons.Clear();

        passCountSlider = null;
        passLateralRangeSlider = null;
        minShotSpeedSlider = null;
        maxShotSpeedSlider = null;
        maxShotReleaseHeightSlider = null;
        minTimeBetweenShotsSlider = null;
        maxTimeBetweenShotsSlider = null;

        isSetup = false;
        isVisible = false;
        suppressControlEvents = false;
        hideFrame = -1;
    }

    private static void BuildHeader(VisualElement parent)
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 16f;
        header.style.paddingRight = 12f;
        header.style.paddingTop = 12f;
        header.style.paddingBottom = 12f;
        header.style.backgroundColor = BgHeader;
        parent.Add(header);

        VisualElement titleWrap = new VisualElement();
        titleWrap.style.flexDirection = FlexDirection.Column;
        titleWrap.style.alignItems = Align.FlexStart;
        header.Add(titleWrap);

        Label title = new Label("Goalie Practice");
        title.style.fontSize = 22f;
        title.style.color = TextPrimary;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleWrap.Add(title);

        Button closeButton = CreateHeaderButton("X", Hide);
        header.Add(closeButton);

        VisualElement accentBar = new VisualElement();
        accentBar.style.height = 2f;
        accentBar.style.backgroundColor = AccentBlue;
        accentBar.style.marginLeft = 16f;
        accentBar.style.marginRight = 16f;
        parent.Add(accentBar);
    }

    private static void BuildBody(VisualElement parent)
    {
        scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.style.flexGrow = 1f;
        scrollView.style.flexShrink = 1f;
        scrollView.style.minHeight = 0f;
        parent.Add(scrollView);

        contentRoot = scrollView.contentContainer;
        contentRoot.style.paddingLeft = 16f;
        contentRoot.style.paddingRight = 20f;
        contentRoot.style.paddingTop = 8f;
        contentRoot.style.paddingBottom = 0f;

        statusLabel = new Label("Loading practice state...");
        statusLabel.style.fontSize = 12f;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.marginBottom = 10f;
        contentRoot.Add(statusLabel);

        AddSectionTitle(contentRoot, "Shot Setup");
        BuildSegmentedRow(
            contentRoot,
            "Shot Selection",
            "Controls which shot heights are chosen. All mixes everything, while Low, Middle, and High limit the finishing shot to those bands.",
            new[] { ("All", "all"), ("Low", "low"), ("Middle", "mid"), ("High", "high") },
            ShotSelectionButtons,
            value => GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionSetShotSelection, value));

        BuildSegmentedRow(
            contentRoot,
            "Shot Distance",
            "Restricts where the shooting sequence ends up. Short keeps the play closer to the goal, Long pushes it farther out.",
            new[] { ("All", "all"), ("Short", "short"), ("Long", "long") },
            ShotDistanceButtons,
            value => GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionSetShotDistance, value));

        BuildSegmentedRow(
            contentRoot,
            "Shot Area",
            "Limits the puck to the left side, centre lane, or right side before the final shot. All allows the full width.",
            new[] { ("All", "all"), ("Left", "left"), ("Centre", "center"), ("Right", "right") },
            ShotAreaButtons,
            value => GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionSetShotArea, value));

        BuildSegmentedRow(
            contentRoot,
            "Constrain Passes",
            "Matches the passes to your distance and area filters when turned on. Turning it off only constrains the spawn and finishing shot.",
            new[] { ("On", "on"), ("Off", "off") },
            ConstrainPassButtons,
            value => GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionSetConstrainPasses, value));

        BuildSliderRow(
            contentRoot,
            "Pass Number",
            "Sets the random pass count range from 0 up to this number before each shot.",
            0f,
            GoaliePracticeMenuProtocol.MaxPassCount,
            1f,
            string.Empty,
            slider => passCountSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetPassCount, GoaliePracticeMenuClient.FormatFloat(value, 1f)));

        BuildSliderRow(
            contentRoot,
            "Pass Lateral Range",
            "Controls how wide the pass pattern can spread from side to side before the shot.",
            0f,
            GoaliePracticeMenuProtocol.MaxPassLateralRange,
            0.1f,
            string.Empty,
            slider => passLateralRangeSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetPassLateralRange, GoaliePracticeMenuClient.FormatFloat(value, 0.1f)));

        AddSectionTitle(contentRoot, "Puck Pace");
        BuildSliderRow(
            contentRoot,
            "Min Shot Speed",
            "Sets the slowest finishing shot speed. The server will keep this below the max speed.",
            0f,
            GoaliePracticeMenuProtocol.MaxShotSpeedKph,
            1f,
            "kph",
            slider => minShotSpeedSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetMinShotSpeedKph, GoaliePracticeMenuClient.FormatFloat(value, 1f)));

        BuildSliderRow(
            contentRoot,
            "Max Shot Speed",
            "Sets the fastest finishing shot speed. The server will keep this above the min speed.",
            0f,
            GoaliePracticeMenuProtocol.MaxShotSpeedKph,
            1f,
            "kph",
            slider => maxShotSpeedSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetMaxShotSpeedKph, GoaliePracticeMenuClient.FormatFloat(value, 1f)));

        BuildSliderRow(
            contentRoot,
            "Max Shot Release Height",
            "If the puck is still above this height when the shot is about to release, that puck gets thrown away and the cycle restarts.",
            0f,
            GoaliePracticeMenuProtocol.MaxShotReleaseHeight,
            0.1f,
            string.Empty,
            slider => maxShotReleaseHeightSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetMaxShotReleaseHeight, GoaliePracticeMenuClient.FormatFloat(value, 0.1f)));

        AddSectionTitle(contentRoot, "Timing");
        BuildSliderRow(
            contentRoot,
            "Min Time Between Shots",
            "Controls the shortest wait between shot cycles. The server will keep this below the max delay.",
            0f,
            GoaliePracticeMenuProtocol.MaxTimeBetweenShots,
            0.1f,
            "s",
            slider => minTimeBetweenShotsSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetMinTimeBetweenShots, GoaliePracticeMenuClient.FormatFloat(value, 0.1f)));

        BuildSliderRow(
            contentRoot,
            "Max Time Between Shots",
            "Controls the longest wait between shot cycles. The server will keep this above the min delay.",
            0f,
            GoaliePracticeMenuProtocol.MaxTimeBetweenShots,
            0.1f,
            "s",
            slider => maxTimeBetweenShotsSlider = slider,
            value => GoaliePracticeMenuClient.SendDebouncedRequest(GoaliePracticeMenuProtocol.ActionSetMaxTimeBetweenShots, GoaliePracticeMenuClient.FormatFloat(value, 0.1f)));
    }

    private static void BuildFooter(VisualElement parent)
    {
        footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.alignItems = Align.Center;
        footer.style.paddingLeft = 16f;
        footer.style.paddingRight = 16f;
        footer.style.paddingTop = 6f;
        footer.style.paddingBottom = 10f;
        footer.style.backgroundColor = BgHeader;
        parent.Add(footer);

        resetDefaultsButton = CreateFooterButton("Reset Defaults", BgButton, () =>
        {
            if (!suppressControlEvents)
            {
                GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionResetDefaults, string.Empty);
            }
        });
        resetDefaultsButton.style.marginRight = 8f;
        footer.Add(resetDefaultsButton);

        toggleSessionButton = CreateFooterButton("Start Practice", AccentBlue, () =>
        {
            if (!suppressControlEvents)
            {
                GoaliePracticeMenuClient.SendImmediateRequest(GoaliePracticeMenuProtocol.ActionToggleSession, string.Empty);
            }
        });
        footer.Add(toggleSessionButton);
    }

    private static void BuildSegmentedRow(
        VisualElement parent,
        string title,
        string tooltipText,
        IEnumerable<(string Label, string Value)> options,
        Dictionary<string, Button> buttonMap,
        Action<string> onSelected)
    {
        VisualElement controls = CreateRow(parent, title, tooltipText);
        buttonMap.Clear();

        foreach ((string label, string value) in options)
        {
            string optionValue = value;
            Button button = new Button(() =>
            {
                if (!suppressControlEvents)
                {
                    onSelected(optionValue);
                }
            })
            {
                text = label
            };

            button.style.minWidth = 76f;
            button.style.marginRight = 6f;
            button.style.marginBottom = 4f;
            StyleOptionButton(button, active: false);
            controls.Add(button);
            buttonMap[optionValue] = button;
        }
    }

    private static void BuildSliderRow(
        VisualElement parent,
        string title,
        string tooltipText,
        float minValue,
        float maxValue,
        float step,
        string unitLabel,
        Action<Slider> assignSlider,
        Action<float> onChanged)
    {
        VisualElement controls = CreateRow(parent, title, tooltipText);

        VisualElement sliderWrap = new VisualElement();
        sliderWrap.style.flexDirection = FlexDirection.Row;
        sliderWrap.style.alignItems = Align.Center;
        sliderWrap.style.flexGrow = 1f;
        sliderWrap.style.minWidth = 300f;
        controls.Add(sliderWrap);

        Slider slider = new Slider
        {
            lowValue = minValue,
            highValue = maxValue,
            direction = SliderDirection.Horizontal,
            showInputField = true
        };
        slider.style.flexGrow = 1f;
        slider.style.minWidth = 240f;
        slider.style.maxWidth = 360f;
        slider.style.marginRight = 8f;
        slider.RegisterCallback<AttachToPanelEvent>(_ => StyleSlider(slider));
        slider.RegisterValueChangedCallback(evt =>
        {
            if (suppressControlEvents)
            {
                return;
            }

            float snapped = ClampStep(evt.newValue, minValue, maxValue, step);
            slider.SetValueWithoutNotify(snapped);
            onChanged(snapped);
        });
        sliderWrap.Add(slider);
        assignSlider(slider);

        if (!string.IsNullOrEmpty(unitLabel))
        {
            Label unit = new Label(unitLabel);
            unit.style.minWidth = 34f;
            unit.style.color = TextSecondary;
            unit.style.fontSize = 12f;
            sliderWrap.Add(unit);
        }
    }

    private static VisualElement CreateRow(VisualElement parent, string title, string tooltipText)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 6f;
        row.style.paddingLeft = 10f;
        row.style.paddingRight = 10f;
        row.style.paddingTop = 8f;
        row.style.paddingBottom = 8f;
        row.style.backgroundColor = BgRow;
        row.style.borderTopLeftRadius = 4f;
        row.style.borderTopRightRadius = 4f;
        row.style.borderBottomLeftRadius = 4f;
        row.style.borderBottomRightRadius = 4f;
        row.style.borderTopWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderBottomWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderTopColor = BorderDark;
        row.style.borderRightColor = BorderDark;
        row.style.borderBottomColor = BorderDark;
        row.style.borderLeftColor = BorderDark;
        parent.Add(row);

        VisualElement left = new VisualElement();
        left.style.flexDirection = FlexDirection.Row;
        left.style.alignItems = Align.Center;
        left.style.minWidth = 225f;
        left.style.marginRight = 12f;
        row.Add(left);

        Label label = new Label(title);
        label.style.color = TextPrimary;
        label.style.fontSize = 14f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginRight = 8f;
        left.Add(label);

        Label helpBubble = new Label("?");
        helpBubble.style.width = 18f;
        helpBubble.style.height = 18f;
        helpBubble.style.unityTextAlign = TextAnchor.MiddleCenter;
        helpBubble.style.fontSize = 11f;
        helpBubble.style.color = TextPrimary;
        helpBubble.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        helpBubble.style.borderTopLeftRadius = 9f;
        helpBubble.style.borderTopRightRadius = 9f;
        helpBubble.style.borderBottomLeftRadius = 9f;
        helpBubble.style.borderBottomRightRadius = 9f;
        helpBubble.style.borderTopWidth = 1f;
        helpBubble.style.borderRightWidth = 1f;
        helpBubble.style.borderBottomWidth = 1f;
        helpBubble.style.borderLeftWidth = 1f;
        helpBubble.style.borderTopColor = AccentBlue;
        helpBubble.style.borderRightColor = AccentBlue;
        helpBubble.style.borderBottomColor = AccentBlue;
        helpBubble.style.borderLeftColor = AccentBlue;
        helpBubble.pickingMode = PickingMode.Position;
        AttachTooltip(helpBubble, tooltipText);
        left.Add(helpBubble);

        VisualElement right = new VisualElement();
        right.style.flexDirection = FlexDirection.Row;
        right.style.flexWrap = Wrap.Wrap;
        right.style.alignItems = Align.Center;
        right.style.flexGrow = 1f;
        row.Add(right);

        return right;
    }

    private static void AddSectionTitle(VisualElement parent, string title)
    {
        Label sectionTitle = new Label(title);
        sectionTitle.style.color = TextPrimary;
        sectionTitle.style.fontSize = 16f;
        sectionTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        sectionTitle.style.marginTop = 4f;
        sectionTitle.style.marginBottom = 8f;
        parent.Add(sectionTitle);
    }

    private static Button CreateHeaderButton(string text, Action onClick)
    {
        Button button = new Button(onClick)
        {
            text = text
        };
        button.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        button.style.color = TextPrimary;
        button.style.fontSize = 16f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.width = 34f;
        button.style.height = 34f;
        button.style.paddingLeft = 8f;
        button.style.paddingRight = 8f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderTopLeftRadius = 0f;
        button.style.borderTopRightRadius = 0f;
        button.style.borderBottomLeftRadius = 0f;
        button.style.borderBottomRightRadius = 0f;
        return button;
    }

    private static Button CreateFooterButton(string text, Color backgroundColor, Action onClick)
    {
        Button button = new Button(onClick)
        {
            text = text
        };
        button.style.minWidth = 136f;
        button.style.height = 38f;
        button.style.backgroundColor = backgroundColor;
        button.style.color = TextPrimary;
        button.style.fontSize = 14f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.paddingLeft = 12f;
        button.style.paddingRight = 12f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderTopLeftRadius = 4f;
        button.style.borderTopRightRadius = 4f;
        button.style.borderBottomLeftRadius = 4f;
        button.style.borderBottomRightRadius = 4f;
        return button;
    }

    private static void StyleSlider(Slider slider)
    {
        slider.style.fontSize = 12f;
        slider.style.overflow = Overflow.Hidden;

        VisualElement? draggerBorder = slider.Q<VisualElement>(className: "unity-base-slider__dragger-border");
        if (draggerBorder != null)
        {
            draggerBorder.style.backgroundColor = BgRow;
        }

        VisualElement? tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
        if (tracker != null)
        {
            tracker.style.backgroundColor = BgButton;
        }

        VisualElement? input = slider.Q<VisualElement>(className: "unity-base-text-field__input");
        if (input != null)
        {
            input.style.backgroundColor = BgRow;
            input.style.color = TextPrimary;
            input.style.fontSize = 13f;
            input.style.borderTopWidth = 1f;
            input.style.borderRightWidth = 1f;
            input.style.borderBottomWidth = 1f;
            input.style.borderLeftWidth = 1f;
            input.style.borderTopColor = BorderGray;
            input.style.borderRightColor = BorderGray;
            input.style.borderBottomColor = BorderGray;
            input.style.borderLeftColor = BorderGray;
        }
    }

    private static void StyleOptionButton(Button button, bool active)
    {
        button.style.backgroundColor = active ? AccentBlue : BgButton;
        button.style.color = TextPrimary;
        button.style.fontSize = 12f;
        button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        button.style.paddingLeft = 10f;
        button.style.paddingRight = 10f;
        button.style.paddingTop = 5f;
        button.style.paddingBottom = 5f;
        button.style.borderTopLeftRadius = 4f;
        button.style.borderTopRightRadius = 4f;
        button.style.borderBottomLeftRadius = 4f;
        button.style.borderBottomRightRadius = 4f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopColor = active ? AccentBlue : BorderGray;
        button.style.borderRightColor = active ? AccentBlue : BorderGray;
        button.style.borderBottomColor = active ? AccentBlue : BorderGray;
        button.style.borderLeftColor = active ? AccentBlue : BorderGray;
    }

    private static void ApplySegmentSelection(Dictionary<string, Button> buttonMap, string selectedValue)
    {
        foreach (KeyValuePair<string, Button> entry in buttonMap)
        {
            StyleOptionButton(entry.Value, entry.Key == selectedValue);
        }
    }

    private static void SetInteractiveState(bool enabled)
    {
        if (contentRoot != null)
        {
            contentRoot.SetEnabled(enabled);
        }

        foreach (Button button in ShotSelectionButtons.Values)
        {
            button.style.backgroundColor = enabled ? button.style.backgroundColor : new StyleColor(BgButtonDisabled);
        }
    }

    private static void ApplyStatus(string? kind, string? message)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.IsNullOrWhiteSpace(message) ? " " : message;
        switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "success":
                statusLabel.style.color = ActiveGreen;
                break;
            case "warning":
                statusLabel.style.color = WarningOrange;
                break;
            case "error":
                statusLabel.style.color = ErrorRed;
                break;
            default:
                statusLabel.style.color = AccentBlue;
                break;
        }
    }

    private static void AttachTooltip(VisualElement target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        target.RegisterCallback<MouseEnterEvent>(_ =>
        {
            ShowTooltip(text, target.worldBound.center);
        });
        target.RegisterCallback<MouseMoveEvent>(evt =>
        {
            ShowTooltip(text, evt.mousePosition);
        });
        target.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            HideTooltip();
        });
    }

    private static void ShowTooltip(string text, Vector2 anchorPosition)
    {
        if (tooltip == null || tooltipLabel == null)
        {
            return;
        }

        tooltipLabel.text = text;
        tooltip.style.display = DisplayStyle.Flex;
        PositionTooltip(anchorPosition);
        tooltip.BringToFront();
    }

    private static void HideTooltip()
    {
        if (tooltip != null)
        {
            tooltip.style.display = DisplayStyle.None;
        }
    }

    private static void PositionTooltip(Vector2 panelPosition)
    {
        VisualElement? root = GetRoot();
        if (tooltip == null || root == null)
        {
            return;
        }

        float left = panelPosition.x + 18f;
        float top = panelPosition.y + 18f;

        float tooltipWidth = tooltip.resolvedStyle.width > 0f ? tooltip.resolvedStyle.width : 300f;
        float tooltipHeight = tooltip.resolvedStyle.height > 0f ? tooltip.resolvedStyle.height : 110f;
        float maxLeft = Mathf.Max(10f, root.resolvedStyle.width - tooltipWidth - 10f);
        float maxTop = Mathf.Max(10f, root.resolvedStyle.height - tooltipHeight - 10f);

        tooltip.style.left = Mathf.Clamp(left, 10f, maxLeft);
        tooltip.style.top = Mathf.Clamp(top, 10f, maxTop);
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

            List<UIView> interactingViews = new List<UIView>();
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

    private static float ClampStep(float value, float minValue, float maxValue, float step)
    {
        float clamped = Mathf.Clamp(value, minValue, maxValue);
        if (step > 0f)
        {
            clamped = Mathf.Round(clamped / step) * step;
        }

        return clamped;
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
    }
}
