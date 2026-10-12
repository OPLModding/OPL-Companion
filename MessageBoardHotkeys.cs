using UnityEngine.InputSystem;
namespace OPLServerBanners;

internal static class MessageBoardHotkeys
{
    public const string ToggleKeyLabel = "F11";

    public static string ToggleHint => $"Esc closes. {ToggleKeyLabel} toggles.";

    public static string ReopenFooterNote => $"Press {ToggleKeyLabel} to open this again later.";

    public static string RetryFooterNote => $"Press {ToggleKeyLabel} to try again later.";

    public static string ReopenAnyTimeText =>
        $"This dashboard opens automatically when players join and can be reopened any time with {ToggleKeyLabel}.";

    public static bool IsTogglePressed(Keyboard keyboard)
    {
        return keyboard.f11Key.wasPressedThisFrame;
    }
}
