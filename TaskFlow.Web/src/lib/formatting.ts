// Date/time formatting helpers. Centralized so components do not each build their own
// `new Date(x).toLocale...()` calls.

export const formatDate = (iso: string) => new Date(iso).toLocaleDateString()
export const formatTime = (iso: string) => new Date(iso).toLocaleTimeString()
