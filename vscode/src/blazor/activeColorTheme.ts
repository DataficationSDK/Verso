import * as vscode from "vscode";
import { log } from "../log";
import {
  MonacoTokenRule,
  TextMateRule,
  buildMonacoRules,
  customizationRules,
  parseJsonc,
} from "./themeTokenRules";

export type MonacoBaseTheme = "vs" | "vs-dark" | "hc-black" | "hc-light";

/** The syntax half of the notebook editor's theme; the webview supplies the colors. */
export interface MonacoTokenTheme {
  base: MonacoBaseTheme;
  rules: MonacoTokenRule[];
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

async function readTokenColors(file: vscode.Uri, depth: number): Promise<TextMateRule[]> {
  if (depth > MAX_INCLUDE_DEPTH || !file.path.toLowerCase().endsWith(".json")) {
    // The other format a theme may use is a TextMate property list, which is rare now
    // and not worth a parser; such a theme keeps the stock syntax colors.
    return [];
  }

  const bytes = await vscode.workspace.fs.readFile(file);
  const theme = parseJsonc(new TextDecoder("utf-8").decode(bytes)) as {
    include?: string;
    tokenColors?: TextMateRule[] | string;
  } | null;
  if (!theme || typeof theme !== "object") return [];

  const dir = vscode.Uri.joinPath(file, "..");
  const rules: TextMateRule[] = [];
  if (typeof theme.include === "string") {
    rules.push(...(await readTokenColors(vscode.Uri.joinPath(dir, theme.include), depth + 1)));
  }
  if (typeof theme.tokenColors === "string") {
    rules.push(...(await readTokenColors(vscode.Uri.joinPath(dir, theme.tokenColors), depth + 1)));
  } else if (Array.isArray(theme.tokenColors)) {
    rules.push(...theme.tokenColors);
  }
  return rules;
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
  try {
    const located = locateTheme(base);
    if (!located) return { base, rules: [] };

    const tokenColors = await readTokenColors(located.file, 0);
    if (tokenColors.length === 0) return { base, rules: [] };

    const custom = vscode.workspace
      .getConfiguration("editor")
      .get<unknown>("tokenColorCustomizations");
    return {
      base,
      rules: buildMonacoRules([...tokenColors, ...customizationRules(custom, located.name)]),
    };
  } catch (err) {
    log.warn(`Could not read the active color theme's syntax colors: ${err}`);
    return { base, rules: [] };
  }
}
