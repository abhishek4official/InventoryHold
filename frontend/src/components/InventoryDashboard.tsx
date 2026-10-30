import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import {
  Alert,
  Box,
  Card,
  CardContent,
  Chip,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useInventory } from '../api/hooks'
import { formatDate, formatDateTime } from '../format'

export function InventoryDashboard({ onSelect }: { onSelect: (sku: string) => void }) {
  const { data: inventory, error, isPending, isFetching } = useInventory()

  return (
    <Card>
      <Box sx={{ height: 4 }}>{isFetching && <LinearProgress />}</Box>
      <CardContent>
        <Typography variant="h6" component="h2">
          Inventory
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Select a product to see each of its items.
        </Typography>

        {error && <Alert severity="error">{error.message}</Alert>}
        {isPending && !error && <Typography color="text.secondary">Loading inventory…</Typography>}

        {inventory && (
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Product</TableCell>
                  <TableCell>Part number</TableCell>
                  <TableCell>Category</TableCell>
                  <TableCell>Location</TableCell>
                  <TableCell>Tracking</TableCell>
                  <TableCell align="right">Available</TableCell>
                  <TableCell align="right">On hold</TableCell>
                  <TableCell>Approx. ship date</TableCell>
                  <TableCell>Last updated</TableCell>
                  <TableCell padding="none" />
                </TableRow>
              </TableHead>
              <TableBody>
                {inventory.map((item) => (
                  <TableRow
                    key={item.sku}
                    hover
                    tabIndex={0}
                    role="button"
                    aria-label={`View items for ${item.name}`}
                    sx={{ cursor: 'pointer' }}
                    onClick={() => onSelect(item.sku)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        onSelect(item.sku)
                      }
                    }}
                  >
                    <TableCell>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>{item.name}</Typography>
                      <Typography variant="caption" color="text.secondary">{item.sku}</Typography>
                    </TableCell>
                    <TableCell>
                      <Typography variant="body2">{item.partNumber}</Typography>
                      <Typography variant="caption" color="text.secondary">{item.manufacturer}</Typography>
                    </TableCell>
                    <TableCell>{item.category}</TableCell>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{item.warehouseLocation}</TableCell>
                    <TableCell>
                      <Chip size="small" variant="outlined" label={item.isSerialized ? 'Serial number' : 'Quantity'} />
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end', alignItems: 'center' }}>
                        {item.lowStock && <Chip size="small" color="warning" label={item.available === 0 ? 'Out of stock' : 'Low stock'} />}
                        <Typography variant="body2" sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                          {item.available} {item.unitOfMeasure}
                        </Typography>
                      </Stack>
                    </TableCell>
                    <TableCell align="right">{item.held}</TableCell>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDate(item.approxShipDate)}</TableCell>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(item.lastUpdatedAt)}</TableCell>
                    <TableCell padding="none"><ChevronRightIcon color="action" /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </CardContent>
    </Card>
  )
}
