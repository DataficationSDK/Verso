/**
 * Turns a VS Code color theme's syntax colors into rules for the Monaco editor inside
 * the notebook webview.
 *
 * The two do not speak the same language. A VS Code theme colors TextMate scopes
 * ("keyword.control.loop", "entity.name.type"), while Monaco's built-in tokenizers emit a
 * much smaller vocabulary ("keyword", "type", "delimiter"). Running the TextMate grammars
 * in the webview would close that gap but costs a WASM regex engine and a grammar per
 * language, so instead each Monaco token is given the scope VS Code would most likely
 * have assigned, and the theme is asked what color that scope gets.
 *
 * Nothing here touches the VS Code API, so it can be exercised as plain functions.
 */

/** One entry of a theme's `tokenColors`, or of a user's `textMateRules`. */
export interface TextMateRule {
  scope?: string | string[];
  settings?: { foreground?: string; fontStyle?: string };
}

/** One Monaco syntax rule, as `versoMonaco.applyTheme` takes it. */
export interface MonacoTokenRule {
  token: string;
  foreground?: string;
  fontStyle?: string;
}

/**
 * Parses JSON that may carry comments and trailing commas, which theme files are
 * allowed to and commonly do.
 */
export function parseJsonc(text: string): unknown {
  let out = "";
  let i = 0;
  const n = text.length;
  while (i < n) {
    const c = text[i];
    if (c === '"') {
      const start = i++;
      while (i < n && text[i] !== '"') {
        i += text[i] === "\\" ? 2 : 1;
      }
      out += text.slice(start, ++i);
    } else if (c === "/" && text[i + 1] === "/") {
      while (i < n && text[i] !== "\n") i++;
    } else if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      i = end < 0 ? n : end + 2;
    } else {
      out += c;
      i++;
    }
  }

  // Trailing commas, now that no comment can sit between the comma and the bracket.
  // Strings are skipped so a "," followed by "]" inside one is left alone.
  let clean = "";
  i = 0;
  while (i < out.length) {
    const c = out[i];
    if (c === '"') {
      const start = i++;
      while (i < out.length && out[i] !== '"') {
        i += out[i] === "\\" ? 2 : 1;
      }
      clean += out.slice(start, ++i);
    } else if (c === ",") {
      let j = i + 1;
      while (j < out.length && /\s/.test(out[j])) j++;
      if (out[j] !== "}" && out[j] !== "]") clean += c;
      i++;
    } else {
      clean += c;
      i++;
    }
  }

  // A byte order mark is legal at the head of a file and fatal to JSON.parse.
  return JSON.parse(clean.replace(/^﻿/, ""));
}

// The shorthand keys of "editor.tokenColorCustomizations" and the scopes VS Code
// expands each of them to.
const CUSTOMIZATION_SCOPES: Record<string, string[]> = {
  comments: ["comment", "punctuation.definition.comment"],
  strings: ["string", "meta.embedded.assembly"],
  keywords: ["keyword", "keyword.control", "storage", "storage.type"],
  numbers: ["constant.numeric"],
  types: ["entity.name.type", "entity.name.class", "support.type", "support.class"],
  functions: ["entity.name.function", "support.function"],
  variables: ["variable", "entity.name.variable"],
};

function themeKeyMatches(key: string, themeName: string): boolean {
  // A key is one or more bracketed names, "[Theme A][Theme B]", and a name may end in
  // "*" to cover a family of themes.
  const names = key.match(/\[[^\]]+\]/g);
  if (!names) return false;
  return names.some((raw) => {
    const name = raw.slice(1, -1);
    return name.endsWith("*")
      ? themeName.startsWith(name.slice(0, -1))
      : name === themeName;
  });
}

function customizationBlock(block: Record<string, unknown>): TextMateRule[] {
  const rules: TextMateRule[] = [];
  for (const [key, scopes] of Object.entries(CUSTOMIZATION_SCOPES)) {
    const value = block[key];
    if (typeof value === "string") {
      rules.push({ scope: scopes, settings: { foreground: value } });
    } else if (value && typeof value === "object") {
      rules.push({ scope: scopes, settings: value as TextMateRule["settings"] });
    }
  }
  if (Array.isArray(block.textMateRules)) {
    rules.push(...(block.textMateRules as TextMateRule[]));
  }
  return rules;
}

/**
 * Expands the user's "editor.tokenColorCustomizations" into rules to append after the
 * theme's own, where they win any tie. Settings scoped to another theme are left out.
 */
export function customizationRules(
  customizations: unknown,
  themeName: string
): TextMateRule[] {
  if (!customizations || typeof customizations !== "object") return [];
  const all = customizations as Record<string, unknown>;
  const rules = customizationBlock(all);
  for (const [key, value] of Object.entries(all)) {
    if (key.startsWith("[") && value && typeof value === "object" &&
        themeKeyMatches(key, themeName)) {
      rules.push(...customizationBlock(value as Record<string, unknown>));
    }
  }
  return rules;
}

interface Resolved {
  foreground?: string;
  fontStyle?: string;
}

/**
 * What a theme gives one scope. As in TextMate, the color and the font style are settled
 * separately, each by the most specific selector that sets it, and a later rule wins a
 * tie. Selectors that depend on a parent scope ("source.cs string") are passed over,
 * because a Monaco token carries no parents to test them against.
 */
export function resolveScope(rules: readonly TextMateRule[], scope: string): Resolved {
  let fg: string | undefined;
  let fgScore = -1;
  let style: string | undefined;
  let styleScore = -1;

  for (const rule of rules) {
    if (!rule || !rule.settings || rule.scope === undefined) continue;
    const selectors = Array.isArray(rule.scope) ? rule.scope : String(rule.scope).split(",");
    for (const raw of selectors) {
      const sel = typeof raw === "string" ? raw.trim() : "";
      if (!sel || /\s/.test(sel)) continue;
      if (scope !== sel && !scope.startsWith(sel + ".")) continue;
      if (typeof rule.settings.foreground === "string" && sel.length >= fgScore) {
        fg = rule.settings.foreground;
        fgScore = sel.length;
      }
      if (typeof rule.settings.fontStyle === "string" && sel.length >= styleScore) {
        style = rule.settings.fontStyle;
        styleScore = sel.length;
      }
    }
  }

  const resolved: Resolved = {};
  if (fg !== undefined) resolved.foreground = fg;
  if (style !== undefined) resolved.fontStyle = style;
  return resolved;
}

// Monaco token -> the scopes to try, most likely first. A Monaco rule also covers every
// token beneath it ("keyword" covers "keyword.cs"), so only the tokens that should differ
// from their parent are listed. Plain identifiers are left out on purpose: Monaco cannot
// tell a type from a method from a local, and painting all three as variables reads worse
// than leaving them in the editor's text color.
const TOKEN_SCOPES: ReadonlyArray<readonly [string, readonly string[]]> = [
  ["comment", ["comment.line.double-slash"]],
  ["comment.doc", ["comment.block.documentation"]],
  ["string", ["string.quoted.double"]],
  ["string.escape", ["constant.character.escape"]],
  ["string.key.json", ["support.type.property-name.json"]],
  ["string.value.json", ["string.quoted.double.json"]],
  ["string.link", ["markup.underline.link"]],
  ["regexp", ["string.regexp"]],
  ["number", ["constant.numeric"]],
  ["constant", ["constant.language"]],
  ["keyword", ["keyword", "storage.type", "keyword.control"]],
  ["keyword.json", ["constant.language.json"]],
  ["keyword.md", ["markup.heading", "entity.name.section"]],
  ["keyword.preprocessor", ["meta.preprocessor", "keyword.control.directive"]],
  ["namespace.cpp", ["meta.preprocessor", "keyword.control.directive"]],
  ["directive", ["meta.preprocessor", "keyword.control.directive"]],
  ["type", ["entity.name.type", "support.type", "support.class"]],
  ["function", ["entity.name.function", "support.function"]],
  ["predefined", ["support.function"]],
  ["variable", ["variable.other.readwrite"]],
  ["variable.predefined", ["variable.language", "support.variable"]],
  ["variable.parameter", ["variable.parameter"]],
  ["variable.md", ["markup.inline.raw"]],
  ["variable.source.md", ["markup.inline.raw"]],
  ["operator", ["keyword.operator"]],
  ["operators", ["keyword.operator"]],
  ["delimiter", ["punctuation.separator", "meta.brace.round"]],
  ["delimiter.html", ["punctuation.definition.tag"]],
  ["delimiter.xml", ["punctuation.definition.tag"]],
  ["tag", ["entity.name.tag"]],
  ["metatag", ["entity.name.tag"]],
  ["key", ["entity.name.tag.yaml"]],
  ["attribute.name", ["entity.other.attribute-name"]],
  ["attribute.value", ["string.quoted.double"]],
  ["attribute.value.number", ["constant.numeric"]],
  ["attribute.value.unit", ["keyword.other.unit"]],
  ["attribute.value.hex", ["constant.other.color"]],
  ["annotation", ["entity.name.function.decorator", "storage.type.annotation"]],
  ["namespace", ["entity.name.namespace", "entity.name.type.namespace"]],
  ["emphasis", ["markup.italic"]],
  ["strong", ["markup.bold"]],
  ["invalid", ["invalid.illegal"]],
];

// Monaco's emphasis and strong tokens mean nothing without a font style, and a theme is
// free to leave that to the markdown grammar's defaults.
const DEFAULT_FONT_STYLE: Record<string, string> = {
  emphasis: "italic",
  strong: "bold",
};

// Several Monaco tokenizers (C#, F#, Java, PowerShell) put the keyword itself in the
// token, "keyword.foreach". That is enough to give back the distinctions a TextMate
// grammar draws between kinds of keyword, which many themes color quite differently.
const KEYWORD_SCOPES: ReadonlyArray<readonly [string, readonly string[]]> = [
  ["keyword.control", [
    "if", "else", "elif", "then", "switch", "case", "match", "for", "foreach", "while",
    "do", "break", "continue", "return", "goto", "throw", "try", "catch", "finally",
    "yield", "await", "when",
  ]],
  ["storage.modifier", [
    "public", "private", "protected", "internal", "static", "readonly", "const", "sealed",
    "abstract", "virtual", "override", "async", "extern", "unsafe", "volatile", "partial",
    "final", "mutable", "inline",
  ]],
  ["storage.type", [
    "class", "struct", "interface", "enum", "record", "delegate", "event", "var", "let",
    "function", "param", "module",
  ]],
  ["keyword.type", [
    "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint", "long",
    "ulong", "short", "ushort", "object", "string", "void", "dynamic", "boolean",
  ]],
  ["keyword.operator.expression", ["new", "typeof", "sizeof", "nameof", "is", "as", "instanceof"]],
  ["constant.language", ["true", "false", "null"]],
  ["variable.language", ["this", "base", "super"]],
];

/**
 * Builds the Monaco rules for a theme's `tokenColors`. A token the theme says nothing
 * about gets no rule, and so falls to the editor's text color as it would in VS Code.
 */
export function buildMonacoRules(tokenColors: readonly TextMateRule[]): MonacoTokenRule[] {
  const rules: MonacoTokenRule[] = [];

  const add = (token: string, scopes: readonly string[]): void => {
    let hit: Resolved = {};
    for (const scope of scopes) {
      hit = resolveScope(tokenColors, scope);
      if (hit.foreground !== undefined) break;
    }
    const fontStyle = hit.fontStyle ?? DEFAULT_FONT_STYLE[token];
    if (hit.foreground === undefined && fontStyle === undefined) return;
    const rule: MonacoTokenRule = { token };
    if (hit.foreground !== undefined) rule.foreground = hit.foreground;
    if (fontStyle !== undefined) rule.fontStyle = fontStyle;
    rules.push(rule);
  };

  for (const [token, scopes] of TOKEN_SCOPES) add(token, scopes);
  for (const [scope, words] of KEYWORD_SCOPES) {
    for (const word of words) add(`keyword.${word}`, [scope]);
  }
  return rules;
}
