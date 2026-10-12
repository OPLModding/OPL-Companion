using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace OPLServerBanners;

public static class ServerBrowserBannerStyling
{
    private const float AnimationIntervalSeconds = 0.05f;
    private const string PasswordProtectedIconName = "PasswordProtectedIcon";

    private static readonly Color BannerBlue = ParseColor("#2466b7", new Color(0.141f, 0.4f, 0.718f));
    private static readonly Color BannerBlueHover = ParseColor("#2d79d5", new Color(0.176f, 0.475f, 0.835f));
    private static readonly Color NameTextColor = ParseColor("#FFFFFF", Color.white);
    private static readonly Color StatsTextColor = ParseColor("#DCEBFF", new Color(0.863f, 0.922f, 1f));
    private static readonly Color IconTintColor = ParseColor("#F4FAFF", new Color(0.957f, 0.98f, 1f));
    private static readonly Color SavedPasswordLockTint = ParseColor("#FFB347", new Color(1f, 0.702f, 0.278f));
    private static readonly Color StarBright = Color.white;
    private static readonly Color StarDim = ParseColor("#9DC0EE", new Color(0.616f, 0.753f, 0.933f));
    private static readonly Color CornerCutColor = new Color(0.196f, 0.196f, 0.196f);

    private static readonly Dictionary<VisualElement, BannerState> StyledRows = new();
    private static readonly List<VisualElement> AnimationRowBuffer = new();
    private static readonly FieldInfo? EndPointVisualElementMapField = AccessTools.Field(typeof(UIServerBrowser), "endPointVisualElementMap");
    private static float nextAnimationTickAt;

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(StyleServerPatch)).Patch();
        harmony.CreateClassProcessor(typeof(UIManagerUpdatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(RemoveAllServersPatch)).Patch();
    }

    private sealed class BannerState
    {
        public VisualElement CornerCutTopRight = null!;

        public VisualElement CornerCutBottomLeft = null!;

        public Label? NameLabel;

        public Label? PlayersLabel;

        public Label? PingLabel;

        public string OriginalNameText = string.Empty;

        public string LastRenderedText = string.Empty;

        public bool IsHovered;

        public bool HasSavedPassword;
    }

    [HarmonyPatch(typeof(UIServerBrowser), "StyleServer")]
    private static class StyleServerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UIServerBrowser __instance, EndPoint endPoint)
        {
            if (EndPointVisualElementMapField?.GetValue(__instance) is not Dictionary<EndPoint, VisualElement> rowMap ||
                !rowMap.TryGetValue(endPoint, out var rowContainer))
            {
                return;
            }

            var serverRow = UQueryExtensions.Q(rowContainer, "Server", (string)null!);
            if (serverRow == null)
            {
                return;
            }

            var currentServerName = UQueryExtensions.Q<Label>(serverRow, "NameLabel", (string)null!)?.text;
            if (!ServerConfigManager.ShouldApplyBanner(endPoint, currentServerName))
            {
                RemoveBanner(serverRow);
                return;
            }

            var nameLabel = UQueryExtensions.Q<Label>(serverRow, "NameLabel", (string)null!);
            var playersLabel = UQueryExtensions.Q<Label>(serverRow, "PlayersLabel", (string)null!);
            var pingLabel = UQueryExtensions.Q<Label>(serverRow, "PingLabel", (string)null!);

            ApplyBanner(serverRow, nameLabel, playersLabel, pingLabel, endPoint);
        }
    }

    [HarmonyPatch(typeof(UIManager), "Update")]
    private static class UIManagerUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TickAnimations();
        }
    }

    [HarmonyPatch(typeof(UIServerBrowser), "RemoveAllServers")]
    private static class RemoveAllServersPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            AnimationRowBuffer.Clear();
            foreach (var serverRow in StyledRows.Keys)
            {
                AnimationRowBuffer.Add(serverRow);
            }

            foreach (var serverRow in AnimationRowBuffer)
            {
                RemoveBanner(serverRow);
            }

            AnimationRowBuffer.Clear();
            StyledRows.Clear();
        }
    }

    private static void ApplyBanner(
        VisualElement serverRow,
        Label? nameLabel,
        Label? playersLabel,
        Label? pingLabel,
        EndPoint endPoint)
    {
        var state = EnsureState(serverRow);
        state.NameLabel = nameLabel;
        state.PlayersLabel = playersLabel;
        state.PingLabel = pingLabel;
        state.HasSavedPassword = ToasterReskinLoaderCompatibility.HasSavedPassword(endPoint);

        serverRow.style.backgroundColor = new StyleColor(state.IsHovered ? BannerBlueHover : BannerBlue);
        serverRow.style.overflow = Overflow.Hidden;
        serverRow.style.borderTopLeftRadius = 0f;
        serverRow.style.borderTopRightRadius = 0f;
        serverRow.style.borderBottomLeftRadius = 0f;
        serverRow.style.borderBottomRightRadius = 0f;

        if (nameLabel != null)
        {
            nameLabel.enableRichText = true;

            var currentText = nameLabel.text ?? string.Empty;
            if (!string.Equals(currentText, state.LastRenderedText, StringComparison.Ordinal))
            {
                state.OriginalNameText = currentText;
            }

            if (string.IsNullOrEmpty(state.OriginalNameText))
            {
                state.OriginalNameText = currentText;
            }
        }

        ApplyForegroundStyles(serverRow, nameLabel, playersLabel, pingLabel, state.HasSavedPassword);
        UpdateAnimation(serverRow, state, Time.unscaledTime);
    }

    private static BannerState EnsureState(VisualElement serverRow)
    {
        if (StyledRows.TryGetValue(serverRow, out var existingState))
        {
            return existingState;
        }

        var cornerCutTopRight = new VisualElement
        {
            name = "OPLBannerCornerTopRight",
            pickingMode = PickingMode.Ignore
        };
        cornerCutTopRight.style.position = Position.Absolute;
        cornerCutTopRight.style.top = -9f;
        cornerCutTopRight.style.right = -9f;
        cornerCutTopRight.style.width = 18f;
        cornerCutTopRight.style.height = 18f;
        cornerCutTopRight.style.backgroundColor = new StyleColor(CornerCutColor);
        cornerCutTopRight.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(45f)));
        serverRow.Add(cornerCutTopRight);

        var cornerCutBottomLeft = new VisualElement
        {
            name = "OPLBannerCornerBottomLeft",
            pickingMode = PickingMode.Ignore
        };
        cornerCutBottomLeft.style.position = Position.Absolute;
        cornerCutBottomLeft.style.bottom = -9f;
        cornerCutBottomLeft.style.left = -9f;
        cornerCutBottomLeft.style.width = 18f;
        cornerCutBottomLeft.style.height = 18f;
        cornerCutBottomLeft.style.backgroundColor = new StyleColor(CornerCutColor);
        cornerCutBottomLeft.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(45f)));
        serverRow.Add(cornerCutBottomLeft);

        var state = new BannerState
        {
            CornerCutTopRight = cornerCutTopRight,
            CornerCutBottomLeft = cornerCutBottomLeft
        };

        StyledRows[serverRow] = state;
        ((CallbackEventHandler)serverRow).RegisterCallback<MouseEnterEvent>(
            (EventCallback<MouseEnterEvent>)OnMouseEnter,
            TrickleDown.NoTrickleDown);
        ((CallbackEventHandler)serverRow).RegisterCallback<MouseLeaveEvent>(
            (EventCallback<MouseLeaveEvent>)OnMouseLeave,
            TrickleDown.NoTrickleDown);
        ((CallbackEventHandler)serverRow).RegisterCallback<DetachFromPanelEvent>(
            (EventCallback<DetachFromPanelEvent>)OnDetachFromPanel,
            TrickleDown.NoTrickleDown);

        return state;
    }

    private static void RemoveBanner(VisualElement serverRow)
    {
        if (!StyledRows.TryGetValue(serverRow, out var state))
        {
            return;
        }

        if (state.NameLabel != null)
        {
            state.NameLabel.text = state.OriginalNameText;
        }

        state.CornerCutTopRight.RemoveFromHierarchy();
        state.CornerCutBottomLeft.RemoveFromHierarchy();
        ((CallbackEventHandler)serverRow).UnregisterCallback<MouseEnterEvent>(
            (EventCallback<MouseEnterEvent>)OnMouseEnter,
            TrickleDown.NoTrickleDown);
        ((CallbackEventHandler)serverRow).UnregisterCallback<MouseLeaveEvent>(
            (EventCallback<MouseLeaveEvent>)OnMouseLeave,
            TrickleDown.NoTrickleDown);
        ((CallbackEventHandler)serverRow).UnregisterCallback<DetachFromPanelEvent>(
            (EventCallback<DetachFromPanelEvent>)OnDetachFromPanel,
            TrickleDown.NoTrickleDown);
        ResetRowStyles(serverRow);

        StyledRows.Remove(serverRow);
    }

    private static void ResetRowStyles(VisualElement serverRow)
    {
        serverRow.style.backgroundColor = new StyleColor(StyleKeyword.Null);
        serverRow.style.color = new StyleColor(StyleKeyword.Null);
        serverRow.style.overflow = new StyleEnum<Overflow>(StyleKeyword.Null);
        serverRow.style.borderTopLeftRadius = new StyleLength(StyleKeyword.Null);
        serverRow.style.borderTopRightRadius = new StyleLength(StyleKeyword.Null);
        serverRow.style.borderBottomLeftRadius = new StyleLength(StyleKeyword.Null);
        serverRow.style.borderBottomRightRadius = new StyleLength(StyleKeyword.Null);
        serverRow.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Null);
        ResetForegroundStyles(serverRow);
    }

    private static void OnMouseEnter(MouseEnterEvent evt)
    {
        var serverRow = ((EventBase)evt).currentTarget as VisualElement;
        if (serverRow == null || !StyledRows.TryGetValue(serverRow, out var state))
        {
            return;
        }

        state.IsHovered = true;
        serverRow.style.backgroundColor = new StyleColor(BannerBlueHover);
    }

    private static void OnMouseLeave(MouseLeaveEvent evt)
    {
        var serverRow = ((EventBase)evt).currentTarget as VisualElement;
        if (serverRow == null || !StyledRows.TryGetValue(serverRow, out var state))
        {
            return;
        }

        state.IsHovered = false;
        serverRow.style.backgroundColor = new StyleColor(BannerBlue);
    }

    private static void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        if (((EventBase)evt).currentTarget is VisualElement serverRow)
        {
            RemoveBanner(serverRow);
        }
    }

    private static void TickAnimations()
    {
        if (StyledRows.Count == 0)
        {
            nextAnimationTickAt = 0f;
            return;
        }

        if (Time.unscaledTime < nextAnimationTickAt)
        {
            return;
        }

        nextAnimationTickAt = Time.unscaledTime + AnimationIntervalSeconds;
        var time = Time.unscaledTime;
        var leftStarColor = ColorUtility.ToHtmlStringRGB(Color.Lerp(StarDim, StarBright, Mathf.Sin(time * 3f) * 0.5f + 0.5f));
        var rightStarColor = ColorUtility.ToHtmlStringRGB(Color.Lerp(StarDim, StarBright, Mathf.Sin(time * 3f + 1.5f) * 0.5f + 0.5f));

        AnimationRowBuffer.Clear();
        foreach (var serverRow in StyledRows.Keys)
        {
            AnimationRowBuffer.Add(serverRow);
        }

        foreach (var serverRow in AnimationRowBuffer)
        {
            if (StyledRows.TryGetValue(serverRow, out var state))
            {
                UpdateAnimation(serverRow, state, leftStarColor, rightStarColor);
            }
        }
    }

    private static void UpdateAnimation(VisualElement serverRow, BannerState state, float time)
    {
        var leftStarColor = ColorUtility.ToHtmlStringRGB(Color.Lerp(StarDim, StarBright, Mathf.Sin(time * 3f) * 0.5f + 0.5f));
        var rightStarColor = ColorUtility.ToHtmlStringRGB(Color.Lerp(StarDim, StarBright, Mathf.Sin(time * 3f + 1.5f) * 0.5f + 0.5f));
        UpdateAnimation(serverRow, state, leftStarColor, rightStarColor);
    }

    private static void UpdateAnimation(
        VisualElement serverRow,
        BannerState state,
        string leftStarColor,
        string rightStarColor)
    {
        if (serverRow.panel == null)
        {
            RemoveBanner(serverRow);
            return;
        }

        serverRow.style.backgroundColor = new StyleColor(state.IsHovered ? BannerBlueHover : BannerBlue);

        if (state.NameLabel == null || string.IsNullOrEmpty(state.OriginalNameText))
        {
            return;
        }

        var renderedText = BuildAnimatedName(state.OriginalNameText, leftStarColor, rightStarColor);
        if (string.Equals(renderedText, state.LastRenderedText, StringComparison.Ordinal))
        {
            return;
        }

        state.NameLabel.text = renderedText;
        state.LastRenderedText = renderedText;
    }

    private static void ApplyForegroundStyles(
        VisualElement root,
        Label? nameLabel,
        Label? playersLabel,
        Label? pingLabel,
        bool hasSavedPassword)
    {
        var textColor = root == playersLabel || root == pingLabel ? StatsTextColor : NameTextColor;
        var iconTintColor = hasSavedPassword && string.Equals(root.name, PasswordProtectedIconName, StringComparison.Ordinal)
            ? SavedPasswordLockTint
            : IconTintColor;

        root.style.color = new StyleColor(textColor);
        root.style.unityBackgroundImageTintColor = new StyleColor(iconTintColor);

        for (var i = 0; i < root.childCount; i++)
        {
            ApplyForegroundStyles(root[i], nameLabel, playersLabel, pingLabel, hasSavedPassword);
        }
    }

    private static void ResetForegroundStyles(VisualElement root)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            ResetForegroundStyles(root[i]);
        }

        root.style.color = new StyleColor(StyleKeyword.Null);
        root.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Null);
    }

    private static string BuildAnimatedName(string originalText, string leftStarColor, string rightStarColor)
    {
        return $"<color=#{leftStarColor}>\u2726</color>  {originalText}  <color=#{rightStarColor}>\u2726</color>";
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) && ColorUtility.TryParseHtmlString(value, out var parsedColor))
        {
            return parsedColor;
        }

        return fallback;
    }
}
