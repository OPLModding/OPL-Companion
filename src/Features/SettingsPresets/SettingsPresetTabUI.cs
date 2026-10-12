using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal static class SettingsPresetTabUI
{
    private const string PresetsTabName = "OPLSettingsPresetsTab";
    private const string PresetsTabContentName = "OPLSettingsPresetsContent";
    private const string NoneChoice = "None";
    private const string ZoneColoursFoldoutKey = "OPLServerBanners.ZoneOverlay.Ui.ZoneColoursOpen";
    private const string ZoneTextFoldoutKey = "OPLServerBanners.ZoneOverlay.Ui.ZoneTextOpen";
    private const float ToggleOuterExtraSize = 8f;
    private const float ToggleShadowPadding = 1f;

    private static readonly Color AccentBlue = new Color(0.42f, 0.68f, 1f);
    private static readonly Color AccentGreen = new Color(0.35f, 0.8f, 0.45f);
    private static readonly Color AccentOrange = new Color(1f, 0.67f, 0.18f);
    private static readonly Color BgRow = new Color(0.25f, 0.25f, 0.25f, 1f);
    private static readonly Color FieldShadowColor = new Color(45f / 255f, 45f / 255f, 45f / 255f, 1f);
    private static readonly Color TextPrimary = Color.white;
    private static readonly Color TextSecondary = new Color(0.82f, 0.82f, 0.82f, 1f);
    private static readonly FieldInfo? SettingsField = typeof(UISettings).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? TabViewField = typeof(UISettings).GetField("tabView", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ZoneColorOption[] ZoneColorOptions =
    {
        new("Z1", "INNER SLOT"),
        new("Z2", "OUTER SLOT"),
        new("Z3", "LEFT FLANK"),
        new("Z4", "RIGHT FLANK"),
        new("Z5", "ABOVE CIRCLES"),
        new("Z6", "BELOW GOAL LINE")
    };

    private static readonly Dictionary<string, string> PresetChoiceToId = new(StringComparer.Ordinal);
    private static readonly List<string> NativeRowClasses = new();
    private static readonly List<string> NativeLabelClasses = new();
    private static readonly List<string> NativeDropdownClasses = new();
    private static readonly List<string> NativeDropdownInputClasses = new();
    private static readonly List<string> NativeKeyBindRowClasses = new();
    private static readonly List<string> NativeKeyBindPathFieldClasses = new();
    private static readonly List<string> NativeKeyBindPathInputClasses = new();
    private static readonly List<string> NativeButtonClasses = new();
    private static readonly List<string> NativeToggleClasses = new();
    private static readonly List<string> NativeToggleInputClasses = new();
    private static readonly List<string> NativeToggleCheckmarkClasses = new();
    private static readonly List<string> NativeSliderClasses = new();
    private static readonly List<string> NativeSliderInputClasses = new();
    private static readonly List<string> NativeSliderTrackerClasses = new();
    private static readonly List<string> NativeSliderDraggerClasses = new();
    private static readonly List<string> NativeSliderDraggerBorderClasses = new();
    private static readonly List<string> NativeSliderTextFieldClasses = new();
    private static readonly List<string> NativeSliderTextInputClasses = new();

    private static VisualElement? contentRoot;
    private static string selectedPresetId = string.Empty;
    private static string statusMessage = string.Empty;
    private static string statusKind = "info";
    private static float? nativeDropdownWidth;
    private static float? nativeDropdownHeight;
    private static float? nativeKeyBindPathWidth;
    private static float? nativeKeyBindPathHeight;
    private static float? nativeToggleInputWidth;
    private static float? nativeToggleInputHeight;
    private static float? nativeDropdownTextFontSize;
    private static FontStyle? nativeDropdownTextFontStyle;
    private static bool nativeStyleProfileCaptured;

    public static bool HasInjectedContent => contentRoot?.panel != null;

    public static void Inject(UISettings uiSettings)
    {
        try
        {
            VisualElement? settings = SettingsField?.GetValue(uiSettings) as VisualElement;
            if (settings == null)
            {
                return;
            }

            CaptureNativeStyleProfile(settings);

            VisualElement? existingContent = settings.Q<VisualElement>(PresetsTabContentName);
            if (existingContent != null)
            {
                if (!ReferenceEquals(contentRoot, existingContent))
                {
                    contentRoot = existingContent;
                    Refresh();
                }

                return;
            }

            TabView? tabView = TabViewField?.GetValue(uiSettings) as TabView ?? settings.Q<TabView>();
            if (tabView == null)
            {
                AddFallbackInlineContent(settings);
                return;
            }

            if (tabView.Q<Tab>(PresetsTabName) != null)
            {
                return;
            }

            Tab tab = new Tab("LOADOUTS")
            {
                name = PresetsTabName
            };
            tab.style.flexGrow = 1f;

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            scrollView.style.minHeight = 0f;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            tab.Add(scrollView);

            contentRoot = scrollView.contentContainer;
            contentRoot.name = PresetsTabContentName;
            contentRoot.style.width = Length.Percent(100f);
            contentRoot.style.minWidth = 0f;
            contentRoot.style.paddingLeft = 0f;
            contentRoot.style.paddingRight = 0f;
            contentRoot.style.paddingTop = 0f;
            contentRoot.style.paddingBottom = 0f;

            tabView.Add(tab);
            Refresh();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to inject Loadouts settings tab: {exception}");
        }
    }

    public static void Refresh(string? message = null, string kind = "info")
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            statusMessage = message ?? string.Empty;
            statusKind = kind;
        }

        if (contentRoot == null)
        {
            return;
        }

        SettingsPresetConfig config = SettingsPresetStore.Config;
        EnsureSelectedPresetExists(config);

        contentRoot.Clear();
        BuildHeader(contentRoot);
        BuildPresetSection(contentRoot, config);
        BuildToggleSection(contentRoot, config);
        BuildZoneOverlaySection(contentRoot);
        BuildCompatibilityFooter(contentRoot);
    }

    public static void SetStatus(string message, string kind = "info")
    {
        statusMessage = message ?? string.Empty;
        statusKind = kind;
        Refresh();
    }

    private static void AddFallbackInlineContent(VisualElement settings)
    {
        ScrollView? scrollView = settings.Q<ScrollView>();
        if (scrollView == null || scrollView.Q<VisualElement>(PresetsTabContentName) != null)
        {
            return;
        }

        VisualElement fallbackRoot = new VisualElement
        {
            name = PresetsTabContentName
        };
        fallbackRoot.style.marginTop = 20f;
        fallbackRoot.style.width = Length.Percent(100f);
        fallbackRoot.style.minWidth = 0f;
        fallbackRoot.style.paddingLeft = 0f;
        fallbackRoot.style.paddingRight = 0f;
        fallbackRoot.style.paddingTop = 0f;
        fallbackRoot.style.paddingBottom = 0f;
        ((VisualElement)scrollView).Add(fallbackRoot);

        contentRoot = fallbackRoot;
        Refresh("Loadouts tab could not be created, so controls were appended below the existing settings.", "warning");
    }

    private static void BuildHeader(VisualElement parent)
    {
        AddSectionTitle(parent, "LOADOUTS");
    }

    private static void BuildPresetSection(VisualElement parent, SettingsPresetConfig config)
    {
        VisualElement nameRow = CreateRow(parent, "LOADOUT NAME");
        TextField nameField = new TextField();
        nameField.value = GetSelectedPreset(config)?.Name ?? GetDefaultPresetName(config);
        StyleFieldWidth(nameField);
        StyleTextField(nameField);
        nameRow.Add(nameField);

        VisualElement selectRow = CreateRow(parent, "SELECTED LOADOUT");
        List<string> presetChoices = BuildPresetChoices(config, includeNone: false);
        DropdownField presetDropdown = CreateStyledDropdown();
        presetDropdown.choices = presetChoices;
        presetDropdown.value = GetPresetChoiceForId(selectedPresetId) ?? presetChoices.FirstOrDefault() ?? NoneChoice;
        presetDropdown.RegisterValueChangedCallback(evt =>
        {
            if (PresetChoiceToId.TryGetValue(evt.newValue, out string id))
            {
                selectedPresetId = id;
                ApplySelectedLoadout();
            }
        });
        selectRow.Add(presetDropdown);

        SettingsPreset? selectedPreset = GetSelectedPreset(config);

        VisualElement saveRow = CreateRow(parent, "SAVE CURRENT");
        saveRow.Add(CreateButton("Save", () =>
        {
            SettingsPreset preset = SettingsPresetRuntime.CaptureCurrentPreset(nameField.value);
            SettingsPresetStore.AddPreset(preset);
            selectedPresetId = preset.Id;
            Refresh($"Saved '{preset.Name}'.", "success");
        }));

        VisualElement updateRow = CreateRow(parent, "UPDATE SELECTED");
        Button updateButton = CreateButton("Update", () =>
        {
            SettingsPreset? preset = GetSelectedPreset(SettingsPresetStore.Config);
            if (preset == null)
            {
                Refresh("Choose a loadout to update.", "warning");
                return;
            }

            SettingsPresetRuntime.OverwritePreset(preset, nameField.value);
            SettingsPresetStore.Save();
            Refresh($"Updated '{preset.Name}'.", "success");
        });
        updateButton.SetEnabled(selectedPreset != null);
        updateRow.Add(updateButton);

        VisualElement deleteRow = CreateRow(parent, "DELETE SELECTED");
        Button deleteButton = CreateButton("Delete", () =>
        {
            SettingsPreset? preset = GetSelectedPreset(SettingsPresetStore.Config);
            if (preset == null)
            {
                Refresh("Choose a loadout to delete.", "warning");
                return;
            }

            string name = preset.Name;
            SettingsPresetStore.DeletePreset(preset.Id);
            selectedPresetId = string.Empty;
            Refresh($"Deleted '{name}'.", "success");
        });
        deleteButton.SetEnabled(selectedPreset != null);
        deleteRow.Add(deleteButton);
    }

    private static void BuildToggleSection(VisualElement parent, SettingsPresetConfig config)
    {
        AddSectionTitle(parent, "LIVE TOGGLE");

        VisualElement keyRow = CreateKeyBindRow(parent, "TOGGLE KEY");
        TextField keyField = CreateKeyBindTextField(
            SettingsPresetClient.IsListeningForToggleKey ? "Press a key..." : SettingsPresetRuntime.GetKeyDisplayName(config.ToggleKeybind),
            () =>
            {
                SettingsPresetClient.StartListeningForToggleKey();
                Refresh("Press any keyboard key for the loadout toggle, or Esc to cancel.", "info");
        });
        keyRow.Add(keyField);

#if WARMUP_OBSTACLES
        VisualElement obstacleEditorRow = CreateKeyBindRow(parent, "OBSTACLE EDITOR");
        TextField obstacleEditorField = CreateKeyBindTextField(
            SettingsPresetClient.IsListeningForWarmupObstacleEditorKey ? "Press a key..." : SettingsPresetRuntime.GetKeyDisplayName(config.WarmupObstacleEditorKeybind),
            () =>
            {
                SettingsPresetClient.StartListeningForWarmupObstacleEditorKey();
                Refresh("Press any keyboard key for the obstacle editor, or Esc to cancel.", "info");
            });
        obstacleEditorRow.Add(obstacleEditorField);
#endif

        List<string> choicesWithNone = BuildPresetChoices(config, includeNone: true);

        VisualElement presetARow = CreateRow(parent, "TOGGLE LOADOUT A");
        DropdownField presetADropdown = CreatePresetSelector(choicesWithNone, config.TogglePresetAId, id =>
        {
            config.TogglePresetAId = id;
            SettingsPresetStore.Save();
            Refresh("Updated Toggle Loadout A.", "success");
        });
        presetARow.Add(presetADropdown);

        VisualElement presetBRow = CreateRow(parent, "TOGGLE LOADOUT B");
        DropdownField presetBDropdown = CreatePresetSelector(choicesWithNone, config.TogglePresetBId, id =>
        {
            config.TogglePresetBId = id;
            SettingsPresetStore.Save();
            Refresh("Updated Toggle Loadout B.", "success");
        });
        presetBRow.Add(presetBDropdown);

    }

    private static void BuildZoneOverlaySection(VisualElement parent)
    {
        AddSectionTitle(parent, "ZONE OVERLAYS");

        VisualElement enabledRow = CreateRow(parent, "SHOW ZONE OVERLAY");
        enabledRow.Add(CreateToggle(ZoneOverlaySettings.Enabled, ZoneOverlaySettings.SetEnabled));

        VisualElement displayModeRow = CreateRow(parent, "ZONE DISPLAY");
        displayModeRow.Add(CreateZoneDisplayModeDropdown());

        VisualElement worldLabelsRow = CreateRow(parent, "SHOW WORLD ZONE LABELS");
        worldLabelsRow.Add(CreateToggle(ZoneOverlaySettings.ShowWorldZoneLabels, ZoneOverlaySettings.SetShowWorldZoneLabels));

        VisualElement minimapLabelsRow = CreateRow(parent, "SHOW MINIMAP ZONE LABELS");
        minimapLabelsRow.Add(CreateToggle(ZoneOverlaySettings.ShowMinimapZoneLabels, ZoneOverlaySettings.SetShowMinimapZoneLabels));

        VisualElement keyRow = CreateKeyBindRow(parent, "ZONE OVERLAY KEY");
        TextField keyField = CreateKeyBindTextField(
            ZoneOverlaySettingsUI.IsListeningForToggleKey
                ? "Press a key..."
                : ZoneOverlaySettings.GetKeyDisplayName(ZoneOverlaySettings.ToggleBinding),
            ZoneOverlaySettingsUI.StartListeningForToggleKey);
        keyRow.Add(keyField);

        AddSliderRow(
            parent,
            "WORLD ZONE LABEL SIZE",
            ZoneOverlaySettings.WorldTextSize,
            ZoneOverlaySettings.MinWorldTextSize,
            ZoneOverlaySettings.MaxWorldTextSize,
            ZoneOverlaySettings.SetWorldTextSize);

        AddSliderRow(
            parent,
            "MINIMAP ZONE LABEL SIZE",
            ZoneOverlaySettings.MinimapTextSize,
            ZoneOverlaySettings.MinMinimapTextSize,
            ZoneOverlaySettings.MaxMinimapTextSize,
            ZoneOverlaySettings.SetMinimapTextSize);

        AddSliderRow(
            parent,
            "WORLD/MINIMAP ZONE OPACITY",
            ZoneOverlaySettings.ZoneOpacity,
            ZoneOverlaySettings.MinZoneOpacity,
            ZoneOverlaySettings.MaxZoneOpacity,
            ZoneOverlaySettings.SetZoneOpacity);

        AddSliderRow(
            parent,
            "ZONE OUTLINE THICKNESS",
            ZoneOverlaySettings.OutlineThickness,
            ZoneOverlaySettings.MinOutlineThickness,
            ZoneOverlaySettings.MaxOutlineThickness,
            ZoneOverlaySettings.SetOutlineThickness);

        AddZoneColourSettings(parent);
        AddZoneTextSettings(parent);
    }

    private static void AddZoneColourSettings(VisualElement parent)
    {
        AddCollapsibleSettingsSection(parent, "ZONE COLOURS", ZoneColoursFoldoutKey, content =>
        {
            for (int i = 0; i < ZoneColorOptions.Length; i++)
            {
                ZoneColorOption option = ZoneColorOptions[i];
                AddHexColorRow(
                    content,
                    option.Label + " COLOUR",
                    () => ZoneOverlaySettings.GetZoneColorHex(option.Id),
                    value => ZoneOverlaySettings.SetZoneColorHex(option.Id, value));
            }
        });
    }

    private static void AddZoneTextSettings(VisualElement parent)
    {
        AddCollapsibleSettingsSection(parent, "ZONE TEXT", ZoneTextFoldoutKey, content =>
        {
            for (int i = 0; i < ZoneColorOptions.Length; i++)
            {
                ZoneColorOption option = ZoneColorOptions[i];
                AddHexColorRow(
                    content,
                    option.Label + " TEXT",
                    () => ZoneOverlaySettings.GetZoneTextColorHex(option.Id),
                    value => ZoneOverlaySettings.SetZoneTextColorHex(option.Id, value));
                AddHexColorRow(
                    content,
                    option.Label + " TEXT OUTLINE",
                    () => ZoneOverlaySettings.GetZoneTextOutlineColorHex(option.Id),
                    value => ZoneOverlaySettings.SetZoneTextOutlineColorHex(option.Id, value));
            }
        });
    }

    private static void AddCollapsibleSettingsSection(
        VisualElement parent,
        string title,
        string playerPrefsKey,
        Action<VisualElement> buildContent)
    {
        bool expanded = PlayerPrefs.GetInt(playerPrefsKey, 0) != 0;
        VisualElement header = new VisualElement();
        ApplyNativeClasses(header, NativeRowClasses);
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.height = 40f;
        header.style.marginTop = 8f;
        header.style.paddingLeft = 18f;
        header.style.paddingRight = 12f;
        header.style.borderBottomWidth = 1f;
        header.style.width = Length.Percent(100f);
        header.style.backgroundColor = BgRow;
        parent.Add(header);

        Label label = new Label();
        ApplyNativeClasses((VisualElement)label, NativeLabelClasses);
        label.style.flexBasis = Length.Percent(100f);
        label.style.flexGrow = 0f;
        label.style.color = TextPrimary;
        label.style.fontSize = 25f;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        header.Add(label);

        VisualElement content = new VisualElement();
        content.style.width = Length.Percent(100f);
        content.style.minWidth = 0f;
        content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        parent.Add(content);

        void UpdateHeader()
        {
            label.text = (expanded ? "v " : "> ") + title;
        }

        UpdateHeader();
        header.RegisterCallback<MouseDownEvent>(evt =>
        {
            expanded = !expanded;
            PlayerPrefs.SetInt(playerPrefsKey, expanded ? 1 : 0);
            PlayerPrefs.Save();
            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateHeader();
            evt.StopPropagation();
        });

        buildContent(content);
    }

    private static void AddHexColorRow(VisualElement parent, string labelText, Func<string> getValue, Func<string, bool> setValue)
    {
        VisualElement row = CreateRow(parent, labelText);
        VisualElement pickerPanel = new VisualElement();
        pickerPanel.style.width = Length.Percent(100f);
        pickerPanel.style.minWidth = 0f;
        pickerPanel.style.display = DisplayStyle.None;
        parent.Add(pickerPanel);

        VisualElement control = new VisualElement();
        control.style.flexDirection = FlexDirection.Row;
        control.style.alignItems = Align.Center;
        control.style.justifyContent = Justify.FlexEnd;
        control.style.width = nativeDropdownWidth.HasValue && nativeDropdownWidth.Value > 1f ? nativeDropdownWidth.Value : 350f;
        control.style.minWidth = 0f;
        control.style.maxWidth = 520f;
        control.style.flexShrink = 1f;

        TextField field = new TextField
        {
            value = getValue(),
            isDelayed = true
        };
        ApplyNativeClasses((VisualElement)field, NativeSliderTextFieldClasses.Count > 0 ? NativeSliderTextFieldClasses : NativeKeyBindPathFieldClasses);
        StyleTextField(field);
        field.style.flexGrow = 1f;
        field.style.flexShrink = 1f;
        field.style.minWidth = 0f;
        field.style.marginRight = 8f;
        field.style.unityTextAlign = TextAnchor.MiddleCenter;

        VisualElement preview = new VisualElement();
        ApplyNativeClasses(preview, NativeSliderTextFieldClasses.Count > 0 ? NativeSliderTextFieldClasses : NativeKeyBindPathFieldClasses);
        float previewHeight = nativeDropdownHeight.HasValue && nativeDropdownHeight.Value > 1f ? nativeDropdownHeight.Value : 38f;
        float previewWidth = Mathf.Max(54f, previewHeight * 1.65f);
        preview.style.width = previewWidth;
        preview.style.minWidth = previewWidth;
        preview.style.maxWidth = previewWidth;
        preview.style.height = previewHeight;
        preview.style.minHeight = previewHeight;
        preview.style.maxHeight = previewHeight;
        preview.style.flexShrink = 0f;
        preview.style.paddingLeft = 4f;
        preview.style.paddingRight = 4f;
        preview.style.paddingTop = 4f;
        preview.style.paddingBottom = 4f;
        ApplyFieldShadowFill(preview);

        VisualElement previewFill = new VisualElement();
        previewFill.style.flexGrow = 1f;
        previewFill.style.width = Length.Percent(100f);
        previewFill.style.height = Length.Percent(100f);
        preview.Add(previewFill);

        Slider? redSlider = null;
        Slider? greenSlider = null;
        Slider? blueSlider = null;
        bool updatingRgbControls = false;
        bool pickerExpanded = false;

        Color32 GetCurrentColor()
        {
            if (ColorUtility.TryParseHtmlString(getValue(), out Color color))
            {
                return color;
            }

            return new Color32(255, 255, 255, 255);
        }

        void UpdatePreviewAndRgb()
        {
            Color32 color = GetCurrentColor();
            previewFill.style.backgroundColor = new Color(color.r / 255f, color.g / 255f, color.b / 255f, 1f);
            if (redSlider == null || greenSlider == null || blueSlider == null)
            {
                return;
            }

            updatingRgbControls = true;
            redSlider.SetValueWithoutNotify(color.r);
            greenSlider.SetValueWithoutNotify(color.g);
            blueSlider.SetValueWithoutNotify(color.b);
            updatingRgbControls = false;
        }

        void ApplyRgb(int channel, float rawValue)
        {
            if (updatingRgbControls)
            {
                return;
            }

            Color32 color = GetCurrentColor();
            byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(rawValue), 0, 255);
            if (channel == 0)
            {
                color.r = value;
            }
            else if (channel == 1)
            {
                color.g = value;
            }
            else
            {
                color.b = value;
            }

            string nextHex = "#" + ColorUtility.ToHtmlStringRGB(color);
            if (setValue(nextHex))
            {
                field.SetValueWithoutNotify(getValue());
            }

            UpdatePreviewAndRgb();
        }

        Slider CreateRgbSlider(string channelLabel, int channel)
        {
            VisualElement sliderRow = CreateRow(pickerPanel, channelLabel);
            Color32 color = GetCurrentColor();
            Slider slider = new Slider
            {
                lowValue = 0f,
                highValue = 255f,
                value = channel == 0 ? color.r : channel == 1 ? color.g : color.b,
                showInputField = true
            };
            ApplyNativeClasses((VisualElement)slider, NativeSliderClasses);
            slider.style.width = nativeDropdownWidth.HasValue && nativeDropdownWidth.Value > 1f ? nativeDropdownWidth.Value : 350f;
            slider.style.minWidth = 0f;
            slider.style.maxWidth = 520f;
            slider.style.flexShrink = 1f;
            StyleSettingsSlider(slider);
            slider.RegisterValueChangedCallback(evt => ApplyRgb(channel, evt.newValue));
            sliderRow.Add(slider);
            return slider;
        }

        redSlider = CreateRgbSlider("RED", 0);
        greenSlider = CreateRgbSlider("GREEN", 1);
        blueSlider = CreateRgbSlider("BLUE", 2);
        UpdatePreviewAndRgb();

        preview.RegisterCallback<MouseDownEvent>(evt =>
        {
            pickerExpanded = !pickerExpanded;
            pickerPanel.style.display = pickerExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            UpdatePreviewAndRgb();
            evt.StopPropagation();
        });
        field.RegisterValueChangedCallback(evt =>
        {
            if (setValue(evt.newValue))
            {
                field.SetValueWithoutNotify(getValue());
                UpdatePreviewAndRgb();
                return;
            }

            field.SetValueWithoutNotify(getValue());
            UpdatePreviewAndRgb();
        });
        field.RegisterCallback<FocusOutEvent>(_ =>
        {
            field.SetValueWithoutNotify(getValue());
            UpdatePreviewAndRgb();
        });
        field.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            VisualElement? input = field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input == null)
            {
                return;
            }

            ApplyNativeClasses(input, NativeSliderTextInputClasses.Count > 0 ? NativeSliderTextInputClasses : NativeKeyBindPathInputClasses);
            input.style.color = TextPrimary;
            input.style.fontSize = 25f;
            input.style.unityTextAlign = TextAnchor.MiddleCenter;
            SetFieldHeight(input);
            input.style.paddingTop = 0f;
            input.style.paddingBottom = 0f;
            ApplyFieldShadowFill(input);
        });
        control.Add(field);
        control.Add(preview);
        row.Add(control);
    }

    private static Toggle CreateToggle(bool value, Action<bool> onChanged)
    {
        Toggle toggle = new Toggle
        {
            value = value
        };
        ApplyNativeClasses((VisualElement)toggle, NativeToggleClasses);
        toggle.style.flexGrow = 0f;
        toggle.style.flexShrink = 0f;
        StyleSettingsToggle(toggle);
        toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        return toggle;
    }

    private static void StyleSettingsToggle(Toggle toggle)
    {
        toggle.style.backgroundColor = FieldShadowColor;
        toggle.style.paddingLeft = ToggleShadowPadding;
        toggle.style.paddingRight = ToggleShadowPadding;
        toggle.style.paddingTop = ToggleShadowPadding;
        toggle.style.paddingBottom = ToggleShadowPadding;
        toggle.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            ApplyToggleNativeVisuals(toggle);
            toggle.schedule.Execute(() => ApplyToggleNativeVisuals(toggle)).ExecuteLater(0);
        });
    }

    private static void ApplyToggleNativeVisuals(Toggle toggle)
    {
        VisualElement? input = toggle.Q<VisualElement>(className: "unity-toggle__input");
        if (input != null)
        {
            ApplyNativeClasses(input, NativeToggleInputClasses);
            float outerSize = StyleToggleInputSize(input);
            StyleToggleShadowSize(toggle, outerSize);
            ApplyFieldShadowFill(input);
        }

        VisualElement? checkmark = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
        if (checkmark != null)
        {
            ApplyNativeClasses(checkmark, NativeToggleCheckmarkClasses);
        }
    }

    private static float StyleToggleInputSize(VisualElement input)
    {
        float baseWidth = nativeToggleInputWidth.HasValue && nativeToggleInputWidth.Value > 1f
            ? nativeToggleInputWidth.Value
            : input.resolvedStyle.width > 1f
                ? input.resolvedStyle.width
                : 24f;
        float baseHeight = nativeToggleInputHeight.HasValue && nativeToggleInputHeight.Value > 1f
            ? nativeToggleInputHeight.Value
            : input.resolvedStyle.height > 1f
                ? input.resolvedStyle.height
                : 24f;
        float outerSize = Mathf.Max(baseWidth, baseHeight) + ToggleOuterExtraSize;
        float size = Mathf.Max(1f, outerSize - ToggleShadowPadding * 2f);
        input.style.width = size;
        input.style.height = size;
        input.style.minWidth = size;
        input.style.minHeight = size;
        input.style.maxWidth = size;
        input.style.maxHeight = size;
        return outerSize;
    }

    private static void StyleToggleShadowSize(Toggle toggle, float size)
    {
        toggle.style.width = size;
        toggle.style.height = size;
        toggle.style.minWidth = size;
        toggle.style.minHeight = size;
        toggle.style.maxWidth = size;
        toggle.style.maxHeight = size;
    }

    private static DropdownField CreateZoneDisplayModeDropdown()
    {
        DropdownField dropdown = CreateStyledDropdown();
        dropdown.choices = new List<string>
        {
            "World and Minimap",
            "World",
            "Minimap"
        };
        dropdown.value = GetZoneDisplayModeLabel(ZoneOverlaySettings.DisplayMode);
        dropdown.RegisterValueChangedCallback(evt =>
        {
            ZoneOverlaySettings.SetDisplayMode(ParseZoneDisplayMode(evt.newValue));
        });
        return dropdown;
    }

    private static void AddSliderRow(
        VisualElement parent,
        string labelText,
        float value,
        float min,
        float max,
        Action<float> onChanged)
    {
        VisualElement row = CreateRow(parent, labelText);
        Slider slider = new Slider
        {
            lowValue = min,
            highValue = max,
            value = value,
            showInputField = true
        };
        ApplyNativeClasses((VisualElement)slider, NativeSliderClasses);
        slider.style.width = nativeDropdownWidth.HasValue && nativeDropdownWidth.Value > 1f ? nativeDropdownWidth.Value : 350f;
        slider.style.minWidth = 0f;
        slider.style.maxWidth = 520f;
        slider.style.flexShrink = 1f;
        StyleSettingsSlider(slider);

        slider.RegisterValueChangedCallback(evt =>
        {
            onChanged(evt.newValue);
        });

        row.Add(slider);
    }

    private static void StyleSettingsSlider(Slider slider)
    {
        slider.style.flexGrow = 0f;
        slider.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            ApplySliderNativeVisuals(slider);
            slider.schedule.Execute(() => ApplySliderNativeVisuals(slider)).ExecuteLater(0);
        });
    }

    private static void ApplySliderNativeVisuals(Slider slider)
    {
        VisualElement? input = slider.Q<VisualElement>(className: "unity-base-slider__input");
        if (input != null)
        {
            ApplyNativeClasses(input, NativeSliderInputClasses);
        }

        VisualElement? tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
        if (tracker != null)
        {
            ApplyNativeClasses(tracker, NativeSliderTrackerClasses);
            ApplyFieldShadowFill(tracker);
        }

        VisualElement? dragger = slider.Q<VisualElement>(className: "unity-base-slider__dragger");
        if (dragger != null)
        {
            ApplyNativeClasses(dragger, NativeSliderDraggerClasses);
        }

        VisualElement? draggerBorder = slider.Q<VisualElement>(className: "unity-base-slider__dragger-border");
        if (draggerBorder != null)
        {
            ApplyNativeClasses(draggerBorder, NativeSliderDraggerBorderClasses);
            ApplyFieldShadowFill(draggerBorder);
        }

        TextField? textField = slider.Q<TextField>();
        if (textField != null)
        {
            ApplyNativeClasses((VisualElement)textField, NativeSliderTextFieldClasses);
            ApplyFieldShadowFill(textField);
        }

        VisualElement? textInput = slider.Q<VisualElement>(className: "unity-base-text-field__input");
        if (textInput != null)
        {
            ApplyNativeClasses(textInput, NativeSliderTextInputClasses);
            ApplyFieldShadowFill(textInput);
            textInput.style.color = TextPrimary;
            textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
        }
    }

    private static void ApplySelectedLoadout()
    {
        SettingsPreset? preset = GetSelectedPreset(SettingsPresetStore.Config);
        if (preset == null)
        {
            Refresh("Choose a loadout to apply.", "warning");
            return;
        }

        SettingsPresetApplyResult result = SettingsPresetRuntime.ApplyPreset(preset, SettingsPresetApplyMode.Full);
        Plugin.Log(result.BuildSummary(preset, SettingsPresetApplyMode.Full));
        Refresh($"Applied \"{preset.Name}\"", result.Success ? "success" : "warning");
    }

    private static void BuildCompatibilityFooter(VisualElement parent)
    {
        string availability = "Compatible with Italiano's Extra UI Options and PakCompetitiveSkating. These options & keybinds will save to your loadouts";
        Label label = new Label(availability);
        ApplyNativeClasses((VisualElement)label, NativeLabelClasses);
        label.style.color = TextSecondary;
        label.style.fontSize = 14f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.marginTop = 12f;
        label.style.paddingLeft = 18f;
        label.style.paddingRight = 12f;
        parent.Add(label);

        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            return;
        }

        Label statusLabel = new Label(FormatFooterStatus(statusMessage));
        ApplyNativeClasses((VisualElement)statusLabel, NativeLabelClasses);
        statusLabel.style.color = statusKind == "success" ? AccentGreen : statusKind == "warning" ? AccentOrange : AccentBlue;
        statusLabel.style.fontSize = 14f;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        statusLabel.style.marginTop = 4f;
        statusLabel.style.paddingLeft = 18f;
        statusLabel.style.paddingRight = 12f;
        parent.Add(statusLabel);
    }

    private static string FormatFooterStatus(string message)
    {
        return (message ?? string.Empty).Trim().TrimEnd('.');
    }

    private static string GetZoneDisplayModeLabel(ZoneOverlayDisplayMode mode)
    {
        return mode switch
        {
            ZoneOverlayDisplayMode.World => "World",
            ZoneOverlayDisplayMode.Minimap => "Minimap",
            _ => "World and Minimap"
        };
    }

    private static ZoneOverlayDisplayMode ParseZoneDisplayMode(string label)
    {
        return label switch
        {
            "World" => ZoneOverlayDisplayMode.World,
            "Minimap" => ZoneOverlayDisplayMode.Minimap,
            _ => ZoneOverlayDisplayMode.WorldAndMinimap
        };
    }

    private static DropdownField CreatePresetSelector(List<string> choices, string presetId, Action<string> onSelected)
    {
        DropdownField dropdown = CreateStyledDropdown();
        dropdown.choices = choices;
        dropdown.value = GetPresetChoiceForId(presetId) ?? NoneChoice;
        dropdown.RegisterValueChangedCallback(evt =>
        {
            string id = PresetChoiceToId.TryGetValue(evt.newValue, out string mappedId) ? mappedId : string.Empty;
            onSelected(id);
        });
        return dropdown;
    }

    private static DropdownField CreateStyledDropdown()
    {
        DropdownField dropdown = new DropdownField();
        ApplyNativeClasses((VisualElement)dropdown, NativeDropdownClasses);
        StyleDropdownField(dropdown);
        ApplyFieldShadowFill(dropdown);
        dropdown.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            VisualElement? input = dropdown.Q<VisualElement>(className: "unity-base-popup-field__input");
            if (input != null)
            {
                ApplyNativeClasses(input, NativeDropdownInputClasses);
                SetFieldHeight(input);
                input.style.paddingTop = 0f;
                input.style.paddingBottom = 0f;
                ApplyFieldShadowFill(input);
            }
        });
        return dropdown;
    }

    private static VisualElement CreateRow(VisualElement parent, string labelText)
    {
        VisualElement row = new VisualElement();
        ApplyNativeClasses(row, NativeRowClasses);
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.FlexStart;
        row.style.minHeight = 50f;
        row.style.height = StyleKeyword.Auto;
        row.style.width = Length.Percent(100f);
        row.style.maxWidth = Length.Percent(100f);
        row.style.flexShrink = 1f;
        row.style.marginTop = 8f;
        row.style.paddingLeft = 18f;
        row.style.paddingRight = 12f;
        row.style.backgroundColor = BgRow;
        row.style.borderBottomWidth = 1f;
        parent.Add(row);

        Label label = new Label(labelText);
        ApplyNativeClasses((VisualElement)label, NativeLabelClasses);
        StyleRowLabel(label);
        row.Add(label);

        VisualElement right = new VisualElement();
        StyleRowControlArea(right);
        row.Add(right);
        return right;
    }

    private static VisualElement CreateKeyBindRow(VisualElement parent, string labelText)
    {
        VisualElement row = new VisualElement();
        ApplyNativeClasses(row, NativeKeyBindRowClasses.Count > 0 ? NativeKeyBindRowClasses : NativeRowClasses);
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.FlexStart;
        row.style.minHeight = 50f;
        row.style.height = StyleKeyword.Auto;
        row.style.width = Length.Percent(100f);
        row.style.maxWidth = Length.Percent(100f);
        row.style.flexShrink = 1f;
        row.style.marginTop = 8f;
        row.style.paddingLeft = 18f;
        row.style.paddingRight = 12f;
        row.style.backgroundColor = BgRow;
        row.style.borderBottomWidth = 1f;
        parent.Add(row);

        Label label = new Label(labelText);
        ApplyNativeClasses((VisualElement)label, NativeLabelClasses);
        StyleRowLabel(label);
        row.Add(label);

        VisualElement right = new VisualElement();
        StyleRowControlArea(right);
        row.Add(right);
        return right;
    }

    private static void AddSectionTitle(VisualElement parent, string title)
    {
        VisualElement row = new VisualElement();
        ApplyNativeClasses(row, NativeRowClasses);
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 40f;
        row.style.marginTop = 8f;
        row.style.paddingLeft = 18f;
        row.style.paddingRight = 12f;
        row.style.borderBottomWidth = 1f;
        row.style.width = Length.Percent(100f);
        parent.Add(row);

        Label label = new Label(title);
        ApplyNativeClasses((VisualElement)label, NativeLabelClasses);
        label.style.flexBasis = Length.Percent(100f);
        label.style.flexGrow = 0f;
        label.style.color = TextPrimary;
        label.style.fontSize = 25f;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(label);
    }

    private static void StyleRowLabel(Label label)
    {
        label.style.color = TextPrimary;
        label.style.fontSize = 25f;
        label.style.flexGrow = 0f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.flexBasis = Length.Percent(42f);
    }

    private static void StyleRowControlArea(VisualElement right)
    {
        right.style.flexDirection = FlexDirection.Row;
        right.style.alignItems = Align.Center;
        right.style.justifyContent = Justify.FlexEnd;
        right.style.flexGrow = 1f;
        right.style.flexShrink = 1f;
        right.style.minWidth = 0f;
        right.style.width = Length.Percent(100f);
    }

    private static void StyleDropdownText(VisualElement element)
    {
        element.style.fontSize = nativeDropdownTextFontSize.HasValue && nativeDropdownTextFontSize.Value > 1f
            ? nativeDropdownTextFontSize.Value
            : 16f;
        element.style.unityFontStyleAndWeight = nativeDropdownTextFontStyle ?? FontStyle.Normal;
    }

    private static Button CreateButton(string text, Action onClick)
    {
        Button button = new Button(onClick)
        {
            text = text
        };
        ApplyNativeClasses((VisualElement)button, NativeButtonClasses);
        button.style.color = TextPrimary;
        StyleDropdownText(button);
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        StyleFieldWidth(button);
        SetFieldHeight(button);
        button.style.paddingLeft = 12f;
        button.style.paddingRight = 12f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        ApplyFieldShadowFill(button);
        button.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (!button.enabledSelf)
            {
                return;
            }

            button.style.backgroundColor = Color.white;
            button.style.color = Color.black;
        });
        button.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            button.style.backgroundColor = FieldShadowColor;
            button.style.color = TextPrimary;
        });
        return button;
    }

    private static void StyleFieldWidth(VisualElement field)
    {
        float width = nativeDropdownWidth.HasValue && nativeDropdownWidth.Value > 1f ? nativeDropdownWidth.Value : 350f;
        field.style.width = width;
        field.style.minWidth = 0f;
        field.style.maxWidth = 520f;
        field.style.flexShrink = 1f;
    }

    private static void StyleDropdownField(DropdownField field)
    {
        StyleFieldWidth(field);
        SetFieldHeight(field);
    }

    private static void StyleTextField(TextField field)
    {
        field.style.fontSize = 25f;
        SetFieldHeight(field);
        ApplyFieldShadowFill(field);
        field.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            VisualElement? input = field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input != null)
            {
                input.style.color = TextPrimary;
                input.style.fontSize = 25f;
                SetFieldHeight(input);
                input.style.paddingTop = 0f;
                input.style.paddingBottom = 0f;
                ApplyFieldShadowFill(input);
            }
        });
    }

    private static TextField CreateKeyBindTextField(string value, Action onClick)
    {
        TextField field = new TextField
        {
            value = value,
            focusable = false
        };
        ApplyNativeClasses((VisualElement)field, NativeKeyBindPathFieldClasses);
        field.style.width = nativeKeyBindPathWidth.HasValue && nativeKeyBindPathWidth.Value > 1f ? nativeKeyBindPathWidth.Value : 350f;
        field.style.minWidth = 0f;
        field.style.flexShrink = 1f;
        if (nativeKeyBindPathHeight.HasValue && nativeKeyBindPathHeight.Value > 1f)
        {
            field.style.height = nativeKeyBindPathHeight.Value;
        }
        ApplyFieldShadowFill(field);

        field.RegisterCallback<MouseDownEvent>(evt =>
        {
            onClick();
            evt.StopPropagation();
        });
        field.RegisterCallback<AttachToPanelEvent>(_ =>
        {
            VisualElement? input = field.Q<VisualElement>(className: "unity-base-text-field__input");
            if (input != null)
            {
                ApplyNativeClasses(input, NativeKeyBindPathInputClasses);
                ApplyFieldShadowFill(input);
            }
        });
        return field;
    }

    private static void SetFieldHeight(VisualElement element)
    {
        float height = nativeDropdownHeight.HasValue && nativeDropdownHeight.Value > 1f ? nativeDropdownHeight.Value : 38f;
        element.style.height = height;
        element.style.minHeight = height;
        element.style.maxHeight = height;
    }

    private static void CaptureNativeStyleProfile(VisualElement settings)
    {
        if (nativeStyleProfileCaptured)
        {
            return;
        }

        VisualElement? row = settings.Q<VisualElement>("FullScreenModeDropdown")
                             ?? settings.Q<VisualElement>("ResolutionDropdown")
                             ?? settings.Q<VisualElement>("HandednessDropdown")
                             ?? settings.Q<VisualElement>("CameraAngleSlider");
        Label? rowLabel = row?.Q<Label>();
        DropdownField? dropdown = row?.Q<DropdownField>();
        VisualElement? dropdownInput = dropdown?.Q<VisualElement>(className: "unity-base-popup-field__input");

        VisualElement? keyBindRow = settings.Q<VisualElement>("MoveForwardKeyBindInput")
                                    ?? settings.Q<VisualElement>("JumpKeyBindInput")
                                    ?? settings.Q<VisualElement>("ScoreboardKeyBindInput");
        TextField? keyBindPathField = keyBindRow?.Q<TextField>("PathTextField");
        VisualElement? keyBindPathInput = keyBindPathField?.Q<VisualElement>(className: "unity-base-text-field__input");
        Button? button = settings.Q<Button>("ResetToDefaultButton");
        Toggle? toggle = (settings.Q<VisualElement>("VSyncToggle")
                          ?? settings.Q<VisualElement>("MotionBlurToggle")
                          ?? settings.Q<VisualElement>("ShowPuckOutlineToggle"))?.Q<Toggle>();
        Slider? slider = (settings.Q<VisualElement>("CameraAngleSlider")
                          ?? settings.Q<VisualElement>("FOVSlider")
                          ?? settings.Q<VisualElement>("MinimapScaleSlider"))?.Q<Slider>();

        CopyClasses(row, NativeRowClasses);
        CopyClasses(rowLabel, NativeLabelClasses);
        CopyClasses(dropdown, NativeDropdownClasses);
        CopyClasses(dropdownInput, NativeDropdownInputClasses);
        CopyClasses(keyBindRow, NativeKeyBindRowClasses);
        CopyClasses(keyBindPathField, NativeKeyBindPathFieldClasses);
        CopyClasses(keyBindPathInput, NativeKeyBindPathInputClasses);
        CopyClasses(button, NativeButtonClasses);
        CopyClasses(toggle, NativeToggleClasses);
        CopyClasses(slider, NativeSliderClasses);

        VisualElement? toggleInput = toggle?.Q<VisualElement>(className: "unity-toggle__input");
        CopyClasses(toggleInput, NativeToggleInputClasses);
        CopyClasses(toggle?.Q<VisualElement>(className: "unity-toggle__checkmark"), NativeToggleCheckmarkClasses);
        CopyClasses(slider?.Q<VisualElement>(className: "unity-base-slider__input"), NativeSliderInputClasses);
        CopyClasses(slider?.Q<VisualElement>(className: "unity-base-slider__tracker"), NativeSliderTrackerClasses);
        CopyClasses(slider?.Q<VisualElement>(className: "unity-base-slider__dragger"), NativeSliderDraggerClasses);
        CopyClasses(slider?.Q<VisualElement>(className: "unity-base-slider__dragger-border"), NativeSliderDraggerBorderClasses);
        CopyClasses(slider?.Q<TextField>(), NativeSliderTextFieldClasses);
        CopyClasses(slider?.Q<VisualElement>(className: "unity-base-text-field__input"), NativeSliderTextInputClasses);

        nativeDropdownWidth = GetResolvedWidth(dropdown);
        nativeDropdownHeight = GetResolvedHeight(dropdown);
        nativeKeyBindPathWidth = GetResolvedWidth(keyBindPathField);
        nativeKeyBindPathHeight = GetResolvedHeight(keyBindPathField);
        nativeToggleInputWidth = GetResolvedWidth(toggleInput);
        nativeToggleInputHeight = GetResolvedHeight(toggleInput);
        nativeDropdownTextFontSize = GetResolvedFontSize(dropdownInput ?? (VisualElement?)dropdown);
        nativeDropdownTextFontStyle = (dropdownInput ?? (VisualElement?)dropdown)?.resolvedStyle.unityFontStyleAndWeight;

        nativeStyleProfileCaptured = true;
    }

    private static float? GetResolvedWidth(VisualElement? element)
    {
        if (element == null || float.IsNaN(element.resolvedStyle.width) || element.resolvedStyle.width <= 1f)
        {
            return null;
        }

        return element.resolvedStyle.width;
    }

    private static float? GetResolvedHeight(VisualElement? element)
    {
        if (element == null || float.IsNaN(element.resolvedStyle.height) || element.resolvedStyle.height <= 1f)
        {
            return null;
        }

        return element.resolvedStyle.height;
    }

    private static float? GetResolvedFontSize(VisualElement? element)
    {
        if (element == null || float.IsNaN(element.resolvedStyle.fontSize) || element.resolvedStyle.fontSize <= 1f)
        {
            return null;
        }

        return element.resolvedStyle.fontSize;
    }

    private static void CopyClasses(VisualElement? source, List<string> destination)
    {
        destination.Clear();
        if (source == null)
        {
            return;
        }

        foreach (string className in source.GetClasses())
        {
            if (!string.IsNullOrWhiteSpace(className))
            {
                destination.Add(className);
            }
        }
    }

    private static void ApplyNativeClasses(VisualElement element, List<string> classes)
    {
        foreach (string className in classes)
        {
            element.AddToClassList(className);
        }
    }

    private static void ApplyFieldShadowFill(VisualElement element)
    {
        element.style.backgroundColor = FieldShadowColor;
    }

    private static List<string> BuildPresetChoices(SettingsPresetConfig config, bool includeNone)
    {
        PresetChoiceToId.Clear();
        List<string> choices = new List<string>();
        if (includeNone)
        {
            choices.Add(NoneChoice);
            PresetChoiceToId[NoneChoice] = string.Empty;
        }

        Dictionary<string, int> nameCounts = config.Presets
            .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (SettingsPreset preset in config.Presets)
        {
            string label = nameCounts.TryGetValue(preset.Name, out int count) && count > 1
                ? $"{preset.Name} ({preset.Id.Substring(0, Math.Min(6, preset.Id.Length))})"
                : preset.Name;
            choices.Add(label);
            PresetChoiceToId[label] = preset.Id;
        }

        if (choices.Count == 0)
        {
            choices.Add(NoneChoice);
            PresetChoiceToId[NoneChoice] = string.Empty;
        }

        return choices;
    }

    private static string? GetPresetChoiceForId(string? presetId)
    {
        foreach (KeyValuePair<string, string> entry in PresetChoiceToId)
        {
            if (string.Equals(entry.Value, presetId ?? string.Empty, StringComparison.Ordinal))
            {
                return entry.Key;
            }
        }

        return null;
    }

    private static SettingsPreset? GetSelectedPreset(SettingsPresetConfig config)
    {
        EnsureSelectedPresetExists(config);
        return SettingsPresetStore.FindPreset(selectedPresetId);
    }

    private static void EnsureSelectedPresetExists(SettingsPresetConfig config)
    {
        if (SettingsPresetStore.FindPreset(selectedPresetId) != null)
        {
            return;
        }

        selectedPresetId = config.Presets.FirstOrDefault()?.Id ?? string.Empty;
    }

    private static string GetDefaultPresetName(SettingsPresetConfig config)
    {
        return $"Loadout {config.Presets.Count + 1}";
    }

    private sealed class ZoneColorOption
    {
        public readonly string Id;
        public readonly string Label;

        public ZoneColorOption(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }
}
