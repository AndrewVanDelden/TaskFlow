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

    // PR #65 review finding (found independently by two reviewers): window.open() must be called
    // synchronously, before the awaited fetch below - calling it AFTER an await is a well-known
    // popup-blocker trigger (Safari enforces this strictly), since the click's "user activation"
    // window can expire during the await. A blank window is claimed immediately, while activation
    // is still fresh, and navigated to the real content once it's ready.
    const previewWindow = mode === 'preview' ? window.open('', '_blank') : null

    try {
      const exportFile = kind === 'resume' ? exportResume : exportCoverLetter
      const { blob, filename } = await exportFile(applicationId, format)
      if (mode === 'preview') {
        if (previewWindow) {
          navigateWindowToBlob(previewWindow, blob)
        } else {
          setError('Your browser blocked the preview. Please allow popups for this site and try again.')
        }
      } else {
        triggerBrowserDownload(blob, filename)
      }
    } catch (err) {
      previewWindow?.close()
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

// Navigates an already-open window to the file, via the browser's own viewer (e.g. its native PDF
// viewer) instead of saving it to disk. Deliberately never revoked: a prior version revoked it on a
// fixed 60-second timer, which raced how long the user actually spends reading the tab before
// trying to save it - a user report (2026-08-24) hit exactly that, saving 5+ minutes after opening
// the preview and getting a dead blob: URL. The browser already releases a blob URL's underlying
// data when the document that created it (this preview tab) is unloaded, so no app-level timer is
// needed - closing the tab is what should end its lifetime, not a guess at how long reading takes.
function navigateWindowToBlob(win: Window, blob: Blob): void {
  const url = URL.createObjectURL(blob)
  win.location.href = url
}
