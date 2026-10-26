import * as vscode from "vscode";
import { log } from "../log";
import {
  MonacoTokenRule,
  SemanticTokenStyle,
  TextMateRule,
  buildMonacoRules,
  buildSemanticRules,
  customizationRules,
  parseJsonc,
  semanticCustomization,
} from "./themeTokenRules";

export type MonacoBaseTheme = "vs" | "vs-dark" | "hc-black" | "hc-light";

/** The syntax half of the notebook editor's theme; the webview supplies the colors. */
export interface MonacoTokenTheme {
  base: MonacoBaseTheme;
  rules: MonacoTokenRule[];
  /** Whether VS Code would show semantic tokens with this theme, so the notebook does too. */
  semanticHighlighting: boolean;
}

/** What a theme file and the files it includes say about syntax colors. */
interface ThemeSyntax {
  tokenColors: TextMateRule[];
  semanticTokenColors: Record<string, SemanticTokenStyle>;
  semanticHighlighting?: boolean;
}

interface ThemeContribution {
  id?: string;
  label?: string;
  uiTheme?: string;
  path?: string;
}

interface LocatedTheme {
  name: string;
  file: vscode.Uri;
}

const MAX_INCLUDE_DEPTH = 8;

export function activeBaseTheme(): MonacoBaseTheme {
  switch (vscode.window.activeColorTheme.kind) {
    case vscode.ColorThemeKind.Dark:
      return "vs-dark";
    case vscode.ColorThemeKind.HighContrast:
      return "hc-black";
    case vscode.ColorThemeKind.HighContrastLight:
      return "hc-light";
    default:
      return "vs";
  }
}

/**
 * The names the active theme could be going by. VS Code reports the kind of the active
 * theme but not which one it is, and "workbench.colorTheme" is only the answer while
 * nothing is following the operating system; when something is, one of the preferred
 * themes is showing instead. Candidates are ordered by likelihood and the caller keeps
 * the first whose declared kind agrees with what is on screen.
 */
function candidateThemeNames(base: MonacoBaseTheme): string[] {
  const workbench = vscode.workspace.getConfiguration("workbench");
  const window = vscode.workspace.getConfiguration("window");
  const preferredKey = {
    "vs": "preferredLightColorTheme",
    "vs-dark": "preferredDarkColorTheme",
    "hc-black": "preferredHighContrastColorTheme",
    "hc-light": "preferredHighContrastLightColorTheme",
  }[base];

  const selected = workbench.get<string>("colorTheme");
  const preferred = workbench.get<string>(preferredKey);
  const ordered = window.get<boolean>("autoDetectColorScheme")
    ? [preferred, selected]
    : [selected, preferred];
  return ordered.filter((n): n is string => typeof n === "string" && n.length > 0);
}

function locateTheme(base: MonacoBaseTheme): LocatedTheme | undefined {
  const names = candidateThemeNames(base);
  let fallback: LocatedTheme | undefined;

  for (const name of names) {
    for (const ext of vscode.extensions.all) {
      const themes: ThemeContribution[] | undefined = ext.packageJSON?.contributes?.themes;
      if (!Array.isArray(themes)) continue;
      for (const theme of themes) {
        // The setting holds the id when the theme declares one and the label otherwise.
        if (!theme?.path || (theme.id ?? theme.label) !== name) continue;
        const located = { name, file: vscode.Uri.joinPath(ext.extensionUri, theme.path) };
        if ((theme.uiTheme ?? "vs") === base) return located;
        fallback ??= located;
      }
    }
  }
  return fallback;
}

async function readThemeSyntax(file: vscode.Uri, depth: number): Promise<ThemeSyntax> {
  const syntax: ThemeSyntax = { tokenColors: [], semanticTokenColors: {} };
  if (depth > MAX_INCLUDE_DEPTH || !file.path.toLowerCase().endsWith(".json")) {
    // The other format a theme may use is a TextMate property list, which is rare now
    // and not worth a parser; such a theme keeps the stock syntax colors.
    return syntax;
  }

  const bytes = await vscode.workspace.fs.readFile(file);
  const theme = parseJsonc(new TextDecoder("utf-8").decode(bytes)) as {
    include?: string;
    tokenColors?: TextMateRule[] | string;
    semanticTokenColors?: Record<string, SemanticTokenStyle>;
    semanticHighlighting?: boolean;
  } | null;
  if (!theme || typeof theme !== "object") return syntax;

  // An included theme comes first, and what this file says is laid over it.
  const dir = vscode.Uri.joinPath(file, "..");
  const merge = (other: ThemeSyntax): void => {
    syntax.tokenColors.push(...other.tokenColors);
    Object.assign(syntax.semanticTokenColors, other.semanticTokenColors);
    if (other.semanticHighlighting !== undefined) syntax.semanticHighlighting = other.semanticHighlighting;
  };
  if (typeof theme.include === "string") {
    merge(await readThemeSyntax(vscode.Uri.joinPath(dir, theme.include), depth + 1));
  }
  if (typeof theme.tokenColors === "string") {
    merge(await readThemeSyntax(vscode.Uri.joinPath(dir, theme.tokenColors), depth + 1));
  } else if (Array.isArray(theme.tokenColors)) {
    syntax.tokenColors.push(...theme.tokenColors);
  }
  if (theme.semanticTokenColors && typeof theme.semanticTokenColors === "object") {
    Object.assign(syntax.semanticTokenColors, theme.semanticTokenColors);
  }
  if (typeof theme.semanticHighlighting === "boolean") {
    syntax.semanticHighlighting = theme.semanticHighlighting;
  }
  return syntax;
}

/**
 * Whether VS Code shows semantic tokens: "editor.semanticHighlighting.enabled" decides
 * when it is a boolean, and otherwise ("configuredByTheme") the user's semantic token
 * customizations or the theme do.
 */
function semanticHighlightingEnabled(fromCustomizations: boolean | undefined, fromTheme: boolean | undefined): boolean {
  const setting = vscode.workspace.getConfiguration("editor").get<unknown>("semanticHighlighting.enabled");
  if (typeof setting === "boolean") return setting;
  return fromCustomizations ?? fromTheme ?? false;
}

/**
 * Reads the active color theme's syntax colors, with the user's own token color
 * customizations laid over them, as rules for the notebook's Monaco editors.
 *
 * VS Code has no API for a theme's token colors, so this finds the theme's file among
 * the installed extensions and reads it. Whenever that fails (the theme is a property
 * list, or lives where this extension host cannot read), the rules come back empty and
 * the editor keeps Monaco's stock syntax colors for the theme's kind.
 */
export async function loadActiveTokenTheme(): Promise<MonacoTokenTheme> {
  const base = activeBaseTheme();
  // Without the theme's file nothing says what VS Code would do, and the stock theme the
  // editor then keeps colors semantic tokens by its own rules.
  const unread: MonacoTokenTheme = { base, rules: [], semanticHighlighting: semanticHighlightingEnabled(undefined, true) };
  try {
    const located = locateTheme(base);
    if (!located) return unread;

    const syntax = await readThemeSyntax(located.file, 0);
    if (syntax.tokenColors.length === 0) return unread;

    const editor = vscode.workspace.getConfiguration("editor");
    const tokenColors = [
      ...syntax.tokenColors,
      ...customizationRules(editor.get<unknown>("tokenColorCustomizations"), located.name),
    ];
    const semantic = semanticCustomization(
      editor.get<unknown>("semanticTokenColorCustomizations"), located.name);
    return {
      base,
      rules: [
        ...buildMonacoRules(tokenColors),
        ...buildSemanticRules(tokenColors, { ...syntax.semanticTokenColors, ...semantic.rules }),
      ],
      semanticHighlighting: semanticHighlightingEnabled(semantic.enabled, syntax.semanticHighlighting),
    };
  } catch (err) {
    log.warn(`Could not read the active color theme's syntax colors: ${err}`);
    return unread;
  }
}
