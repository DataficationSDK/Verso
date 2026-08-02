"""Writes out the strings a language is still missing, for somebody to translate.

`translate.py` does this and the translating in one go, against the API. This does the same
work up to the point where the words are actually chosen, and hands that part to whoever is
reading: an assistant in a session, a translator with a text editor, a translation service.
What comes back goes in through `merge.py`.

    python3 build/i18n/export.py de
    python3 build/i18n/export.py de --set Verso.Ado/Strings    # one resource set
    python3 build/i18n/export.py de --limit 100                # a hundred at a time
    python3 build/i18n/export.py de --all                      # including what is done

Only the missing strings are asked for, so exporting again after a partial merge asks for
what is still outstanding and nothing else. That is what makes a language safe to do in
passes: run this, translate, merge, run this again.

The file carries the English and whatever note the developer left, because that note is the
only context a translator gets. It does not carry the glossary, which is a document to read
once rather than a preamble to repeat.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from resources import LANGUAGE_NAMES, LOCALES, REPO_ROOT, discover, display

# Working files, not artifacts. Ignored by git, because a half-finished German batch is
# nobody else's business and the finished translations live in the resource files.
PENDING = REPO_ROOT / "build" / "i18n" / "pending"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("locale", choices=LOCALES, help="The language to ask for.")
    parser.add_argument(
        "--set",
        dest="sets",
        action="append",
        help="Limit to resource sets whose name contains this. Repeatable.",
    )
    parser.add_argument(
        "--limit",
        type=int,
        help="Ask for at most this many strings, so a large language can be done in passes.",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Include strings that already have a translation.",
    )
    parser.add_argument("--out", type=Path, help="Where to write. Defaults to pending/<locale>.json")
    args = parser.parse_args()

    sets = discover()
    if not sets:
        print("No neutral resource files found.", file=sys.stderr)
        return 1

    if args.sets:
        wanted = [s.lower() for s in args.sets]
        sets = [s for s in sets if any(w in s.name.lower() for w in wanted)]
        if not sets:
            print(f"No resource set matches {', '.join(args.sets)}.", file=sys.stderr)
            return 1

    body: dict[str, dict[str, dict[str, str]]] = {}
    total = 0

    for resource_set in sets:
        if args.limit is not None and total >= args.limit:
            break

        source = resource_set.source()
        existing = resource_set.translation(args.locale)

        entries: dict[str, dict[str, str]] = {}
        for key in sorted(source):
            if not args.all and key in existing:
                continue
            if args.limit is not None and total + len(entries) >= args.limit:
                break

            entry = {"en": source[key].value}
            if source[key].comment:
                entry["note"] = source[key].comment
            entries[key] = entry

        if entries:
            body[resource_set.name] = entries
            total += len(entries)

    if not total:
        print(f"{LANGUAGE_NAMES[args.locale]} is up to date. Nothing to export.")
        return 0

    out = args.out or (PENDING / f"{args.locale}.json")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(
        json.dumps(
            {"locale": args.locale, "language": LANGUAGE_NAMES[args.locale], "sets": body},
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )

    for name, entries in body.items():
        print(f"  {name}: {len(entries)}")

    print(f"\n{total} strings for {LANGUAGE_NAMES[args.locale]} in {display(out)}")
    print("Read build/i18n/glossary.md, then answer with merge.py's shape.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
