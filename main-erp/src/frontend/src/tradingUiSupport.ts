export type TradingPerspective = 'shipments' | 'customs' | 'costs' | 'reports'
export type TradingLifecycle = 'PROJECT' | 'ORDER' | 'SHIPMENT' | 'LEG' | 'CUSTOMS'

const transitions: Record<TradingLifecycle, Record<string, string[]>> = {
  PROJECT: { DRAFT: ['ACTIVE','CANCELLED'], ACTIVE: ['WAITING_ORDER','CANCELLED'], WAITING_ORDER: ['FX_PROCESS','CANCELLED'], FX_PROCESS: ['READY_TO_SHIP','CANCELLED'], READY_TO_SHIP: ['IN_TRANSIT','CANCELLED'], IN_TRANSIT: ['IN_CUSTOMS','CANCELLED'], IN_CUSTOMS: ['CLEARED','CANCELLED'], CLEARED: ['DELIVERED','CANCELLED'], DELIVERED: ['CLOSED'], CLOSED: [], CANCELLED: [] },
  ORDER: { DRAFT: ['SUBMITTED','CANCELLED'], SUBMITTED: ['ACTIVE','SUSPENDED','CANCELLED'], ACTIVE: ['SUSPENDED','CANCELLED'], SUSPENDED: ['ACTIVE','CANCELLED'], CANCELLED: [] },
  SHIPMENT: { DRAFT: ['WAITING_LINK','CANCELLED'], WAITING_LINK: ['READY','CANCELLED'], READY: ['IN_TRANSIT','CANCELLED'], IN_TRANSIT: ['ARRIVED','CANCELLED'], ARRIVED: ['IN_CUSTOMS','CANCELLED'], IN_CUSTOMS: ['PARTIAL_CLEARED','CLEARED','CANCELLED'], PARTIAL_CLEARED: ['CLEARED','CANCELLED'], CLEARED: ['DELIVERED','CANCELLED'], DELIVERED: [], CANCELLED: [] },
  LEG: { PLANNED: ['DEPARTED','CANCELLED'], DEPARTED: ['IN_TRANSIT','DELAYED','CANCELLED'], IN_TRANSIT: ['ARRIVED','DELAYED','CANCELLED'], DELAYED: ['IN_TRANSIT','ARRIVED','CANCELLED'], ARRIVED: [], CANCELLED: [] },
  CUSTOMS: { NOT_STARTED: ['DOCUMENTS_PENDING','CANCELLED'], DOCUMENTS_PENDING: ['IN_PROGRESS','BLOCKED','CANCELLED'], IN_PROGRESS: ['PARTIAL','COMPLETE','BLOCKED','CANCELLED'], PARTIAL: ['COMPLETE','BLOCKED','CANCELLED'], BLOCKED: ['DOCUMENTS_PENDING','IN_PROGRESS','CANCELLED'], COMPLETE: [], CANCELLED: [] },
}

const lifecycleOrder: Record<TradingLifecycle, string[]> = {
  PROJECT: ['DRAFT','ACTIVE','WAITING_ORDER','FX_PROCESS','READY_TO_SHIP','IN_TRANSIT','IN_CUSTOMS','CLEARED','DELIVERED','CLOSED'],
  ORDER: ['DRAFT','SUBMITTED','ACTIVE','SUSPENDED'],
  SHIPMENT: ['DRAFT','WAITING_LINK','READY','IN_TRANSIT','ARRIVED','IN_CUSTOMS','PARTIAL_CLEARED','CLEARED','DELIVERED'],
  LEG: ['PLANNED','DEPARTED','IN_TRANSIT','DELAYED','ARRIVED'],
  CUSTOMS: ['NOT_STARTED','DOCUMENTS_PENDING','IN_PROGRESS','PARTIAL','BLOCKED','COMPLETE'],
}

export function tradingStatusOptions(kind: TradingLifecycle, current: string, correction: boolean) {
  const normal = transitions[kind][current] ?? []
  if (!correction) return normal
  const ordered = lifecycleOrder[kind]
  const currentIndex = ordered.indexOf(current)
  return [...new Set([...normal, ...ordered.slice(0, Math.max(0, currentIndex))])]
}

export function tradingLinearOptions(sequence: string[], current: string, correction: boolean) {
  if (current === 'CANCELLED') return []
  const index = sequence.indexOf(current)
  const values = index >= 0 && index + 1 < sequence.length ? [sequence[index + 1]] : []
  if (current !== 'CANCELLED') values.push('CANCELLED')
  if (correction && index > 0) values.push(...sequence.slice(0, index))
  return [...new Set(values)]
}

export function buildTradingPerspectiveQuery(perspective: TradingPerspective, page: number,
  pageSize: number, search: string, status: string) {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  const normalizedSearch = search.trim()
  if (normalizedSearch) query.set('search', normalizedSearch)
  if (perspective === 'costs') {
    if (status === 'ALL') query.set('includeVoided', 'true')
  } else if (status) query.set('status', status)
  return query
}
