"""Generates the pseudo-locale from the English strings.

The pseudo-locale is a translation nobody reads. Every letter is accented and every string
is bracketed and padded, which makes it worth running for two reasons: anything still
showing plain English is a string somebody forgot to move into a resource file, and
anything clipped or wrapped badly is a place where a real translation, which tends to run
longer than English, will not fit.

    python3 build/i18n/pseudo.py

Rerun it whenever English strings are added or changed. The output is committed, so a
reviewer sees the same thing the next person to run the interface will.
"""

from __future__ import annotations

import sys

from resources import PLACEHOLDER, PSEUDO, Entry, discover, display

# One accented stand-in per letter, chosen to stay recognisable so a screenshot is still
# readable enough to tell which string is which.
ACCENTS = str.maketrans(
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ",
    "àbçdéfghïjklmñòpqrštùvwxÿzÀBÇDÉFGHÏJKLMÑÒPQRŠTÙVWXÝZ",
)

# Padding, so a string that only just fits in English is seen not to fit here. Real
# translations into German run roughly a third longer than the English they come from.
PAD = "···"


def disguise(text: str) -> str:
    """Accents a string, leaving its placeholders alone.

    A placeholder is filled in at runtime with a name or a number, so accenting one would
    either break the lookup or produce a value the reader cannot recognise.
    """
    parts: list[str] = []
    position = 0

    for match in PLACEHOLDER.finditer(text):
        parts.append(text[position : match.start()].translate(ACCENTS))
        parts.append(match.group(0))
        position = match.end()

    parts.append(text[position:].translate(ACCENTS))
    return "".join(parts)


def main() -> int:
    sets = discover()
    if not sets:
        print("No neutral resource files found.", file=sys.stderr)
        return 1

    for resource_set in sets:
        source = resource_set.source()
        generated = {
            key: Entry(f"[!!{disguise(entry.value)}{PAD}!!]")
            for key, entry in source.items()
        }

        path = resource_set.path_for(PSEUDO)
        path.parent.mkdir(parents=True, exist_ok=True)
        resource_set.save(path, generated)

        print(f"{len(generated):5d}  {display(path)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
