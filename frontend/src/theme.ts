import { createTheme } from '@mui/material/styles'

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: 'class' },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#4f46e5' },
        background: { default: '#f5f6fa', paper: '#ffffff' },
      },
    },
    dark: {
      palette: {
        primary: { main: '#a5b4fc' },
        background: { default: '#0f1220', paper: '#181c2e' },
      },
    },
  },
  shape: { borderRadius: 10 },
  typography: {
    fontFamily: 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
    button: { textTransform: 'none', fontWeight: 600 },
    h6: { fontWeight: 650 },
  },
  components: {
    MuiButton: { defaultProps: { disableElevation: true } },
    MuiCard: { defaultProps: { variant: 'outlined' } },
    MuiTableCell: {
      styleOverrides: {
        head: ({ theme }) => ({
          fontSize: '0.75rem',
          fontWeight: 600,
          whiteSpace: 'nowrap',
          color: theme.vars.palette.text.secondary,
        }),
      },
    },
  },
})
