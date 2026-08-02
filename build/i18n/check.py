"""Checks the translations against the English strings they came from.

Translations are committed files, so they drift: a string gets added and only English
knows about it, a key gets renamed and four files keep the old one, a translation quietly
loses the `{0}` that was going to be filled in with a file name. None of that shows up at
build time, because a missing translation falls back to English and a broken placeholder
only fails when the message is finally shown.

    python3 build/i18n/check.py

Prints what is wrong and exits non-zero, so it can run in continuous integration. It needs
no API key: it reads the committed files and nothing else.
"""

from __future__ import annotations

import json
import re
import sys

from resources import LOCALES, PSEUDO, REPO_ROOT, discover, display, placeholders

# How the editor's manifest points at a string it wants translated. A reference with no
# entry behind it is drawn on screen exactly as written, braces and all.
NLS_REFERENCE = re.compile(r"^%([^%]+)%$")


def check_locale(resource_set, locale: str, source) -> list[str]:
    """Every problem found in one language of one resource set."""
    problems: list[str] = []
    path = resource_set.path_for(locale)

    if not path.exists():
        return [f"{display(path)}: missing, {len(source)} strings untranslated"]

    translated = resource_set.translation(locale)
    where = display(path)

    for key in sorted(set(source) - set(translated)):
        problems.append(f"{where}: missing key {key}")

    # An orphan is usually a rename that only landed in English, and it is worth reporting
    # rather than deleting, because the translation it holds may still be wanted under the
    # new name.
    for key in sorted(set(translated) - set(source)):
        problems.append(f"{where}: no longer in English, key {key}")

    for key in sorted(set(source) & set(translated)):
        expected = sorted(placeholders(source[key].value))
        actual = sorted(placeholders(translated[key].value))
        if expected != actual:
            problems.append(
                f"{where}: placeholders differ in {key}, "
                f"English has {expected or 'none'} and the translation has {actual or 'none'}"
            )

        if not translated[key].value.strip():
            problems.append(f"{where}: empty translation for {key}")

    return problems


def check_manifest() -> list[str]:
    """Whether the editor manifest and the strings behind it still agree.

    The manifest names its translatable strings indirectly, and nothing verifies the
    naming: a reference with no entry is drawn literally, so a mistyped key shows up as
    `%command.newNotebook.title%` in the Command Palette rather than as a failure. An entry
    with nothing referencing it is the same mistake seen from the other side, and it also
    costs every translator the work of translating a string nobody will read.
    """
    manifest = REPO_ROOT / "vscode" / "package.json"
    strings = REPO_ROOT / "vscode" / "package.nls.json"
    if not manifest.exists() or not strings.exists():
        return []

    referenced: set[str] = set()

    def walk(node) -> None:
        if isinstance(node, dict):
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for value in node:
                walk(value)
        elif isinstance(node, str) and (match := NLS_REFERENCE.match(node)):
            referenced.add(match.group(1))

    walk(json.loads(manifest.read_text(encoding="utf-8")))
    declared = set(json.loads(strings.read_text(encoding="utf-8")))

    where = display(strings)
    return [
        *(f"{where}: {key} is used in package.json but not declared here"
          for key in sorted(referenced - declared)),
        *(f"{where}: {key} is declared here but nothing in package.json uses it"
          for key in sorted(declared - referenced)),
    ]


def main() -> int:
    sets = discover()
    if not sets:
        print("No neutral resource files found.", file=sys.stderr)
        return 1

    problems: list[str] = check_manifest()
    strings = 0

    for resource_set in sets:
        source = resource_set.source()
        strings += len(source)

        # The pseudo-locale is checked alongside the real ones. It is generated, so a
        # problem there means pseudo.py has not been run since the English changed, which
        # is worth catching for the same reason: it is what the coverage sweep runs on.
        for locale in [*LOCALES, PSEUDO]:
            problems.extend(check_locale(resource_set, locale, source))

    for problem in problems:
        print(problem)

    languages = len(LOCALES) + 1
    if problems:
        print(f"\n{len(problems)} problems across {strings} strings in {languages} languages.")
        return 1

    print(f"{strings} strings, {languages} languages, no problems.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
