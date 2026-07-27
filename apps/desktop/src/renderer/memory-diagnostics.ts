export type MemoryDiagnosticCounter =
  | "detailRequestsStarted"
  | "detailRequestsCompleted"
  | "resyncRequested"
  | "resyncCoalesced"
  | "resyncCompleted"
  | "projectionAppended"
  | "projectionReplaced"
  | "ignoredStaleResponses";

export function incrementMemoryDiagnostic(counter: MemoryDiagnosticCounter): void {
  if (import.meta.env.VITE_CAICLI_MEMORY_DIAGNOSTICS !== "1") return;
  window.caicliMemoryDiagnostics?.increment(counter);
}
