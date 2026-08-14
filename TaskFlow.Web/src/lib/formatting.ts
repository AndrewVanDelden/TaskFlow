// Date/time formatting helpers. Centralized so components do not each build their own
// `new Date(x).toLocale...()` calls.

export const formatDate = (iso: string) => new Date(iso).toLocaleDateString()
export const formatTime = (iso: string) => new Date(iso).toLocaleTimeString()

// `now` defaults to the real clock but can be pinned by callers (tests) for determinism.
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const diffSeconds = Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 1000))

  if (diffSeconds < 60) return 'just now'
  const diffMinutes = Math.floor(diffSeconds / 60)
  if (diffMinutes < 60) return `${diffMinutes}m ago`
  const diffHours = Math.floor(diffMinutes / 60)
  if (diffHours < 24) return `${diffHours}h ago`
  const diffDays = Math.floor(diffHours / 24)
  return `${diffDays}d ago`
}
