import { useInventory } from '../api/hooks'
import { formatDate } from '../format'

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
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Product</th>
                <th>Part number</th>
                <th>Category</th>
                <th>Location</th>
                <th>Tracking</th>
                <th className="number">Available</th>
                <th className="number">On hold</th>
                <th>Approx. ship date</th>
              </tr>
            </thead>
            <tbody>
              {inventory.map((item) => (
                <tr key={item.sku}>
                  <td>
                    {item.name}
                    <div className="muted small">{item.sku}</div>
                  </td>
                  <td>
                    {item.partNumber}
                    <div className="muted small">{item.manufacturer}</div>
                  </td>
                  <td>{item.category}</td>
                  <td>{item.warehouseLocation}</td>
                  <td>{item.isSerialized ? 'Serial number' : 'Quantity'}</td>
                  <td className={item.available === 0 ? 'number out-of-stock' : 'number'}>
                    {item.available} {item.unitOfMeasure}
                    {item.lowStock && <div className="low-stock small">Low stock</div>}
                  </td>
                  <td className="number">{item.held}</td>
                  <td>{formatDate(item.approxShipDate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
