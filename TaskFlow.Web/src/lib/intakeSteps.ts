import type { IntakeStage } from '../hooks/useIntakeFlow'

// Epic 3.1 Sprint 4 (U4.1) - pure stage -> step mapping for IngestDocument's 3-step indicator.
// Lives here (not inlined in IngestDocument.tsx) so it stays a plain function export: co-locating
// it with the IngestDocument component export tripped the repo's react-refresh/only-export-
// components lint rule (a file that exports a component must only export components). Unit-tested
// directly for all 5 IntakeStage values (see intakeSteps.test.ts) since 'building' -> 3 is not
// otherwise reachable through IngestDocument's own integration tests: once useIntakeFlow's
// startTailoring() navigates to /board on success (U4.4), setStage('building') and navigate
// land in the same React commit (confirmed empirically - no intermediate paint), so IngestDocument
// unmounts before 'building' is ever visible there. The mapping still handles it correctly - cheap,
// correct, and forward-compatible if that navigation timing ever changes.
export function stepForStage(stage: IntakeStage): 1 | 2 | 3 {
  switch (stage) {
    case 'provide':
    case 'parsing':
      return 1
    case 'review':
    case 'starting':
      return 2
    case 'building':
      return 3
  }
}
