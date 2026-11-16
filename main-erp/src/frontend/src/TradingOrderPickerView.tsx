import { useTranslation } from 'react-i18next'
import { FormField } from './ui'
import type { OrderCurrency } from './tradingOrderSupport'

export type OrderPickerItem = OrderCurrency & { status?: string; projectNumber?: string; titleFa?: string; titleEn?: string }
export type OrderPickerMode = 'project' | 'currency'

// Shared presentation for live paged API lookups and their standalone HTML export.
export function TradingOrderPickerView({ mode, value, query, items, page, totalCount, loading, error, disabled = false,
  onSearch, onSelect, onPage, onRetry }: {
  mode: OrderPickerMode; value: string; query: string; items: OrderPickerItem[]; page: number; totalCount: number;
  loading: boolean; error: string; disabled?: boolean; onSearch: (query: string) => void;
  onSelect: (id: string) => void; onPage: (page: number) => void; onRetry: () => void
}) {
  const { t, i18n } = useTranslation()
  const en = (i18n.resolvedLanguage ?? i18n.language).startsWith('en')
  const label = t(mode === 'project' ? 'trading.orders.selectProject' : 'trading.orders.fields.currencyId')
  return <div className="trade-setup-picker" data-order-picker={mode}>
    <FormField label={t('trading.setup.searchFor', { label })}><input type="search" maxLength={200} value={query} disabled={disabled} onChange={e => onSearch(e.target.value)} /></FormField>
    <FormField label={label} required error={error || undefined} hint={loading ? t('common.loading') : error ? undefined : !totalCount ? t(mode === 'project' ? 'trading.orders.noProjects' : 'trading.setup.noOptions') : undefined}>
      <select name={mode === 'project' ? 'selectedProjectId' : 'currencyId'} required value={value} disabled={disabled || (loading && !value)} aria-busy={loading} onChange={e => onSelect(e.target.value)}>
        <option value="">{t('trading.setup.choose')}</option>{items.map(x => <option value={x.id} key={x.id}>{x.code} — {en ? x.nameEn || x.nameFa : x.nameFa}</option>)}
      </select>
    </FormField>
    {error ? <button type="button" className="button button--compact" disabled={disabled || loading} onClick={onRetry}>{t('trading.setup.retry')}</button> : <div className="trade-setup-picker__paging">
      <button type="button" className="button button--compact" disabled={disabled || loading || page <= 1} onClick={() => onPage(page - 1)}>{t('common.previous')}</button>
      <small>{t('trading.setup.lookupPage', { page, count: totalCount })}</small>
      <button type="button" className="button button--compact" disabled={disabled || loading || page * 25 >= totalCount} onClick={() => onPage(page + 1)}>{t('common.next')}</button>
    </div>}
  </div>
}
