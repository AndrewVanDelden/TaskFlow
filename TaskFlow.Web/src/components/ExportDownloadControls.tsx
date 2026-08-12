import { useExportDownload } from '../hooks/useExportDownload'

// A Done task's own generated-document downloads: PDF and Markdown buttons, each showing its own
// loading state so one download doesn't block the other, plus a shared error if the last attempt
// failed (role="alert", matching ApplicationReviewCard's error-display convention).
export function ExportDownloadControls({
  applicationId,
  kind,
}: {
  applicationId: number
  kind: string
}) {
  const { downloading, error, download } = useExportDownload(applicationId)
  const exportKind = kind === 'CoverLetterTailoring' ? 'coverLetter' : 'resume'

  return (
    <div className="mt-2" onPointerDown={(e) => e.stopPropagation()}>
      <div className="flex gap-2">
        <button
          onClick={() => download(exportKind, 'pdf')}
          disabled={downloading.has(`${exportKind}-pdf`)}
          className="flex-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-40 disabled:cursor-not-allowed text-white text-xs font-semibold px-2 py-1 rounded"
        >
          {downloading.has(`${exportKind}-pdf`) ? 'Downloading…' : 'Download PDF'}
        </button>
        <button
          onClick={() => download(exportKind, 'markdown')}
          disabled={downloading.has(`${exportKind}-markdown`)}
          className="flex-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-40 disabled:cursor-not-allowed text-white text-xs font-semibold px-2 py-1 rounded"
        >
          {downloading.has(`${exportKind}-markdown`) ? 'Downloading…' : 'Download Markdown'}
        </button>
      </div>
      {error && (
        <p className="text-xs text-red-400 mt-1" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
