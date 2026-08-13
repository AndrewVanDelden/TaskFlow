import type { TaskKind } from '../types'
import type { ExportKind } from '../hooks/useExportDownload'

// Single source of truth for deriving other vocabularies from a task's Kind (Epic 3 Pre-Merge
// Code Review, finding 3.15): TaskCardView's display badge and ExportDownloadControls' export-route
// key used to switch on the same 'ResumeTailoring'/'CoverLetterTailoring' string literals
// independently, with no shared source.
export function taskKindLabel(kind: TaskKind): string {
  switch (kind) {
    case 'ResumeTailoring':
      return 'Resume'
    case 'CoverLetterTailoring':
      return 'Cover letter'
    case 'Generic':
      return kind
  }
}

export function exportKindFor(kind: TaskKind): ExportKind {
  return kind === 'CoverLetterTailoring' ? 'coverLetter' : 'resume'
}
