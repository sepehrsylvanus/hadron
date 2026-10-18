import { useTranslation } from 'react-i18next'
import { formatBusinessDate } from './i18n/businessDate'
import { DataTable, StatusBadge } from './ui'
import { formatOrderAmount } from './tradingOrderSupport'
import type { ManagedOrder } from './tradingOrderSupport'

export function TradingOrdersListView({ items, language, loading, filtered, canManage, busy = false, onEdit, onStatus }: {
  items: ManagedOrder[]; language: string; loading: boolean; filtered: boolean; canManage: boolean; busy?: boolean;
  onEdit: (order: ManagedOrder) => void; onStatus: (order: ManagedOrder) => void
}) {
  const { t } = useTranslation()
  return <DataTable headers={[t('trading.orders.fields.registrationNumber'), t('trading.orders.fields.registrationDate'), t('trading.orders.fields.registeredAmount'), t('trading.orders.fields.currencyId'), t('common.status'), t('common.actions')]}
    loading={loading} empty={!items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('trading.noOrders')} noResultTitle={t('common.noResultsTitle')}>
    <>{items.map(x => <tr key={x.id}><td><bdi>{x.registrationNumber}</bdi><small className="trade-order-note">{x.notes || '—'}</small></td>
      <td>{formatBusinessDate(x.registrationDate, language)}</td><td><bdi>{formatOrderAmount(x.registeredAmount, language)}</bdi></td><td><bdi>{x.currencyCode}</bdi></td>
      <td><StatusBadge text={t(`trading.codes.${x.status}`)} tone={x.status === 'CANCELLED' ? 'critical' : x.status === 'ACTIVE' ? 'positive' : 'neutral'} /></td>
      <td><div className="ui-inline-actions">{canManage && x.status === 'DRAFT' && <button className="button button--compact" type="button" disabled={busy} onClick={() => onEdit(x)}>{t('common.edit')}</button>}
        {canManage && x.status !== 'CANCELLED' && <button className="button button--compact" type="button" disabled={busy} onClick={() => onStatus(x)}>{t('trading.changeOperationalStatus')}</button>}{!canManage && '—'}</div></td>
    </tr>)}</>
  </DataTable>
}
