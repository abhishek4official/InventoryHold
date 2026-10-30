import type { CreateHoldRequest, Hold, InventoryItem, ProblemDetails } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(describe(status, problem))
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

function describe(status: number, problem: ProblemDetails | null): string {
  const fieldErrors = Object.values(problem?.errors ?? {}).flat()
  if (fieldErrors.length > 0) return fieldErrors.join(' ')
  return problem?.detail ?? problem?.title ?? `The request failed (HTTP ${status}).`
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, init)
  } catch {
    throw new Error('Could not reach the server. Check your connection and try again.')
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as ProblemDetails | null
    throw new ApiError(response.status, problem)
  }
  return (await response.json()) as T
}

export const api = {
  getInventory: () => request<InventoryItem[]>('/api/inventory'),

  getActiveHolds: () => request<Hold[]>('/api/holds?status=Active'),

  createHold: (body: CreateHoldRequest) =>
    request<Hold>('/api/holds', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  releaseHold: (holdId: string) =>
    request<Hold>(`/api/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE' }),
}
