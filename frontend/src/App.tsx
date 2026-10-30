import { ActiveHoldsList } from './components/ActiveHoldsList'
import { CreateHoldForm } from './components/CreateHoldForm'
import { InventoryDashboard } from './components/InventoryDashboard'

export default function App() {
  return (
    <main className="app">
      <h1>Inventory holds</h1>
      <div className="layout">
        <div className="column">
          <InventoryDashboard />
        </div>
        <div className="column">
          <CreateHoldForm />
          <ActiveHoldsList />
        </div>
      </div>
    </main>
  )
}
