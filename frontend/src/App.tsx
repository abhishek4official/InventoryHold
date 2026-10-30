import { useState } from 'react'
import DarkModeIcon from '@mui/icons-material/DarkMode'
import Inventory2Icon from '@mui/icons-material/Inventory2'
import LightModeIcon from '@mui/icons-material/LightMode'
import { AppBar, Box, Container, IconButton, Toolbar, Tooltip, Typography } from '@mui/material'
import { useColorScheme } from '@mui/material/styles'
import { ActiveHoldsList } from './components/ActiveHoldsList'
import { CreateHoldForm } from './components/CreateHoldForm'
import { InventoryDashboard } from './components/InventoryDashboard'
import { ItemDetailsDrawer } from './components/ItemDetailsDrawer'

function ThemeToggle() {
  const { mode, systemMode, setMode } = useColorScheme()
  const isDark = (mode === 'system' ? systemMode : mode) === 'dark'
  const label = isDark ? 'Switch to light theme' : 'Switch to dark theme'

  return (
    <Tooltip title={label}>
      <IconButton color="inherit" aria-label={label} onClick={() => setMode(isDark ? 'light' : 'dark')}>
        {isDark ? <LightModeIcon /> : <DarkModeIcon />}
      </IconButton>
    </Tooltip>
  )
}

export default function App() {
  const [selectedSku, setSelectedSku] = useState<string | null>(null)

  return (
    <>
      <AppBar position="sticky" elevation={0} color="default" sx={{ bgcolor: 'background.paper', borderBottom: 1, borderColor: 'divider' }}>
        <Toolbar>
          <Inventory2Icon color="primary" sx={{ mr: 1.5 }} />
          <Typography variant="h6" component="h1" sx={{ flexGrow: 1 }}>
            Inventory Holds
          </Typography>
          <ThemeToggle />
        </Toolbar>
      </AppBar>

      <Container maxWidth="xl" component="main" sx={{ py: 3, display: 'grid', gap: 2 }}>
        <InventoryDashboard onSelect={setSelectedSku} />
        <Box sx={{ display: 'grid', gap: 2, alignItems: 'start', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))' } }}>
          <CreateHoldForm />
          <ActiveHoldsList />
        </Box>
      </Container>

      <ItemDetailsDrawer sku={selectedSku} onClose={() => setSelectedSku(null)} />
    </>
  )
}
