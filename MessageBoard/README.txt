MessageBoard
============

Edit `board.json` to control the panel players see.

Basic flow:
- `enabled` turns the whole panel on or off.
- `showOnJoin` decides whether it opens automatically.
- `tabs` is the panel itself.
- Each tab contains a list of `blocks`.

Top-level template:
{
  "schemaVersion": 1,
  "enabled": true,
  "showOnJoin": true,
  "openDelayMs": 500,
  "title": "Welcome to OPL",
  "subtitle": "Server info and links",
  "footerNote": "Press F11 to open this again later.",
  "tabs": []
}

Each tab needs:
- `id` : a unique id like `about` or `rules`
- `label` : the button text on the left
- `blocks` : the content shown when that tab is open

Minimal tab example:
{
  "id": "about",
  "label": "About",
  "blocks": [
    { "type": "heading", "text": "Welcome" },
    { "type": "paragraph", "text": "Thanks for joining the server." }
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
  "type": "announcementBox",
  "tone": "warning",
  "titleTone": "#FFE08A",
  "textTone": "neutral",
  "title": "Tonight's Maintenance",
  "text": "Servers restart at 9:00 PM.",
  "blocks": [
    {
      "type": "bulletList",
      "tone": "warning",
      "items": [
        "Expected downtime: 15 minutes",
        "Matchmaking will be unavailable"
      ]
    }
  ]
}

{
  "type": "paragraph",
  "tone": "success",
  "text": "You can tint a plain paragraph by setting tone."
}

{
  "type": "buttonRow",
  "title": "Links",
  "tone": "neutral",
  "titleTone": "info",
  "buttons": [
    { "label": "Discord", "url": "https://example.com/discord", "backgroundTone": "success", "textTone": "#FFFFFF" },
    { "label": "Rules", "url": "https://example.com/rules", "tone": "warning" },
    { "label": "Stats", "url": "https://example.com/stats", "backgroundTone": "#4DB6FF", "textTone": "#081018" }
  ]
}

{
  "type": "collapsibleSection",
  "title": "League Rules",
  "tone": "#4DB6FF",
  "titleTone": "#FFE08A",
  "defaultExpanded": true,
  "blocks": [
    { "type": "paragraph", "text": "Explain the rule set here." }
  ]
}

Quick tips:
- Keep valid JSON: commas matter, and all text must be in quotes.
- If something is just body text, use a `paragraph`.
- Use `announcementBox` for callouts you want to stand out.
- Use `collapsibleSection` for longer content you want players to expand.
