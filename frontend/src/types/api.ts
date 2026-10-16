/** Types mirroring the HabitFlow API contract (/api/v1). Local dates are "yyyy-MM-dd" strings. */

export type LocalDate = string
export type Instant = string

export type Weekday = 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday'

export type HabitType = 'binary' | 'quantity'
export type ScheduleKind = 'daily' | 'weekdays' | 'everyNDays' | 'timesPerWeek'
export type PeriodUnit = 'day' | 'week'
export type HabitColor =
  'teal' | 'emerald' | 'lime' | 'amber' | 'orange' | 'rose' | 'pink' | 'violet' | 'indigo' | 'sky' | 'slate'
export type HabitDayStatus =
  'notScheduled' | 'future' | 'pending' | 'completed' | 'partial' | 'missed' | 'optional'
export type LogChangeAction = 'created' | 'updated' | 'deleted'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface User {
  id: string
  email: string
  displayName: string
  timeZoneId: string
  emailRemindersEnabled: boolean
  createdAt: Instant
}

export interface AuthCapabilities {
  passwordResetAvailable: boolean
  emailRemindersAvailable: boolean
}

export interface Category {
  id: string
  name: string
  color: HabitColor
  icon: string
  isBuiltIn: boolean
  habitCount: number
}

export interface Schedule {
  kind: ScheduleKind
  weekdays: Weekday[] | null
  timesPerWeek: number | null
  intervalDays: number | null
  targetValue: number | null
  unit: PeriodUnit
}

export interface ScheduleInput {
  kind: ScheduleKind
  weekdays?: Weekday[] | null
  timesPerWeek?: number | null
  intervalDays?: number | null
  targetValue?: number | null
}

export interface HabitDay {
  date: LocalDate
  status: HabitDayStatus
  scheduled: boolean
  quantity: number | null
  completed: boolean
  target: number | null
  note: string | null
}

export interface StreakBrief {
  unit: PeriodUnit
  current: number
  best: number
}

export interface WeekProgress {
  weekStart: LocalDate
  weekEnd: LocalDate
  completed: number
  required: number
  daysLeft: number
  achieved: boolean
}

export interface Compliance {
  scheduledDayRate: number | null
  weeklyRate: number | null
  combinedRate: number | null
  logCompletionRate: number | null
  scheduledDays: number
  completedScheduledDays: number
  weeks: number
  weeksAchieved: number
  expectedOccurrences: number
  completedOccurrences: number
  extraCompletions: number
  logsRecorded: number
  logsCompleted: number
  totalQuantity: number
}

export interface StreakRun {
  length: number
  start: LocalDate
  end: LocalDate
}

export interface Streak {
  unit: PeriodUnit
  current: number
  currentStart: LocalDate | null
  best: StreakRun | null
  lastCompletedDate: LocalDate | null
  currentWeek: WeekProgress | null
  allTime: Compliance
}

export interface CategoryRef {
  id: string
  name: string
}

export interface HabitListItem {
  id: string
  name: string
  description: string | null
  category: CategoryRef
  color: HabitColor
  icon: string
  type: HabitType
  unit: string | null
  schedule: Schedule
  isArchived: boolean
  today: HabitDay
  streak: StreakBrief
  week: WeekProgress | null
}

export interface Habit {
  id: string
  name: string
  description: string | null
  category: CategoryRef
  color: HabitColor
  icon: string
  type: HabitType
  unit: string | null
  startDate: LocalDate
  timeZoneId: string | null
  effectiveTimeZoneId: string
  isArchived: boolean
  archivedAt: Instant | null
  schedule: Schedule
  scheduleHistory: { effectiveFrom: LocalDate; schedule: Schedule }[]
  pauses: { start: LocalDate; end: LocalDate | null }[]
  hasLogs: boolean
  today: HabitDay
  streak: Streak
  version: number
  createdAt: Instant
  updatedAt: Instant
}

export interface HabitInput {
  name: string
  description: string | null
  categoryId: string
  color: HabitColor
  icon: string
  unit: string | null
  startDate: LocalDate
  timeZoneId: string | null
  schedule: ScheduleInput
}

export interface CreateHabitInput extends HabitInput {
  type: HabitType
}

export interface UpdateHabitInput extends HabitInput {
  version: number
}

export interface HabitLog {
  date: LocalDate
  quantity: number | null
  completed: boolean
  note: string | null
  createdAt: Instant
  updatedAt: Instant
}

export interface LogResult {
  log: HabitLog | null
  day: HabitDay
  streak: StreakBrief
  week: WeekProgress | null
}

export interface LogInput {
  completed?: boolean | null
  quantity?: number | null
  note?: string | null
}

export interface LogChange {
  date: LocalDate
  action: LogChangeAction
  previousQuantity: number | null
  newQuantity: number | null
  previousCompleted: boolean | null
  newCompleted: boolean | null
  at: Instant
}

// Calendar
export type CalendarDayState = 'empty' | 'future' | 'pending' | 'complete' | 'partial' | 'missed'

export interface CalendarDay {
  date: LocalDate
  state: CalendarDayState
  scheduled: number
  completed: number
  extra: number
  weeklyCompletions: number
  percent: number | null
}

export interface CalendarRange {
  from: LocalDate
  to: LocalDate
  today: LocalDate
  days: CalendarDay[]
}

export interface CalendarHabitRef {
  id: string
  name: string
  icon: string
  color: HabitColor
  type: HabitType
  unit: string | null
  isArchived: boolean
  schedule: Schedule
}

export interface CalendarHabitDay {
  habit: CalendarHabitRef
  day: HabitDay
  week: WeekProgress | null
  canLog: boolean
}

export interface CalendarDayDetail {
  date: LocalDate
  today: LocalDate
  summary: CalendarDay
  habits: CalendarHabitDay[]
}

export interface CalendarWeekRow {
  habit: CalendarHabitRef
  days: HabitDay[]
  week: WeekProgress | null
  /** null for archived habits (read-only). */
  loggableFrom: LocalDate | null
  today: LocalDate
}

export interface CalendarWeek {
  from: LocalDate
  to: LocalDate
  today: LocalDate
  days: CalendarDay[]
  habits: CalendarWeekRow[]
}

// Goals
export type GoalMetric = 'manual' | 'habitCompletions' | 'habitQuantity' | 'habitStreak'
export type GoalStatus = 'active' | 'paused' | 'completed' | 'cancelled'

export interface GoalProgress {
  current: number
  target: number
  percent: number
  exceededBy: number
  targetReached: boolean
}

export interface Goal {
  id: string
  name: string
  description: string | null
  metric: GoalMetric
  targetValue: number
  unit: string
  startDate: LocalDate
  deadline: LocalDate | null
  status: GoalStatus
  completedAt: Instant | null
  habits: {
    id: string
    name: string
    icon: string
    color: HabitColor
    unit: string | null
    isArchived: boolean
  }[]
  progress: GoalProgress
  isOverdue: boolean
  daysLeft: number | null
  version: number
  createdAt: Instant
  updatedAt: Instant
}

export interface CreateGoalInput {
  name: string
  description: string | null
  metric: GoalMetric
  targetValue: number
  unit: string | null
  startDate: LocalDate | null
  deadline: LocalDate | null
  habitIds: string[]
}

export interface UpdateGoalInput {
  name: string
  description: string | null
  targetValue: number
  unit: string | null
  deadline: LocalDate | null
  version: number
}

export interface GoalEntry {
  id: string
  date: LocalDate
  amount: number
  note: string | null
  createdAt: Instant
}

export interface GoalHistoryPoint {
  date: LocalDate
  value: number
}

// Statistics
export interface DailyPoint {
  date: LocalDate
  scheduled: number
  completed: number
  rate: number | null
}

export interface PeriodPoint {
  start: LocalDate
  end: LocalDate
  expected: number
  completed: number
  rate: number | null
}

export interface WeekdayPoint {
  weekday: Weekday
  scheduled: number
  completed: number
  rate: number | null
  completions: number
}

export interface HabitStatsRow {
  habitId: string
  name: string
  icon: string
  color: HabitColor
  type: HabitType
  unit: string | null
  isArchived: boolean
  compliance: Compliance
  streak: StreakBrief
}

export interface Statistics {
  from: LocalDate
  to: LocalDate
  summary: Compliance
  comparison: {
    previousFrom: LocalDate
    previousTo: LocalDate
    previous: Compliance
    combinedRateDelta: number | null
  }
  daily: DailyPoint[]
  weekly: PeriodPoint[]
  monthly: PeriodPoint[]
  weekdays: WeekdayPoint[]
  habits: HabitStatsRow[]
  goals: { active: number; paused: number; completed: number; cancelled: number; completedInRange: number }
}

export interface HabitStatistics {
  from: LocalDate
  to: LocalDate
  compliance: Compliance
  streak: Streak
  streakSeries: { date: LocalDate; length: number; unit: PeriodUnit }[]
  weekdays: WeekdayPoint[]
  quantities: { date: LocalDate; quantity: number | null; target: number | null; completed: boolean }[]
  weekly: PeriodPoint[]
}

// Dashboard
export interface StreakHighlight {
  habitId: string
  name: string
  icon: string
  color: HabitColor
  length: number
  unit: PeriodUnit
}

export interface Activity {
  habitId: string
  habitName: string
  icon: string
  color: HabitColor
  unit: string | null
  date: LocalDate
  action: LogChangeAction
  completed: boolean | null
  quantity: number | null
  at: Instant
}

export interface Dashboard {
  displayName: string
  today: LocalDate
  timeZoneId: string
  hasHabits: boolean
  todaySummary: { scheduled: number; completed: number; percent: number | null }
  pending: HabitListItem[]
  completed: HabitListItem[]
  weekly: HabitListItem[]
  topCurrentStreak: StreakHighlight | null
  bestStreak: StreakHighlight | null
  activeGoals: Goal[]
  week: { from: LocalDate; to: LocalDate; compliance: Compliance; previousCombinedRate: number | null }
  last30Days: DailyPoint[]
  upcomingReminders: {
    id: string
    title: string
    nextFireAt: Instant
    habitName: string | null
    goalName: string | null
  }[]
  recentActivity: Activity[]
  unreadNotifications: number
}

// Reminders and notifications
export type ReminderChannel = 'inApp' | 'inAppAndEmail'

export interface Reminder {
  id: string
  title: string
  description: string | null
  localTime: string
  weekdays: Weekday[]
  timeZoneId: string
  isActive: boolean
  channel: ReminderChannel
  habit: { id: string; name: string } | null
  goal: { id: string; name: string } | null
  nextFireAt: Instant | null
  lastFiredAt: Instant | null
  createdAt: Instant
}

export interface ReminderInput {
  title: string
  description: string | null
  localTime: string
  weekdays: Weekday[]
  timeZoneId: string | null
  channel: ReminderChannel
  habitId: string | null
  goalId: string | null
}

export type NotificationKind = 'reminder' | 'invitation' | 'challenge' | 'sharedGoal'

export interface AppNotification {
  id: string
  kind: NotificationKind
  title: string
  body: string | null
  link: string | null
  createdAt: Instant
  readAt: Instant | null
  emailStatus: 'notRequested' | 'pending' | 'sent' | 'failed'
}

// Challenges
export type ChallengeRule = 'completionCount' | 'quantityTotal' | 'dailyCompletion'
export type ChallengePhase = 'upcoming' | 'active' | 'finished'
export type ChallengeResult = 'inProgress' | 'completed' | 'notCompleted' | 'abandoned'

export interface ChallengeStanding {
  displayName: string
  isMe: boolean
  progress: number
  percent: number
  result: ChallengeResult
  leftOn: LocalDate | null
}

export interface ChallengeSummary {
  id: string
  name: string
  startDate: LocalDate
  endDate: LocalDate
  rule: ChallengeRule
  targetValue: number
  unit: string | null
  phase: ChallengePhase
  participants: number
  isCreator: boolean
  me: ChallengeStanding | null
}

export interface Challenge {
  id: string
  name: string
  description: string | null
  startDate: LocalDate
  endDate: LocalDate
  rule: ChallengeRule
  targetValue: number
  unit: string | null
  phase: ChallengePhase
  joinCode: string
  isCreator: boolean
  canDelete: boolean
  canEditRules: boolean
  myHabit: { id: string; name: string } | null
  standings: ChallengeStanding[]
}

export interface ChallengePreview {
  id: string
  name: string
  description: string | null
  startDate: LocalDate
  endDate: LocalDate
  rule: ChallengeRule
  targetValue: number
  unit: string | null
  phase: ChallengePhase
  participants: number
  alreadyJoined: boolean
}

// Shared goals
export type SharedGoalRole = 'owner' | 'member'
export type SharedGoalStatus = 'active' | 'completed' | 'cancelled'

export interface ProgressValue {
  current: number
  target: number
  percent: number
  exceededBy: number
  targetReached: boolean
}

export interface SharedGoalSummary {
  id: string
  name: string
  unit: string
  status: SharedGoalStatus
  deadline: LocalDate | null
  myRole: SharedGoalRole
  participants: number
  collective: ProgressValue
  myTotal: number
}

export interface SharedParticipant {
  participantId: string
  displayName: string
  role: SharedGoalRole
  isMe: boolean
  active: boolean
  total: number
  individual: ProgressValue | null
}

export interface SharedGoal {
  id: string
  name: string
  description: string | null
  unit: string
  collectiveTarget: number
  individualTarget: number | null
  startDate: LocalDate
  deadline: LocalDate | null
  status: SharedGoalStatus
  myRole: SharedGoalRole
  version: number
  collective: ProgressValue
  participants: SharedParticipant[]
  myContributions: { id: string; date: LocalDate; amount: number; note: string | null; createdAt: Instant }[]
  pendingInvitations: { id: string; email: string; createdAt: Instant }[]
}

export interface MyInvitation {
  id: string
  sharedGoalId: string
  goalName: string
  invitedBy: string
  createdAt: Instant
}
