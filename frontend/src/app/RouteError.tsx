import { AlertTriangle } from 'lucide-react'
import { Link, useRouteError } from 'react-router'
import { EmptyState } from '@/components/page'
import { Button } from '@/components/ui/button'

/** Shown when a page throws while rendering or a code-split chunk fails to load (e.g. after a deploy). */
export function RouteError() {
  const error = useRouteError()
  const chunkFailed =
    error instanceof TypeError && /dynamically imported module|Importing a module/i.test(error.message)

  return (
    <div className="p-6">
      <EmptyState
        icon={AlertTriangle}
        title={chunkFailed ? 'Hay una nueva versión disponible' : 'Algo salió mal'}
        description={
          chunkFailed
            ? 'Recarga la página para continuar.'
            : 'Se produjo un error inesperado al mostrar esta pantalla. Puedes volver al panel e intentarlo de nuevo.'
        }
        action={
          chunkFailed ? (
            <Button onClick={() => window.location.reload()}>Recargar</Button>
          ) : (
            <Button asChild>
              <Link to="/">Volver al panel</Link>
            </Button>
          )
        }
      />
    </div>
  )
}
