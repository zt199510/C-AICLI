import type { CatalogItemData, ComposerStateData, ContextDescriptorData } from "../generated/desktop-contracts";

export type ComposerStatus = "editing" | "validating" | "enqueueing" | "queued" | "error";
export type ComposerCatalogKind = "skill" | "expert" | "automation";

export interface SelectedCatalogItem {
  readonly kind: ComposerCatalogKind;
  readonly id: string;
  readonly label: string;
  readonly catalogRevision: string;
}

export interface ComposerDraft {
  readonly text: string;
  readonly contextSelections: readonly ContextDescriptorData[];
  readonly catalogSelections: readonly SelectedCatalogItem[];
  readonly status: ComposerStatus;
  readonly error: string | null;
}

export interface MentionResults {
  readonly loading: boolean;
  readonly error: string | null;
  readonly truncated: boolean;
  readonly context: readonly ContextDescriptorData[];
  readonly skills: readonly CatalogItemData[];
  readonly experts: readonly CatalogItemData[];
  readonly automations: readonly CatalogItemData[];
  readonly revisions: Readonly<Record<"skills" | "experts" | "automations", string>>;
}

export interface ComposerUiState {
  readonly drafts: Readonly<Record<string, ComposerDraft>>;
  readonly snapshot: ComposerStateData | null;
  readonly snapshotStatus: "idle" | "loading" | "ready" | "error";
  readonly mentions: MentionResults;
}

const emptyMentions: MentionResults = {
  loading: false, error: null, truncated: false, context: [], skills: [], experts: [], automations: [],
  revisions: { skills: "", experts: "", automations: "" },
};

export const initialComposerUiState: ComposerUiState = Object.freeze({
  drafts: {}, snapshot: null, snapshotStatus: "idle", mentions: emptyMentions,
});

export type ComposerAction =
  | { type: "reset" }
  | { type: "snapshot-loading" }
  | { type: "snapshot"; snapshot: ComposerStateData }
  | { type: "snapshot-error" }
  | { type: "snapshot-clear" }
  | { type: "text"; key: string; text: string }
  | { type: "add-context"; key: string; item: ContextDescriptorData }
  | { type: "add-catalog"; key: string; item: SelectedCatalogItem }
  | { type: "remove-context"; key: string; selectionId: string }
  | { type: "remove-catalog"; key: string; kind: ComposerCatalogKind; id: string }
  | { type: "status"; key: string; status: ComposerStatus; error?: string | null }
  | { type: "settle"; key: string }
  | { type: "clear-draft"; key: string }
  | { type: "move-draft"; fromKey: string; toKey: string }
  | { type: "mentions-loading" }
  | { type: "mentions"; value: MentionResults }
  | { type: "mentions-close" };

export function composerReducer(state: ComposerUiState, action: ComposerAction): ComposerUiState {
  switch (action.type) {
    case "reset": return initialComposerUiState;
    case "snapshot-loading": return { ...state, snapshotStatus: "loading" };
    case "snapshot": {
      const current = state.snapshot;
      const sameQueue = current?.workspaceId === action.snapshot.workspaceId && current.threadId === action.snapshot.threadId;
      if (sameQueue && action.snapshot.queueRevision < current.queueRevision) return state;
      return { ...state, snapshotStatus: "ready", snapshot: action.snapshot };
    }
    case "snapshot-error": return { ...state, snapshotStatus: "error", snapshot: null };
    case "snapshot-clear": return { ...state, snapshotStatus: "idle", snapshot: null, mentions: emptyMentions };
    case "text": return updateDraft(state, action.key, draft => ({ ...draft, text: action.text, status: "editing", error: null }));
    case "add-context": return updateDraft(state, action.key, draft => draft.contextSelections.some(item => item.selectionId === action.item.selectionId)
      ? draft : { ...draft, contextSelections: [...draft.contextSelections, action.item], status: "editing", error: null });
    case "add-catalog": return updateDraft(state, action.key, draft => draft.catalogSelections.some(item => item.kind === action.item.kind && item.id === action.item.id)
      ? draft : { ...draft, catalogSelections: [...draft.catalogSelections, action.item], status: "editing", error: null });
    case "remove-context": return updateDraft(state, action.key, draft => ({ ...draft, contextSelections: draft.contextSelections.filter(item => item.selectionId !== action.selectionId) }));
    case "remove-catalog": return updateDraft(state, action.key, draft => ({ ...draft, catalogSelections: draft.catalogSelections.filter(item => item.kind !== action.kind || item.id !== action.id) }));
    case "status": return updateDraft(state, action.key, draft => ({ ...draft, status: action.status, error: action.error ?? null }));
    case "settle": return updateDraft(state, action.key, draft =>
      draft.status === "validating" || draft.status === "enqueueing"
        ? { ...draft, status: "editing", error: null }
        : draft);
    case "clear-draft": return { ...state, drafts: { ...state.drafts, [action.key]: emptyDraft("queued") } };
    case "move-draft": {
      const draft = state.drafts[action.fromKey];
      if (!draft || action.fromKey === action.toKey) return state;
      const drafts = { ...state.drafts, [action.toKey]: draft };
      delete drafts[action.fromKey];
      return { ...state, drafts };
    }
    case "mentions-loading": return { ...state, mentions: { ...emptyMentions, loading: true } };
    case "mentions": return { ...state, mentions: action.value };
    case "mentions-close": return { ...state, mentions: emptyMentions };
  }
}

export function draftKey(workspaceId: string, threadId: string): string { return `${workspaceId}/${threadId}`; }
export function currentDraft(state: ComposerUiState, key: string | null): ComposerDraft {
  return key ? state.drafts[key] ?? emptyDraft() : emptyDraft();
}

function updateDraft(state: ComposerUiState, key: string, update: (draft: ComposerDraft) => ComposerDraft): ComposerUiState {
  return { ...state, drafts: { ...state.drafts, [key]: update(state.drafts[key] ?? emptyDraft()) } };
}

function emptyDraft(status: ComposerStatus = "editing"): ComposerDraft {
  return { text: "", contextSelections: [], catalogSelections: [], status, error: null };
}
