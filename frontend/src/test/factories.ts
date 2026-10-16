import type { Compliance, Dashboard, Goal, HabitDay, HabitListItem } from '@/types/api'

export const TODAY = '2026-10-12'

export function day(overrides: Partial<HabitDay> = {}): HabitDay {
  return {
    date: TODAY,
    status: 'pending',
    scheduled: true,
    quantity: null,
    completed: false,
    target: null,
    note: null,
    ...overrides,
  }
}

export function habitItem(overrides: Partial<HabitListItem> = {}): HabitListItem {
  return {
    id: 'h1',
    name: 'Meditar',
    description: null,
    category: { id: 'c1', name: 'Mente' },
    color: 'teal',
    icon: 'brain',
    type: 'binary',
    unit: null,
    schedule: {
      kind: 'daily',
      weekdays: null,
      timesPerWeek: null,
      intervalDays: null,
      targetValue: null,
      unit: 'day',
    },
    isArchived: false,
    today: day(),
    streak: { unit: 'day', current: 3, best: 7 },
    week: null,
    ...overrides,
  }
}

export function compliance(overrides: Partial<Compliance> = {}): Compliance {
  return {
    scheduledDayRate: null,
    weeklyRate: null,
    combinedRate: null,
    logCompletionRate: null,
    scheduledDays: 0,
    completedScheduledDays: 0,
    weeks: 0,
    weeksAchieved: 0,
    expectedOccurrences: 0,
    completedOccurrences: 0,
    extraCompletions: 0,
    logsRecorded: 0,
    logsCompleted: 0,
    totalQuantity: 0,
    ...overrides,
  }
}

export function goal(overrides: Partial<Goal> = {}): Goal {
  return {
    id: 'g1',
    name: 'Leer 12 libros',
    description: null,
    metric: 'manual',
    targetValue: 12,
    unit: 'libros',
    startDate: '2026-01-01',
    deadline: '2026-12-31',
    status: 'active',
    completedAt: null,
    habits: [],
    progress: { current: 3, target: 12, percent: 25, exceededBy: 0, targetReached: false },
    isOverdue: false,
    daysLeft: 80,
    version: 1,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

export function dashboard(overrides: Partial<Dashboard> = {}): Dashboard {
  return {
    displayName: 'Ana',
    today: TODAY,
    timeZoneId: 'Europe/Madrid',
    hasHabits: true,
    todaySummary: { scheduled: 2, completed: 1, percent: 50 },
    pending: [habitItem()],
    completed: [habitItem({ id: 'h2', name: 'Leer', today: day({ status: 'completed', completed: true }) })],
    weekly: [],
    topCurrentStreak: {
      habitId: 'h1',
      name: 'Meditar',
      icon: 'brain',
      color: 'teal',
      length: 3,
      unit: 'day',
    },
    bestStreak: { habitId: 'h1', name: 'Meditar', icon: 'brain', color: 'teal', length: 7, unit: 'day' },
    activeGoals: [goal()],
    week: {
      from: '2026-10-12',
      to: '2026-10-18',
      compliance: compliance({ combinedRate: 80, expectedOccurrences: 5, completedOccurrences: 4 }),
      previousCombinedRate: 70,
    },
    last30Days: [{ date: TODAY, scheduled: 2, completed: 1, rate: 50 }],
    upcomingReminders: [],
    recentActivity: [],
    unreadNotifications: 0,
    ...overrides,
  }
}
