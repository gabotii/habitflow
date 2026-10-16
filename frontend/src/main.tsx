import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { AppProviders, createQueryClient } from './app/providers'
import { router } from './app/router'
import './index.css'

const queryClient = createQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders client={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
)
