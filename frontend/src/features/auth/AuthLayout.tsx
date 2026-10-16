import { CalendarCheck2, Flame, Target } from 'lucide-react'
import type { ReactNode } from 'react'
import { Logo } from '@/components/logo'

const highlights = [
  { icon: Flame, text: 'Rachas que respetan tus días programados' },
  { icon: CalendarCheck2, text: 'Un calendario con tu historial real, día a día' },
  { icon: Target, text: 'Objetivos con progreso medido, no estimado' },
]

export function AuthLayout({
  title,
  subtitle,
  children,
}: {
  title: string
  subtitle: string
  children: ReactNode
}) {
  return (
    <div className="grid min-h-dvh lg:grid-cols-[1fr_1.1fr]">
      <aside className="hidden flex-col justify-between bg-sidebar p-10 text-sidebar-foreground lg:flex">
        <Logo inverted />
        <div>
          <p className="max-w-md text-3xl leading-tight font-bold text-white">
            Pequeños pasos, todos los días. Y la constancia, a la vista.
          </p>
          <ul className="mt-8 space-y-4">
            {highlights.map(({ icon: Icon, text }) => (
              <li key={text} className="flex items-center gap-3">
                <Icon className="size-5 text-indigo-300" aria-hidden />
                {text}
              </li>
            ))}
          </ul>
        </div>
        <p className="text-xs text-sidebar-foreground/70">Tus datos son privados: solo tú puedes verlos.</p>
      </aside>
      <main className="flex items-center justify-center px-4 py-10 sm:px-8">
        <div className="w-full max-w-sm">
          <div className="mb-8 lg:hidden">
            <Logo />
          </div>
          <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
          <p className="mt-1 mb-6 text-sm text-muted-foreground">{subtitle}</p>
          {children}
        </div>
      </main>
    </div>
  )
}
