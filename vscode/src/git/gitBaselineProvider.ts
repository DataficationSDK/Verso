import * as vscode from "vscode";
import { GitAPI, GitExtension, Ref, RefType, Repository } from "./vscodeGitApi";
import { log } from "../log";

/**
 * Resolves notebook baselines from git history via the built-in `vscode.git` extension.
 *
 * The git extension is a soft dependency: it is acquired lazily with every failure swallowed,
 * so a fork or profile that strips the built-in git support leaves the extension fully
 * functional with git comparison sources simply reported as unavailable. No
 * `extensionDependencies` entry exists for it by design.
 */
export class GitBaselineProvider {
  private api: GitAPI | undefined;
  private activationPromise: Promise<void> | undefined;

  /** Acquires the git API once; safe to call repeatedly. */
  activate(): Promise<void> {
    this.activationPromise ??= this.activateCore();
    return this.activationPromise;
  }

  private async activateCore(): Promise<void> {
    try {
      const extension =
        vscode.extensions.getExtension<GitExtension>("vscode.git");
      if (!extension) {
        log.info("vscode.git extension not present; git comparisons unavailable");
        return;
      }
      const exports = extension.isActive
        ? extension.exports
        : await extension.activate();
      if (!exports?.enabled) {
        log.info("vscode.git extension disabled; git comparisons unavailable");
        return;
      }
      this.api = exports.getAPI(1);
    } catch (err) {
      log.warn(`vscode.git API unavailable: ${err}`);
      this.api = undefined;
    }
  }

  isAvailableFor(uri: vscode.Uri | undefined): boolean {
    return uri !== undefined && this.getRepo(uri) !== undefined;
  }

  async showAtRef(uri: vscode.Uri, ref: string): Promise<string> {
    const repo = this.getRepo(uri);
    if (!repo) {
      throw new Error(
        vscode.l10n.t("This notebook is not inside a git repository.")
      );
    }
    try {
      return await repo.show(ref, uri.fsPath);
    } catch (err) {
      throw new Error(this.friendlyShowError(err, uri, ref));
    }
  }

  /**
   * Branches and tags of the repository containing `uri`, shaped for a quick pick.
   * Local branches first, then remote branches, then tags.
   */
  listRefsForQuickPick(
    uri: vscode.Uri
  ): Array<{ label: string; description: string; ref: string }> {
    const repo = this.getRepo(uri);
    if (!repo) {
      return [];
    }

    const describe = (kind: string) => (r: Ref) => ({
      label:
        r.name ??
        r.commit ??
        vscode.l10n.t({
          message: "(unnamed)",
          comment: ["Stands in for a branch or tag that has no name to show."],
        }),
      description: `${kind}${r.commit ? ` ${r.commit.substring(0, 8)}` : ""}`,
      ref: r.name ?? r.commit ?? "",
    });

    const refs = repo.state.refs;
    return [
      ...refs
        .filter((r) => r.type === RefType.Head && r.name)
        .map(
          describe(
            vscode.l10n.t({
              message: "branch",
              comment: [
                "Says what kind of thing is listed, shown beside its name. A line of work in version control.",
              ],
            })
          )
        ),
      ...refs
        .filter((r) => r.type === RefType.RemoteHead && r.name)
        .map(
          describe(
            vscode.l10n.t({
              message: "remote branch",
              comment: ["A branch that lives on the server rather than on this machine."],
            })
          )
        ),
      ...refs
        .filter((r) => r.type === RefType.Tag && r.name)
        .map(
          describe(
            vscode.l10n.t({
              message: "tag",
              comment: ["A name pinned to one point in a project's history."],
            })
          )
        ),
    ].filter((item) => item.ref.length > 0);
  }

  private getRepo(uri: vscode.Uri): Repository | undefined {
    return this.api?.getRepository(uri) ?? undefined;
  }

  private friendlyShowError(err: unknown, uri: vscode.Uri, ref: string): string {
    const raw = err instanceof Error ? err.message : String(err);
    const fileName = uri.path.split("/").pop() ?? uri.fsPath;
    if (
      raw.includes("exists on disk, but not in") ||
      raw.includes("does not exist in") ||
      // The git API's repo.show throws this when the file has no entry in the ref's
      // tree (untracked, ignored, or a path-casing mismatch with the index), with the
      // repository's ENTIRE file listing appended. Never surface that raw message.
      raw.includes("relative path not found")
    ) {
      return vscode.l10n.t(
        "'{0}' is not tracked at '{1}'. Commit the file first, or pick a different ref.",
        fileName,
        ref
      );
    }
    if (raw.includes("unknown revision") || raw.includes("invalid object name")) {
      return vscode.l10n.t("'{0}' is not a known branch, tag, or commit.", ref);
    }

    // Fallback: keep only the first line, capped, so an unexpected git failure stays a
    // sentence rather than a wall of output. The detail itself is whatever git said,
    // which git says in its own language and this cannot translate.
    const firstLine = raw.split("\n", 1)[0] ?? "";
    const detail =
      firstLine.length > 200 ? `${firstLine.substring(0, 200)}...` : firstLine;
    return vscode.l10n.t(
      "git could not read '{0}' at '{1}': {2}",
      fileName,
      ref,
      detail
    );
  }
}
