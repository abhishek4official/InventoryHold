import { ActiveHoldsList } from './components/ActiveHoldsList'
import { CreateHoldForm } from './components/CreateHoldForm'
import { InventoryDashboard } from './components/InventoryDashboard'

export default function App() {
  return (
    <main className="app">
      <h1>Inventory holds</h1>
      <InventoryDashboard />
      <div className="layout">
        <CreateHoldForm />
        <ActiveHoldsList />
      </div>
    </main>
  )
}
