#!/usr/bin/env python3
"""
Turns the ProtoQA dataset (about 9,700 Family Feud style boards, CC BY 4.0) into a file for the Family Feud
questions page ("Paste a lot at once"), one board per line:

    Name something you bring to the beach | Towel: 38 | Sunscreen: 25 | Umbrella: 15

Run it on your own computer (it needs internet access to huggingface.co, standard library only):

    python tools/import_protoqa.py                 # writes feud-import.txt
    python tools/import_protoqa.py -n 300 -o x.txt

Then paste the file's contents into /family-feud/questions. Lines the game can't use are skipped there and listed.
"""
import argparse
import json
import re
import urllib.parse
import urllib.request

API = "https://datasets-server.huggingface.co/rows"
DATASET = "community-datasets/proto_qa"
PAGE = 100  # the rows API serves at most 100 rows per request

# Boards for a party with family around: skip drinking, drugs and the cruder material.
BLOCKED = re.compile(
    r"\b(alcohol|beer|wine|liquor|vodka|whiskey|booze|drunk|drink(ing)? (game|problem)|cigarette|smok\w*|cocaine|"
    r"marijuana|weed|drug\w*|sex\w*|porn\w*|naked|nude|penis|vagina|breast\w*|butt|ass|fuck\w*|shit\w*|damn|"
    r"bitch\w*|condom\w*|stripper\w*|coffee|gun\w*|kill\w*|murder\w*|suicide)\b",
    re.I,
)


def fetch(offset, split):
    query = urllib.parse.urlencode(
        {"dataset": DATASET, "config": "proto_qa", "split": split, "offset": offset, "length": PAGE}
    )
    with urllib.request.urlopen(f"{API}?{query}", timeout=60) as response:
        return json.load(response)


def board(row):
    """Returns (question, [(answer, points)]) or None when the board isn't usable."""
    question = (row.get("question") or "").strip()
    clusters = row.get("answer-clusters") or {}
    counts, groups = clusters.get("count") or [], clusters.get("answers") or []
    if not question or len(counts) != len(groups):
        return None

    answers = []
    for count, names in zip(counts, groups):
        if not names:
            continue
        answers.append((str(names[0]).strip().title(), int(count)))
    answers.sort(key=lambda a: -a[1])
    answers = answers[:8]
    if len(answers) < 3:
        return None

    text = question + " " + " ".join(a for a, _ in answers)
    if BLOCKED.search(text) or "|" in text or ":" in " ".join(a for a, _ in answers):
        return None

    # Scale the raw counts so the board adds up to about 100, the way a survey does.
    total = sum(p for _, p in answers)
    scaled = [(a, max(1, round(p * 100 / total))) for a, p in answers]
    return question, scaled


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("-o", "--out", default="feud-import.txt")
    parser.add_argument("-n", "--max", type=int, default=500, help="most boards to write (default 500)")
    parser.add_argument("--split", default="train")
    args = parser.parse_args()

    lines, seen, offset = [], set(), 0
    while len(lines) < args.max:
        page = fetch(offset, args.split)
        rows = page.get("rows") or []
        if not rows:
            break
        for item in rows:
            result = board(item["row"])
            if result is None:
                continue
            question, answers = result
            key = re.sub(r"\W+", " ", question.lower()).strip()
            if key in seen:
                continue
            seen.add(key)
            lines.append(question + " | " + " | ".join(f"{a}: {p}" for a, p in answers))
            if len(lines) >= args.max:
                break
        offset += PAGE
        if offset >= page.get("num_rows_total", offset):
            break

    with open(args.out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"Wrote {len(lines)} boards to {args.out}. Paste them into /family-feud/questions.")


if __name__ == "__main__":
    main()
