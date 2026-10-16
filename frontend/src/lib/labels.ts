import {
  Apple,
  Bed,
  Bike,
  BookOpen,
  Brain,
  CalendarClock,
  CigaretteOff,
  CircleCheck,
  CircleDashed,
  CircleDot,
  CircleMinus,
  CircleX,
  Clock,
  Code,
  Coffee,
  Droplet,
  Dumbbell,
  Flower,
  Footprints,
  GraduationCap,
  Heart,
  Languages,
  Leaf,
  ListChecks,
  Moon,
  Music,
  PenLine,
  PhoneOff,
  Salad,
  Smile,
  Sparkles,
  Sun,
  Target,
  Timer,
  Users,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import type {
  CalendarDayState,
  GoalMetric,
  GoalStatus,
  HabitColor,
  HabitDayStatus,
  PeriodUnit,
  Schedule,
  ScheduleInput,
  Weekday,
} from '@/types/api'

export const WEEKDAYS: { value: Weekday; short: string; label: string }[] = [
  { value: 'monday', short: 'L', label: 'Lunes' },
  { value: 'tuesday', short: 'M', label: 'Martes' },
  { value: 'wednesday', short: 'X', label: 'Miércoles' },
  { value: 'thursday', short: 'J', label: 'Jueves' },
  { value: 'friday', short: 'V', label: 'Viernes' },
  { value: 'saturday', short: 'S', label: 'Sábado' },
  { value: 'sunday', short: 'D', label: 'Domingo' },
]

export const WEEKDAY_LABEL = Object.fromEntries(WEEKDAYS.map((d) => [d.value, d.label])) as Record<
  Weekday,
  string
>

export function unitLabel(unit: PeriodUnit, count: number): string {
  if (unit === 'week') return count === 1 ? 'semana' : 'semanas'
  return count === 1 ? 'día' : 'días'
}

/** "Todos los días", "L, X, V", "4 veces por semana", "Cada 3 días" (+ daily target). */
export function describeSchedule(schedule: Schedule | ScheduleInput, unit?: string | null): string {
  let text: string
  switch (schedule.kind) {
    case 'daily':
      text = 'Todos los días'
      break
    case 'weekdays': {
      const days = schedule.weekdays ?? []
      if (days.length === 5 && !days.includes('saturday') && !days.includes('sunday'))
        text = 'De lunes a viernes'
      else if (days.length === 2 && days.includes('saturday') && days.includes('sunday'))
        text = 'Fines de semana'
      else
        text = WEEKDAYS.filter((d) => days.includes(d.value))
          .map((d) => d.label.slice(0, 3))
          .join(', ')
      break
    }
    case 'timesPerWeek':
      text = schedule.timesPerWeek === 1 ? '1 vez por semana' : `${schedule.timesPerWeek} veces por semana`
      break
    case 'everyNDays':
      text = `Cada ${schedule.intervalDays} días`
      break
  }
  if (schedule.targetValue) text += ` · meta ${schedule.targetValue}${unit ? ` ${unit}` : ''}`
  return text
}

interface StatusMeta {
  label: string
  icon: LucideIcon
  className: string
}

/** Day status of one habit. Colour never carries the meaning alone: every status has an icon and a label. */
export const DAY_STATUS: Record<HabitDayStatus, StatusMeta> = {
  completed: { label: 'Completado', icon: CircleCheck, className: 'bg-success-soft text-success' },
  partial: { label: 'Parcial', icon: CircleDot, className: 'bg-warning-soft text-warning' },
  pending: { label: 'Pendiente', icon: Clock, className: 'bg-primary-soft text-primary' },
  missed: { label: 'No cumplido', icon: CircleX, className: 'bg-danger-soft text-danger' },
  optional: { label: 'Disponible', icon: CircleDashed, className: 'bg-muted text-muted-foreground' },
  future: { label: 'Programado', icon: CalendarClock, className: 'bg-muted text-muted-foreground' },
  notScheduled: { label: 'No programado', icon: CircleMinus, className: 'bg-muted text-muted-foreground' },
}

export const CALENDAR_STATE: Record<CalendarDayState, StatusMeta> = {
  complete: { label: 'Todo cumplido', icon: CircleCheck, className: 'bg-success-soft text-success' },
  partial: { label: 'Cumplido en parte', icon: CircleDot, className: 'bg-warning-soft text-warning' },
  missed: { label: 'Sin cumplir', icon: CircleX, className: 'bg-danger-soft text-danger' },
  pending: { label: 'Hoy, en curso', icon: Clock, className: 'bg-primary-soft text-primary' },
  future: { label: 'Programado', icon: CalendarClock, className: 'text-muted-foreground' },
  empty: { label: 'Sin hábitos programados', icon: CircleMinus, className: 'text-muted-foreground' },
}

export const GOAL_STATUS: Record<GoalStatus, { label: string; className: string }> = {
  active: { label: 'Activo', className: 'bg-primary-soft text-primary' },
  paused: { label: 'En pausa', className: 'bg-warning-soft text-warning' },
  completed: { label: 'Completado', className: 'bg-success-soft text-success' },
  cancelled: { label: 'Cancelado', className: 'bg-muted text-muted-foreground' },
}

export const GOAL_METRIC: Record<GoalMetric, { label: string; description: string }> = {
  manual: {
    label: 'Cantidad manual',
    description: 'Registras tú el avance (por ejemplo, libros leídos o kilómetros).',
  },
  habitCompletions: {
    label: 'Veces que cumples hábitos',
    description: 'Cuenta los días completados de los hábitos vinculados.',
  },
  habitQuantity: {
    label: 'Cantidad acumulada de un hábito',
    description: 'Suma lo registrado en hábitos cuantificables con la misma unidad.',
  },
  habitStreak: {
    label: 'Racha de un hábito',
    description: 'La racha más larga del hábito dentro del periodo del objetivo.',
  },
}

/** Static class names per colour (Tailwind needs them literally in the source). */
export const HABIT_COLOR: Record<HabitColor, { label: string; chip: string; solid: string; ring: string }> = {
  teal: {
    label: 'Verde azulado',
    chip: 'bg-teal-100 text-teal-800 dark:bg-teal-950 dark:text-teal-200',
    solid: 'bg-teal-600',
    ring: 'ring-teal-600',
  },
  emerald: {
    label: 'Esmeralda',
    chip: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
    solid: 'bg-emerald-600',
    ring: 'ring-emerald-600',
  },
  lime: {
    label: 'Lima',
    chip: 'bg-lime-100 text-lime-800 dark:bg-lime-950 dark:text-lime-200',
    solid: 'bg-lime-600',
    ring: 'ring-lime-600',
  },
  amber: {
    label: 'Ámbar',
    chip: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
    solid: 'bg-amber-500',
    ring: 'ring-amber-500',
  },
  orange: {
    label: 'Naranja',
    chip: 'bg-orange-100 text-orange-800 dark:bg-orange-950 dark:text-orange-200',
    solid: 'bg-orange-500',
    ring: 'ring-orange-500',
  },
  rose: {
    label: 'Rosa',
    chip: 'bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-200',
    solid: 'bg-rose-500',
    ring: 'ring-rose-500',
  },
  pink: {
    label: 'Fucsia',
    chip: 'bg-pink-100 text-pink-800 dark:bg-pink-950 dark:text-pink-200',
    solid: 'bg-pink-500',
    ring: 'ring-pink-500',
  },
  violet: {
    label: 'Violeta',
    chip: 'bg-violet-100 text-violet-800 dark:bg-violet-950 dark:text-violet-200',
    solid: 'bg-violet-600',
    ring: 'ring-violet-600',
  },
  indigo: {
    label: 'Índigo',
    chip: 'bg-indigo-100 text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200',
    solid: 'bg-indigo-600',
    ring: 'ring-indigo-600',
  },
  sky: {
    label: 'Celeste',
    chip: 'bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-200',
    solid: 'bg-sky-600',
    ring: 'ring-sky-600',
  },
  slate: {
    label: 'Gris',
    chip: 'bg-slate-200 text-slate-800 dark:bg-slate-700 dark:text-slate-100',
    solid: 'bg-slate-600',
    ring: 'ring-slate-600',
  },
}

export const HABIT_COLORS = Object.keys(HABIT_COLOR) as HabitColor[]

export const HABIT_ICON: Record<string, { icon: LucideIcon; label: string }> = {
  'circle-check': { icon: CircleCheck, label: 'Check' },
  'book-open': { icon: BookOpen, label: 'Libro' },
  dumbbell: { icon: Dumbbell, label: 'Pesas' },
  droplet: { icon: Droplet, label: 'Agua' },
  brain: { icon: Brain, label: 'Mente' },
  code: { icon: Code, label: 'Código' },
  moon: { icon: Moon, label: 'Luna' },
  languages: { icon: Languages, label: 'Idiomas' },
  heart: { icon: Heart, label: 'Corazón' },
  leaf: { icon: Leaf, label: 'Hoja' },
  'pen-line': { icon: PenLine, label: 'Escritura' },
  music: { icon: Music, label: 'Música' },
  sun: { icon: Sun, label: 'Sol' },
  bike: { icon: Bike, label: 'Bicicleta' },
  footprints: { icon: Footprints, label: 'Caminar' },
  apple: { icon: Apple, label: 'Fruta' },
  bed: { icon: Bed, label: 'Dormir' },
  coffee: { icon: Coffee, label: 'Café' },
  target: { icon: Target, label: 'Objetivo' },
  wallet: { icon: Wallet, label: 'Ahorro' },
  smile: { icon: Smile, label: 'Ánimo' },
  'graduation-cap': { icon: GraduationCap, label: 'Estudio' },
  flower: { icon: Flower, label: 'Flor' },
  timer: { icon: Timer, label: 'Temporizador' },
  users: { icon: Users, label: 'Personas' },
  sparkles: { icon: Sparkles, label: 'Brillo' },
  salad: { icon: Salad, label: 'Ensalada' },
  'cigarette-off': { icon: CigaretteOff, label: 'Sin tabaco' },
  'list-checks': { icon: ListChecks, label: 'Lista' },
  'phone-off': { icon: PhoneOff, label: 'Sin móvil' },
}

export const HABIT_ICONS = Object.keys(HABIT_ICON)
