import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CalendarRange, FileSpreadsheet, ReceiptText, Wallet, X } from 'lucide-react'
import { formatCashAmount } from './cashFlowUi'
import { formatBusinessDate, formatJalaliInputValue, parseJalaliDateInput, toAsciiDigits, toPersianDigits } from './i18n/businessDate'
import { FormField } from './ui'
import {
  addCalendarDays,
  buildSupplierLedger,
  summarizeSupplierLedger,
  supplierLedgerDateRange,
  type ResolvedSupplierBalance,
  type SupplierBalanceSource,
} from './supplierFinancialLedger'
import { buildXlsxWorkbook, downloadXlsxFile, supplierStatementSheet } from './supplierLedgerExport'

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
 * Persian UI uses Shamsi (Jalali) dates everywhere; the internal filter
 * values stay Gregorian ISO dates. Render it with `key={supplier.id}` so
 * date filters reset per supplier.
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
  const persian = !language.toLowerCase().startsWith('en')
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

  function changeRange(nextFrom: string, nextTo: string) {
    setPreset('CUSTOM')
    setFrom(nextFrom)
    setTo(nextTo)
  }

  function money(value: string) {
    return formatCashAmount(value, balance.decimalPrecision, language)
  }

  const totalLabel = `${money(balance.amount)}${balance.currencyCode ? ` ${balance.currencyCode}` : ''}`

  function exportStatement() {
    const sheet = supplierStatementSheet({
      sheetName: t('suppliers.ledger.exportSheet'),
      supplierName: supplier.name,
      supplierNumber: supplier.supplierNumber,
      currencyCode: balance.currencyCode,
      periodLabel: `${formatBusinessDate(from, language)} — ${formatBusinessDate(to, language)}`,
      totalBalance: balance.amount,
      openingBalance: summary.openingBalance,
      totalPayable: summary.totalPayable,
      totalPaid: summary.totalPaid,
      closingBalance: summary.closingBalance,
      entries: summary.rows,
      language,
      formatDate: iso => formatBusinessDate(iso, language),
      kindLabel: kind => t(`suppliers.ledger.types.${kind}`),
      labels: {
        supplier: t('suppliers.supplier'), number: t('common.number'), currency: t('suppliers.ledger.exportCurrency'),
        period: t('suppliers.ledger.exportPeriod'), totalBalance: t('suppliers.ledger.totalBalance'),
        openingBalance: t('suppliers.ledger.openingBalance'), totalPayable: t('suppliers.ledger.periodPayable'),
        totalPaid: t('suppliers.ledger.periodPaid'), closingBalance: t('suppliers.ledger.closingBalance'),
        date: t('suppliers.ledger.date'), projectCode: t('suppliers.ledger.exportProjectCode'),
        projectTitle: t('suppliers.ledger.project'), type: t('suppliers.ledger.type'),
        previousBalance: t('suppliers.ledger.previousBalance'), payable: t('suppliers.ledger.projectPayable'),
        paidToday: t('suppliers.ledger.paidToday'), balance: t('suppliers.ledger.balance'),
        periodTotal: t('suppliers.ledger.periodTotal'),
      },
    })
    downloadXlsxFile(`supplier-statement-${supplier.supplierNumber}-${from}_${to}.xlsx`, buildXlsxWorkbook([sheet]))
  }

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

      <div className="supplier-ledger-drawer__body">
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
            <LedgerDateField label={t('suppliers.ledger.fromDate')} value={from} persian={persian} onChange={value => changeRange(value, to)} />
            <LedgerDateField label={t('suppliers.ledger.toDate')} value={to} persian={persian} onChange={value => changeRange(from, value)} />
          </div>
          <div className="supplier-ledger-presets" role="group" aria-label={t('suppliers.ledger.quickRanges')}>
            <CalendarRange aria-hidden="true" />
            {PRESETS.map(item => <button key={item.id} type="button" className={`supplier-ledger-preset${preset === item.id ? ' is-active' : ''}`} aria-pressed={preset === item.id} onClick={() => applyPreset(item.id, item.days)}>{t(`suppliers.ledger.presets.${item.id}`)}</button>)}
          </div>
        </div>

        <div className="supplier-ledger-summary">
          <div className="supplier-ledger-summary__card"><span>{t('suppliers.ledger.openingBalance')}</span><strong dir="ltr" className={summary.openingBalance.startsWith('-') ? 'is-negative' : undefined}>{money(summary.openingBalance)}</strong></div>
          <div className="supplier-ledger-summary__card"><span>{t('suppliers.ledger.periodPayable')}</span><strong dir="ltr">{money(summary.totalPayable)}</strong></div>
          <div className="supplier-ledger-summary__card"><span>{t('suppliers.ledger.periodPaid')}</span><strong dir="ltr">{money(summary.totalPaid)}</strong></div>
          <div className="supplier-ledger-summary__card is-closing"><span>{t('suppliers.ledger.closingBalance')}</span><strong dir="ltr" className={summary.closingBalance.startsWith('-') ? 'is-negative' : undefined}>{money(summary.closingBalance)}{balance.currencyCode ? ` ${balance.currencyCode}` : ''}</strong></div>
        </div>

        <p className="supplier-ledger-formula">{t('suppliers.ledger.formula')}</p>

        <div className="supplier-ledger-table-wrap">
          <table className="ui-data-table supplier-ledger-table">
            <thead><tr>
              <th scope="col">{t('suppliers.ledger.date')}</th>
              <th scope="col">{t('suppliers.ledger.project')}</th>
              <th scope="col">{t('suppliers.ledger.previousBalance')}</th>
              <th scope="col">{t('suppliers.ledger.projectPayable')}</th>
              <th scope="col">{t('suppliers.ledger.paidToday')}</th>
              <th scope="col">{t('suppliers.ledger.balance')}</th>
            </tr></thead>
            <tbody>
              {summary.rows.length === 0
                ? <tr><td colSpan={6}><div className="supplier-ledger-empty"><strong>{t('suppliers.ledger.emptyTitle')}</strong><span>{t('suppliers.ledger.emptyDescription')}</span></div></td></tr>
                : summary.rows.map(entry => <tr key={entry.id}>
                  <td><span className="supplier-ledger-date" dir="ltr">{formatBusinessDate(entry.date, language)}</span></td>
                  <td className="supplier-ledger-project"><strong>{persian ? entry.project.titleFa : entry.project.titleEn}</strong><small><span className="ui-technical">{entry.project.code}</span><span className={`supplier-ledger-kind supplier-ledger-kind--${entry.kind.toLowerCase()}`}>{t(`suppliers.ledger.types.${entry.kind}`)}</span></small></td>
                  <td className="ui-number">{money(entry.previousBalance)}</td>
                  <td className="ui-number">{Number(entry.payableAmount) === 0 ? '—' : money(entry.payableAmount)}</td>
                  <td className="ui-number">{Number(entry.paidAmount) === 0 ? '—' : money(entry.paidAmount)}</td>
                  <td className={`ui-number supplier-ledger-running${entry.balance.startsWith('-') ? ' is-negative' : ''}`}>{money(entry.balance)}</td>
                </tr>)}
            </tbody>
            {summary.rows.length > 0 && <tfoot><tr>
              <td colSpan={3}>{t('suppliers.ledger.periodTotal')}</td>
              <td className="ui-number">{money(summary.totalPayable)}</td>
              <td className="ui-number">{money(summary.totalPaid)}</td>
              <td className={`ui-number supplier-ledger-running${summary.closingBalance.startsWith('-') ? ' is-negative' : ''}`}>{money(summary.closingBalance)}</td>
            </tr></tfoot>}
          </table>
        </div>

        <footer className="supplier-ledger-drawer__footer">
          <p>{t('suppliers.ledger.footerNote')}</p>
          <div className="supplier-ledger-drawer__footer-actions">
            <button type="button" className="button supplier-ledger-export-button" onClick={exportStatement} disabled={summary.rows.length === 0}><FileSpreadsheet aria-hidden="true" />{t('suppliers.ledger.exportExcel')}</button>
            <button type="button" className="button" onClick={onClose}>{t('common.close')}</button>
          </div>
        </footer>
      </div>
    </section>
  </div>
}

/**
 * Date filter field. In Persian UI this is a Shamsi (Jalali) text field that
 * displays Persian digits (۱۴۰۵/۰۶/۰۲) and converts to a Gregorian ISO date
 * on blur/Enter; in English UI it stays a native Gregorian date input.
 */
export function LedgerDateField({ label, value, persian, onChange }: { label: string; value: string; persian: boolean; onChange: (value: string) => void }) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState(() => (persian ? formatJalaliInputValue(value) : value))
  const [error, setError] = useState('')
  const [syncedValue, setSyncedValue] = useState(value)

  // Keep the field in sync when quick-range presets change the value
  // (React's adjust-state-during-render pattern; no effect needed).
  if (value !== syncedValue) {
    setSyncedValue(value)
    setDraft(persian ? formatJalaliInputValue(value) : value)
    setError('')
  }

  if (!persian) {
    return <FormField label={label}>
      <input type="date" dir="ltr" value={value} onChange={event => onChange(event.target.value)} />
    </FormField>
  }

  function commit() {
    const resolved = parseJalaliDateInput(draft)
    if (!resolved) { setError(t('suppliers.ledger.jalaliDateInvalid')); return }
    setError('')
    setDraft(formatJalaliInputValue(resolved))
    onChange(resolved)
  }

  return <FormField label={label} hint={t('suppliers.ledger.jalaliDateHint')} error={error || undefined}>
    <input type="text" inputMode="numeric" dir="ltr" className="supplier-ledger-jalali-input" placeholder={toPersianDigits('1405/06/02')} value={draft}
      onChange={event => { setDraft(formatPartialJalaliInput(event.target.value)); setError('') }}
      onBlur={commit}
      onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); commit() } }} />
  </FormField>
}

/** Formats up to 8 typed digits as a Persian-digit Jalali date: ۱۴۰۵/۰۶/۰۲ */
function formatPartialJalaliInput(value: string) {
  const digits = toAsciiDigits(value).replace(/[^0-9]/g, '').slice(0, 8)
  const groups = [digits.slice(0, 4), digits.slice(4, 6), digits.slice(6, 8)].filter(Boolean)
  return toPersianDigits(groups.join('/'))
}
