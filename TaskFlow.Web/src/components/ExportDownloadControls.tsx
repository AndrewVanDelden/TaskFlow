import type { TaskKind } from '../types'
import type { ExportFormat } from '../api/jobApplications'
import { useExportDownload, type DownloadMode } from '../hooks/useExportDownload'
import { exportKindFor } from '../lib/taskKind'

const FORMATS: { format: ExportFormat; label: string }[] = [
  { format: 'pdf', label: 'PDF' },
  { format: 'markdown', label: 'Markdown' },
]

// A task's generated-document controls: PDF and Markdown buttons, each showing its own loading
// state so one action doesn't block the other, plus a shared error if the last attempt failed
// (role="alert", matching ApplicationReviewCard's error-display convention). mode="download"
// (default) saves the file to disk (Done/Approved tasks, the original use case); mode="preview"
// opens it in a new tab instead (ApplicationReviewCard's Review-stage use case, user report
// 2026-08-22) - button labels read "View"/"Opening…" rather than "Download"/"Downloading…" so they
// stay honest about what actually happens.
export function ExportDownloadControls({
  applicationId,
  kind,
  mode = 'download',
}: {
  applicationId: number
  kind: TaskKind
  mode?: DownloadMode
}) {
  const { downloading, error, download } = useExportDownload(applicationId)
  const exportKind = exportKindFor(kind)
  const actionLabel = mode === 'preview' ? 'View' : 'Download'
  const busyLabel = mode === 'preview' ? 'Opening…' : 'Downloading…'

  return (
    <div className="mt-2" onPointerDown={(e) => e.stopPropagation()}>
      <div className="flex gap-2">
        {FORMATS.map(({ format, label }) => (
          <button
            key={format}
            onClick={() => download(exportKind, format, mode)}
            disabled={downloading.has(`${exportKind}-${format}`)}
            className="flex-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-50 disabled:cursor-not-allowed text-white text-xs font-semibold px-2 py-1 rounded"
          >
            {downloading.has(`${exportKind}-${format}`) ? busyLabel : `${actionLabel} ${label}`}
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
