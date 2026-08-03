import * as vscode from "vscode";

/**
 * The setting that names the notebook interface language. Exported so a
 * configuration-change listener can test for it without repeating the string.
 */
export const LANGUAGE_SETTING = "verso.language";

/**
 * Language tags Verso ships an interface in, mirroring the .NET side. Kept here rather
 * than asked for at runtime because the language has to be decided before anything .NET
 * starts: it is passed to the host process on its command line and to the notebook app
 * as a boot parameter.
 */
const SHIPPED = ["en", "de", "es", "ja", "zh-Hans"];

/**
 * VS Code display-language identifiers that are not the language tag .NET expects.
 * There are only a handful, and the Chinese pair is the reason this table exists at all:
 * `zh-cn` has to become `zh-Hans` because .NET names Chinese by script, and `zh-tw` maps
 * to a script Verso does not ship, so it deliberately finds no translation rather than
 * being served simplified characters.
 */
const DISPLAY_LANGUAGES: Record<string, string> = {
  "zh-cn": "zh-Hans",
  "zh-tw": "zh-Hant",
};

/**
 * Narrows a language tag onto a shipped language, dropping the region when there is no
 * regional translation, so `de-AT` finds German.
 *
 * @returns The shipped tag, or undefined when the language is not one of them.
 */
function match(tag: string | undefined): string | undefined {
  if (!tag) {
    return undefined;
  }

  const normalized = DISPLAY_LANGUAGES[tag.toLowerCase()] ?? tag;
  const candidates = [normalized, normalized.split("-")[0]];

  for (const candidate of candidates) {
    const shipped = SHIPPED.find(
      (s) => s.toLowerCase() === candidate.toLowerCase()
    );
    if (shipped) {
      return shipped;
    }
  }

  return undefined;
}

/**
 * The language to run the notebook interface and the host process in.
 *
 * An explicit setting is passed through as written, even when it is not a shipped
 * language, so that a hand-set value reaches .NET and is either understood there or
 * falls back there. Left on auto, the VS Code display language is used when Verso has
 * that language and nothing is returned when it does not, which leaves the .NET side
 * free to answer from the environment or the operating system instead.
 *
 * @returns A language tag, or undefined to let the .NET side decide.
 */
export function resolveLanguage(): string | undefined {
  const setting = vscode.workspace
    .getConfiguration("verso")
    .get<string>("language")
    ?.trim();

  if (setting && setting !== "auto") {
    return setting;
  }

  return match(vscode.env.language);
}
