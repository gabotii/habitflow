import { Check } from 'lucide-react'
import { cn } from '@/lib/utils'

export function Logo({ inverted = false, className }: { inverted?: boolean; className?: string }) {
  return (
    <span
      className={cn(
        'flex items-center gap-2 text-lg font-extrabold tracking-tight',
        inverted && 'text-white',
        className,
      )}
    >
      <span className="grid size-8 place-items-center rounded-lg bg-primary text-primary-foreground shadow-sm">
        <Check className="size-5" strokeWidth={3} aria-hidden />
      </span>
      HabitFlow
    </span>
  )
}
