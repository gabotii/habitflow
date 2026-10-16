import { X } from 'lucide-react'
import { AlertDialog as AlertPrimitive, Dialog as DialogPrimitive } from 'radix-ui'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { Button } from './button'

const overlayClass = 'fixed inset-0 z-50 bg-slate-950/40 backdrop-blur-[2px] dark:bg-black/60'
const contentClass =
  'fixed top-1/2 left-1/2 z-50 flex max-h-[calc(100dvh-2rem)] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border border-border bg-surface shadow-xl focus:outline-none'

export const Dialog = DialogPrimitive.Root
export const DialogTrigger = DialogPrimitive.Trigger
export const DialogClose = DialogPrimitive.Close

interface DialogContentProps extends ComponentProps<typeof DialogPrimitive.Content> {
  title: string
  description?: ReactNode
  size?: 'md' | 'lg' | 'xl'
  footer?: ReactNode
}

/** Modal with a title, scrollable body and optional sticky footer. Focus is trapped and Escape closes it. */
export function DialogContent({
  title,
  description,
  size = 'md',
  footer,
  className,
  children,
  ...props
}: DialogContentProps) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className={overlayClass} />
      <DialogPrimitive.Content
        className={cn(contentClass, { md: 'max-w-lg', lg: 'max-w-2xl', xl: 'max-w-4xl' }[size], className)}
        {...props}
      >
        <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
          <div>
            <DialogPrimitive.Title className="text-lg font-semibold">{title}</DialogPrimitive.Title>
            {description ? (
              <DialogPrimitive.Description className="mt-1 text-sm text-muted-foreground">
                {description}
              </DialogPrimitive.Description>
            ) : (
              <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
            )}
          </div>
          <DialogPrimitive.Close asChild>
            <Button variant="ghost" size="icon" aria-label="Cerrar">
              <X />
            </Button>
          </DialogPrimitive.Close>
        </div>
        <div className="overflow-y-auto px-5 py-4">{children}</div>
        {footer ? (
          <div className="flex justify-end gap-2 border-t border-border px-5 py-3">{footer}</div>
        ) : null}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  )
}

interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: ReactNode
  confirmLabel?: string
  destructive?: boolean
  pending?: boolean
  onConfirm: () => void
}

/** Confirmation for destructive actions. The cancel button gets initial focus. */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = 'Confirmar',
  destructive = true,
  pending = false,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <AlertPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <AlertPrimitive.Portal>
        <AlertPrimitive.Overlay className={overlayClass} />
        <AlertPrimitive.Content className={cn(contentClass, 'max-w-md p-5')}>
          <AlertPrimitive.Title className="text-lg font-semibold">{title}</AlertPrimitive.Title>
          <AlertPrimitive.Description className="mt-2 text-sm text-muted-foreground">
            {description}
          </AlertPrimitive.Description>
          <div className="mt-5 flex justify-end gap-2">
            <AlertPrimitive.Cancel asChild>
              <Button variant="outline">Cancelar</Button>
            </AlertPrimitive.Cancel>
            <Button
              variant={destructive ? 'danger' : 'primary'}
              disabled={pending}
              onClick={(event) => {
                event.preventDefault()
                onConfirm()
              }}
            >
              {pending ? 'Procesando…' : confirmLabel}
            </Button>
          </div>
        </AlertPrimitive.Content>
      </AlertPrimitive.Portal>
    </AlertPrimitive.Root>
  )
}
