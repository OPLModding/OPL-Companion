using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class WarmupObstacleEditorUI
{
    private static readonly Color AccentBlue = new Color(0.35f, 0.72f, 1f, 1f);
    private static readonly Color ActiveGreen = new Color(0.18f, 0.76f, 0.34f, 1f);
    private static readonly Color WarningOrange = new Color(0.88f, 0.62f, 0.18f, 1f);
    private static readonly Color ErrorRed = new Color(0.9f, 0.16f, 0.12f, 1f);
    private static readonly Color BgDark = new Color(0.08f, 0.09f, 0.1f, 0.94f);
    private static readonly Color BgRow = new Color(0.14f, 0.15f, 0.16f, 0.96f);
    private static readonly Color BgButton = new Color(0.2f, 0.22f, 0.24f, 1f);
    private static readonly Color BorderGray = new Color(0.34f, 0.36f, 0.38f, 1f);
    private static readonly Color TextPrimary = Color.white;
    private static readonly Color TextSecondary = new Color(0.68f, 0.72f, 0.76f, 1f);

    private static bool isSetup;
    private static bool isVisible;
    private static bool suppressFieldEvents;
    private static int hideFrame = -1;
    private static string selectedAssetId = WarmupObstacleCatalog.PasserId;
    private static bool useAimPosition = true;
    private static Vector3 manualPosition = Vector3.zero;
    private static Vector3 currentPosition = Vector3.zero;
    private static Vector3 userEuler = Vector3.zero;
    private static int highlightedObjectId;

    private static VisualElement? panel;
    private static VisualElement? openHint;
    private static VisualElement? assetList;
    private static Label? statusLabel;
    private static Label? countLabel;
    private static Label? selectedLabel;
    private static Label? deleteLabel;
    private static Label? voteLabel;
    private static Button? placeButton;
    private static Button? deleteButton;
    private static Button? useAimButton;
    private static Button? clearButton;
    private static Button? voteYesButton;
    private static Button? voteNoButton;
    private static FloatField? posXField;
    private static FloatField? posYField;
    private static FloatField? posZField;
    private static FloatField? rotXField;
    private static FloatField? rotYField;
    private static FloatField? rotZField;
    private static readonly Dictionary<string, Button> AssetButtons = new(StringComparer.OrdinalIgnoreCase);

    private static WarmupObstacleProtocol.State? currentState;
    private static GameObject? ghostObject;
    private static string ghostAssetId = string.Empty;
    private static GameObject? editorCameraObject;
    private static Camera? editorCamera;
    private static BaseCamera? previousCamera;
    private static bool previousUnityCameraEnabled;
    private static bool previousAudioListenerEnabled;
    private static bool hasPreviousCameraComponentState;
    private static float cameraPitch;
    private static float cameraYaw;

    public static bool IsVisible => isVisible;

    public static void EnsureSetup()
    {
        if (isSetup && panel?.panel != null && openHint?.panel != null)
        {
            return;
        }

        VisualElement? root = GetRoot();
        if (root == null)
        {
            return;
        }

        Destroy();

        panel = new VisualElement
        {
            name = "WarmupObstacleEditorPanel"
        };
        panel.style.position = Position.Absolute;
        panel.style.left = 16f;
        panel.style.top = 68f;
        panel.style.bottom = 18f;
        panel.style.width = 390f;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = BgDark;
        panel.style.borderTopLeftRadius = 6f;
        panel.style.borderTopRightRadius = 6f;
        panel.style.borderBottomLeftRadius = 6f;
        panel.style.borderBottomRightRadius = 6f;
        panel.style.borderTopWidth = 1f;
        panel.style.borderRightWidth = 1f;
        panel.style.borderBottomWidth = 1f;
        panel.style.borderLeftWidth = 1f;
        panel.style.borderTopColor = BorderGray;
        panel.style.borderRightColor = BorderGray;
        panel.style.borderBottomColor = BorderGray;
        panel.style.borderLeftColor = BorderGray;
        panel.style.display = DisplayStyle.None;
        root.Add(panel);

        BuildPanel(panel);

        openHint = new VisualElement
        {
            name = "WarmupObstacleEditorHint",
            pickingMode = PickingMode.Ignore
        };
        openHint.style.position = Position.Absolute;
        openHint.style.left = 16f;
        openHint.style.bottom = 98f;
        openHint.style.display = DisplayStyle.None;
        Label hintLabel = new Label("F3 - Obstacle Editor");
        hintLabel.style.color = new Color(0.55f, 0.58f, 0.62f, 1f);
        hintLabel.style.fontSize = 18f;
        openHint.Add(hintLabel);
        root.Add(openHint);

        isSetup = true;
        if (currentState != null)
        {
            ApplyState(currentState);
        }
    }

    public static void Show()
    {
        EnsureSetup();
        if (panel == null)
        {
            return;
        }

        panel.style.display = DisplayStyle.Flex;
        isVisible = true;
        UpdateOpenHint(false);
        EnterEditorCamera();
        EnsureGhost();
        SetGameInputSuppressed(true);
    }

    public static void Hide()
    {
        if (panel != null)
        {
            panel.style.display = DisplayStyle.None;
        }

        isVisible = false;
        highlightedObjectId = 0;
        DestroyGhost();
        WarmupObstacleClientWorld.TryFindLookedAtObstacle(new Ray(Vector3.down * 1000f, Vector3.down), 0.01f, out _);
        ExitEditorCamera();
        SetGameInputSuppressed(false);
        hideFrame = Time.frameCount;
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

    public static void ApplyState(WarmupObstacleProtocol.State state)
    {
        currentState = state;
        EnsureSetup();
        if (!isSetup)
        {
            return;
        }

        ApplyStatus(state.StatusKind, state.StatusMessage);
        if (countLabel != null)
        {
            countLabel.text = $"{state.ObjectCount}/{state.MaxObjects}";
        }

        RebuildAssetListIfNeeded(state.Assets);
        if (!state.Assets.Any(asset => string.Equals(asset.Id, selectedAssetId, StringComparison.OrdinalIgnoreCase)) && state.Assets.Length > 0)
        {
            SelectAsset(state.Assets[0].Id);
        }

        placeButton?.SetEnabled(state.CanEdit && state.ObjectCount < state.MaxObjects);
        clearButton?.SetEnabled(state.CanEdit && state.ObjectCount > 0);
        UpdateSelectedLabel();
        UpdateDeleteState();
        ApplyVoteState(state.ClearVote);
    }

    public static void ShowStatus(string kind, string message)
    {
        EnsureSetup();
        ApplyStatus(kind, message);
    }

    public static void Tick()
    {
        if (!isVisible)
        {
            return;
        }

        SetGameInputSuppressed(true);
        TickEditorCamera();
        TickGhost();
        TickSelection();
        HandleEditorHotkeys();
        UpdateFields();
    }

    public static void Destroy()
    {
        DestroyGhost();
        ExitEditorCamera();
        panel?.RemoveFromHierarchy();
        openHint?.RemoveFromHierarchy();
        panel = null;
        openHint = null;
        assetList = null;
        statusLabel = null;
        countLabel = null;
        selectedLabel = null;
        deleteLabel = null;
        voteLabel = null;
        placeButton = null;
        deleteButton = null;
        useAimButton = null;
        clearButton = null;
        voteYesButton = null;
        voteNoButton = null;
        posXField = null;
        posYField = null;
        posZField = null;
        rotXField = null;
        rotYField = null;
        rotZField = null;
        AssetButtons.Clear();
        isSetup = false;
        isVisible = false;
        hideFrame = -1;
    }

    private static void BuildPanel(VisualElement parent)
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 14f;
        header.style.paddingRight = 10f;
        header.style.paddingTop = 10f;
        header.style.paddingBottom = 9f;
        header.style.backgroundColor = new Color(0.04f, 0.045f, 0.05f, 0.98f);
        parent.Add(header);

        Label title = new Label("Warmup Obstacles");
        title.style.color = TextPrimary;
        title.style.fontSize = 20f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        Button closeButton = CreateButton("X", Hide);
        closeButton.style.width = 32f;
        closeButton.style.minWidth = 32f;
        header.Add(closeButton);

        VisualElement body = new ScrollView(ScrollViewMode.Vertical);
        body.style.flexGrow = 1f;
        body.style.paddingLeft = 12f;
        body.style.paddingRight = 12f;
        body.style.paddingTop = 10f;
        body.style.paddingBottom = 10f;
        parent.Add(body);

        statusLabel = new Label("Loading obstacle state...");
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.fontSize = 12f;
        statusLabel.style.marginBottom = 8f;
        body.Add(statusLabel);

        VisualElement countRow = CreateRow(body);
        Label countTitle = CreateLabel("Objects");
        countRow.Add(countTitle);
        countLabel = new Label("0/60");
        countLabel.style.color = AccentBlue;
        countLabel.style.fontSize = 13f;
        countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        countRow.Add(countLabel);

        AddSectionLabel(body, "Asset");
        assetList = new VisualElement();
        assetList.style.flexDirection = FlexDirection.Row;
        assetList.style.flexWrap = Wrap.Wrap;
        body.Add(assetList);

        selectedLabel = new Label();
        selectedLabel.style.color = TextSecondary;
        selectedLabel.style.fontSize = 12f;
        selectedLabel.style.marginTop = 4f;
        selectedLabel.style.marginBottom = 8f;
        body.Add(selectedLabel);

        AddSectionLabel(body, "Placement");
        VisualElement positionRow = CreateFieldRow(body, "Position");
        posXField = CreateFloatField("X", OnPositionChanged);
        posYField = CreateFloatField("Y", OnPositionChanged);
        posZField = CreateFloatField("Z", OnPositionChanged);
        positionRow.Add(posXField);
        positionRow.Add(posYField);
        positionRow.Add(posZField);

        VisualElement placementButtons = new VisualElement();
        placementButtons.style.flexDirection = FlexDirection.Row;
        placementButtons.style.flexWrap = Wrap.Wrap;
        placementButtons.style.marginBottom = 8f;
        body.Add(placementButtons);
        useAimButton = CreateButton("Use Aim", () =>
        {
            useAimPosition = true;
            UpdateSelectedLabel();
        });
        placementButtons.Add(useAimButton);
        placementButtons.Add(CreateButton("Y +0.1", () => NudgePosition(Vector3.up * 0.1f)));
        placementButtons.Add(CreateButton("Y -0.1", () => NudgePosition(Vector3.down * 0.1f)));

        VisualElement rotationRow = CreateFieldRow(body, "Rotation");
        rotXField = CreateFloatField("X", OnRotationChanged);
        rotYField = CreateFloatField("Y", OnRotationChanged);
        rotZField = CreateFloatField("Z", OnRotationChanged);
        rotationRow.Add(rotXField);
        rotationRow.Add(rotYField);
        rotationRow.Add(rotZField);

        VisualElement rotationButtons = new VisualElement();
        rotationButtons.style.flexDirection = FlexDirection.Row;
        rotationButtons.style.flexWrap = Wrap.Wrap;
        rotationButtons.style.marginBottom = 8f;
        body.Add(rotationButtons);
        rotationButtons.Add(CreateButton("Yaw -5", () => NudgeRotation(0f, -5f, 0f)));
        rotationButtons.Add(CreateButton("Yaw +5", () => NudgeRotation(0f, 5f, 0f)));
        rotationButtons.Add(CreateButton("X +15", () => NudgeRotation(15f, 0f, 0f)));
        rotationButtons.Add(CreateButton("Z +15", () => NudgeRotation(0f, 0f, 15f)));

        placeButton = CreateButton("Place", () =>
        {
            WarmupObstacleClient.PlaceObject(selectedAssetId, currentPosition, userEuler);
        });
        placeButton.style.backgroundColor = AccentBlue;
        body.Add(placeButton);

        AddSectionLabel(body, "Delete");
        deleteLabel = new Label("Aim at a placed object to delete it.");
        deleteLabel.style.color = TextSecondary;
        deleteLabel.style.fontSize = 12f;
        deleteLabel.style.whiteSpace = WhiteSpace.Normal;
        deleteLabel.style.marginBottom = 6f;
        body.Add(deleteLabel);
        deleteButton = CreateButton("Delete Highlighted", () =>
        {
            if (highlightedObjectId > 0)
            {
                WarmupObstacleClient.DeleteObject(highlightedObjectId);
            }
        });
        deleteButton.style.backgroundColor = ErrorRed;
        body.Add(deleteButton);

        AddSectionLabel(body, "Clear All");
        clearButton = CreateButton("Start Clear Vote", WarmupObstacleClient.StartClearVote);
        clearButton.style.backgroundColor = WarningOrange;
        body.Add(clearButton);

        voteLabel = new Label("No active clear vote.");
        voteLabel.style.color = TextSecondary;
        voteLabel.style.fontSize = 12f;
        voteLabel.style.whiteSpace = WhiteSpace.Normal;
        voteLabel.style.marginTop = 6f;
        voteLabel.style.marginBottom = 6f;
        body.Add(voteLabel);

        VisualElement voteButtons = new VisualElement();
        voteButtons.style.flexDirection = FlexDirection.Row;
        voteButtons.style.flexWrap = Wrap.Wrap;
        body.Add(voteButtons);
        voteYesButton = CreateButton("F1 Yes", () => WarmupObstacleClient.CastClearVote(true));
        voteYesButton.style.backgroundColor = ActiveGreen;
        voteButtons.Add(voteYesButton);
        voteNoButton = CreateButton("F2 No", () => WarmupObstacleClient.CastClearVote(false));
        voteNoButton.style.backgroundColor = ErrorRed;
        voteButtons.Add(voteNoButton);
    }

    private static void RebuildAssetListIfNeeded(WarmupObstacleProtocol.AssetInfo[] assets)
    {
        if (assetList == null || assets.Length == AssetButtons.Count && assets.All(asset => AssetButtons.ContainsKey(asset.Id)))
        {
            StyleAssetButtons();
            return;
        }

        assetList.Clear();
        AssetButtons.Clear();
        foreach (WarmupObstacleProtocol.AssetInfo asset in assets)
        {
            string assetId = asset.Id;
            Button button = CreateButton(asset.Label, () => SelectAsset(assetId));
            button.tooltip = $"{asset.Label} ({asset.Source})";
            button.style.marginBottom = 6f;
            assetList.Add(button);
            AssetButtons[assetId] = button;
        }

        StyleAssetButtons();
    }

    private static void SelectAsset(string assetId)
    {
        selectedAssetId = assetId;
        DestroyGhost();
        EnsureGhost();
        StyleAssetButtons();
        UpdateSelectedLabel();
    }

    private static void StyleAssetButtons()
    {
        foreach (KeyValuePair<string, Button> entry in AssetButtons)
        {
            bool active = string.Equals(entry.Key, selectedAssetId, StringComparison.OrdinalIgnoreCase);
            entry.Value.style.backgroundColor = active ? AccentBlue : BgButton;
            entry.Value.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    private static void ApplyVoteState(WarmupObstacleProtocol.ClearVoteState? vote)
    {
        if (voteLabel == null)
        {
            return;
        }

        bool active = vote != null && vote.IsActive;
        if (!active)
        {
            voteLabel.text = "No active clear vote.";
            voteLabel.style.color = TextSecondary;
            voteYesButton?.SetEnabled(false);
            voteNoButton?.SetEnabled(false);
            return;
        }

        voteLabel.text = $"{vote!.Initiator} started a clear vote. Yes {vote.YesCount}/{vote.RequiredVotes}, No {vote.NoCount}, {vote.SecondsRemaining:0}s left.";
        voteLabel.style.color = vote.LocalPlayerHasVoted ? ActiveGreen : WarningOrange;
        voteYesButton?.SetEnabled(true);
        voteNoButton?.SetEnabled(true);
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

    private static void TickGhost()
    {
        EnsureGhost();
        if (!ghostObject)
        {
            return;
        }

        Vector3 targetPosition = useAimPosition
            ? GetAimPoint()
            : manualPosition;
        ghostObject.transform.SetPositionAndRotation(targetPosition, WarmupObstaclePlacement.GetWorldRotation(selectedAssetId, userEuler));
        WarmupObstacleAssets.ClampBottomToIce(ghostObject.transform);
        currentPosition = ghostObject.transform.position;
        if (!useAimPosition)
        {
            manualPosition = currentPosition;
        }
    }

    private static void TickSelection()
    {
        highlightedObjectId = 0;
        if (editorCamera == null)
        {
            UpdateDeleteState();
            return;
        }

        Ray ray = GetEditorRay();
        if (WarmupObstacleClientWorld.TryFindLookedAtObstacle(ray, 120f, out int objectId))
        {
            highlightedObjectId = objectId;
        }

        UpdateDeleteState();
    }

    private static void HandleEditorHotkeys()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            WarmupObstacleClient.PlaceObject(selectedAssetId, currentPosition, userEuler);
        }

        if ((keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) && highlightedObjectId > 0)
        {
            WarmupObstacleClient.DeleteObject(highlightedObjectId);
        }

        if (keyboard.qKey.wasPressedThisFrame)
        {
            NudgeRotation(0f, -5f, 0f);
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            NudgeRotation(0f, 5f, 0f);
        }

        if (keyboard.cKey.wasPressedThisFrame)
        {
            NudgeRotation(15f, 0f, 0f);
        }

        if (keyboard.vKey.wasPressedThisFrame)
        {
            NudgeRotation(-15f, 0f, 0f);
        }

        if (keyboard.zKey.wasPressedThisFrame)
        {
            NudgeRotation(0f, 0f, -15f);
        }

        if (keyboard.xKey.wasPressedThisFrame)
        {
            NudgeRotation(0f, 0f, 15f);
        }

        if (keyboard.f1Key.wasPressedThisFrame)
        {
            WarmupObstacleClient.CastClearVote(true);
        }

        if (keyboard.f2Key.wasPressedThisFrame)
        {
            WarmupObstacleClient.CastClearVote(false);
        }
    }

    private static void EnsureGhost()
    {
        if (ghostObject && string.Equals(ghostAssetId, selectedAssetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DestroyGhost();
        ghostObject = WarmupObstacleAssets.CreateInstance(selectedAssetId, $"WarmupObstacleGhost_{selectedAssetId}", serverPhysical: false);
        if (!ghostObject)
        {
            return;
        }

        ghostAssetId = selectedAssetId;
        Object.DontDestroyOnLoad(ghostObject);
        WarmupObstacleAssets.PrepareGhost(ghostObject);
    }

    private static void DestroyGhost()
    {
        if (ghostObject)
        {
            Object.Destroy(ghostObject);
        }

        ghostObject = null;
        ghostAssetId = string.Empty;
    }

    private static void UpdateSelectedLabel()
    {
        if (selectedLabel == null)
        {
            return;
        }

        string mode = useAimPosition ? "aim" : "manual XYZ";
        string label = currentState?.Assets.FirstOrDefault(asset => string.Equals(asset.Id, selectedAssetId, StringComparison.OrdinalIgnoreCase))?.Label
                       ?? selectedAssetId;
        selectedLabel.text = $"{label} selected. Placement mode: {mode}. Yaw is free; X/Z rotation snap to 15 degrees.";
    }

    private static void UpdateDeleteState()
    {
        if (deleteLabel != null)
        {
            deleteLabel.text = highlightedObjectId > 0
                ? $"Obstacle {highlightedObjectId} highlighted. Press Delete/Backspace or use the button."
                : "Aim at a placed object to delete it.";
            deleteLabel.style.color = highlightedObjectId > 0 ? ErrorRed : TextSecondary;
        }

        deleteButton?.SetEnabled(highlightedObjectId > 0);
    }

    private static void UpdateFields()
    {
        suppressFieldEvents = true;
        posXField?.SetValueWithoutNotify(RoundField(currentPosition.x));
        posYField?.SetValueWithoutNotify(RoundField(currentPosition.y));
        posZField?.SetValueWithoutNotify(RoundField(currentPosition.z));
        Vector3 sanitizedEuler = WarmupObstaclePlacement.SanitizeUserEuler(userEuler);
        if (sanitizedEuler != userEuler)
        {
            userEuler = sanitizedEuler;
        }

        rotXField?.SetValueWithoutNotify(RoundField(userEuler.x));
        rotYField?.SetValueWithoutNotify(RoundField(userEuler.y));
        rotZField?.SetValueWithoutNotify(RoundField(userEuler.z));
        suppressFieldEvents = false;
    }

    private static void OnPositionChanged()
    {
        if (suppressFieldEvents)
        {
            return;
        }

        useAimPosition = false;
        manualPosition = new Vector3(
            posXField?.value ?? currentPosition.x,
            posYField?.value ?? currentPosition.y,
            posZField?.value ?? currentPosition.z);
        UpdateSelectedLabel();
    }

    private static void OnRotationChanged()
    {
        if (suppressFieldEvents)
        {
            return;
        }

        userEuler = WarmupObstaclePlacement.SanitizeUserEuler(new Vector3(
            rotXField?.value ?? userEuler.x,
            rotYField?.value ?? userEuler.y,
            rotZField?.value ?? userEuler.z));
    }

    private static void NudgePosition(Vector3 delta)
    {
        manualPosition = currentPosition + delta;
        useAimPosition = false;
        UpdateSelectedLabel();
    }

    private static void NudgeRotation(float x, float y, float z)
    {
        userEuler = WarmupObstaclePlacement.SanitizeUserEuler(userEuler + new Vector3(x, y, z));
    }

    private static void EnterEditorCamera()
    {
        if (editorCamera)
        {
            return;
        }

        previousCamera = CameraManager.GetActiveCamera();
        Vector3 startPosition = Vector3.zero;
        Quaternion startRotation = Quaternion.Euler(55f, 0f, 0f);
        float fov = SettingsManager.Fov;
        Camera? sourceCamera = null;
        if (previousCamera && previousCamera.UnityCamera)
        {
            sourceCamera = previousCamera.UnityCamera;
            startPosition = previousCamera.transform.position;
            startRotation = previousCamera.transform.rotation;
            fov = sourceCamera.fieldOfView;
            previousUnityCameraEnabled = sourceCamera.enabled;
            previousAudioListenerEnabled = previousCamera.AudioListener && previousCamera.AudioListener.enabled;
            hasPreviousCameraComponentState = true;
        }
        else
        {
            Player? localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
            if (localPlayer?.PlayerBody != null)
            {
                startPosition = localPlayer.PlayerBody.transform.position + Vector3.up * 5f - localPlayer.PlayerBody.transform.forward * 7f;
                startRotation = Quaternion.LookRotation((localPlayer.PlayerBody.transform.position + Vector3.up) - startPosition, Vector3.up);
            }
        }

        editorCameraObject = new GameObject("WarmupObstacleEditorCamera");
        Object.DontDestroyOnLoad(editorCameraObject);
        editorCamera = editorCameraObject.AddComponent<Camera>();
        if (sourceCamera)
        {
            editorCamera.CopyFrom(sourceCamera);
            editorCamera.depth = sourceCamera.depth + 10f;
        }

        editorCamera.fieldOfView = fov;
        editorCamera.nearClipPlane = 0.03f;
        editorCamera.farClipPlane = 500f;
        editorCameraObject.AddComponent<AudioListener>();
        editorCameraObject.transform.SetPositionAndRotation(startPosition, startRotation);
        if (sourceCamera)
        {
            sourceCamera.enabled = false;
        }

        if (previousCamera && previousCamera.AudioListener)
        {
            previousCamera.AudioListener.enabled = false;
        }

        Vector3 euler = startRotation.eulerAngles;
        cameraPitch = NormalizeSignedAngle(euler.x);
        cameraYaw = euler.y;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    private static void ExitEditorCamera()
    {
        if (editorCameraObject)
        {
            Object.Destroy(editorCameraObject);
        }

        editorCameraObject = null;
        editorCamera = null;
        if (previousCamera && hasPreviousCameraComponentState)
        {
            if (previousCamera.UnityCamera)
            {
                previousCamera.UnityCamera.enabled = previousUnityCameraEnabled;
            }

            if (previousCamera.AudioListener)
            {
                previousCamera.AudioListener.enabled = previousAudioListenerEnabled;
            }
        }

        previousCamera = null;
        previousUnityCameraEnabled = false;
        previousAudioListenerEnabled = false;
        hasPreviousCameraComponentState = false;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    private static void TickEditorCamera()
    {
        if (editorCameraObject == null)
        {
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        Mouse? mouse = Mouse.current;
        if (keyboard == null)
        {
            return;
        }

        Transform transform = editorCameraObject.transform;
        float speed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 15f : 7.5f;
        Vector3 move = Vector3.zero;
        if (keyboard.wKey.isPressed)
        {
            move += transform.forward;
        }

        if (keyboard.sKey.isPressed)
        {
            move -= transform.forward;
        }

        if (keyboard.dKey.isPressed)
        {
            move += transform.right;
        }

        if (keyboard.aKey.isPressed)
        {
            move -= transform.right;
        }

        if (keyboard.spaceKey.isPressed)
        {
            move += Vector3.up;
        }

        if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
        {
            move -= Vector3.up;
        }

        if (move.sqrMagnitude > 0.001f)
        {
            transform.position += move.normalized * speed * Time.unscaledDeltaTime;
        }

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            cameraYaw += delta.x * 0.12f;
            cameraPitch = Mathf.Clamp(cameraPitch - delta.y * 0.12f, -85f, 85f);
            transform.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        }
    }

    private static Vector3 GetAimPoint()
    {
        if (editorCamera == null)
        {
            return currentPosition;
        }

        Ray ray = GetEditorRay();
        Plane icePlane = new Plane(Vector3.up, Vector3.zero);
        if (icePlane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        return editorCamera.transform.position + editorCamera.transform.forward * 7f;
    }

    private static Ray GetEditorRay()
    {
        if (editorCamera == null)
        {
            return new Ray(Vector3.zero, Vector3.forward);
        }

        Mouse? mouse = Mouse.current;
        Vector2 screenPosition = mouse != null
            ? mouse.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        return editorCamera.ScreenPointToRay(screenPosition);
    }

    private static VisualElement CreateRow(VisualElement parent)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 8f;
        row.style.paddingLeft = 10f;
        row.style.paddingRight = 10f;
        row.style.paddingTop = 8f;
        row.style.paddingBottom = 8f;
        row.style.backgroundColor = BgRow;
        row.style.borderTopLeftRadius = 4f;
        row.style.borderTopRightRadius = 4f;
        row.style.borderBottomLeftRadius = 4f;
        row.style.borderBottomRightRadius = 4f;
        parent.Add(row);
        return row;
    }

    private static VisualElement CreateFieldRow(VisualElement parent, string label)
    {
        VisualElement wrapper = new VisualElement();
        wrapper.style.marginBottom = 7f;
        parent.Add(wrapper);
        Label rowLabel = CreateLabel(label);
        rowLabel.style.marginBottom = 4f;
        wrapper.Add(rowLabel);
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        wrapper.Add(row);
        return row;
    }

    private static FloatField CreateFloatField(string label, Action onChanged)
    {
        FloatField field = new FloatField(label);
        field.style.width = 116f;
        field.style.marginRight = 4f;
        field.style.color = TextPrimary;
        field.RegisterValueChangedCallback(_ => onChanged());
        return field;
    }

    private static Button CreateButton(string text, Action onClick)
    {
        Button button = new Button(onClick)
        {
            text = text
        };
        button.style.minWidth = 82f;
        button.style.height = 32f;
        button.style.marginRight = 6f;
        button.style.marginBottom = 6f;
        button.style.backgroundColor = BgButton;
        button.style.color = TextPrimary;
        button.style.fontSize = 12f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.paddingLeft = 8f;
        button.style.paddingRight = 8f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderTopLeftRadius = 4f;
        button.style.borderTopRightRadius = 4f;
        button.style.borderBottomLeftRadius = 4f;
        button.style.borderBottomRightRadius = 4f;
        return button;
    }

    private static Label CreateLabel(string text)
    {
        Label label = new Label(text);
        label.style.color = TextPrimary;
        label.style.fontSize = 13f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private static void AddSectionLabel(VisualElement parent, string text)
    {
        Label label = new Label(text);
        label.style.color = TextPrimary;
        label.style.fontSize = 15f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = 4f;
        label.style.marginBottom = 7f;
        parent.Add(label);
    }

    private static void SetGameInputSuppressed(bool suppressed)
    {
        try
        {
            GlobalStateManager.SetUIState(new Dictionary<string, object>
            {
                { "isMouseRequired", suppressed }
            });
        }
        catch
        {
        }
    }

    private static VisualElement? GetRoot()
    {
        return MonoBehaviourSingleton<UIManager>.Instance?.RootVisualElement;
    }

    private static float RoundField(float value)
    {
        return float.Parse(value.ToString("0.###", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static float NormalizeSignedAngle(float angle)
    {
        while (angle > 180f)
        {
            angle -= 360f;
        }

        while (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }
}
