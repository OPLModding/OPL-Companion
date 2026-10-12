using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OPLServerBanners;

internal sealed class MessageBoardLoadResult
{
    private MessageBoardLoadResult()
    {
    }

    public bool Success { get; private set; }

    public string RawJson { get; private set; } = string.Empty;

    public string Revision { get; private set; } = string.Empty;

    public string ErrorMessage { get; private set; } = string.Empty;

    public MessageBoardDocument? Document { get; private set; }

    public static MessageBoardLoadResult CreateSuccess(MessageBoardDocument document, string rawJson, string revision)
    {
        return new MessageBoardLoadResult
        {
            Success = true,
            Document = document,
            RawJson = rawJson,
            Revision = revision
        };
    }

    public static MessageBoardLoadResult CreateFailure(string errorMessage)
    {
        return new MessageBoardLoadResult
        {
            Success = false,
            ErrorMessage = errorMessage ?? string.Empty
        };
    }
}

internal static class MessageBoardContentManager
{
    public const string FolderName = "MessageBoard";
    public const string BoardFileName = "board.json";
    public const string ReadmeFileName = "README.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true
    };

    private static MessageBoardLoadResult? cachedLoadResult;
    private static string cachedBoardPath = string.Empty;
    private static long cachedBoardLength = -1L;
    private static DateTime cachedBoardLastWriteUtc = DateTime.MinValue;
    private static bool filesEnsured;

    public static string GetBoardDirectoryPath()
    {
        return Path.Combine(ServerFileSystemPaths.GetGameRootPath(), FolderName);
    }

    public static string GetBoardPath()
    {
        return Path.Combine(GetBoardDirectoryPath(), BoardFileName);
    }

    public static string GetReadmePath()
    {
        return Path.Combine(GetBoardDirectoryPath(), ReadmeFileName);
    }

    public static void EnsureFilesExist()
    {
        string directoryPath = GetBoardDirectoryPath();
        TryMigrateLegacyDirectory(directoryPath);
        Directory.CreateDirectory(directoryPath);

        WriteTextIfMissing(GetBoardPath(), SerializeDocument(CreateDefaultDocument()));
        WriteTextIfMissing(GetReadmePath(), GetDefaultReadmeContents());
        filesEnsured = true;
    }

    public static MessageBoardLoadResult LoadBoardFromDisk()
    {
        try
        {
            EnsureFilesExistIfNeeded();

            string boardPath = GetBoardPath();
            FileInfo boardFileInfo = new FileInfo(boardPath);
            if (TryGetCachedLoadResult(boardPath, boardFileInfo, out MessageBoardLoadResult? cachedResult))
            {
                return cachedResult!;
            }

            string json = File.ReadAllText(boardPath);
            if (!TryDeserializeDocument(json, out MessageBoardDocument? document, out string errorMessage))
            {
                MessageBoardLoadResult failure = MessageBoardLoadResult.CreateFailure(errorMessage);
                CacheLoadResult(boardPath, boardFileInfo, failure);
                return failure;
            }

            string normalizedJson = SerializeDocument(document!);
            string revision = ComputeRevision(normalizedJson);
            MessageBoardLoadResult success = MessageBoardLoadResult.CreateSuccess(document!, normalizedJson, revision);
            CacheLoadResult(boardPath, boardFileInfo, success);
            return success;
        }
        catch (Exception exception)
        {
            string errorMessage = $"Failed to load message board: {exception.Message}";
            Plugin.LogError(errorMessage);
            return MessageBoardLoadResult.CreateFailure(errorMessage);
        }
    }

    private static void EnsureFilesExistIfNeeded()
    {
        if (filesEnsured && File.Exists(GetBoardPath()))
        {
            return;
        }

        EnsureFilesExist();
    }

    private static bool TryGetCachedLoadResult(
        string boardPath,
        FileInfo boardFileInfo,
        out MessageBoardLoadResult? loadResult)
    {
        loadResult = null;
        if (cachedLoadResult == null
            || !string.Equals(cachedBoardPath, boardPath, StringComparison.OrdinalIgnoreCase)
            || !boardFileInfo.Exists
            || cachedBoardLength != boardFileInfo.Length
            || cachedBoardLastWriteUtc != boardFileInfo.LastWriteTimeUtc)
        {
            return false;
        }

        loadResult = cachedLoadResult;
        return true;
    }

    private static void CacheLoadResult(string boardPath, FileInfo boardFileInfo, MessageBoardLoadResult loadResult)
    {
        if (!boardFileInfo.Exists)
        {
            return;
        }

        cachedBoardPath = boardPath;
        cachedBoardLength = boardFileInfo.Length;
        cachedBoardLastWriteUtc = boardFileInfo.LastWriteTimeUtc;
        cachedLoadResult = loadResult;
    }

    public static bool TryDeserializeDocument(string json, out MessageBoardDocument? document, out string errorMessage)
    {
        document = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "The server message board file is empty.";
            return false;
        }

        try
        {
            MessageBoardDocument? parsed = JsonSerializer.Deserialize<MessageBoardDocument>(json, JsonOptions);
            document = NormalizeDocument(parsed);
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"The server message board JSON is invalid: {exception.Message}";
            Plugin.LogError(errorMessage);
            return false;
        }
    }

    private static void WriteTextIfMissing(string path, string contents)
    {
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, contents);
    }

    private static void TryMigrateLegacyDirectory(string targetDirectoryPath)
    {
        string legacyDirectoryPath = GetLegacyBoardDirectoryPath();
        if (!Directory.Exists(legacyDirectoryPath))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectoryPath);
        CopyDirectoryContentsIfMissing(legacyDirectoryPath, targetDirectoryPath);
    }

    private static string GetLegacyBoardDirectoryPath()
    {
        return Path.Combine(ServerFileSystemPaths.GetPluginDirectoryPath(), FolderName);
    }

    private static void CopyDirectoryContentsIfMissing(string sourceDirectoryPath, string targetDirectoryPath)
    {
        foreach (string directoryPath in Directory.GetDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = GetRelativePathFromDirectory(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetDirectoryPath, relativePath));
        }

        foreach (string filePath in Directory.GetFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = GetRelativePathFromDirectory(sourceDirectoryPath, filePath);
            string destinationPath = Path.Combine(targetDirectoryPath, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (!File.Exists(destinationPath))
            {
                File.Copy(filePath, destinationPath, overwrite: false);
            }
        }
    }

    private static string GetRelativePathFromDirectory(string sourceDirectoryPath, string targetPath)
    {
        string normalizedSourceDirectoryPath = sourceDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (targetPath.StartsWith(normalizedSourceDirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return targetPath.Substring(normalizedSourceDirectoryPath.Length);
        }

        return Path.GetFileName(targetPath);
    }

    private static string SerializeDocument(MessageBoardDocument document)
    {
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static MessageBoardDocument NormalizeDocument(MessageBoardDocument? document)
    {
        MessageBoardDocument source = document ?? CreateDefaultDocument();

        MessageBoardDocument normalized = new MessageBoardDocument
        {
            SchemaVersion = source.SchemaVersion > 0 ? source.SchemaVersion : 1,
            Enabled = source.Enabled,
            ShowOnJoin = source.ShowOnJoin,
            OpenDelayMs = ClampInt(source.OpenDelayMs, 0, 10000),
            Title = Fallback(source.Title, "Welcome to OPL"),
            Subtitle = (source.Subtitle ?? string.Empty).Trim(),
            FooterNote = Fallback(source.FooterNote, MessageBoardHotkeys.ReopenFooterNote),
            Tabs = new List<MessageBoardTab>()
        };

        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (MessageBoardTab tab in source.Tabs ?? new List<MessageBoardTab>())
        {
            MessageBoardTab normalizedTab = NormalizeTab(tab, normalized.Tabs.Count + 1, usedIds);
            normalized.Tabs.Add(normalizedTab);
        }

        if (normalized.Tabs.Count == 0)
        {
            normalized.Tabs.Add(new MessageBoardTab
            {
                Id = "home",
                Label = "Home",
                Blocks = new List<MessageBoardBlock>
                {
                    new()
                    {
                        Type = MessageBoardBlockTypes.Paragraph,
                        Text = "This server message board does not have any tabs yet."
                    }
                }
            });
        }

        return normalized;
    }

    private static MessageBoardTab NormalizeTab(MessageBoardTab tab, int tabIndex, HashSet<string> usedIds)
    {
        string label = Fallback(tab?.Label, $"Tab {tabIndex}");
        string requestedId = Fallback(tab?.Id, label);
        string normalizedId = MakeUniqueId(requestedId, usedIds, $"tab-{tabIndex}");

        List<MessageBoardBlock> normalizedBlocks = new();
        foreach (MessageBoardBlock block in tab?.Blocks ?? new List<MessageBoardBlock>())
        {
            MessageBoardBlock normalizedBlock = NormalizeBlock(block);
            normalizedBlocks.Add(normalizedBlock);
        }

        if (normalizedBlocks.Count == 0)
        {
            normalizedBlocks.Add(new MessageBoardBlock
            {
                Type = MessageBoardBlockTypes.Paragraph,
                Text = "This tab is empty."
            });
        }

        return new MessageBoardTab
        {
            Id = normalizedId,
            Label = label,
            Blocks = normalizedBlocks
        };
    }

    private static MessageBoardBlock NormalizeBlock(MessageBoardBlock block)
    {
        List<MessageBoardBlock> nestedBlocks = new();
        foreach (MessageBoardBlock nestedBlock in block?.Blocks ?? new List<MessageBoardBlock>())
        {
            nestedBlocks.Add(NormalizeBlock(nestedBlock));
        }

        List<string> items = new();
        foreach (string item in block?.Items ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                items.Add(item.Trim());
            }
        }

        List<MessageBoardRuleItem> ruleItems = new();
        foreach (MessageBoardRuleItem ruleItem in block?.RuleItems ?? new List<MessageBoardRuleItem>())
        {
            if (ruleItem == null)
            {
                continue;
            }

            string title = Fallback(ruleItem.Title, "Rule");
            string text = (ruleItem.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            ruleItems.Add(new MessageBoardRuleItem
            {
                Title = title,
                Text = text
            });
        }

        List<MessageBoardActionButton> buttons = new();
        foreach (MessageBoardActionButton button in block?.Buttons ?? new List<MessageBoardActionButton>())
        {
            if (button == null)
            {
                continue;
            }

            string label = Fallback(button.Label, "Open Link");
            string url = (button.Url ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            buttons.Add(new MessageBoardActionButton
            {
                Label = label,
                Url = url,
                Tone = NormalizeOptionalTone(button.Tone),
                TextTone = NormalizeOptionalTone(button.TextTone),
                BackgroundTone = NormalizeOptionalTone(button.BackgroundTone)
            });
        }

        string blockType = NormalizeBlockType(block?.Type);
        return new MessageBoardBlock
        {
            Type = blockType,
            Title = (block?.Title ?? string.Empty).Trim(),
            Text = (block?.Text ?? string.Empty).Trim(),
            Tone = NormalizeOptionalTone(block?.Tone),
            TitleTone = NormalizeOptionalTone(block?.TitleTone),
            TextTone = NormalizeOptionalTone(block?.TextTone),
            DefaultExpanded = block?.DefaultExpanded ?? true,
            Height = ClampFloat(block?.Height ?? 12f, 4f, 120f),
            Items = items,
            RuleItems = ruleItems,
            Buttons = buttons,
            Blocks = nestedBlocks
        };
    }

    private static MessageBoardDocument CreateDefaultDocument()
    {
        return new MessageBoardDocument
        {
            Title = "Welcome to OPL",
            Subtitle = "Server info, announcements, and quick links",
            FooterNote = "Edit MessageBoard/board.json on the server to change this dashboard.",
            Tabs = new List<MessageBoardTab>
            {
                new()
                {
                    Id = "about",
                    Label = "About",
                    Blocks = new List<MessageBoardBlock>
                    {
                        new()
                        {
                            Type = MessageBoardBlockTypes.Heading,
                            Text = "Welcome"
                        },
                        new()
                        {
                            Type = MessageBoardBlockTypes.Paragraph,
                            Text = MessageBoardHotkeys.ReopenAnyTimeText
                        },
                        new()
                        {
                            Type = MessageBoardBlockTypes.AnnouncementBox,
                            Tone = MessageBoardTones.Info,
                            Title = "Server Notice",
                            Text = "Use this area for league updates, special event callouts, or anything you want everyone to see right away."
                        },
                        new()
                        {
                            Type = MessageBoardBlockTypes.CollapsibleSection,
                            Title = "What belongs here?",
                            Tone = MessageBoardTones.Neutral,
                            DefaultExpanded = true,
                            Blocks = new List<MessageBoardBlock>
                            {
                                new()
                                {
                                    Type = MessageBoardBlockTypes.BulletList,
                                    Items = new List<string>
                                    {
                                        "League or community overview",
                                        "How players should use the server",
                                        "Where to find rules, Discord, or schedules"
                                    }
                                }
                            }
                        }
                    }
                },
                new()
                {
                    Id = "rules",
                    Label = "Rules",
                    Blocks = new List<MessageBoardBlock>
                    {
                        new()
                        {
                            Type = MessageBoardBlockTypes.AnnouncementBox,
                            Tone = MessageBoardTones.Warning,
                            Title = "Keep it fun",
                            Text = "Put your most important conduct note here so it is hard to miss."
                        },
                        new()
                        {
                            Type = MessageBoardBlockTypes.RuleList,
                            RuleItems = new List<MessageBoardRuleItem>
                            {
                                new() { Title = "Respect", Text = "Treat everyone well and keep chat readable." },
                                new() { Title = "Sportsmanship", Text = "Avoid griefing, crease harassment, and deliberate disruption." },
                                new() { Title = "Cheating", Text = "No exploits, hacks, or anything that gives an unfair advantage." }
                            }
                        }
                    }
                },
                new()
                {
                    Id = "links",
                    Label = "Links",
                    Blocks = new List<MessageBoardBlock>
                    {
                        new()
                        {
                            Type = MessageBoardBlockTypes.Paragraph,
                            Text = "Add buttons here for your community links."
                        },
                        new()
                        {
                            Type = MessageBoardBlockTypes.ButtonRow,
                            Buttons = new List<MessageBoardActionButton>
                            {
                                new() { Label = "Discord", Url = "https://example.com/discord" },
                                new() { Label = "Rules", Url = "https://example.com/rules" }
                            }
                        }
                    }
                }
            }
        };
    }

    private static string GetDefaultReadmeContents()
    {
        return
@"MessageBoard
============

Edit `board.json` to control the panel players see.

Basic flow:
- `enabled` turns the whole panel on or off.
- `showOnJoin` decides whether it opens automatically.
- `tabs` is the panel itself.
- Each tab contains a list of `blocks`.

Top-level template:
{
  ""schemaVersion"": 1,
  ""enabled"": true,
  ""showOnJoin"": true,
  ""openDelayMs"": 500,
  ""title"": ""Welcome to OPL"",
  ""subtitle"": ""Server info and links"",
  ""footerNote"": ""__MESSAGE_BOARD_REOPEN_FOOTER__"",
  ""tabs"": []
}

Each tab needs:
- `id` : a unique id like `about` or `rules`
- `label` : the button text on the left
- `blocks` : the content shown when that tab is open

Minimal tab example:
{
  ""id"": ""about"",
  ""label"": ""About"",
  ""blocks"": [
    { ""type"": ""heading"", ""text"": ""Welcome"" },
    { ""type"": ""paragraph"", ""text"": ""Thanks for joining the server."" }
  ]
}

Block types:
- `heading` : `text`, optional `tone`, optional `textTone`
- `paragraph` : `text`, optional `tone`, optional `textTone`
- `bulletList` : optional `title`, `items`, optional `tone`, optional `titleTone`, optional `textTone`
- `ruleList` : optional `title`, `ruleItems`, optional `tone`, optional `titleTone`, optional `textTone`
- `buttonRow` : optional `title`, optional `tone`, optional `titleTone`, `buttons`
- `buttonRow.buttons[]` : `label`, `url`, optional `tone`, optional `textTone`, optional `backgroundTone`
- `divider` : no extra fields
- `spacer` : `height`
- `collapsibleSection` : `title`, optional `tone`, optional `titleTone`, optional `textTone`, `defaultExpanded`, `blocks`
- `announcementBox` : `title`, `text`, optional `tone`, optional `titleTone`, optional `textTone`, optional `blocks`

Tone values:
- Named tones: `info`, `success`, `warning`, `error`, `neutral`
- Hex colors also work: `#4DB6FF`, `#FFAA00`, `#7ED957`

How tone fields behave:
- `tone` controls the main accent for the block.
- `titleTone` overrides title-like text when the block has a title.
- `textTone` overrides body text.
- `buttonRow` buttons can each set their own `tone` for the accent and border.
- `buttonRow` buttons can set `textTone` for the label color.
- `buttonRow` buttons can set `backgroundTone` for a filled button color.
- If a button does not set `backgroundTone`, it keeps the default dark fill.
- `announcementBox` can also render nested child `blocks`, such as bullet lists.
- `divider` and `spacer` ignore tone fields.
- Nested blocks inside `collapsibleSection` use their own tones; tones do not cascade into child blocks.

Examples:
{
  ""type"": ""announcementBox"",
  ""tone"": ""warning"",
  ""titleTone"": ""#FFE08A"",
  ""textTone"": ""neutral"",
  ""title"": ""Tonight's Maintenance"",
  ""text"": ""Servers restart at 9:00 PM."",
  ""blocks"": [
    {
      ""type"": ""bulletList"",
      ""tone"": ""warning"",
      ""items"": [
        ""Expected downtime: 15 minutes"",
        ""Matchmaking will be unavailable""
      ]
    }
  ]
}

{
  ""type"": ""paragraph"",
  ""tone"": ""success"",
  ""text"": ""You can tint a plain paragraph by setting tone.""
}

{
  ""type"": ""buttonRow"",
  ""title"": ""Links"",
  ""tone"": ""neutral"",
  ""titleTone"": ""info"",
  ""buttons"": [
    { ""label"": ""Discord"", ""url"": ""https://example.com/discord"", ""backgroundTone"": ""success"", ""textTone"": ""#FFFFFF"" },
    { ""label"": ""Rules"", ""url"": ""https://example.com/rules"", ""tone"": ""warning"" },
    { ""label"": ""Stats"", ""url"": ""https://example.com/stats"", ""backgroundTone"": ""#4DB6FF"", ""textTone"": ""#081018"" }
  ]
}

{
  ""type"": ""collapsibleSection"",
  ""title"": ""League Rules"",
  ""tone"": ""#4DB6FF"",
  ""titleTone"": ""#FFE08A"",
  ""defaultExpanded"": true,
  ""blocks"": [
    { ""type"": ""paragraph"", ""text"": ""Explain the rule set here."" }
  ]
}

Quick tips:
- Keep valid JSON: commas matter, and all text must be in quotes.
- If something is just body text, use a `paragraph`.
- Use `announcementBox` for callouts you want to stand out.
- Use `collapsibleSection` for longer content you want players to expand.
".Replace("__MESSAGE_BOARD_REOPEN_FOOTER__", MessageBoardHotkeys.ReopenFooterNote);
    }

    private static string ComputeRevision(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    private static string NormalizeBlockType(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "heading" => MessageBoardBlockTypes.Heading,
            "paragraph" => MessageBoardBlockTypes.Paragraph,
            "bulletlist" => MessageBoardBlockTypes.BulletList,
            "bullets" => MessageBoardBlockTypes.BulletList,
            "rulelist" => MessageBoardBlockTypes.RuleList,
            "rules" => MessageBoardBlockTypes.RuleList,
            "buttonrow" => MessageBoardBlockTypes.ButtonRow,
            "buttons" => MessageBoardBlockTypes.ButtonRow,
            "divider" => MessageBoardBlockTypes.Divider,
            "spacer" => MessageBoardBlockTypes.Spacer,
            "collapsiblesection" => MessageBoardBlockTypes.CollapsibleSection,
            "collapsible" => MessageBoardBlockTypes.CollapsibleSection,
            "section" => MessageBoardBlockTypes.CollapsibleSection,
            "announcementbox" => MessageBoardBlockTypes.AnnouncementBox,
            "announcement" => MessageBoardBlockTypes.AnnouncementBox,
            "callout" => MessageBoardBlockTypes.AnnouncementBox,
            _ => MessageBoardBlockTypes.Paragraph
        };
    }

    private static string NormalizeOptionalTone(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        string normalized = trimmed.ToLowerInvariant();
        return normalized switch
        {
            MessageBoardTones.Info => MessageBoardTones.Info,
            MessageBoardTones.Success => MessageBoardTones.Success,
            MessageBoardTones.Warning => MessageBoardTones.Warning,
            MessageBoardTones.Error => MessageBoardTones.Error,
            MessageBoardTones.Neutral => MessageBoardTones.Neutral,
            _ when IsHexColor(trimmed) => trimmed,
            _ => string.Empty
        };
    }

    private static bool IsHexColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
        {
            return false;
        }

        int hexDigits = value.Length - 1;
        if (hexDigits != 3 && hexDigits != 4 && hexDigits != 6 && hexDigits != 8)
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string MakeUniqueId(string requestedId, HashSet<string> usedIds, string fallback)
    {
        StringBuilder builder = new();
        foreach (char character in requestedId ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if ((character == '-' || character == '_' || character == ' ') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string baseId = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = fallback;
        }

        string uniqueId = baseId;
        int suffix = 2;
        while (!usedIds.Add(uniqueId))
        {
            uniqueId = $"{baseId}-{suffix}";
            suffix++;
        }

        return uniqueId;
    }

    private static string Fallback(string? value, string fallback)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed!;
    }

    private static int ClampInt(int value, int minValue, int maxValue)
    {
        if (value < minValue)
        {
            return minValue;
        }

        return value > maxValue ? maxValue : value;
    }

    private static float ClampFloat(float value, float minValue, float maxValue)
    {
        if (value < minValue)
        {
            return minValue;
        }

        return value > maxValue ? maxValue : value;
    }
}
