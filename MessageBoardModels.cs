using System.Collections.Generic;

namespace OPLServerBanners;

public sealed class MessageBoardDocument
{
    public int SchemaVersion { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public bool ShowOnJoin { get; set; } = true;

    public int OpenDelayMs { get; set; } = 500;

    public string Title { get; set; } = "Welcome to OPL";

    public string Subtitle { get; set; } = "Server info, rules, and links";

    public string FooterNote { get; set; } = MessageBoardHotkeys.ReopenFooterNote;

    public List<MessageBoardTab> Tabs { get; set; } = new();
}

public sealed class MessageBoardTab
{
    public string Id { get; set; } = "home";

    public string Label { get; set; } = "Home";

    public List<MessageBoardBlock> Blocks { get; set; } = new();
}

public sealed class MessageBoardBlock
{
    public string Type { get; set; } = MessageBoardBlockTypes.Paragraph;

    public string Title { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Tone { get; set; } = string.Empty;

    public string TitleTone { get; set; } = string.Empty;

    public string TextTone { get; set; } = string.Empty;

    public bool DefaultExpanded { get; set; } = true;

    public float Height { get; set; } = 12f;

    public List<string> Items { get; set; } = new();

    public List<MessageBoardRuleItem> RuleItems { get; set; } = new();

    public List<MessageBoardActionButton> Buttons { get; set; } = new();

    public List<MessageBoardBlock> Blocks { get; set; } = new();
}

public sealed class MessageBoardRuleItem
{
    public string Title { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class MessageBoardActionButton
{
    public string Label { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Tone { get; set; } = string.Empty;

    public string TextTone { get; set; } = string.Empty;

    public string BackgroundTone { get; set; } = string.Empty;
}

internal static class MessageBoardBlockTypes
{
    public const string Heading = "heading";
    public const string Paragraph = "paragraph";
    public const string BulletList = "bulletList";
    public const string RuleList = "ruleList";
    public const string ButtonRow = "buttonRow";
    public const string Divider = "divider";
    public const string Spacer = "spacer";
    public const string CollapsibleSection = "collapsibleSection";
    public const string AnnouncementBox = "announcementBox";
}

internal static class MessageBoardTones
{
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Neutral = "neutral";
}
