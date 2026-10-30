// Mirrors InventoryHold.Contracts.

export type HoldStatus = 'Active' | 'Released' | 'Expired'

export interface InventoryItem {
  sku: string
  name: string
  partNumber: string
  manufacturer: string
  category: string
  warehouseLocation: string
  unitOfMeasure: string
  available: number
  held: number
  reorderPoint: number
  lowStock: boolean
  isSerialized: boolean
  /** ISO date (yyyy-MM-dd); null when nothing is available to ship. */
  approxShipDate: string | null
}

export interface HoldItem {
  sku: string
  productName: string
  partNumber: string
  quantity: number
  /** ISO date (yyyy-MM-dd). */
  approxShipDate: string | null
  /** The exact units held; empty for products not tracked by serial. */
  serialNumbers: string[]
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
