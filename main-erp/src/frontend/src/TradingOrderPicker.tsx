import { useEffect, useState } from 'react'
import { tradingRequest } from './tradingApi'
import type { OrderCurrency, OrderPage, OrderProject } from './tradingOrderSupport'
import { TradingOrderPickerView } from './TradingOrderPickerView'
import type { OrderPickerItem as Item, OrderPickerMode as PickerMode } from './TradingOrderPickerView'

export function TradingOrderPicker({ resource, mode, value, existing, onSelect, onReady, disabled = false }: {
  resource: string; mode: PickerMode; value: string; existing?: OrderCurrency;
  onSelect: (item: Item | null) => void | boolean; onReady?: (ready: boolean) => void; disabled?: boolean
}) {
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [retry, setRetry] = useState(0)
  const [selected, setSelected] = useState<OrderCurrency | null>(existing ?? null)
  const [data, setData] = useState<OrderPage<Item> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLoading(true); setError(''); onReady?.(false)
      const qs = new URLSearchParams({ search: query.trim(), page: String(page), pageSize: '25' })
      tradingRequest<OrderPage<OrderCurrency & OrderProject>>(`${resource}?${qs}`, 'GET', undefined, controller.signal).then(result => {
        if (controller.signal.aborted) return
        const items = result.items.map(x => mode === 'currency' ? x : { ...x, code: x.projectNumber, nameFa: x.titleFa, nameEn: x.titleEn })
        setData({ ...result, items }); onReady?.(true)
      }).catch((e: Error) => { if (!controller.signal.aborted) { setError(e.message); onReady?.(false) } })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }, query ? 250 : 0)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [resource, mode, query, page, retry, onReady])
  const items: Item[] = selected?.id === value && !data?.items.some(x => x.id === value) ? [selected, ...(data?.items ?? [])] : data?.items ?? []
  return <TradingOrderPickerView mode={mode} value={value} query={query} items={items} page={page} totalCount={data?.totalCount ?? 0}
    loading={loading} error={error} disabled={disabled} onSearch={next => { setQuery(next); setPage(1) }} onPage={setPage} onRetry={() => setRetry(x => x + 1)}
    onSelect={id => { const item = items.find(x => x.id === id) ?? null; if (onSelect(item) !== false) setSelected(item) }} />
}
