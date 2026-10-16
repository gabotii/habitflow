import { Lock, Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { HabitIcon } from '@/components/habit-visuals'
import { Button } from '@/components/ui/button'
import { ConfirmDialog, Dialog, DialogContent } from '@/components/ui/dialog'
import { Input, Select } from '@/components/ui/form-controls'
import { errorMessage } from '@/lib/api-client'
import { HABIT_COLOR, HABIT_COLORS, HABIT_ICON, HABIT_ICONS } from '@/lib/labels'
import type { Category, HabitColor } from '@/types/api'
import { useCategories, useDeleteCategory, useSaveCategory } from './api'

interface Draft {
  id?: string
  name: string
  color: HabitColor
  icon: string
}

const EMPTY: Draft = { name: '', color: 'teal', icon: 'circle-check' }

/** Built-in categories are read-only; custom ones can be edited and deleted while unused. */
export function CategoriesDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const categories = useCategories()
  const save = useSaveCategory()
  const remove = useDeleteCategory()
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [deleting, setDeleting] = useState<Category | null>(null)

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (!draft.name.trim()) return
    save.mutate(
      { ...draft, name: draft.name.trim() },
      {
        onSuccess: () => {
          toast.success(draft.id ? 'Categoría actualizada' : 'Categoría creada')
          setDraft(EMPTY)
        },
        onError: (error) => toast.error(errorMessage(error)),
      },
    )
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title="Categorías"
        description="Organiza tus hábitos. Las predeterminadas no se pueden cambiar."
      >
        <form
          onSubmit={submit}
          className="mb-5 grid gap-2 rounded-lg bg-muted p-3 sm:grid-cols-[1fr_auto_auto_auto]"
        >
          <Input
            aria-label="Nombre de la categoría"
            placeholder={draft.id ? 'Nuevo nombre' : 'Nueva categoría'}
            maxLength={40}
            value={draft.name}
            onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          />
          <Select
            aria-label="Color"
            value={draft.color}
            onChange={(e) => setDraft({ ...draft, color: e.target.value as HabitColor })}
          >
            {HABIT_COLORS.map((c) => (
              <option key={c} value={c}>
                {HABIT_COLOR[c].label}
              </option>
            ))}
          </Select>
          <Select
            aria-label="Icono"
            value={draft.icon}
            onChange={(e) => setDraft({ ...draft, icon: e.target.value })}
          >
            {HABIT_ICONS.map((i) => (
              <option key={i} value={i}>
                {HABIT_ICON[i].label}
              </option>
            ))}
          </Select>
          <div className="flex gap-2">
            <Button type="submit" disabled={save.isPending || !draft.name.trim()}>
              {draft.id ? 'Guardar' : 'Añadir'}
            </Button>
            {draft.id ? (
              <Button variant="ghost" onClick={() => setDraft(EMPTY)}>
                Cancelar
              </Button>
            ) : null}
          </div>
        </form>
        <ul className="divide-y divide-border">
          {categories.data?.map((category) => (
            <li key={category.id} className="flex items-center gap-3 py-2">
              <HabitIcon icon={category.icon} color={category.color} size="sm" />
              <span className="flex-1 text-sm font-medium">{category.name}</span>
              <span className="text-xs text-muted-foreground">
                {category.habitCount} {category.habitCount === 1 ? 'hábito' : 'hábitos'}
              </span>
              {category.isBuiltIn ? (
                <Lock className="size-4 text-muted-foreground" aria-label="Predeterminada" />
              ) : (
                <>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={`Editar ${category.name}`}
                    onClick={() =>
                      setDraft({
                        id: category.id,
                        name: category.name,
                        color: category.color,
                        icon: category.icon,
                      })
                    }
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={`Eliminar ${category.name}`}
                    onClick={() => setDeleting(category)}
                  >
                    <Trash2 />
                  </Button>
                </>
              )}
            </li>
          ))}
        </ul>
        <ConfirmDialog
          open={deleting !== null}
          onOpenChange={(value) => !value && setDeleting(null)}
          title="¿Eliminar la categoría?"
          description={`«${deleting?.name}» se eliminará. Solo es posible si no tiene hábitos, ni siquiera archivados.`}
          confirmLabel="Eliminar"
          pending={remove.isPending}
          onConfirm={() =>
            deleting &&
            remove.mutate(deleting.id, {
              onSuccess: () => {
                toast.success('Categoría eliminada')
                setDeleting(null)
              },
              onError: (error) => {
                toast.error(errorMessage(error))
                setDeleting(null)
              },
            })
          }
        />
      </DialogContent>
    </Dialog>
  )
}
