"""Fills in the translations that are missing from the shipped languages, against the API.

Reads every neutral resource file, works out which keys a language has not been given yet,
and asks Claude for those and only those. Existing translations are left alone, so adding a
handful of English strings costs a handful of translations rather than a retranslation of
the interface.

This is one of two routes, and the one that costs an API key. `export.py` and `merge.py`
are the other: they do the same reading and the same writing, and hand the part in between
to whoever is translating. See `README.md`.

    pip install anthropic
    export ANTHROPIC_API_KEY=...
    python3 build/i18n/translate.py                 # everything missing, all languages
    python3 build/i18n/translate.py --locale ja     # one language
    python3 build/i18n/translate.py --all           # replace what is already there
    python3 build/i18n/translate.py --dry-run       # report the work without doing it

The output is committed, and nothing in the build or in continuous integration runs this,
so no API key is needed to build Verso or to check the translations. Run `check.py`
afterwards, and have a reader of the language look over the result before shipping it.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

from resources import (
    LANGUAGE_NAMES,
    LOCALES,
    REPO_ROOT,
    Entry,
    discover,
    display,
    placeholders,
)

MODEL = "claude-opus-5"

# Enough for a batch of short interface strings with room to spare. A batch that would
# exceed it fails loudly rather than returning a truncated object, because the response has
# to parse as JSON to be used at all.
MAX_TOKENS = 16000

# Small enough that one bad batch is cheap to redo, large enough that the model sees
# neighbouring strings and keeps their wording consistent with each other.
BATCH_SIZE = 40

SYSTEM = """You are translating the interface of Verso, a computational notebook \
application, from English into {language}. The people reading it are programmers, data \
analysts, and scientists, and they will read your translation while working.

Follow this glossary exactly.

{glossary}

You are given a JSON array. Each element has a `key` naming the string, a `source` holding \
the English, and sometimes a `note` written for you by the developer explaining where the \
string appears and what constrains it. Honour the note.

Reply with a JSON object mapping each key to its translation, and nothing else: no prose \
before or after it, no code fence, no commentary. Include every key you were given."""


def build_prompt(items: list[tuple[str, Entry]]) -> str:
    payload = []
    for key, entry in items:
        element = {"key": key, "source": entry.value}
        if entry.comment:
            element["note"] = entry.comment
        payload.append(element)

    return json.dumps(payload, ensure_ascii=False, indent=2)


def parse_reply(text: str) -> dict[str, str]:
    """Reads the model's reply, tolerating a code fence it was asked not to add."""
    body = text.strip()

    if body.startswith("```"):
        body = body.split("\n", 1)[1] if "\n" in body else ""
        if body.rstrip().endswith("```"):
            body = body.rstrip()[: -len("```")]

    return json.loads(body)


def translate_batch(client, locale: str, glossary: str, items: list[tuple[str, Entry]]) -> dict[str, str]:
    """One request. Returns the translations that came back, whether or not they are sound."""
    system = SYSTEM.format(language=LANGUAGE_NAMES[locale], glossary=glossary)

    # Streamed because a large batch can run long enough to reach the request timeout, and
    # a timeout here would throw away a batch that was nearly finished.
    with client.messages.stream(
        model=MODEL,
        max_tokens=MAX_TOKENS,
        thinking={"type": "adaptive"},
        system=system,
        messages=[{"role": "user", "content": build_prompt(items)}],
    ) as stream:
        message = stream.get_final_message()

    text = "".join(block.text for block in message.content if block.type == "text")
    return parse_reply(text)


def sound(source: str, translation: str) -> bool:
    """Whether a translation can be used without a person looking at it first."""
    return bool(translation.strip()) and sorted(placeholders(source)) == sorted(
        placeholders(translation)
    )


def translate(client, locale: str, glossary: str, items: list[tuple[str, Entry]]) -> dict[str, str]:
    """Translates a batch and retries once for anything that came back unusable."""
    result = translate_batch(client, locale, glossary, items)

    retry = [
        (key, entry)
        for key, entry in items
        if not sound(entry.value, result.get(key, ""))
    ]

    if retry:
        print(f"    retrying {len(retry)} strings", flush=True)
        result.update(translate_batch(client, locale, glossary, retry))

    for key, entry in items:
        if not sound(entry.value, result.get(key, "")):
            # Left to English rather than written out wrong. check.py reports it as
            # missing, which is the truth and is fixable by rerunning.
            print(f"    gave up on {key}", file=sys.stderr)
            result.pop(key, None)

    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--locale",
        action="append",
        choices=LOCALES,
        help="Translate one language. Repeatable. Defaults to all of them.",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Retranslate strings that already have a translation.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Report what would be translated without calling the API.",
    )
    args = parser.parse_args()

    locales = args.locale or LOCALES
    glossary = (REPO_ROOT / "build" / "i18n" / "glossary.md").read_text(encoding="utf-8")

    sets = discover()
    if not sets:
        print("No neutral resource files found.", file=sys.stderr)
        return 1

    client = None
    if not args.dry_run:
        try:
            import anthropic
        except ImportError:
            print("This needs the anthropic package: pip install anthropic", file=sys.stderr)
            return 1

        if not os.environ.get("ANTHROPIC_API_KEY"):
            print("Set ANTHROPIC_API_KEY.", file=sys.stderr)
            return 1

        client = anthropic.Anthropic()

    for resource_set in sets:
        source = resource_set.source()

        for locale in locales:
            existing = resource_set.translation(locale)
            wanted = [
                (key, source[key])
                for key in sorted(source)
                if args.all or key not in existing
            ]

            if not wanted:
                continue

            path = resource_set.path_for(locale)
            print(f"{display(path)}: {len(wanted)} strings", flush=True)

            if args.dry_run:
                continue

            translations: dict[str, str] = {}
            for start in range(0, len(wanted), BATCH_SIZE):
                batch = wanted[start : start + BATCH_SIZE]
                print(f"  {start + 1}-{start + len(batch)}", flush=True)
                translations.update(translate(client, locale, glossary, batch))

            # Rebuilt from the English keys, so a key that was renamed or dropped leaves
            # with it rather than lingering in four languages.
            merged = {
                key: Entry(translations.get(key) or existing[key].value)
                for key in source
                if key in translations or key in existing
            }

            path.parent.mkdir(parents=True, exist_ok=True)
            resource_set.save(path, merged)

    return 0


if __name__ == "__main__":
    sys.exit(main())
