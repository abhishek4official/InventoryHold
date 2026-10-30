import { useEffect, useRef, useState } from 'react'
import TimerOutlinedIcon from '@mui/icons-material/TimerOutlined'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  LinearProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material'
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
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2 }}>
          <Typography variant="h6" component="h2">Active holds</Typography>
          {holds && <Chip size="small" label={holds.length} />}
        </Stack>

        {error && <Alert severity="error">{error.message}</Alert>}
        {isPending && !error && <Typography color="text.secondary">Loading holds…</Typography>}
        {holds?.length === 0 && <Typography color="text.secondary">No active holds.</Typography>}

        <Stack spacing={1.5}>
          {holds?.map((hold) => (
            <HoldRow key={hold.id} hold={hold} now={now} />
          ))}
        </Stack>
      </CardContent>
    </Card>
  )
}

function HoldRow({ hold, now }: { hold: Hold; now: number }) {
  const release = useReleaseHold()
  const [confirming, setConfirming] = useState(false)

  const expiresAt = new Date(hold.expiresAt).getTime()
  const total = expiresAt - new Date(hold.createdAt).getTime()
  const remaining = expiresAt - now
  const isUrgent = remaining < 60_000

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'flex-start', mb: 1 }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle2" component="span" sx={{ mr: 1 }}>{hold.customerId}</Typography>
          <Chip size="small" color="primary" variant="outlined" label={hold.status} />
          <Typography variant="caption" color="text.secondary" component="div" sx={{ fontFamily: 'monospace', overflowWrap: 'anywhere' }}>
            {hold.id}
          </Typography>
        </Box>
        <Chip
          icon={<TimerOutlinedIcon />}
          color={isUrgent ? 'error' : 'default'}
          label={formatRemaining(remaining)}
          aria-label="Time remaining"
          sx={{ fontVariantNumeric: 'tabular-nums', fontWeight: 600, ml: 1 }}
        />
      </Stack>

      <LinearProgress
        variant="determinate"
        color={isUrgent ? 'error' : 'primary'}
        value={Math.max(0, Math.min(100, (remaining / total) * 100))}
        sx={{ mb: 1.5, borderRadius: 1 }}
      />

      <Stack spacing={1} sx={{ mb: 1.5 }}>
        {hold.items.map((item) => (
          <Box key={item.sku}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {item.quantity} × {item.productName}
            </Typography>
            <Typography variant="caption" color="text.secondary" component="div">
              Part {item.partNumber} · Ships about {formatDate(item.approxShipDate)}
            </Typography>
            {item.serialNumbers.length > 0 && (
              <Stack direction="row" useFlexGap spacing={0.5} sx={{ flexWrap: 'wrap', mt: 0.5 }}>
                {item.serialNumbers.map((serial) => (
                  <Chip key={serial} size="small" variant="outlined" label={serial} sx={{ fontFamily: 'monospace' }} />
                ))}
              </Stack>
            )}
          </Box>
        ))}
      </Stack>

      <Button size="small" variant="outlined" color="error" onClick={() => setConfirming(true)}>
        Release hold
      </Button>

      <Dialog open={confirming} onClose={() => !release.isPending && setConfirming(false)}>
        <DialogTitle>Release this hold?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            The stock held for {hold.customerId} goes back to available inventory. This can't be undone.
          </DialogContentText>
          {release.error && <Alert severity="error" sx={{ mt: 2 }}>{release.error.message}</Alert>}
        </DialogContent>
        <DialogActions>
          <Button disabled={release.isPending} onClick={() => setConfirming(false)}>Keep hold</Button>
          <Button color="error" variant="contained" disabled={release.isPending} onClick={() => release.mutate(hold.id)}>
            {release.isPending ? 'Releasing…' : 'Yes, release'}
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  )
}
