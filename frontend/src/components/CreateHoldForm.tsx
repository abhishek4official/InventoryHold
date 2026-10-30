import { useState, type FormEvent } from 'react'
import { useCreateHold, useInventory } from '../api/hooks'

interface Line {
  key: number
  sku: string
  quantity: string
}

let nextKey = 1
const emptyLine = (): Line => ({ key: nextKey++, sku: '', quantity: '1' })

export function CreateHoldForm() {
  const { data: inventory = [] } = useInventory()
  const createHold = useCreateHold()
  const [customerId, setCustomerId] = useState('customer-1')
  const [lines, setLines] = useState<Line[]>(() => [emptyLine()])

  const updateLine = (key: number, change: Partial<Line>) =>
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...change } : line)))

  const chosenSkus = new Set(lines.map((line) => line.sku))
  const canAddLine = lines.length < inventory.length

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    createHold.mutate(
      {
        customerId: customerId.trim(),
        items: lines.map((line) => ({ sku: line.sku, quantity: Number(line.quantity) })),
      },
      { onSuccess: () => setLines([emptyLine()]) },
    )
  }

  return (
    <section className="panel">
      <header className="panel-header">
        <h2>Create hold</h2>
      </header>

      <form onSubmit={handleSubmit}>
        <label className="field">
          Customer ID
          <input value={customerId} onChange={(e) => setCustomerId(e.target.value)} required />
        </label>

        {lines.map((line) => (
          <div className="line" key={line.key}>
            <label className="field grow">
              Product
              <select value={line.sku} onChange={(e) => updateLine(line.key, { sku: e.target.value })} required>
                <option value="">Select a product</option>
                {inventory.map((item) => (
                  <option
                    key={item.sku}
                    value={item.sku}
                    disabled={item.sku !== line.sku && chosenSkus.has(item.sku)}
                  >
                    {item.name} ({item.available} available)
                  </option>
                ))}
              </select>
            </label>
            <label className="field quantity">
              Quantity
              <input
                type="number"
                min={1}
                value={line.quantity}
                onChange={(e) => updateLine(line.key, { quantity: e.target.value })}
                required
              />
            </label>
            <button
              type="button"
              className="secondary"
              disabled={lines.length === 1}
              aria-label="Remove this product"
              onClick={() => setLines((current) => current.filter((other) => other.key !== line.key))}
            >
              Remove
            </button>
          </div>
        ))}

        {createHold.error && <p className="error" role="alert">{createHold.error.message}</p>}
        {createHold.isSuccess && <p className="success" role="status">Hold placed.</p>}

        <div className="actions">
          <button
            type="button"
            className="secondary"
            disabled={!canAddLine}
            onClick={() => setLines((current) => [...current, emptyLine()])}
          >
            Add another product
          </button>
          <button type="submit" disabled={createHold.isPending}>
            {createHold.isPending ? 'Placing hold…' : 'Place hold'}
          </button>
        </div>
      </form>
    </section>
  )
}
