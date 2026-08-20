import { describe, it, expect } from 'vitest'
import { stepForStage } from './intakeSteps'

// Epic 3.1 Sprint 4 (U4.1). Split out of IngestDocument.test.tsx so the pure mapping function could
// live in its own module (react-refresh/only-export-components forbids a component file from also
// exporting a plain function). Covers all 5 IntakeStage values, including 'building' -> 3, which
// IngestDocument's own integration tests cannot reach (see IngestDocument.test.tsx and
// lib/intakeSteps.ts's own comments for why: startTailoring()'s navigate-on-success unmounts the
// component in the same commit that stage flips to 'building').
describe('stepForStage', () => {
  it('maps provide and parsing to step 1', () => {
    expect(stepForStage('provide')).toBe(1)
    expect(stepForStage('parsing')).toBe(1)
  })

  it('maps review and starting to step 2', () => {
    expect(stepForStage('review')).toBe(2)
    expect(stepForStage('starting')).toBe(2)
  })

  it('maps building to step 3', () => {
    expect(stepForStage('building')).toBe(3)
  })
})
