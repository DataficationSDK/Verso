import * as vscode from "vscode";

/**
 * Minimal typings for the built-in `vscode.git` extension's exported API, limited to the
 * surface the notebook diff feature uses. The full contract lives in the VS Code repository's
 * `extensions/git/src/api/git.d.ts`; these shapes mirror API version 1.
 */

export const enum RefType {
  Head = 0,
  RemoteHead = 1,
  Tag = 2,
}

export interface Ref {
  readonly type: RefType;
  readonly name?: string;
  readonly commit?: string;
  readonly remote?: string;
}

export interface RepositoryState {
  readonly HEAD: Ref | undefined;
  readonly refs: Ref[];
}

export interface Repository {
  readonly rootUri: vscode.Uri;
  readonly state: RepositoryState;
  /** Returns the content of `path` (an absolute file system path) as of `ref`. */
  show(ref: string, path: string): Promise<string>;
}

export interface GitAPI {
  readonly repositories: Repository[];
  getRepository(uri: vscode.Uri): Repository | null;
}

export interface GitExtension {
  readonly enabled: boolean;
  getAPI(version: 1): GitAPI;
}
