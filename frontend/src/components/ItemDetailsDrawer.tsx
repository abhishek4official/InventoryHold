import { useState } from 'react'
import CloseIcon from '@mui/icons-material/Close'
import {
  Alert,
  Box,
  Chip,
  Divider,
  Drawer,
  IconButton,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useInventory } from '../api/hooks'
import type { InventoryItem } from '../api/types'
import { formatDate, formatDateTime } from '../format'

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="div">{label}</Typography>
      <Typography variant="body2">{value}</Typography>
    </Box>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Typography variant="h5" component="div" sx={{ fontWeight: 650 }}>{value}</Typography>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
    </Paper>
  )
}

function UnitsTable({ item }: { item: InventoryItem }) {
  const [search, setSearch] = useState('')
  const term = search.trim().toLowerCase()
  const units = item.units.filter((unit) =>
    [unit.serialNumber, unit.lotNumber, unit.holdId ?? ''].some((value) => value.toLowerCase().includes(term)),
  )

  return (
    <>
      <TextField
        size="small"
        fullWidth
        label="Search serial, lot or hold ID"
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        sx={{ mb: 1.5 }}
      />
      <TableContainer>
        <Table size="small" stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell>#</TableCell>
              <TableCell>Serial number</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Lot</TableCell>
              <TableCell>Condition</TableCell>
              <TableCell>Received</TableCell>
              <TableCell>Last updated</TableCell>
              <TableCell>Hold ID</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {units.map((unit, index) => (
              <TableRow key={unit.serialNumber} hover>
                <TableCell>{index + 1}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>{unit.serialNumber}</TableCell>
                <TableCell>
                  <Chip
                    size="small"
                    color={unit.status === 'Available' ? 'success' : 'warning'}
                    label={unit.status === 'Available' ? 'Available' : 'On hold'}
                  />
                </TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{unit.lotNumber}</TableCell>
                <TableCell>{unit.condition}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDate(unit.receivedAt)}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(unit.lastUpdatedAt)}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
                  {unit.holdId ? unit.holdId.slice(0, 8) : '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {units.length === 0 && (
        <Typography color="text.secondary" sx={{ mt: 2 }}>No items match your search.</Typography>
      )}
    </>
  )
}

export function ItemDetailsDrawer({ sku, onClose }: { sku: string | null; onClose: () => void }) {
  // Read from the shared query so the panel stays current while holds change or expire.
  const { data: inventory } = useInventory()
  const item = inventory?.find((candidate) => candidate.sku === sku)

  return (
    <Drawer
      anchor="right"
      open={sku !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', md: 1020 }, maxWidth: '100%' } } }}
    >
      {item && (
        <Box sx={{ p: 3 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 2 }}>
            <Box>
              <Typography variant="h6" component="h2">{item.name}</Typography>
              <Typography variant="body2" color="text.secondary">{item.sku}</Typography>
            </Box>
            <IconButton aria-label="Close" onClick={onClose}><CloseIcon /></IconButton>
          </Box>

          <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', mb: 3 }}>
            <Stat label="Total" value={item.available + item.held} />
            <Stat label="Available" value={item.available} />
            <Stat label="On hold" value={item.held} />
          </Box>

          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(4, minmax(0, 1fr))' }, mb: 3 }}>
            <Detail label="Part number" value={item.partNumber} />
            <Detail label="Manufacturer" value={item.manufacturer} />
            <Detail label="Category" value={item.category} />
            <Detail label="Location" value={item.warehouseLocation} />
            <Detail label="Unit of measure" value={item.unitOfMeasure} />
            <Detail label="Reorder point" value={String(item.reorderPoint)} />
            <Detail label="Approx. ship date" value={formatDate(item.approxShipDate)} />
            <Detail label="Last updated" value={formatDateTime(item.lastUpdatedAt)} />
          </Box>

          <Divider sx={{ mb: 2 }} />
          <Typography variant="subtitle1" component="h3" sx={{ fontWeight: 650, mb: 1.5 }}>
            Items ({item.available + item.held})
          </Typography>

          {item.isSerialized ? (
            <UnitsTable key={item.sku} item={item} />
          ) : (
            <Alert severity="info">
              This product is tracked by quantity, so its items have no individual serial numbers.
            </Alert>
          )}
        </Box>
      )}
    </Drawer>
  )
}
