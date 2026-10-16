import { Table2 } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/display'
import { cn, formatNumber } from '@/lib/utils'

/**
 * Chart theme: CSS variables defined per theme in src/index.css (validated there for each card surface):
 * one series hue, a second ordinal step nearer the surface that still clears 2:1, recessive hairline chrome.
 */
export const CHART = {
  series: 'var(--chart-series)',
  seriesLight: 'var(--chart-series-light)',
  grid: 'var(--chart-grid)',
  axis: 'var(--chart-axis)',
  ink: 'var(--chart-ink)',
  inkSecondary: 'var(--chart-ink-secondary)',
  target: 'var(--chart-ink-secondary)',
} as const

const axisProps = {
  stroke: CHART.axis,
  tick: { fill: CHART.axis, fontSize: 12 },
  tickLine: false,
  axisLine: { stroke: CHART.grid },
} as const

export interface TableColumn<T> {
  header: string
  cell: (row: T) => ReactNode
  numeric?: boolean
}

/**
 * Frame shared by every chart: title, optional subtitle and a "table view" toggle so no value is reachable only by
 * hovering (and screen-reader users get the data as a real table).
 */
export function ChartCard<T>({
  title,
  subtitle,
  rows,
  columns,
  children,
  className,
  empty,
  legend,
}: {
  title: string
  subtitle?: ReactNode
  rows: T[]
  columns: TableColumn<T>[]
  children: ReactNode
  className?: string
  /** Shown instead of the chart when there is nothing to plot. */
  empty?: ReactNode
  /** Needed whenever colour distinguishes marks (e.g. target met vs not). */
  legend?: { label: string; color: string; shape?: 'rect' | 'line' }[]
}) {
  const [showTable, setShowTable] = useState(false)
  const id = useId()
  return (
    <Card className={cn('p-5', className)}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 id={`${id}-title`} className="text-base font-semibold">
            {title}
          </h2>
          {subtitle ? <p className="text-sm text-muted-foreground">{subtitle}</p> : null}
        </div>
        {rows.length > 0 && !empty ? (
          <Button
            variant="ghost"
            size="sm"
            aria-pressed={showTable}
            onClick={() => setShowTable((v) => !v)}
            aria-label={showTable ? `Ver gráfico: ${title}` : `Ver tabla: ${title}`}
          >
            <Table2 /> {showTable ? 'Gráfico' : 'Tabla'}
          </Button>
        ) : null}
      </div>
      {empty ? (
        <div className="grid h-48 place-items-center text-center text-sm text-muted-foreground">{empty}</div>
      ) : showTable ? (
        <div className="max-h-72 overflow-auto">
          <table className="w-full text-sm" aria-labelledby={`${id}-title`}>
            <thead className="sticky top-0 bg-surface text-left text-xs text-muted-foreground">
              <tr>
                {columns.map((c) => (
                  <th
                    key={c.header}
                    scope="col"
                    className={cn('py-1.5 pr-3 font-semibold', c.numeric && 'text-right')}
                  >
                    {c.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-border tabular-nums">
              {rows.map((row, i) => (
                <tr key={i}>
                  {columns.map((c) => (
                    <td key={c.header} className={cn('py-1.5 pr-3', c.numeric && 'text-right')}>
                      {c.cell(row)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <>
          {legend ? (
            <ul className="mb-2 flex flex-wrap gap-4 text-xs text-muted-foreground">
              {legend.map((item) => (
                <li key={item.label} className="flex items-center gap-1.5">
                  <span
                    aria-hidden
                    className={item.shape === 'line' ? 'h-0.5 w-4' : 'size-2.5 rounded-sm'}
                    style={{ background: item.color }}
                  />
                  {item.label}
                </li>
              ))}
            </ul>
          ) : null}
          <div className="h-60" role="img" aria-labelledby={`${id}-title`}>
            {children}
          </div>
        </>
      )}
    </Card>
  )
}

/** The part of Recharts' tooltip props we use (its generics do not compose well with ours). */
interface TooltipRenderProps {
  active?: boolean
  label?: unknown
  payload?: ReadonlyArray<{ value?: unknown; dataKey?: unknown; name?: unknown }>
}

/** Tooltip: the value leads, the label follows (secondary). */
function ChartTooltip({
  active,
  payload,
  label,
  formatLabel,
  formatValue,
}: TooltipRenderProps & {
  formatLabel: (label: string) => string
  formatValue: (value: number | null, key: string) => string
}) {
  if (!active || !payload?.length) return null
  return (
    <div className="rounded-lg border border-border bg-surface px-3 py-2 text-sm shadow-lg">
      <p className="text-xs text-muted-foreground">{formatLabel(String(label))}</p>
      {payload.map((entry) => (
        <p key={String(entry.dataKey)} className="flex items-center gap-2">
          <span aria-hidden className="h-0.5 w-3 rounded-full" style={{ background: CHART.series }} />
          <strong>
            {formatValue(typeof entry.value === 'number' ? entry.value : null, String(entry.dataKey))}
          </strong>
          <span className="text-muted-foreground">{String(entry.name ?? '')}</span>
        </p>
      ))}
    </div>
  )
}

/** Percentage over time (0–100 axis). Days without anything scheduled are gaps, not zeros. */
export function RateLineChart<T extends object>({
  data,
  xKey,
  yKey,
  name,
  formatX,
}: {
  data: T[]
  xKey: string
  yKey: string
  name: string
  formatX: (value: string) => string
}) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
        <CartesianGrid vertical={false} stroke={CHART.grid} />
        <XAxis dataKey={xKey} tickFormatter={formatX} minTickGap={24} {...axisProps} />
        <YAxis domain={[0, 100]} ticks={[0, 25, 50, 75, 100]} tickFormatter={(v) => `${v}%`} {...axisProps} />
        <Tooltip
          cursor={{ stroke: CHART.axis, strokeWidth: 1 }}
          content={(props) => (
            <ChartTooltip
              {...props}
              formatLabel={formatX}
              formatValue={(v) =>
                v === null || v === undefined ? 'Sin programación' : `${formatNumber(v)} %`
              }
            />
          )}
        />
        <Line
          type="monotone"
          dataKey={yKey}
          name={name}
          stroke={CHART.series}
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
          dot={
            data.length <= 31
              ? { r: 4, fill: CHART.series, stroke: 'var(--color-surface)', strokeWidth: 2 }
              : false
          }
          activeDot={{ r: 5, fill: CHART.series, stroke: 'var(--color-surface)', strokeWidth: 2 }}
          connectNulls={false}
          isAnimationActive={false}
        />
      </LineChart>
    </ResponsiveContainer>
  )
}

/** Column chart of one measure; optional per-bar emphasis (e.g. target met) and a target reference line. */
export function ColumnChart<T extends object>({
  data,
  xKey,
  yKey,
  name,
  formatX,
  formatValue,
  emphasize,
  target,
  percent = false,
}: {
  data: T[]
  xKey: string
  yKey: string
  name: string
  formatX: (value: string) => string
  formatValue: (value: number | null) => string
  emphasize?: (row: T) => boolean
  target?: number | null
  percent?: boolean
}) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -12 }} barCategoryGap="20%">
        <CartesianGrid vertical={false} stroke={CHART.grid} />
        <XAxis dataKey={xKey} tickFormatter={formatX} minTickGap={8} {...axisProps} />
        <YAxis
          {...axisProps}
          domain={percent ? [0, 100] : [0, 'auto']}
          ticks={percent ? [0, 25, 50, 75, 100] : undefined}
          tickFormatter={(v: number) => (percent ? `${v}%` : formatNumber(v))}
          allowDecimals={!percent}
        />
        <Tooltip
          cursor={{ fill: CHART.grid, opacity: 0.4 }}
          content={(props) => (
            <ChartTooltip {...props} formatLabel={formatX} formatValue={(v) => formatValue(v)} />
          )}
        />
        {target ? (
          <ReferenceLine
            y={target}
            stroke={CHART.target}
            strokeWidth={1}
            label={{
              value: `Meta ${formatNumber(target)}`,
              position: 'insideTopLeft',
              fill: CHART.inkSecondary,
              fontSize: 12,
            }}
          />
        ) : null}
        <Bar dataKey={yKey} name={name} maxBarSize={24} radius={[4, 4, 0, 0]} isAnimationActive={false}>
          {data.map((row, i) => (
            <Cell key={i} fill={emphasize && !emphasize(row) ? CHART.seriesLight : CHART.series} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

/** A running value over time (streak length, cumulative goal progress) as a line over a light wash. */
export function RunningAreaChart<T extends object>({
  data,
  xKey,
  yKey,
  name,
  formatX,
  formatValue,
  target,
}: {
  data: T[]
  xKey: string
  yKey: string
  name: string
  formatX: (value: string) => string
  formatValue: (value: number | null) => string
  target?: number
}) {
  const gradientId = useId()
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={CHART.series} stopOpacity={0.12} />
            <stop offset="100%" stopColor={CHART.series} stopOpacity={0.02} />
          </linearGradient>
        </defs>
        <CartesianGrid vertical={false} stroke={CHART.grid} />
        <XAxis dataKey={xKey} tickFormatter={formatX} minTickGap={24} {...axisProps} />
        <YAxis {...axisProps} allowDecimals={false} tickFormatter={(v: number) => formatNumber(v)} />
        <Tooltip
          cursor={{ stroke: CHART.axis, strokeWidth: 1 }}
          content={(props) => (
            <ChartTooltip {...props} formatLabel={formatX} formatValue={(v) => formatValue(v)} />
          )}
        />
        {target ? (
          <ReferenceLine
            y={target}
            stroke={CHART.target}
            strokeWidth={1}
            label={{
              value: `Meta ${formatNumber(target)}`,
              position: 'insideTopLeft',
              fill: CHART.inkSecondary,
              fontSize: 12,
            }}
          />
        ) : null}
        <Area
          type="stepAfter"
          dataKey={yKey}
          name={name}
          stroke={CHART.series}
          strokeWidth={2}
          fill={`url(#${gradientId})`}
          isAnimationActive={false}
        />
      </AreaChart>
    </ResponsiveContainer>
  )
}
