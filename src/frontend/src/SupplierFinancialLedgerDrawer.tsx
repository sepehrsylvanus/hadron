import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CalendarRange, ReceiptText, Wallet, X } from 'lucide-react'
import { formatCashAmount } from './cashFlowUi'
import { formatBusinessDate } from './i18n/businessDate'
import { FormField } from './ui'
import {
  addCalendarDays,
  buildSupplierLedger,
  summarizeSupplierLedger,
  supplierLedgerDateRange,
  type ResolvedSupplierBalance,
  type SupplierBalanceSource,
} from './supplierFinancialLedger'

type LedgerSupplier = SupplierBalanceSource & { name: string }
type Preset = 'ALL' | 'DAYS_30' | 'DAYS_90' | 'DAYS_180' | 'CUSTOM'

const PRESETS: Array<{ id: Exclude<Preset, 'CUSTOM'>; days: number | null }> = [
  { id: 'ALL', days: null },
  { id: 'DAYS_30', days: 30 },
  { id: 'DAYS_90', days: 90 },
  { id: 'DAYS_180', days: 180 },
]

/**
 * Modern slide-over statement for one supplier. All rows are generated
 * locally (fake/demo data) and always reconcile to the clicked total.
 * Render it with `key={supplier.id}` so date filters reset per supplier.
 */
export default function SupplierFinancialLedgerDrawer({
  supplier,
  balance,
  onClose,
}: {
  supplier: LedgerSupplier
  balance: ResolvedSupplierBalance
  onClose: () => void
}) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const titleId = useId()
  const drawerRef = useRef<HTMLElement>(null)
  const onCloseRef = useRef(onClose)
  useEffect(() => { onCloseRef.current = onClose }, [onClose])

  const entries = useMemo(() => buildSupplierLedger(supplier, balance), [supplier, balance])
  const fullRange = useMemo(() => supplierLedgerDateRange(entries), [entries])
  const [preset, setPreset] = useState<Preset>('ALL')
  const [from, setFrom] = useState(fullRange.from)
  const [to, setTo] = useState(fullRange.to)

  const summary = useMemo(
    () => summarizeSupplierLedger(entries, from, to, balance.decimalPrecision),
    [entries, from, to, balance.decimalPrecision],
  )

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const body = document.body
    const previousOverflow = body?.style.overflow ?? ''
    if (body) body.style.overflow = 'hidden'
    drawerRef.current?.querySelector<HTMLElement>('button:not([disabled])')?.focus()
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onCloseRef.current() }
    }
    document.addEventListener('keydown', key)
    return () => {
      document.removeEventListener('keydown', key)
      if (body) body.style.overflow = previousOverflow
      previous?.focus?.()
    }
  }, [])

  function applyPreset(next: Exclude<Preset, 'CUSTOM'>, days: number | null) {
    setPreset(next)
    setTo(fullRange.to)
    setFrom(days == null ? fullRange.from : addCalendarDays(fullRange.to, -(days - 1)))
  }

  function money(value: string) {
    return formatCashAmount(value, balance.decimalPrecision, language)
  }

  const totalLabel = `${money(balance.amount)}${balance.currencyCode ? ` ${balance.currencyCode}` : ''}`

  return <div className="supplier-ledger-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <section ref={drawerRef} className="supplier-ledger-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId}>
      <header className="supplier-ledger-drawer__header">
        <div className="supplier-ledger-drawer__identity">
          <p className="supplier-ledger-drawer__eyebrow"><ReceiptText aria-hidden="true" />{t('suppliers.ledger.title')}</p>
          <h2 id={titleId}>{supplier.name}</h2>
          <p className="supplier-ledger-drawer__meta">
            <span className="ui-technical">{supplier.supplierNumber}</span>
            {balance.currencyCode && <span className="ui-technical">{balance.currencyCode}</span>}
            <span>{t('suppliers.ledger.transactionsCount', { count: entries.length })}</span>
          </p>
        </div>
        <button type="button" className="button button--icon supplier-ledger-drawer__close" onClick={onClose} aria-label={t('common.close')} title={t('common.close')}><X aria-hidden="true" /></button>
      </header>

      <div className="supplier-ledger-hero">
        <div>
          <span className="supplier-ledger-hero__label"><Wallet aria-hidden="true" />{t('suppliers.ledger.totalBalance')}</span>
          <strong className={`supplier-ledger-hero__amount${balance.amount.startsWith('-') ? ' is-negative' : ''}`} dir="ltr">{totalLabel}</strong>
        </div>
        {balance.isFake && <span className="supplier-ledger-demo-badge">{t('suppliers.ledger.demoBadge')}</span>}
      </div>
      {balance.isFake && <p className="supplier-ledger-demo-note" role="note">{t('suppliers.ledger.demoNotice')}</p>}

      <div className="supplier-ledger-filters">
        <div className="supplier-ledger-filters__dates">
          <FormField label={t('suppliers.ledger.fromDate')}>
            <input type="date" dir="ltr" value={from} max={to || undefined} onChange={event => { setFrom(event.target.value); setPreset('CUSTOM') }} />
          </FormField>
          <FormField label={t('suppliers.ledger.toDate')}>
            <input type="date" dir="ltr" value={to} min={from || undefined} onChange={event => { setTo(event.target.value); setPreset('CUSTOM') }} />
          </FormField>
        </div>
        <div className="supplier-ledger-presets" role="group" aria-label={t('suppliers.ledger.quickRanges')}>
          <CalendarRange aria-hidden="true" />
          {PRESETS.map(item => <button key={item.id} type="button" className={`supplier-ledger-preset${preset === item.id ? ' is-active' : ''}`} aria-pressed={preset === item.id} onClick={() => applyPreset(item.id, item.days)}>{t(`suppliers.ledger.presets.${item.id}`)}</button>)}
        </div>
      </div>

      <dl className="supplier-ledger-summary">
        <div><dt>{t('suppliers.ledger.openingBalance')}</dt><dd dir="ltr" className={summary.openingBalance.startsWith('-') ? 'is-negative' : undefined}>{money(summary.openingBalance)}</dd></div>
        <div><dt>{t('suppliers.ledger.periodDebit')}</dt><dd dir="ltr">{money(summary.totalDebit)}</dd></div>
        <div><dt>{t('suppliers.ledger.periodCredit')}</dt><dd dir="ltr">{money(summary.totalCredit)}</dd></div>
        <div className="is-closing"><dt>{t('suppliers.ledger.closingBalance')}</dt><dd dir="ltr" className={summary.closingBalance.startsWith('-') ? 'is-negative' : undefined}>{money(summary.closingBalance)}{balance.currencyCode ? ` ${balance.currencyCode}` : ''}</dd></div>
      </dl>

      <div className="supplier-ledger-table-wrap">
        <table className="ui-data-table supplier-ledger-table">
          <thead><tr>
            <th scope="col">{t('suppliers.ledger.date')}</th>
            <th scope="col">{t('suppliers.ledger.document')}</th>
            <th scope="col">{t('suppliers.ledger.type')}</th>
            <th scope="col">{t('suppliers.ledger.description')}</th>
            <th scope="col">{t('suppliers.ledger.debit')}</th>
            <th scope="col">{t('suppliers.ledger.credit')}</th>
            <th scope="col">{t('suppliers.ledger.balance')}</th>
          </tr></thead>
          <tbody>
            {summary.rows.length === 0
              ? <tr><td colSpan={7}><div className="supplier-ledger-empty"><strong>{t('suppliers.ledger.emptyTitle')}</strong><span>{t('suppliers.ledger.emptyDescription')}</span></div></td></tr>
              : summary.rows.map(entry => <tr key={entry.id}>
                <td className="supplier-ledger-date"><span dir="ltr">{formatBusinessDate(entry.date, language)}</span><small dir="ltr">{entry.date}</small></td>
                <td className="ui-technical">{entry.reference}</td>
                <td><span className={`supplier-ledger-kind supplier-ledger-kind--${entry.kind.toLowerCase()}`}>{t(`suppliers.ledger.types.${entry.kind}`)}</span></td>
                <td className="supplier-ledger-description">{t(`suppliers.ledger.descriptions.${entry.kind}`)}</td>
                <td className="ui-number">{Number(entry.debit) === 0 ? '—' : money(entry.debit)}</td>
                <td className="ui-number">{Number(entry.credit) === 0 ? '—' : money(entry.credit)}</td>
                <td className={`ui-number supplier-ledger-running${entry.runningBalance.startsWith('-') ? ' is-negative' : ''}`}>{money(entry.runningBalance)}</td>
              </tr>)}
          </tbody>
          {summary.rows.length > 0 && <tfoot><tr>
            <td colSpan={4}>{t('suppliers.ledger.periodTotal')}</td>
            <td className="ui-number">{money(summary.totalDebit)}</td>
            <td className="ui-number">{money(summary.totalCredit)}</td>
            <td className={`ui-number supplier-ledger-running${summary.closingBalance.startsWith('-') ? ' is-negative' : ''}`}>{money(summary.closingBalance)}</td>
          </tr></tfoot>}
        </table>
      </div>

      <footer className="supplier-ledger-drawer__footer">
        <p>{t('suppliers.ledger.footerNote')}</p>
        <button type="button" className="button" onClick={onClose}>{t('common.close')}</button>
      </footer>
    </section>
  </div>
}
