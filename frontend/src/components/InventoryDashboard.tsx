import { useInventory } from '../api/hooks'

export function InventoryDashboard() {
  const { data: inventory, error, isPending, isFetching } = useInventory()

  return (
    <section className="panel">
      <header className="panel-header">
        <h2>Inventory</h2>
        {isFetching && !isPending && <span className="muted">Updating…</span>}
      </header>

      {error && <p className="error" role="alert">{error.message}</p>}
      {isPending && !error && <p className="muted">Loading inventory…</p>}

      {inventory && (
        <table>
          <thead>
            <tr>
              <th>Product</th>
              <th className="number">Available</th>
              <th className="number">On hold</th>
            </tr>
          </thead>
          <tbody>
            {inventory.map((item) => (
              <tr key={item.sku}>
                <td>
                  {item.name}
                  <div className="muted small">{item.sku}</div>
                </td>
                <td className={item.available === 0 ? 'number out-of-stock' : 'number'}>{item.available}</td>
                <td className="number">{item.held}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
