/** Formats an ISO date (yyyy-MM-dd) in UTC, so the day never shifts with the viewer's time zone. */
export function formatDate(isoDate: string | null) {
  if (!isoDate) return '—'
  return new Date(`${isoDate.slice(0, 10)}T00:00:00Z`).toLocaleDateString(undefined, {
    timeZone: 'UTC',
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

/** Formats a timestamp in the viewer's own time zone. */
export function formatDateTime(isoDateTime: string | null) {
  if (!isoDateTime) return '—'
  return new Date(isoDateTime).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
