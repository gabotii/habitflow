import { Bell, CheckCheck } from 'lucide-react'
import { Popover } from 'radix-ui'
import { useState } from 'react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { formatRelative } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { useMarkNotifications, useNotifications, useUnreadCount } from './api'

/** Bell with the unread count; notifications come from persisted reminder firings. */
export function NotificationsMenu() {
  const [open, setOpen] = useState(false)
  const unread = useUnreadCount()
  const list = useNotifications(open)
  const mark = useMarkNotifications()
  const count = unread.data?.count ?? 0

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative"
          aria-label={count > 0 ? `Notificaciones: ${count} sin leer` : 'Notificaciones'}
        >
          <Bell />
          {count > 0 ? (
            <span className="absolute -top-0.5 -right-0.5 grid min-w-4.5 place-items-center rounded-full bg-danger px-1 text-[10px] font-bold text-danger-foreground">
              {count > 99 ? '99+' : count}
            </span>
          ) : null}
        </Button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={8}
          className="z-50 w-[min(22rem,calc(100vw-2rem))] rounded-xl border border-border bg-surface shadow-xl"
        >
          <div className="flex items-center justify-between border-b border-border px-4 py-3">
            <p className="font-semibold">Notificaciones</p>
            {count > 0 ? (
              <Button variant="ghost" size="sm" onClick={() => mark.mutate('all')} disabled={mark.isPending}>
                <CheckCheck /> Marcar todo leído
              </Button>
            ) : null}
          </div>
          <div className="max-h-96 overflow-y-auto">
            {list.isPending ? (
              <p className="p-4 text-sm text-muted-foreground">Cargando…</p>
            ) : list.isError ? (
              <p className="p-4 text-sm text-danger">No se pudieron cargar las notificaciones.</p>
            ) : list.data.items.length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">
                No tienes notificaciones. Aparecerán aquí cuando salten tus recordatorios.
              </p>
            ) : (
              <ul className="divide-y divide-border">
                {list.data.items.map((n) => {
                  const content = (
                    <>
                      <p className={cn('text-sm', !n.readAt && 'font-semibold')}>
                        {!n.readAt ? (
                          <span
                            className="mr-1.5 inline-block size-2 rounded-full bg-primary"
                            aria-label="Sin leer"
                          />
                        ) : null}
                        {n.title}
                      </p>
                      {n.body ? <p className="text-xs text-muted-foreground">{n.body}</p> : null}
                      <p className="mt-0.5 text-xs text-muted-foreground">{formatRelative(n.createdAt)}</p>
                    </>
                  )
                  const markRead = () => !n.readAt && mark.mutate(n.id)
                  return (
                    <li key={n.id}>
                      {n.link ? (
                        <Link
                          to={n.link}
                          className="block px-4 py-3 hover:bg-muted"
                          onClick={() => {
                            markRead()
                            setOpen(false)
                          }}
                        >
                          {content}
                        </Link>
                      ) : (
                        <button
                          type="button"
                          className="block w-full px-4 py-3 text-left hover:bg-muted"
                          onClick={markRead}
                        >
                          {content}
                        </button>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
