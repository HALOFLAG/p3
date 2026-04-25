"""Shared card extraction logic for nobinobiTRPG reference PDFs.

Used by extract_reference.py (markdown dump) and build_card_db.py (xlsx + JSON
card database). Each card PDF is a 2-column × N-row sheet; font size tells
title from body from judgement (roughly 9pt / 8pt / 7pt).
"""

from __future__ import annotations

import re
from pathlib import Path

import pdfplumber


# ---------- tunables -------------------------------------------------------

Y_TOL = 3.0              # px tolerance when grouping words into a text line
TITLE_SIZE_MARGIN = 0.5  # line is a title if its size >= body_size + margin
MIN_CARD_LINES = 2
MIN_TITLE_LEN = 2

JUDGMENT_PREFIX = re.compile(r"^\s*(判定|劇情|判\s定|劇\s情)")


# ---------- line / card segmentation --------------------------------------

def _group_words_to_lines(
    words: list[dict],
) -> list[tuple[float, str, float]]:
    """Return [(top_y, text, dominant_font_size)] ordered by y."""
    if not words:
        return []
    words_sorted = sorted(words, key=lambda w: (w["top"], w["x0"]))
    lines: list[tuple[float, str, float]] = []
    bucket: list[dict] = []
    bucket_top = words_sorted[0]["top"]

    def flush(bk: list[dict], top: float) -> None:
        if not bk:
            return
        bk.sort(key=lambda x: x["x0"])
        text = "".join(x["text"] for x in bk)
        sizes = [round(float(x.get("size", 0)), 1) for x in bk if x.get("size")]
        size = max(set(sizes), key=sizes.count) if sizes else 0.0
        lines.append((top, text, size))

    for w in words_sorted:
        if abs(w["top"] - bucket_top) <= Y_TOL:
            bucket.append(w)
        else:
            flush(bucket, bucket_top)
            bucket = [w]
            bucket_top = w["top"]
    flush(bucket, bucket_top)
    return lines


def _split_lines_into_cards(
    lines: list[tuple[float, str, float]],
) -> list[list[str]]:
    if not lines:
        return []
    sizes = [ln[2] for ln in lines if ln[2] > 0]
    if not sizes:
        return [[ln[1] for ln in lines]]
    body_size = max(set(sizes), key=sizes.count)
    title_threshold = body_size + TITLE_SIZE_MARGIN

    cards: list[list[str]] = []
    current: list[str] = []
    for _top, text, size in lines:
        if size >= title_threshold:
            if current:
                cards.append(current)
            current = [text]
        else:
            if not current:
                current = [text]
            else:
                current.append(text)
    if current:
        cards.append(current)
    return cards


def _extract_cards_from_page(page) -> list[list[str]]:
    words = page.extract_words(
        x_tolerance=2, y_tolerance=2,
        keep_blank_chars=False, use_text_flow=False,
        extra_attrs=["size"],
    )
    if not words:
        return []
    mid = page.width / 2
    left = [w for w in words if w["x0"] < mid]
    right = [w for w in words if w["x0"] >= mid]
    out: list[list[str]] = []
    for col in (left, right):
        lines = _group_words_to_lines(col)
        out.extend(_split_lines_into_cards(lines))
    return out


def is_usable_card(card: list[str]) -> bool:
    if len(card) < MIN_CARD_LINES:
        return False
    title = card[0].strip()
    if len(title) < MIN_TITLE_LEN:
        return False
    if not any("\u4e00" <= ch <= "\u9fff" or ch.isalpha() for ch in title):
        return False
    if "，" in title or "。" in title:
        return False
    if title.startswith("「") or title.startswith("『"):
        return False
    narrative_preamble = 0
    for ln in card[1:]:
        if not ln.strip():
            continue
        if JUDGMENT_PREFIX.match(ln):
            break
        narrative_preamble += 1
    return narrative_preamble >= 2


# ---------- public API -----------------------------------------------------

def split_card_body(card: list[str]) -> tuple[str, str]:
    """Given a card's lines (title is card[0]), return (story, check).

    `story` = the narrative body up to the first 判定/劇情 line.
    `check` = everything from the first 判定/劇情 line onward, kept as raw text.
    """
    story_lines: list[str] = []
    check_lines: list[str] = []
    hit_check = False
    for ln in card[1:]:
        if not hit_check and JUDGMENT_PREFIX.match(ln):
            hit_check = True
        (check_lines if hit_check else story_lines).append(ln)
    return (
        "\n".join(s.strip() for s in story_lines if s.strip()),
        "\n".join(s.strip() for s in check_lines if s.strip()),
    )


def extract_cards_from_pdf(pdf_path: Path) -> tuple[list[list[str]], int]:
    """Extract usable cards from a single PDF.

    Returns (cards, dropped_count). Each card is a list of lines with card[0]
    being the title.
    """
    all_cards: list[list[str]] = []
    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            all_cards.extend(_extract_cards_from_page(page))
    kept: list[list[str]] = []
    dropped = 0
    for c in all_cards:
        if is_usable_card(c):
            kept.append(c)
        else:
            dropped += 1
    return kept, dropped
