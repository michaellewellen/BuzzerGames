#!/usr/bin/env python3
"""
Turns the Buy a Vowel Boards Wheel of Fortune Puzzle Compendium into a file you can paste into
BuzzerGames at /wheel-of-fortune/puzzles (the bulk import box).

Needs only Python 3.8+ (no installs). Run it on your own computer, it has to reach the site.

    python tools/scrape_wheel_compendium.py                 # fetch every season page, write wheel-import.txt
    python tools/scrape_wheel_compendium.py --seasons 30-44 # only some seasons
    python tools/scrape_wheel_compendium.py --from-files page1.html page2.html   # read saved pages instead of fetching
    python tools/scrape_wheel_compendium.py --list-categories                    # what bonus categories exist (no output file)
    python tools/scrape_wheel_compendium.py --selftest

Be kind to the site: pages are cached in ./compendium-cache (so nothing is fetched twice) and there is a pause
between requests. The compendium is volunteers' work and says "all rights reserved"; this is meant for one slow,
private pull for a party game, not for republishing.

What it keeps:
  regular  R1, R2, R3* (prize puzzle) ...      -> "category | puzzle"
  quick    TT1 + TT2 + TT3 (triple toss-up)    -> "theme | p1 | p2 | p3"   (also FF1-3 with --ff)
  bonus    BR, without the word-play categories (Same Name, Before & After, Rhyme Time...)           -> "category | puzzle"
It drops puzzles that cannot fit the 12/14/14/12 board, duplicates, and anything mentioning alcohol or coffee.
Crossword (^) puzzles are left out unless you pass --crosswords.
"""
import argparse
import html
import random
import re
import sys
import time
import urllib.error
import urllib.request
from html.parser import HTMLParser
from pathlib import Path

BASE = "https://buyavowel.boards.net/page/"
INDEX = BASE + "compendiumindex"
USER_AGENT = "Mozilla/5.0 (compatible; BuzzerGamesPuzzleImport/1.0; one slow personal pull for a party game)"

BOARD_CAPACITY = 52          # tiles on the board
BOARD_LONGEST_WORD = 14      # widest row
CATEGORY_MAX = 40

# Mirrors the app's own warning list; the scraper drops these instead of just warning.
BANNED_WORDS = ["BEER", "WINE", "VODKA", "WHISKEY", "WHISKY", "TEQUILA", "COCKTAIL", "MARTINI", "CHAMPAGNE",
                "COFFEE", "LATTE", "ESPRESSO", "CAPPUCCINO"]
BANNED_RE = re.compile(r"\b(" + "|".join(BANNED_WORDS) + r")S?\b")

# The bonus round has no word-play categories (same list the app warns about). Real bonus categories like Person, Place,
# Thing, Phrase and What Are You Doing? are all fine; see --list-categories for what exists.
CUTESY_BONUS = {"SAME NAME", "BEFORE & AFTER", "RHYME TIME", "CROSSWORD CLUE", "CROSSWORD", "SAME LETTER", "ANAGRAM"}


# ---- Parsing ---------------------------------------------------------------------------------------------------------

ROW_RE = re.compile(r"<tr[^>]*>(.*?)</tr>", re.S | re.I)
CELL_RE = re.compile(r"<t[dh][^>]*>(.*?)</t[dh]>", re.S | re.I)
TAG_RE = re.compile(r"<[^>]+>")


def clean_cell(raw):
    text = html.unescape(TAG_RE.sub("", raw)).replace("\xa0", " ")
    return " ".join(text.split())


class _TableReader(HTMLParser):
    """Collects rows of cell text. A new <tr> or <td> closes the previous one, so missing end tags (common in
    hand-written forum HTML) don't matter."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.rows = []
        self._row = None
        self._cell = None

    def _close_cell(self):
        if self._cell is not None and self._row is not None:
            self._row.append(" ".join("".join(self._cell).replace("\xa0", " ").split()))
        self._cell = None

    def _close_row(self):
        self._close_cell()
        if self._row:
            self.rows.append(self._row)
        self._row = None

    def handle_starttag(self, tag, attrs):
        if tag == "tr":
            self._close_row()
            self._row = []
        elif tag in ("td", "th"):
            self._close_cell()
            if self._row is None:
                self._row = []
            self._cell = []
        elif tag == "br" and self._cell is not None:
            self._cell.append(" ")

    def handle_endtag(self, tag):
        if tag in ("td", "th"):
            self._close_cell()
        elif tag in ("tr", "table", "tbody"):
            self._close_row()

    def handle_data(self, data):
        if self._cell is not None:
            self._cell.append(data)

    def close(self):
        super().close()
        self._close_row()


def parse_rows(page_html):
    """Yield dicts with puzzle, category, episode, round for every puzzle row of one compendium page."""
    reader = _TableReader()
    reader.feed(page_html)
    reader.close()
    columns = None
    for cells in reader.rows:
        upper = [c.upper() for c in cells]
        if "PUZZLE" in upper and "CATEGORY" in upper and "ROUND" in upper:
            columns = {name: upper.index(name) for name in ("PUZZLE", "CATEGORY", "ROUND")}
            columns["EP#"] = upper.index("EP#") if "EP#" in upper else None
            columns["DATE"] = upper.index("DATE") if "DATE" in upper else None
            continue
        if columns is None or len(cells) <= max(columns["PUZZLE"], columns["CATEGORY"], columns["ROUND"]):
            continue
        episode = cells[columns["EP#"]] if columns["EP#"] is not None else ""
        date = cells[columns["DATE"]] if columns["DATE"] is not None else ""
        yield {
            "puzzle": cells[columns["PUZZLE"]],
            "category": cells[columns["CATEGORY"]],
            "episode": episode or date,
            "round": cells[columns["ROUND"]].replace(" ", ""),
        }


def describe_page(page_html):
    """A few facts for when a page doesn't parse, so the layout can be worked out from the output alone."""
    i = page_html.upper().find("PUZZLE")
    around = page_html[max(0, i - 150): i + 350].replace("\n", " ") if i >= 0 else "(the word PUZZLE never appears)"
    return f"{len(page_html)} characters, <tr> x{page_html.lower().count('<tr')}, <td> x{page_html.lower().count('<td')}; near the first PUZZLE: {around}"


def normalize_puzzle(text):
    text = text.upper().replace("’", "'").replace("‘", "'").replace("“", '"').replace("”", '"')
    text = text.replace("|", " ").replace("#", " ")
    return " ".join(text.split())


def normalize_category(text):
    text = text.replace("|", " ").replace("#", " ")
    text = " ".join(text.split())
    return text[:CATEGORY_MAX].strip()


def fits_board(text):
    letters_and_marks = text.replace(" ", "")
    if not text or len(letters_and_marks) > BOARD_CAPACITY:
        return False
    return all(len(word) <= BOARD_LONGEST_WORD for word in text.split())


def banned(category, text):
    return bool(BANNED_RE.search((category + " " + text).upper()))


ROUND_RE = re.compile(r"^R(\d+)([*^]?)$", re.I)
TT_RE = re.compile(r"^TT(\d+)$", re.I)
FF_RE = re.compile(r"^FF(\d+)$", re.I)


def collect(rows, ff=False, crosswords=False):
    """Sort parsed rows into the three kinds. Returns (regular, quick, bonus) lists, plus counts of codes we ignored."""
    regular, bonus, quick = [], [], []
    ignored = {}
    triples = {}   # (episode, "TT") or (episode, "FF") -> {number: (category, puzzle)}
    for r in rows:
        code = r["round"]
        text = normalize_puzzle(r["puzzle"])
        category = normalize_category(r["category"])
        if not text or not category:
            continue
        m = ROUND_RE.match(code)
        if m:
            if m.group(2) == "^" and not crosswords:
                continue
            regular.append((category, text))
            continue
        if code.upper() == "BR":
            bonus.append((category, text))
            continue
        m = TT_RE.match(code)
        if m:
            triples.setdefault((r["episode"], "TT"), {})[int(m.group(1))] = (category, text)
            continue
        m = FF_RE.match(code)
        if m and ff:
            triples.setdefault((r["episode"], "FF"), {})[int(m.group(1))] = (category, text)
            continue
        ignored[code] = ignored.get(code, 0) + 1
    for (_, _), parts in triples.items():
        if not all(n in parts for n in (1, 2, 3)):
            continue
        three = [parts[1], parts[2], parts[3]]
        if len({c.upper() for c, _ in three}) != 1:
            continue
        quick.append((three[0][0], [t for _, t in three]))
    return regular, quick, bonus, ignored


# ---- Fetching --------------------------------------------------------------------------------------------------------

def fetch(url, cache_dir, delay):
    cache_dir.mkdir(parents=True, exist_ok=True)
    name = re.sub(r"[^A-Za-z0-9]+", "_", url.split("//", 1)[-1]) + ".html"
    path = cache_dir / name
    if path.exists():
        return path.read_text(encoding="utf-8", errors="replace")
    time.sleep(delay)
    request = urllib.request.Request(url, headers={
        "User-Agent": USER_AGENT,
        "Accept": "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "en-US,en;q=0.9",
    })
    with urllib.request.urlopen(request, timeout=60) as response:
        body = response.read().decode("utf-8", errors="replace")
    path.write_text(body, encoding="utf-8")
    return body


def season_pages(index_html, wanted):
    """Page names like compendium44, discovered from the index page; falls back to 1..44."""
    names = sorted({int(n) for n in re.findall(r"/page/compendium(\d+)", index_html)})
    if not names:
        names = list(range(1, 45))
    return [n for n in names if wanted is None or n in wanted]


def parse_seasons(text):
    if not text:
        return None
    wanted = set()
    for part in text.split(","):
        if "-" in part:
            a, b = part.split("-", 1)
            wanted.update(range(int(a), int(b) + 1))
        else:
            wanted.add(int(part))
    return wanted


# ---- Output ----------------------------------------------------------------------------------------------------------

def build_output(regular, quick, bonus, args, rng):
    seen = set()

    def unique(items, key):
        out = []
        for item in items:
            k = key(item)
            if k not in seen:
                seen.add(k)
                out.append(item)
        return out

    regular = [(c, t) for c, t in regular if fits_board(t) and not banned(c, t)]
    regular = unique(regular, lambda x: ("r", x[1]))
    quick = [(c, ts) for c, ts in quick if all(fits_board(t) for t in ts) and not any(banned(c, t) for t in ts)]
    quick = unique(quick, lambda x: ("q", tuple(x[1])))
    bonus = [(c, t) for c, t in bonus if fits_board(t) and not banned(c, t)]
    if not args.all_bonus_categories:
        bonus = [(c, t) for c, t in bonus if c.upper() not in CUTESY_BONUS]
    bonus = unique(bonus, lambda x: ("b", x[1]))

    def sample(items, limit):
        if limit and len(items) > limit:
            items = rng.sample(items, limit)
        return items

    regular, quick, bonus = sample(regular, args.regular), sample(quick, args.quick), sample(bonus, args.bonus)
    lines = ["# regular"] + [f"{c} | {t}" for c, t in regular]
    lines += ["", "# quick"] + [f"{c} | " + " | ".join(ts) for c, ts in quick]
    lines += ["", "# bonus"] + [f"{c} | {t}" for c, t in bonus]
    return "\n".join(lines) + "\n", (len(regular), len(quick), len(bonus))


SAMPLE_PAGE = """
<table><tr><td>PUZZLE</td><td>CATEGORY</td><td>DATE</td><td>EP#</td><td>ROUND</td></tr>
<tr><td>BREAK A LEG</td><td>Phrase</td><td>9/14/26</td><td>#1</td><td>R1</td></tr>
<tr><td>OLIVE OIL AND FETA CHEESE</td><td>Food &amp; Drink</td><td>9/14/26</td><td>#1</td><td>R3*</td></tr>
<tr><td>W<b>A</b>GON CENTER</td><td>Welcome _____</td><td>9/14/26</td><td>#1</td><td>R2^</td></tr>
<tr><td>HOT-AIR BALLOONS</td><td>Up In The Air</td><td>9/14/26</td><td>#1</td><td>TT1</td></tr>
<tr><td>THE BIRDS AND THE BEES</td><td>Up In The Air</td><td>9/14/26</td><td>#1</td><td>TT2</td></tr>
<tr><td>UNIDENTIFIED FLYING OBJECTS</td><td>Up In The Air</td><td>9/14/26</td><td>#1</td><td>TT3</td></tr>
<tr><td>COFFEE BREAK</td><td>Phrase</td><td>9/14/26</td><td>#1</td><td>R4</td></tr>
<tr><td>MATH PRODIGY</td><td>Person</td><td>9/14/26</td><td>#1</td><td>BR</td></tr>
<tr><td>PIGGLY-WIGGLY</td><td>Rhyme Time</td><td>9/14/26</td><td>#1</td><td>BR</td></tr>
</table>"""


def selftest():
    rows = list(parse_rows(SAMPLE_PAGE))
    assert len(rows) == 9, len(rows)
    loose = "<table><tr><td>PUZZLE<td>CATEGORY<td>ROUND<tr><td>A B<td>Phrase<td>R1<tr><td>C&amp;D<br>E<td>Thing<td>R2*</table>"
    assert [r["puzzle"] for r in parse_rows(loose)] == ["A B", "C&D E"], list(parse_rows(loose))
    regular, quick, bonus, ignored = collect(rows)
    assert [t for _, t in regular] == ["BREAK A LEG", "OLIVE OIL AND FETA CHEESE", "COFFEE BREAK"], regular
    assert quick == [("Up In The Air", ["HOT-AIR BALLOONS", "THE BIRDS AND THE BEES", "UNIDENTIFIED FLYING OBJECTS"])], quick
    assert len(bonus) == 2 and not ignored
    args = argparse.Namespace(regular=0, quick=0, bonus=0, all_bonus_categories=False)
    text, counts = build_output(regular, quick, bonus, args, random.Random(1))
    assert counts == (2, 1, 1), counts          # coffee dropped, Rhyme Time bonus dropped
    assert "COFFEE" not in text and "PIGGLY" not in text
    assert not fits_board("SUPERCALIFRAGILISTICEXPIALIDOCIOUS")
    assert fits_board("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG")
    assert not fits_board("A " * 60)
    print("selftest ok")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seasons", help="for example 30-44 or 1,5,40-44 (default: every season page)")
    ap.add_argument("--from-files", nargs="+", metavar="HTML", help="read saved pages instead of fetching")
    ap.add_argument("--out", default="wheel-import.txt")
    ap.add_argument("--cache", default="compendium-cache")
    ap.add_argument("--delay", type=float, default=3.0, help="seconds to wait before each fetch (default 3)")
    ap.add_argument("--regular", type=int, default=2500, help="most regular puzzles to write, picked at random (0 = all)")
    ap.add_argument("--quick", type=int, default=300, help="most quick sets to write (0 = all)")
    ap.add_argument("--bonus", type=int, default=400, help="most bonus puzzles to write (0 = all)")
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--ff", action="store_true", help="also make quick sets from Fortune Frenzy puzzles FF1-FF3")
    ap.add_argument("--crosswords", action="store_true", help="keep crossword (^) puzzles as regular puzzles")
    ap.add_argument("--all-bonus-categories", action="store_true", help="keep word-play bonus categories too")
    ap.add_argument("--list-categories", action="store_true", help="print bonus categories with counts and exit")
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()

    if args.selftest:
        selftest()
        return

    pages = []
    if args.from_files:
        for name in args.from_files:
            pages.append((name, Path(name).read_text(encoding="utf-8", errors="replace")))
    else:
        cache = Path(args.cache)
        print("Fetching the index...", file=sys.stderr)
        try:
            index_html = fetch(INDEX, cache, args.delay)
        except urllib.error.HTTPError as ex:
            sys.exit(f"The site answered {ex.code} {ex.reason}. It may not accept scripts. Fallback: open the season pages in your "
                     "browser, press Ctrl+S to save each as 'Webpage, HTML only', then run:\n"
                     "  python tools\\scrape_wheel_compendium.py --from-files season43.html season44.html ...")
        numbers = season_pages(index_html, parse_seasons(args.seasons))
        print(f"{len(numbers)} season pages to read (3 s apart, cached for next time).", file=sys.stderr)
        for n in numbers:
            try:
                pages.append((f"compendium{n}", fetch(f"{BASE}compendium{n}", cache, args.delay)))
                print(f"  compendium{n} ok", file=sys.stderr)
            except Exception as ex:  # keep going; one missing page shouldn't lose the rest
                print(f"  compendium{n} failed: {ex}", file=sys.stderr)

    all_regular, all_quick, all_bonus, ignored_total = [], [], [], {}
    for name, page in pages:
        rows = list(parse_rows(page))
        regular, quick, bonus, ignored = collect(rows, ff=args.ff, crosswords=args.crosswords)
        if not rows:
            print(f"  {name}: no puzzle table found (different layout?)", file=sys.stderr)
            print(f"    {describe_page(page)}", file=sys.stderr)
        all_regular += regular
        all_quick += quick
        all_bonus += bonus
        for k, v in ignored.items():
            ignored_total[k] = ignored_total.get(k, 0) + v

    if args.list_categories:
        counts = {}
        for c, _ in all_bonus:
            counts[c] = counts.get(c, 0) + 1
        for c, n in sorted(counts.items(), key=lambda x: -x[1]):
            print(f"{n:5d}  {c}{'   (left out by default)' if c.upper() in CUTESY_BONUS else ''}")
        return

    rng = random.Random(args.seed)
    text, counts = build_output(all_regular, all_quick, all_bonus, args, rng)
    Path(args.out).write_text(text, encoding="utf-8")
    print(f"Wrote {args.out}: {counts[0]} regular, {counts[1]} quick sets, {counts[2]} bonus.", file=sys.stderr)
    if ignored_total:
        top = ", ".join(f"{k}x{v}" for k, v in sorted(ignored_total.items(), key=lambda x: -x[1])[:8])
        print(f"Round codes not used: {top}", file=sys.stderr)
    print("Now paste the file into the import box at /wheel-of-fortune/puzzles.", file=sys.stderr)


if __name__ == "__main__":
    main()
