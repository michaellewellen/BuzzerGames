#!/usr/bin/env python3
"""
Turns the ProtoQA dataset (about 9,700 real Family Feud boards scraped from fan sites, CC BY 4.0) into Family Feud question files.

    git clone --depth 1 https://github.com/iesl/protoqa-data
    python tools/import_protoqa.py protoqa-data --json src/BuzzerGames.Core/Data/Feud/feud-starter.json   # the bundled bank
    python tools/import_protoqa.py protoqa-data --text feud-import.txt -n 300                              # lines to paste into /family-feud/questions

Questions are spoken by the emcee, never shown on the TV, so they are put back into sentence case. Anything with drinking, drugs, sex,
violence or crude language is dropped, boards with fewer than four answers are dropped, and only the top eight answers are kept.
Standard library only.
"""
import argparse
import json
import re
import string
from pathlib import Path

FILES = ["data/train/train.jsonl", "data/dev/dev.scraped.jsonl", "data/dev/dev.crowdsourced.jsonl"]

BLOCKED = re.compile(
    r"\b(alcohol\w*|beer|beers|wine|liquor|vodka|whiskey|whisky|booze|boozy|drunk|drinking|tequila|bar|bars|bartender\w*|cocktail\w*|champagne|"
    r"hangover|cigar\w*|cigarette\w*|smok\w*|tobacco|cocaine|marijuana|weed|pot|drug\w*|high|stoned|"
    r"sex\w*|porn\w*|naked|nude|nudity|penis|vagina|breast\w*|boob\w*|butt|butts|ass|asses|fuck\w*|shit\w*|damn\w*|hell|bitch\w*|crap|"
    r"condom\w*|stripper\w*|strip|lingerie|underwear|panties|thong|bra|bras|bikini|orgasm\w*|erotic\w*|kinky|seduc\w*|hooker\w*|prostitut\w*|"
    r"virgin\w*|pregnan\w*|menstrual|period|diaper\w*|poop\w*|pee|urinat\w*|fart\w*|vomit\w*|puke|gay|lesbian|"
    r"gun\w*|kill\w*|murder\w*|suicide|rape\w*|abuse\w*|divorce\w*|cheat\w*|affair\w*|jail|prison|arrest\w*|steal\w*|crime\w*|"
    r"coffee|lust\w*|flirt\w*|hook ?up|one night|stripping|cleavage|hickey|date night|make ?out|kiss\w*|bed ?room|honeymoon|"
    r"making love|lover|nude|shot glass|hefner|playboy|personals?|break ?up|dating|desperate|romance|romantic|mistress|sexy|attractive|girlfriend|boyfriend|ex-?wife|ex-?husband|wedding night|nipple\w*|testicle\w*|balls|genital\w*|private parts|toilet paper|bathroom|toilet|gas|burp\w*)\b",
    re.I,
)
# Words a title-cased question loses when it is lower-cased, put back.
PROPER = [
    "Christmas", "Halloween", "Thanksgiving", "Easter", "Valentine's Day", "Valentine", "Santa Claus", "Santa", "Mother's Day", "Father's Day",
    "New Year's", "Fourth Of July", "Independence Day", "Hanukkah", "Disney", "Disneyland", "Disney World", "Walmart", "Target", "Costco", "Amazon",
    "McDonald's", "Burger King", "Wendy's", "Starbucks", "Subway", "Pizza Hut", "Domino's", "Google", "Facebook", "Twitter", "Instagram", "Netflix",
    "YouTube", "Apple", "iPhone", "Barbie", "Batman", "Superman", "Spider-Man", "Mickey Mouse", "Winnie The Pooh", "Santa's", "God", "Bible",
    "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "January", "February", "March", "April", "June", "July",
    "August", "September", "October", "November", "December", "American", "America", "Americans", "French", "Italian", "Mexican", "Chinese",
    "Japanese", "German", "Irish", "English", "Spanish", "Canadian", "Texas", "California", "Florida", "New York", "Las Vegas", "Hollywood",
    "Europe", "Paris", "London", "Hawaii", "Alaska", "Canada", "Mexico", "Africa", "Asia", "Earth", "Mars", "Jesus", "Mr.", "Mrs.", "Ms.", "Dr.",
    "Monopoly", "Scrabble", "Lego", "Legos", "Play-Doh", "Ikea", "Uber", "Lyft", "Airbnb", "Pinterest", "Tinder", "Snapchat", "TikTok", "Zoom",
    "Oscar", "Oscars", "Super Bowl", "Olympics", "Olympic", "Wizard Of Oz", "Harry Potter", "Star Wars", "Star Trek", "Pokemon", "Nintendo",
    "Xbox", "PlayStation", "Marvel", "Dr. Seuss", "Cinderella", "Snow White", "Peter Pan", "Pinocchio", "Dumbo", "Bambi", "Frozen", "Shrek",
]
PROPER_RE = [(re.compile(r"\b" + re.escape(p.lower()) + r"\b", re.I), p) for p in sorted(PROPER, key=len, reverse=True)]


def sentence_case(text):
    text = text.replace("\u2019", "'").replace("\u2018", "'").replace("\u201c", '"').replace("\u201d", '"')
    text = re.sub(r",(?=\S)", ", ", text)
    text = re.sub(r"\s+", " ", text).strip()
    text = text.lower()
    text = re.sub(r"\bu\.?s\.? (?=state|president|city|cities|senator|coin|dollar|open|team)", "U.S. ", text)
    text = re.sub(r"\bu\.?s\.?a\.?\b", "USA", text)
    text = re.sub(r"\btv\b", "TV", text)
    text = re.sub(r"\bi\b", "I", text)
    text = re.sub(r"\bi'(m|ll|ve|d)\b", lambda m: "I'" + m.group(1), text)
    for pattern, proper in PROPER_RE:
        text = pattern.sub(proper, text)
    # Capitalise the first letter and the first letter after ". ", "? " and "! ".
    text = re.sub(r"(^|[.?!]\s+)([a-z])", lambda m: m.group(1) + m.group(2).upper(), text)
    return text


def nice_answer(text):
    text = text.replace("\u2019", "'").replace("\u2018", "'")
    text = re.sub(r"\btv\b", "TV", text, flags=re.I)
    text = re.sub(r"\s+", " ", text).strip(" .,;:-")
    return re.sub(r"\bTv\b", "TV", string.capwords(text)) if text else ""


def board(row):
    question = sentence_case(row["question"]["original"])
    clusters = (row.get("answers") or {}).get("clusters") or {}
    answers, seen = [], set()
    for cluster in clusters.values():
        names, count = cluster.get("answers") or [], int(cluster.get("count") or 0)
        if not names or count < 1:
            continue
        text = nice_answer(names[0])
        key = re.sub(r"[^a-z0-9]", "", text.lower())
        if not text or len(text) > 40 or key in seen:
            continue
        seen.add(key)
        answers.append((text, count))
    answers.sort(key=lambda a: -a[1])
    answers = answers[:8]
    if len(answers) < 4 or len(question) < 15 or len(question) > 160:
        return None
    if BLOCKED.search(question) or any(BLOCKED.search(a) for a, _ in answers):
        return None
    if "|" in question or any("|" in a or ":" in a for a, _ in answers):
        return None
    total = sum(p for _, p in answers)
    if total > 100:
        answers = [(a, max(1, round(p * 100 / total))) for a, p in answers]
    answers = [(a, min(p, 99)) for a, p in answers]
    over = sum(p for _, p in answers) - 100
    if over > 0:
        answers[0] = (answers[0][0], answers[0][1] - over)
    if sum(p for _, p in answers) < 60:
        return None  # a partial survey makes a flat board
    return question, answers


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("data", help="the cloned protoqa-data folder")
    parser.add_argument("--json", help="write the bundled-bank format here")
    parser.add_argument("--text", help="write 'question | answer: points | ...' lines here")
    parser.add_argument("-n", "--max", type=int, default=0, help="most boards to write (default all)")
    args = parser.parse_args()

    boards, seen, read = [], set(), 0
    for name in FILES:
        path = Path(args.data) / name
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            read += 1
            result = board(json.loads(line))
            if result is None:
                continue
            key = re.sub(r"\W+", " ", result[0].lower()).strip()
            if key in seen:
                continue
            seen.add(key)
            boards.append(result)
    if args.max:
        boards = boards[: args.max]

    if args.json:
        data = {"boards": [{"question": q, "answers": [{"text": a, "points": p} for a, p in ans]} for q, ans in boards]}
        Path(args.json).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    if args.text:
        Path(args.text).write_text("\n".join(q + " | " + " | ".join(f"{a}: {p}" for a, p in ans) for q, ans in boards) + "\n", encoding="utf-8")
    print(f"Read {read} boards, kept {len(boards)}.")


if __name__ == "__main__":
    main()
