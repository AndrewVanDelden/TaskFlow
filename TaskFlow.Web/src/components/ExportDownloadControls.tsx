import type { TaskKind } from '../types'
import type { ExportFormat } from '../api/jobApplications'
import { useExportDownload } from '../hooks/useExportDownload'
import { exportKindFor } from '../lib/taskKind'

const FORMATS: { format: ExportFormat; label: string }[] = [
  { format: 'pdf', label: 'PDF' },
  { format: 'markdown', label: 'Markdown' },
]

// A Done task's own generated-document downloads: PDF and Markdown buttons, each showing its own
// loading state so one download doesn't block the other, plus a shared error if the last attempt
// failed (role="alert", matching ApplicationReviewCard's error-display convention).
export function ExportDownloadControls({
  applicationId,
  kind,
}: {
  applicationId: number
  kind: TaskKind
}) {
  const { downloading, error, download } = useExportDownload(applicationId)
  const exportKind = exportKindFor(kind)

  return (
    <div className="mt-2" onPointerDown={(e) => e.stopPropagation()}>
      <div className="flex gap-2">
        {FORMATS.map(({ format, label }) => (
          <button
            key={format}
            onClick={() => download(exportKind, format)}
            disabled={downloading.has(`${exportKind}-${format}`)}
            className="flex-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-50 disabled:cursor-not-allowed text-white text-xs font-semibold px-2 py-1 rounded"
          >
            {downloading.has(`${exportKind}-${format}`) ? 'Downloading…' : `Download ${label}`}
          </button>
        ))}
      </div>
      {error && (
        <p className="text-xs text-red-400 mt-1" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
