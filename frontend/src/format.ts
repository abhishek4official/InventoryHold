/** Formats an ISO date (yyyy-MM-dd) in UTC, so the day never shifts with the viewer's time zone. */
export function formatDate(isoDate: string | null) {
  if (!isoDate) return '—'
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString(undefined, {
    timeZone: 'UTC',
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}
