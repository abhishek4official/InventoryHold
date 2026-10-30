import { useEffect, useRef, useState } from 'react'
import { useActiveHolds, useRefreshStockAndHolds, useReleaseHold } from '../api/hooks'
import type { Hold } from '../api/types'
import { formatDate } from '../format'

function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}

function formatRemaining(ms: number) {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000))
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = String(totalSeconds % 60).padStart(2, '0')
  return `${minutes}:${seconds}`
}

export function ActiveHoldsList() {
  const { data: holds, error, isPending } = useActiveHolds()
  const refresh = useRefreshStockAndHolds()
  const now = useNow(1000)

  // Listing a due hold makes the server expire it, so one refresh per hold at zero updates both lists.
  const refreshedForExpiry = useRef(new Set<string>())
  useEffect(() => {
    const newlyDue = holds?.filter(
      (hold) => new Date(hold.expiresAt).getTime() <= now && !refreshedForExpiry.current.has(hold.id),
    )
    if (newlyDue?.length) {
      newlyDue.forEach((hold) => refreshedForExpiry.current.add(hold.id))
      void refresh()
    }
  }, [holds, now, refresh])

  return (
    <section className="panel">
      <header className="panel-header">
        <h2>Active holds</h2>
        {holds && <span className="muted">{holds.length}</span>}
      </header>

      {error && <p className="error" role="alert">{error.message}</p>}
      {isPending && !error && <p className="muted">Loading holds…</p>}
      {holds?.length === 0 && <p className="muted">No active holds.</p>}

      <ul className="holds">
        {holds?.map((hold) => (
          <HoldRow key={hold.id} hold={hold} remainingMs={new Date(hold.expiresAt).getTime() - now} />
        ))}
      </ul>
    </section>
  )
}

function HoldRow({ hold, remainingMs }: { hold: Hold; remainingMs: number }) {
  const release = useReleaseHold()
  const [confirming, setConfirming] = useState(false)

  return (
    <li className="hold">
      <div className="hold-summary">
        <div>
          <strong>{hold.customerId}</strong> <span className="badge">{hold.status}</span>
          <div className="muted small">{hold.id}</div>
        </div>
        <div className={remainingMs < 60_000 ? 'countdown urgent' : 'countdown'} aria-label="Time remaining">
          {formatRemaining(remainingMs)}
        </div>
      </div>

      <ul className="hold-items">
        {hold.items.map((item) => (
          <li key={item.sku}>
            {item.quantity} × {item.productName}
            <div className="muted small">
              Part {item.partNumber} · Ships about {formatDate(item.approxShipDate)}
            </div>
            {item.serialNumbers.length > 0 && (
              <div className="muted small">Serials: {item.serialNumbers.join(', ')}</div>
            )}
          </li>
        ))}
      </ul>

      {release.error && <p className="error" role="alert">{release.error.message}</p>}

      {confirming ? (
        <div className="actions">
          <span>Release this hold and return its stock?</span>
          <button type="button" className="danger" disabled={release.isPending} onClick={() => release.mutate(hold.id)}>
            {release.isPending ? 'Releasing…' : 'Yes, release'}
          </button>
          <button type="button" className="secondary" disabled={release.isPending} onClick={() => setConfirming(false)}>
            Keep hold
          </button>
        </div>
      ) : (
        <div className="actions">
          <button type="button" className="secondary" onClick={() => setConfirming(true)}>
            Release hold
          </button>
        </div>
      )}
    </li>
  )
}
