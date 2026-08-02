"""Puts translated strings back into the files the build reads.

Takes what `export.py` asked for and somebody answered, and writes each string into the
resource file for its language. Translations already there are kept, so a language can be
done in passes, and a key English no longer has is dropped rather than carried in four
languages under a name nothing looks up.

    python3 build/i18n/merge.py build/i18n/pending/de.answer.json

The answer names its own language, so there is no argument to get wrong. Its shape is the
export's, with each string in place of the object describing it:

    {
      "locale": "de",
      "sets": {
        "Verso.Blazor.Shared/UI": { "Toolbar_Run": "Ausführen" }
      }
    }

A translation that dropped a placeholder, or came back empty, or answers a key English does
not have, is refused and named. Writing it would leave a message with a hole in it that
nothing notices until it is finally shown. Everything sound in the same run is still
written, so a rerun only has to cover what was named.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from resources import LANGUAGE_NAMES, LOCALES, Entry, discover, display, placeholders


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("answer", type=Path, help="The filled-in file to read.")
    args = parser.parse_args()

    if not args.answer.exists():
        print(f"{args.answer} does not exist.", file=sys.stderr)
        return 1

    payload = json.loads(args.answer.read_text(encoding="utf-8"))
    locale = payload.get("locale")
    if locale not in LOCALES:
        print(f"The file names its language as {locale!r}, which Verso does not ship.", file=sys.stderr)
        return 1

    by_name = {resource_set.name: resource_set for resource_set in discover()}
    refused: list[str] = []
    written = 0

    for name, answers in payload.get("sets", {}).items():
        resource_set = by_name.get(name)
        if resource_set is None:
            refused.append(f"{name}: no such resource set")
            continue

        source = resource_set.source()
        existing = resource_set.translation(locale)
        accepted: dict[str, str] = {}

        for key, value in answers.items():
            if not isinstance(value, str):
                refused.append(f"{name}/{key}: expected a translation, found {type(value).__name__}")
            elif key not in source:
                refused.append(f"{name}/{key}: not a key English has")
            elif not value.strip():
                refused.append(f"{name}/{key}: empty")
            elif sorted(placeholders(source[key].value)) != sorted(placeholders(value)):
                refused.append(
                    f"{name}/{key}: placeholders differ, English has "
                    f"{sorted(placeholders(source[key].value)) or 'none'} and this has "
                    f"{sorted(placeholders(value)) or 'none'}"
                )
            else:
                accepted[key] = value

        if not accepted:
            continue

        # Rebuilt from the English keys rather than updated in place, so a key that was
        # renamed or dropped since the last pass leaves with it. Notes stay in the neutral
        # file: they are written for a translator, and nothing reads them back out of here.
        merged = {
            key: Entry(accepted.get(key) or existing[key].value)
            for key in source
            if key in accepted or key in existing
        }

        path = resource_set.path_for(locale)
        path.parent.mkdir(parents=True, exist_ok=True)
        resource_set.save(path, merged)

        outstanding = len(source) - len(merged)
        note = f", {outstanding} still missing" if outstanding else ""
        print(f"  {display(path)}: {len(accepted)} written, {len(merged)} of {len(source)}{note}")
        written += len(accepted)

    plural = "string" if written == 1 else "strings"
    print(f"\n{written} {plural} merged into {LANGUAGE_NAMES[locale]}.", flush=True)

    if refused:
        print(f"\n{len(refused)} refused:", file=sys.stderr)
        for problem in refused:
            print(f"  {problem}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
