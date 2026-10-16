import { Tabs as TabsPrimitive } from 'radix-ui'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export const Tabs = TabsPrimitive.Root
export const TabsContent = TabsPrimitive.Content

export function TabsList({ className, ...props }: ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List
      className={cn('inline-flex flex-wrap gap-1 rounded-lg bg-muted p-1 text-sm', className)}
      {...props}
    />
  )
}

export function TabsTrigger({ className, ...props }: ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      className={cn(
        'rounded-md px-3 py-1.5 font-medium text-muted-foreground transition-colors data-[state=active]:bg-surface data-[state=active]:text-foreground data-[state=active]:shadow-xs',
        className,
      )}
      {...props}
    />
  )
}
