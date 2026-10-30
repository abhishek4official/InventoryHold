import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

const keys = {
  inventory: ['inventory'] as const,
  activeHolds: ['holds', 'active'] as const,
}

// Holds also expire on the server with no action from this client, so both lists poll gently.
const POLL_INTERVAL_MS = 15_000

export function useInventory() {
  return useQuery({
    queryKey: keys.inventory,
    queryFn: api.getInventory,
    refetchInterval: POLL_INTERVAL_MS,
  })
}

export function useActiveHolds() {
  return useQuery({
    queryKey: keys.activeHolds,
    queryFn: api.getActiveHolds,
    refetchInterval: POLL_INTERVAL_MS,
  })
}

/** Every hold change moves stock, so the two lists are always refreshed together. */
export function useRefreshStockAndHolds() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: keys.inventory }),
      queryClient.invalidateQueries({ queryKey: keys.activeHolds }),
    ])
}

export function useCreateHold() {
  const refresh = useRefreshStockAndHolds()
  return useMutation({ mutationFn: api.createHold, onSettled: refresh })
}

export function useReleaseHold() {
  const refresh = useRefreshStockAndHolds()
  // Refreshing on failure too: a 409 or 410 means this client's view of the hold was out of date.
  return useMutation({ mutationFn: api.releaseHold, onSettled: refresh })
}
