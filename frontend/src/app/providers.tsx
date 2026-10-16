import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Toaster } from 'sonner'
import { ApiError } from '@/lib/api-client'
import { AuthProvider } from '@/features/auth/auth-context'

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: true,
        // Client errors (404, 401, validation) will not fix themselves; retry only transient failures.
        retry: (failureCount, error) =>
          failureCount < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
      },
    },
  })
}

export function AppProviders({ client, children }: { client: QueryClient; children: ReactNode }) {
  return (
    <QueryClientProvider client={client}>
      <AuthProvider>{children}</AuthProvider>
      <Toaster position="top-right" richColors closeButton toastOptions={{ duration: 4000 }} />
    </QueryClientProvider>
  )
}
