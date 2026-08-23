import { useState } from 'react'
import { exportResume, exportCoverLetter, type ExportFormat } from '../api/jobApplications'

export type ExportKind = 'resume' | 'coverLetter'

// 'download' (default) saves the file to disk, matching every existing caller (Done/Approved
// tasks). 'preview' opens it in a new tab instead - used by Review-stage controls, where the
// point is to inspect the real output before approving, not keep a copy (user report, 2026-08-22).
export type DownloadMode = 'download' | 'preview'

// Owns the download action's loading/error state for one application's export buttons, mirroring
// useApplicationReview's shape (an in-flight flag + a single error). "downloading" is a Set of
// active keys, each "<kind>-<format>" (e.g. "resume-pdf"), not a single string|null - both buttons
// for a task (PDF and Markdown) can be clicked before either resolves, and a single shared value
// cannot represent two independently in-flight downloads: whichever finished first would clear the
// other's still-in-flight indicator via `finally` (PR #48 review finding). A Set lets each key's
// presence be added/removed independently.
export function useExportDownload(applicationId: number) {
  const [downloading, setDownloading] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  const download = async (kind: ExportKind, format: ExportFormat, mode: DownloadMode = 'download') => {
    const key = `${kind}-${format}`
    setDownloading((prev) => new Set(prev).add(key))
    setError(null)
    try {
      const exportFile = kind === 'resume' ? exportResume : exportCoverLetter
      const { blob, filename } = await exportFile(applicationId, format)
      if (mode === 'preview') {
        openBlobInNewTab(blob)
      } else {
        triggerBrowserDownload(blob, filename)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to download the file.')
    } finally {
      setDownloading((prev) => {
        const next = new Set(prev)
        next.delete(key)
        return next
      })
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

// Opens the file in a new tab via the browser's own viewer (e.g. its native PDF viewer) instead of
// saving it to disk. The object URL is revoked after a delay, not immediately: window.open's tab
// still needs to load the content asynchronously, so revoking synchronously here would race it.
function openBlobInNewTab(blob: Blob): void {
  const url = URL.createObjectURL(blob)
  window.open(url, '_blank')
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
