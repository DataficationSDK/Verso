"""Reading and writing the files that hold Verso's translatable strings.

Two formats carry the interface: .NET resource files under `src/**/Resources`, and the
JSON bundles the editor extension uses. They differ enough in shape that the scripts
around them would each grow two code paths, so both are wrapped here and everything else
in this directory works in terms of keys, values, and translator notes.

Run nothing here directly. `export.py`, `merge.py`, `translate.py`, `pseudo.py`, and
`check.py` are the entry points.
"""

from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path
from xml.sax.saxutils import escape

# Where the scripts sit relative to the repository.
REPO_ROOT = Path(__file__).resolve().parents[2]

# The languages Verso ships an interface in, English aside. Mirrors VersoCultures.Supported.
LOCALES = ["de", "es", "ja", "zh-Hans"]

# Generated rather than translated, and never offered in a picker. See pseudo.py.
PSEUDO = "qps-Ploc"

# Human names, used to address the translator and to caption a report.
LANGUAGE_NAMES = {
    "de": "German",
    "es": "Spanish",
    "ja": "Japanese",
    "zh-Hans": "Simplified Chinese",
    PSEUDO: "Pseudo-locale",
}

# The editor names languages its own way, so its file names do not match the .NET ones.
# Only the entries that actually differ are listed; anything absent is used as written.
VSCODE_IDS = {
    "zh-Hans": "zh-cn",
    PSEUDO: "qps-ploc",
}

# Anything a translation has to carry through unchanged: .NET's positional {0} and the
# named {placeholder} the editor bundles use. Doubled braces are an escaped literal brace
# and are matched first so they are not mistaken for an empty placeholder.
PLACEHOLDER = re.compile(r"\{\{|\}\}|\{[^{}]*\}")

# Copied from a resource file written by the .NET tooling, so generated files are
# byte-identical in everything but their entries and no editor reformats them on open.
RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


@dataclass
class Entry:
    """One string, and whatever context a translator was given for it."""

    value: str
    comment: str = ""


def display(path: Path) -> str:
    """A path as it should appear in a report, relative to the repository where it can be."""
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def placeholders(text: str) -> list[str]:
    """The placeholder tokens in a string, in the order they appear.

    Which tokens are present is what a check compares, not the order they arrive in: a
    translation is free to move `{0}` behind `{1}` because the target language puts the
    sentence together differently, but a translation that loses `{0}` altogether produces
    a message with a hole in it rather than an error.
    """
    return [m.group(0) for m in PLACEHOLDER.finditer(text) if m.group(0) not in ("{{", "}}")]


class ResourceSet:
    """A neutral file and the translated files that shadow it."""

    def __init__(self, neutral: Path):
        self.neutral = neutral

    @property
    def name(self) -> str:
        """A short, stable way to name this set in a report or a handoff file.

        The full path is unwieldy to type and to read, and the file name alone is
        ambiguous: ten assemblies each have a `Strings.resx`. So a set is named by what
        distinguishes it, which is the assembly it belongs to.
        """
        raise NotImplementedError

    def path_for(self, locale: str) -> Path:
        raise NotImplementedError

    def load(self, path: Path) -> dict[str, Entry]:
        raise NotImplementedError

    def save(self, path: Path, entries: dict[str, Entry]) -> None:
        raise NotImplementedError

    def source(self) -> dict[str, Entry]:
        return self.load(self.neutral)

    def translation(self, locale: str) -> dict[str, Entry]:
        """The entries already translated into a language, empty when the file is new."""
        path = self.path_for(locale)
        return self.load(path) if path.exists() else {}


class ResxSet(ResourceSet):
    """A .NET resource file, which compiles into one satellite assembly per language."""

    @property
    def name(self) -> str:
        # `src/Verso.Ado/Resources/Strings.resx` is named `Verso.Ado/Strings`, which is the
        # assembly whose satellite it becomes and the file within it.
        return f"{self.neutral.parents[1].name}/{self.neutral.stem}"

    def path_for(self, locale: str) -> Path:
        return self.neutral.with_name(f"{self.neutral.stem}.{locale}.resx")

    def load(self, path: Path) -> dict[str, Entry]:
        root = ElementTree.parse(path).getroot()
        entries: dict[str, Entry] = {}

        for data in root.findall("data"):
            name = data.get("name")
            # Entries carrying a type or mimetype hold something other than a string,
            # such as an icon. There are none today, and translating one would be wrong.
            if name is None or data.get("type") or data.get("mimetype"):
                continue

            value = data.findtext("value") or ""
            comment = data.findtext("comment") or ""
            entries[name] = Entry(value, comment)

        return entries

    def save(self, path: Path, entries: dict[str, Entry]) -> None:
        lines = [RESX_HEADER]

        for name in sorted(entries):
            entry = entries[name]
            lines.append(f'  <data name="{escape(name)}" xml:space="preserve">\n')
            lines.append(f"    <value>{escape(entry.value)}</value>\n")
            if entry.comment:
                lines.append(f"    <comment>{escape(entry.comment)}</comment>\n")
            lines.append("  </data>\n")

        lines.append("</root>")
        path.write_text("".join(lines), encoding="utf-8")


class JsonSet(ResourceSet):
    """One of the editor extension's bundles.

    Covers both `package.nls.json`, which names commands and settings, and
    `bundle.l10n.json`, which holds the strings the extension code passes through
    `vscode.l10n.t`. They share a format: a key maps either to the string itself or to an
    object carrying the string plus notes for whoever translates it.
    """

    @property
    def name(self) -> str:
        # Already unique and already short, so the path is the name, less the extension.
        return display(self.neutral)[: -len(".json")]

    def path_for(self, locale: str) -> Path:
        stem = self.neutral.name[: -len(".json")]
        return self.neutral.with_name(f"{stem}.{VSCODE_IDS.get(locale, locale)}.json")

    def load(self, path: Path) -> dict[str, Entry]:
        raw = json.loads(path.read_text(encoding="utf-8"))
        entries: dict[str, Entry] = {}

        for name, value in raw.items():
            if isinstance(value, dict):
                comment = value.get("comment", "")
                if isinstance(comment, list):
                    comment = " ".join(comment)
                entries[name] = Entry(value.get("message", ""), comment)
            else:
                entries[name] = Entry(value)

        return entries

    def save(self, path: Path, entries: dict[str, Entry]) -> None:
        # Notes are for translators, so they stay in the neutral file and are not copied
        # into the translations, where they would only be read back by the next script.
        body = {name: entries[name].value for name in sorted(entries)}
        path.write_text(
            json.dumps(body, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )


def discover() -> list[ResourceSet]:
    """Every neutral file in the repository, in a stable order.

    A file is neutral when its name carries no language, so `UI.resx` is a source and
    `UI.de.resx` is one of its translations.
    """
    sets: list[ResourceSet] = []

    for path in sorted((REPO_ROOT / "src").glob("**/Resources/*.resx")):
        if "." in path.stem or {"bin", "obj"} & set(path.parts):
            continue
        sets.append(ResxSet(path))

    for relative in ("vscode/package.nls.json", "vscode/l10n/bundle.l10n.json"):
        path = REPO_ROOT / relative
        if path.exists():
            sets.append(JsonSet(path))

    return sets
