import { useState } from 'react'
import { exportResume, exportCoverLetter, type ExportFormat } from '../api/jobApplications'

export type ExportKind = 'resume' | 'coverLetter'

// Owns the download action's loading/error state for one application's export buttons, mirroring
// useApplicationReview's shape (an in-flight flag + a single error). "downloading" is keyed by
// "<kind>-<format>" (e.g. "resume-pdf") rather than a plain boolean so a caller can show a loading
// state on the one button that's in flight without disabling the others.
export function useExportDownload(applicationId: number) {
  const [downloading, setDownloading] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const download = async (kind: ExportKind, format: ExportFormat) => {
    setDownloading(`${kind}-${format}`)
    setError(null)
    try {
      const exportFile = kind === 'resume' ? exportResume : exportCoverLetter
      const { blob, filename } = await exportFile(applicationId, format)
      triggerBrowserDownload(blob, filename)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to download the file.')
    } finally {
      setDownloading(null)
    }
  }

  return { downloading, error, download }
}

// Standard browser download pattern: an object URL for the blob, a detached <a download> clicked
// programmatically, then the object URL released.
function triggerBrowserDownload(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.click()
  URL.revokeObjectURL(url)
}
