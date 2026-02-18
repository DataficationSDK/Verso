// JSON-RPC 2.0 message types

export interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: number;
  method: string;
  params?: unknown;
}

export interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: number;
  result?: unknown;
  error?: JsonRpcError;
}

export interface JsonRpcNotification {
  jsonrpc: "2.0";
  method: string;
  params?: unknown;
}

export interface JsonRpcError {
  code: number;
  message: string;
  data?: unknown;
}

// --- Notebook DTOs ---

export interface NotebookOpenParams {
  content: string;
}

export interface NotebookSetFilePathParams {
  filePath: string;
}

export interface NotebookOpenResult {
  title?: string;
  cells: CellDto[];
  defaultKernel?: string;
}

export interface NotebookSaveResult {
  content: string;
}

export interface LanguagesResult {
  languages: LanguageDto[];
}

export interface LanguageDto {
  id: string;
  displayName: string;
}

export interface ToolbarActionsResult {
  actions: ToolbarActionDto[];
}

export interface ToolbarActionDto {
  actionId: string;
  displayName: string;
  icon?: string;
  placement: string;
  order: number;
}

// --- Cell DTOs ---

export interface CellDto {
  id: string;
  type: string;
  language?: string;
  source: string;
  outputs: CellOutputDto[];
}

export interface CellOutputDto {
  mimeType: string;
  content: string;
  isError: boolean;
  errorName?: string;
  errorStackTrace?: string;
}

export interface CellAddParams {
  type: string;
  language?: string;
  source: string;
}

export interface CellInsertParams {
  index: number;
  type: string;
  language?: string;
  source: string;
}

export interface CellRemoveParams {
  cellId: string;
}

export interface CellMoveParams {
  fromIndex: number;
  toIndex: number;
}

export interface CellUpdateSourceParams {
  cellId: string;
  source: string;
}

export interface CellGetParams {
  cellId: string;
}

// --- Execution DTOs ---

export interface ExecutionRunParams {
  cellId: string;
}

export interface ExecutionResultDto {
  cellId: string;
  status: string;
  executionCount: number;
  elapsedMs: number;
  outputs: CellOutputDto[];
  errorMessage?: string;
}

export interface ExecutionRunAllResult {
  results: ExecutionResultDto[];
}

export interface ExecutionStateNotification {
  cellId: string;
  state: "running" | "completed" | "failed" | "cancelled";
}

// --- Kernel DTOs ---

export interface KernelRestartParams {
  kernelId?: string;
}

export interface CompletionsParams {
  cellId: string;
  code: string;
  cursorPosition: number;
}

export interface CompletionsResult {
  items: CompletionDto[];
}

export interface CompletionDto {
  displayText: string;
  insertText: string;
  kind: string;
  description?: string;
  sortText?: string;
}

export interface DiagnosticsParams {
  cellId: string;
  code: string;
}

export interface DiagnosticsResult {
  items: DiagnosticDto[];
}

export interface DiagnosticDto {
  severity: string;
  message: string;
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
  code?: string;
}

export interface HoverParams {
  cellId: string;
  code: string;
  cursorPosition: number;
}

export interface HoverResult {
  content?: string;
  mimeType: string;
  range?: RangeDto;
}

export interface RangeDto {
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
}

// --- Layout DTOs ---

export interface LayoutsResult {
  layouts: LayoutDto[];
}

export interface LayoutDto {
  id: string;
  displayName: string;
  icon?: string;
  requiresCustomRenderer: boolean;
  isActive: boolean;
}

export interface LayoutSwitchParams {
  layoutId: string;
}

export interface LayoutRenderResult {
  html: string;
}

export interface LayoutUpdateCellParams {
  cellId: string;
  row: number;
  col: number;
  width: number;
  height: number;
}

export interface LayoutSetEditModeParams {
  editMode: boolean;
}

// --- Theme DTOs ---

export interface ThemeResult {
  themeId: string;
  displayName: string;
  themeKind: string;
  colors: Record<string, string>;
  syntaxColors: Record<string, string>;
  typography: ThemeTypographyDto;
  spacing: ThemeSpacingDto;
}

export interface ThemeTypographyDto {
  editorFont: FontDto;
  uiFont: FontDto;
  proseFont: FontDto;
  codeOutputFont: FontDto;
}

export interface FontDto {
  family: string;
  sizePx: number;
  weight: number;
  lineHeight: number;
}

export interface ThemeSpacingDto {
  cellPadding: number;
  cellGap: number;
  toolbarHeight: number;
  sidebarWidth: number;
  contentMarginHorizontal: number;
  contentMarginVertical: number;
  cellBorderRadius: number;
  buttonBorderRadius: number;
  outputPadding: number;
  scrollbarWidth: number;
}

// --- Theme Switching DTOs ---

export interface ThemesResult {
  themes: ThemeListItemDto[];
}

export interface ThemeListItemDto {
  id: string;
  displayName: string;
  themeKind: string;
  isActive: boolean;
}

export interface ThemeSwitchParams {
  themeId: string;
}

// --- Extension Management DTOs ---

export interface ExtensionListResult {
  extensions: ExtensionInfoDto[];
}

export interface ExtensionInfoDto {
  extensionId: string;
  name: string;
  version: string;
  author?: string;
  description?: string;
  status: string;
  capabilities: string[];
}

export interface ExtensionToggleParams {
  extensionId: string;
}

// --- Settings DTOs ---

export interface SettingsGetDefinitionsResult {
  extensions: ExtensionSettingsDto[];
}

export interface ExtensionSettingsDto {
  extensionId: string;
  extensionName: string;
  definitions: SettingDefinitionDto[];
  currentValues: Record<string, unknown>;
}

export interface SettingDefinitionDto {
  name: string;
  displayName: string;
  description: string;
  settingType: string;
  defaultValue?: unknown;
  category?: string;
  constraints?: SettingConstraintsDto;
  order: number;
}

export interface SettingConstraintsDto {
  minValue?: number;
  maxValue?: number;
  pattern?: string;
  choices?: string[];
  maxLength?: number;
  maxItems?: number;
}

export interface SettingsGetResult {
  extensionId: string;
  values: Record<string, unknown>;
}

export interface SettingsUpdateParams {
  extensionId: string;
  name: string;
  value?: unknown;
}

export interface SettingsResetParams {
  extensionId: string;
  name: string;
}

export interface SettingsGetParams {
  extensionId: string;
}

export interface SettingsChangedNotification {
  extensionId: string;
  name: string;
  value?: unknown;
}

// --- Toolbar DTOs ---

export interface ToolbarGetEnabledStatesParams {
  placement: string;
  selectedCellIds: string[];
}

export interface ToolbarGetEnabledStatesResult {
  states: Record<string, boolean>;
}

export interface ToolbarExecuteParams {
  actionId: string;
  selectedCellIds: string[];
}

// --- Variable Explorer DTOs ---

export interface VariableListResult {
  variables: VariableEntryDto[];
}

export interface VariableEntryDto {
  name: string;
  typeName: string;
  valuePreview: string;
  isExpandable: boolean;
}

export interface VariableInspectParams {
  name: string;
}

export interface VariableInspectResult {
  name: string;
  typeName: string;
  mimeType: string;
  content: string;
}
