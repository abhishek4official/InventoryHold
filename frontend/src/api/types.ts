// Mirrors InventoryHold.Contracts.

export type HoldStatus = 'Active' | 'Released' | 'Expired'

export interface InventoryItem {
  sku: string
  name: string
  available: number
  held: number
}

export interface HoldItem {
  sku: string
  productName: string
  quantity: number
}

export interface Hold {
  id: string
  customerId: string
  status: HoldStatus
  items: HoldItem[]
  createdAt: string
  expiresAt: string
  closedAt: string | null
}

export interface CreateHoldRequest {
  customerId: string
  items: { sku: string; quantity: number }[]
}

/** RFC 7807 body returned by every API error. */
export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}
