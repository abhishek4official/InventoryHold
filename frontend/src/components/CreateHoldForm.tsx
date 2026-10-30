import { useState, type FormEvent } from 'react'
import AddIcon from '@mui/icons-material/Add'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import { Alert, Box, Button, Card, CardContent, IconButton, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
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
  const isComplete = customerId.trim() !== '' && lines.every((line) => line.sku !== '' && line.quantity !== '')

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
    <Card>
      <CardContent>
        <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
          Create hold
        </Typography>

        <Box component="form" onSubmit={handleSubmit}>
          <Stack spacing={2}>
            <TextField
              size="small"
              label="Customer ID"
              value={customerId}
              onChange={(event) => setCustomerId(event.target.value)}
              required
            />

            {lines.map((line) => (
              <Stack direction="row" spacing={1} key={line.key} sx={{ alignItems: 'center' }}>
                <TextField
                  select
                  size="small"
                  label="Product"
                  value={line.sku}
                  onChange={(event) => updateLine(line.key, { sku: event.target.value })}
                  sx={{ flexGrow: 1, minWidth: 0 }}
                >
                  {inventory.map((item) => (
                    <MenuItem
                      key={item.sku}
                      value={item.sku}
                      disabled={item.sku !== line.sku && chosenSkus.has(item.sku)}
                    >
                      {item.name} ({item.available} available)
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  size="small"
                  type="number"
                  label="Quantity"
                  value={line.quantity}
                  onChange={(event) => updateLine(line.key, { quantity: event.target.value })}
                  slotProps={{ htmlInput: { min: 1 } }}
                  sx={{ width: 100, flexShrink: 0 }}
                  required
                />
                <Tooltip title="Remove this product">
                  <span>
                    <IconButton
                      aria-label="Remove this product"
                      disabled={lines.length === 1}
                      onClick={() => setLines((current) => current.filter((other) => other.key !== line.key))}
                    >
                      <DeleteOutlineIcon />
                    </IconButton>
                  </span>
                </Tooltip>
              </Stack>
            ))}

            {createHold.error && <Alert severity="error">{createHold.error.message}</Alert>}
            {createHold.isSuccess && <Alert severity="success">Hold placed.</Alert>}

            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                startIcon={<AddIcon />}
                disabled={!canAddLine}
                onClick={() => setLines((current) => [...current, emptyLine()])}
              >
                Add another product
              </Button>
              <Button type="submit" variant="contained" disabled={!isComplete || createHold.isPending}>
                {createHold.isPending ? 'Placing hold…' : 'Place hold'}
              </Button>
            </Stack>
          </Stack>
        </Box>
      </CardContent>
    </Card>
  )
}
