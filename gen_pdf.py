import json
import os
import struct
import textwrap

from fpdf import FPDF


ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "MessageBoard_BlockReference.pdf")
EXAMPLE_JSON_PATH = os.path.join(ROOT, "MessageBoard_AllElements_Example.json")
EXAMPLES_DIR = os.path.join(ROOT, "Examples")


PAGE_W = 210
PAGE_H = 297
MARGIN = 12
CONTENT_W = PAGE_W - (MARGIN * 2)
BOTTOM_Y = PAGE_H - 14

G_BG_HEADER = (20, 20, 20)
G_BG_DARK = (31, 31, 31)
G_BG_ROW = (38, 38, 38)
G_BLUE = (102, 179, 255)
G_GREEN = (77, 204, 102)
G_ORANGE = (224, 158, 46)
G_RED = (230, 51, 51)
G_NEUTRAL = (153, 171, 191)
G_TEXT_PRI = (255, 255, 255)
G_TEXT_SEC = (199, 199, 199)
G_TEXT_MUT = (158, 158, 158)
G_BORDER = (56, 56, 56)

C_PAGE = (245, 246, 250)
C_TEXT = (24, 28, 46)
C_TEXT2 = (85, 93, 118)
C_TAG = (220, 225, 242)
C_CODE_BG = (20, 22, 36)
C_CODE_FG = (182, 212, 255)
C_CODE_BORDER = (50, 58, 88)
C_PANEL = (255, 255, 255)
C_SEP = (205, 210, 228)

TAB_NOTES = {
    "Overview": [
        "This is an example of the base panel layout and default text behavior.",
        "Shown are uses of paragraph tone, textTone, announcementBox, dividers, and spacers.",
        "Includes the default-color note that is useful to know when making new boards.",
    ],
    "Tones": [
        "These are the five named tones as well as full announcement boxes.",
        "The hex-tone example is in case we want custom accents for any reason.",
    ],
    "Lists": [
        "This displays examples of bulletLists with tone, titleTone, and textTone overrides.",
        "Also shows a ruleList using the normal defaults for comparison.",
    ],
    "Links": [
        "This displays examples of a buttonRow using a row tone plus per-button tone overrides.",
        "Highlights that buttonRow uses titleTone, but not textTone.",
    ],
    "Sections": [
        "Shows collapsible section headers, nested blocks, and an empty section example.",
        "Shows that nested blocks keep their own tones instead of inheriting the parent tone.",
    ],
}


class PDF(FPDF):
    def footer(self):
        return


def fr(pdf, x, y, w, h, color):
    pdf.set_fill_color(*color)
    pdf.set_draw_color(*color)
    pdf.rect(x, y, w, h, "F")


def sr(pdf, x, y, w, h, color, line_width=0.25):
    pdf.set_line_width(line_width)
    pdf.set_draw_color(*color)
    pdf.rect(x, y, w, h, "D")


def hl(pdf, x, y, w, color, line_width=0.35):
    pdf.set_line_width(line_width)
    pdf.set_draw_color(*color)
    pdf.line(x, y, x + w, y)


def tone_color(name):
    return {
        "info": G_BLUE,
        "success": G_GREEN,
        "warning": G_ORANGE,
        "error": G_RED,
        "neutral": G_NEUTRAL,
    }.get(name, G_BLUE)


def new_page(pdf):
    pdf.add_page()
    fr(pdf, 0, 0, PAGE_W, PAGE_H, C_PAGE)
    pdf.set_xy(MARGIN, MARGIN)


def band_title(pdf, text, accent=G_BLUE):
    y = pdf.get_y()
    fr(pdf, MARGIN, y, CONTENT_W, 8, C_TAG)
    fr(pdf, MARGIN, y, 3, 8, accent)
    pdf.set_xy(MARGIN + 5, y + 1)
    pdf.set_font("Helvetica", "B", 10)
    pdf.set_text_color(*C_TEXT)
    pdf.cell(CONTENT_W - 7, 6, text)
    pdf.set_y(y + 12)


def body_text(pdf, text, size=8, color=C_TEXT2, line_height=4.2):
    pdf.set_font("Helvetica", "", size)
    pdf.set_text_color(*color)
    pdf.multi_cell(CONTENT_W, line_height, text)


def bullet_list(pdf, items):
    pdf.set_font("Helvetica", "", 8)
    pdf.set_text_color(*C_TEXT2)
    for item in items:
        x = pdf.get_x()
        y = pdf.get_y()
        pdf.set_xy(x + 3, y)
        pdf.multi_cell(CONTENT_W - 3, 4.2, f"- {item}")
        pdf.set_y(pdf.get_y() + 1)


def read_png_size(path):
    with open(path, "rb") as handle:
        header = handle.read(24)
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Unsupported image format for {path}")
    width, height = struct.unpack(">II", header[16:24])
    return width, height


def screenshot_path_for_label(label):
    return os.path.join(EXAMPLES_DIR, f"{label} Tab.png")


def render_screenshot(pdf, label):
    path = screenshot_path_for_label(label)
    if not os.path.exists(path):
        raise FileNotFoundError(f"Missing screenshot for tab '{label}': {path}")

    width_px, height_px = read_png_size(path)
    max_w = CONTENT_W
    max_h = 112
    scale = min(max_w / width_px, max_h / height_px)
    render_w = width_px * scale
    render_h = height_px * scale
    x = MARGIN + ((CONTENT_W - render_w) / 2)
    y = pdf.get_y()

    pdf.image(path, x=x, y=y, w=render_w, h=render_h)
    sr(pdf, x, y, render_w, render_h, C_SEP, 0.3)
    pdf.set_y(y + render_h + 4)


def chunked_code_lines(lines, max_chars=82):
    wrapped = []
    for line in lines:
        if not line:
            wrapped.append("")
            continue

        indent = len(line) - len(line.lstrip(" "))
        prefix = " " * indent
        content = line[indent:]
        available = max(20, max_chars - indent)
        parts = textwrap.wrap(
            content,
            width=available,
            break_long_words=False,
            break_on_hyphens=False,
            drop_whitespace=False,
        )
        if not parts:
            wrapped.append(prefix)
            continue

        wrapped.append(prefix + parts[0])
        for part in parts[1:]:
            wrapped.append(prefix + part)
    return wrapped


def code_block(pdf, title, lines):
    line_height = 3.6
    padding = 3
    wrapped = chunked_code_lines(lines)
    index = 0
    first_page = True

    while index < len(wrapped):
        if pdf.get_y() + 24 > BOTTOM_Y:
            new_page(pdf)

        heading = title if first_page else f"{title} (cont.)"
        band_title(pdf, heading, accent=G_BLUE)

        available_h = BOTTOM_Y - pdf.get_y()
        usable_h = max(18, available_h - 2)
        max_lines = max(4, int((usable_h - (padding * 2)) / line_height))
        chunk = wrapped[index:index + max_lines]
        height = (padding * 2) + (len(chunk) * line_height)

        x = MARGIN
        y = pdf.get_y()
        fr(pdf, x, y, CONTENT_W, height, C_CODE_BG)
        sr(pdf, x, y, CONTENT_W, height, C_CODE_BORDER, 0.25)

        pdf.set_font("Courier", "", 6.6)
        pdf.set_text_color(*C_CODE_FG)
        for line_index, line in enumerate(chunk):
            pdf.set_xy(x + padding, y + padding + (line_index * line_height))
            pdf.cell(CONTENT_W - (padding * 2), line_height, line)

        pdf.set_y(y + height + 6)
        index += len(chunk)
        first_page = False


def pretty_json_lines(value):
    return json.dumps(value, indent=2).splitlines()


def render_cover(pdf, document):
    new_page(pdf)
    fr(pdf, 0, 0, PAGE_W, 36, G_BG_HEADER)
    fr(pdf, 0, 32, PAGE_W, 4, G_BLUE)

    pdf.set_xy(MARGIN, 8)
    pdf.set_font("Helvetica", "B", 22)
    pdf.set_text_color(*G_TEXT_PRI)
    pdf.cell(CONTENT_W, 10, "OPL Message Board")

    pdf.set_xy(MARGIN, 21)
    pdf.set_font("Helvetica", "", 12)
    pdf.set_text_color(*G_BLUE)
    pdf.cell(CONTENT_W, 8, "Screenshot Reference Guide")

    pdf.set_xy(MARGIN, 46)
    pdf.set_font("Helvetica", "", 9)
    pdf.set_text_color(*C_TEXT2)
    pdf.multi_cell(
        CONTENT_W,
        4.8,
        "This reconstruction uses the real in-game screenshots from the Examples folder and "
        "the matching JSON from MessageBoard_AllElements_Example.json.",
    )


def render_defaults_page(pdf, document):
    new_page(pdf)
    band_title(pdf, "Defaults And Quick Rules", accent=G_BLUE)
    body_text(
        pdf,
        "The message board is defined by one top-level JSON document that allows us to style this "
        "coherently. Through this, we can make as many custom tabs containing information as we "
        "want. Each tab contains 'blocks', which render unique UI element such as a heading, "
        "paragraph, list, button row, divider, spacer, or collapsible section. This doc aims to "
        "guide you how to put these together.",
        size=8.5,
    )
    pdf.set_y(pdf.get_y() + 2)
    body_text(
        pdf,
        "Examples of these behaviors are shown in the tab sections below. Each one includes the "
        "in-game screenshot and the JSON used to generate it.",
        size=8.5,
    )
    pdf.set_y(pdf.get_y() + 3)
    body_text(pdf, "Tips to note:", size=8.5, color=C_TEXT)
    pdf.set_y(pdf.get_y() + 1)
    bullet_list(
        pdf,
        [
            "Headings default to the info-blue accent if no tone is supplied.",
            "Standard paragraph body text uses the built-in light secondary text color.",
            "List titles, rule titles, and button row titles default to white unless titleTone is supplied.",
            "Paragraphs can use tone directly, and textTone overrides the paragraph text color.",
            "buttonRow buttons inherit the row tone unless a specific button supplies its own tone.",
            "buttonRow does not use textTone for the buttons themselves.",
            "Nested blocks inside a collapsible section keep their own tones.",
            "divider and spacer ignore tone fields.",
        ],
    )
    render_summary_section(pdf, document)


def render_tab_section(pdf, tab):
    label = tab["label"]
    new_page(pdf)
    band_title(pdf, f"{label} Tab", accent=G_BLUE)
    render_screenshot(pdf, label)

    notes = TAB_NOTES.get(label)
    if notes:
        band_title(pdf, f"{label} Highlights", accent=G_BLUE)
        bullet_list(pdf, notes)

    code_block(
        pdf,
        f"{label} segment from Example.json",
        pretty_json_lines(tab),
    )


def render_summary_section(pdf, document, start_new_page=False):
    if start_new_page:
        new_page(pdf)
    else:
        if pdf.get_y() + 48 > BOTTOM_Y:
            new_page(pdf)
        else:
            pdf.set_y(pdf.get_y() + 4)
    band_title(pdf, "Top-Level Document Template", accent=G_BLUE)
    body_text(
        pdf,
        "This top-level object controls the message board as a whole. It sets whether the "
        "board is enabled, how it opens, the shared title and footer text, and which tabs "
        "are included underneath it.",
        size=8.5,
    )
    pdf.set_y(pdf.get_y() + 3)
    top_level = {
        "schemaVersion": document["schemaVersion"],
        "enabled": document["enabled"],
        "showOnJoin": document["showOnJoin"],
        "openDelayMs": document["openDelayMs"],
        "title": document["title"],
        "subtitle": document["subtitle"],
        "footerNote": document["footerNote"],
        "tabs": ["..."],
    }
    code_block(pdf, "Document Root", pretty_json_lines(top_level))


def render_summary_page(pdf, document):
    render_summary_section(pdf, document, start_new_page=True)


def render_full_example_section(pdf, document):
    new_page(pdf)
    band_title(pdf, "Full Example Document", accent=G_BLUE)
    body_text(
        pdf,
        "This is the complete example JSON in one place, using the same content shown across the "
        "tab sections above.",
        size=8.5,
    )
    pdf.set_y(pdf.get_y() + 3)
    code_block(pdf, "Full Example.json", pretty_json_lines(document))


def main():
    with open(EXAMPLE_JSON_PATH, "r", encoding="utf-8") as handle:
        document = json.load(handle)

    pdf = PDF("P", "mm", "A4")
    pdf.set_auto_page_break(False)
    pdf.set_margins(MARGIN, MARGIN, MARGIN)

    render_defaults_page(pdf, document)

    for tab in document["tabs"]:
        render_tab_section(pdf, tab)

    render_full_example_section(pdf, document)

    pdf.output(OUT)
    print(f"Written: {OUT}")


if __name__ == "__main__":
    main()
