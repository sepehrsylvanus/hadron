import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { CalendarRange, FileSpreadsheet, ListChecks, ReceiptText, X } from 'lucide-react'
import { formatCashAmount } from './cashFlowUi'
import { formatBusinessDate } from './i18n/businessDate'
import { FormField, LoadingState, MessageStrip, PageHeader, Pagination } from './ui'
import SupplierFinancialLedgerDrawer, { LedgerDateField } from './SupplierFinancialLedgerDrawer'
import {
  addCalendarDays,
  buildCombinedSupplierLedger,
  resolveSupplierBalance,
  summarizeSupplierLedger,
  supplierLedgerDateRange,
  totalBalancesByCurrency,
  type SupplierBalanceSource,
  type SupplierLedgerKind,
  type SupplierWithBalance,
} from './supplierFinancialLedger'
import { buildXlsxWorkbook, downloadXlsxFile, selectedLedgerSheets } from './supplierLedgerExport'

type SupplierApiItem = SupplierBalanceSource & {
  name: string
  country: { id: string; code: string; nameFa: string; nameEn: string; isActive: boolean }
  isActive: boolean
}
type Preset = 'ALL' | 'DAYS_30' | 'DAYS_90' | 'DAYS_180' | 'CUSTOM'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const PAGE_SIZE = 25
const ledgerRowKey = (row: { supplierId: string; id: string }) => `${row.supplierId}::${row.id}`
const LEDGER_KINDS: SupplierLedgerKind[] = ['PAYABLE', 'PAYMENT', 'PAYABLE_AND_PAYMENT']
const PRESETS: Array<{ id: Exclude<Preset, 'CUSTOM'>; days: number | null }> = [
  { id: 'ALL', days: null },
  { id: 'DAYS_30', days: 30 },
  { id: 'DAYS_90', days: 90 },
  { id: 'DAYS_180', days: 180 },
]

/**
 * Overall financial balances page: every supplier's (fake/demo) ledger
 * merged into one big table, overall totals per currency, and a
 * per-supplier summary whose balances open the single-supplier statement.
 */
export default function SupplierFinancialBalancesPage() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const persian = !language.toLowerCase().startsWith('en')
  const [suppliers, setSuppliers] = useState<SupplierApiItem[]>([])
  const [balanceCurrencies, setBalanceCurrencies] = useState<Array<{ id: string; code: string; decimalPrecision?: number }>>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [currencyFilter, setCurrencyFilter] = useState('ALL')
  const [kindFilter, setKindFilter] = useState('ALL')
  const [preset, setPreset] = useState<Preset>('ALL')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [rangeReady, setRangeReady] = useState(false)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<SupplierWithBalance | null>(null)
  const [selectedKeys, setSelectedKeys] = useState<string[]>([])

  useEffect(() => {
    let active = true
    void (async () => {
      setLoading(true)
      try {
        const first = await fetchSuppliersPage(1)
        const pageCount = Math.max(1, Math.ceil(first.totalCount / 100))
        let all = [...first.items]
        for (let nextPage = 2; nextPage <= pageCount; nextPage++) {
          const next = await fetchSuppliersPage(nextPage)
          all = [...all, ...next.items]
        }
        if (active) setSuppliers(all)
      } catch (caught) {
        if (active) setError(caught instanceof Error ? caught.message : t('common.error'))
      } finally {
        if (active) setLoading(false)
      }
    })()
    return () => { active = false }
  }, [t])

  const needsBalanceCurrencies = suppliers.some(item => item.financialBalanceAmount != null && item.financialBalanceCurrencyId != null && !item.financialBalanceCurrency?.code && !item.financialBalanceCurrencyCode)
  useEffect(() => {
    if (!needsBalanceCurrencies || balanceCurrencies.length > 0) return
    let active = true
    void fetch(`${api}/reference-data/currencies?includeInactive=true`)
      .then(async response => { if (!response.ok) throw new Error(await problem(response)); return response.json() })
      .then(values => { if (active) setBalanceCurrencies(values) })
      .catch(() => undefined)
    return () => { active = false }
  }, [balanceCurrencies.length, needsBalanceCurrencies])

  const suppliersWithBalance = useMemo<SupplierWithBalance[]>(
    () => suppliers
      .map(supplier => ({ supplier, balance: resolveSupplierBalance(supplier, balanceCurrencies) }))
      .filter(item => !item.balance.isPending),
    [suppliers, balanceCurrencies],
  )
  const combinedRows = useMemo(() => buildCombinedSupplierLedger(suppliersWithBalance), [suppliersWithBalance])
  const fullRange = useMemo(() => supplierLedgerDateRange(combinedRows), [combinedRows])
  const currencyTotals = useMemo(() => totalBalancesByCurrency(suppliersWithBalance), [suppliersWithBalance])
  const currencyOptions = useMemo(
    () => [...new Set(suppliersWithBalance.map(item => item.balance.currencyCode).filter((code): code is string => Boolean(code)))].sort(),
    [suppliersWithBalance],
  )
  const anyFake = suppliersWithBalance.some(item => item.balance.isFake)

  // Default the filter to the full ledger range once data arrives
  // (React's adjust-state-during-render pattern; no effect needed).
  if (!rangeReady && fullRange.from && fullRange.to) {
    setRangeReady(true)
    setFrom(fullRange.from)
    setTo(fullRange.to)
  }

  const entriesBySupplier = useMemo(() => {
    const grouped = new Map<string, typeof combinedRows>()
    for (const row of combinedRows) {
      const rows = grouped.get(row.supplierId) ?? []
      rows.push(row)
      grouped.set(row.supplierId, rows)
    }
    return grouped
  }, [combinedRows])

  const queryText = search.trim().toLowerCase()
  const matchesSupplier = (name: string, number: string) =>
    !queryText || name.toLowerCase().includes(queryText) || number.toLowerCase().includes(queryText)

  const filteredRows = useMemo(
    () => combinedRows
      .filter(row => (!from || row.date >= from) && (!to || row.date <= to))
      .filter(row => currencyFilter === 'ALL' || row.currencyCode === currencyFilter)
      .filter(row => kindFilter === 'ALL' || row.kind === kindFilter)
      .filter(row => matchesSupplier(row.supplierName, row.supplierNumber) || row.project.code.toLowerCase().includes(queryText) || row.project.titleFa.includes(search.trim()) || row.project.titleEn.toLowerCase().includes(queryText))
      .sort((left, right) => right.date.localeCompare(left.date) || left.supplierName.localeCompare(right.supplierName)),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- matchesSupplier/queryText are derived from search state
    [combinedRows, from, to, currencyFilter, kindFilter, queryText],
  )
  const pageRows = filteredRows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const singleCurrency = filteredRows.length > 0 && filteredRows.every(row => row.currencyCode === filteredRows[0].currencyCode && row.decimalPrecision === filteredRows[0].decimalPrecision)
    ? filteredRows[0]
    : null

  // Multi-row selection for the combined ledger: sums of the ticked rows
  // (per currency) are shown separately from the overall totals.
  const rowsByKey = useMemo(() => new Map(combinedRows.map(row => [ledgerRowKey(row), row])), [combinedRows])
  const selectedRows = useMemo(
    () => selectedKeys.map(key => rowsByKey.get(key)).filter((row): row is (typeof combinedRows)[number] => Boolean(row)),
    [selectedKeys, rowsByKey],
  )
  const selectedTotals = useMemo(() => {
    const grouped = new Map<string, { currencyCode: string | null; decimalPrecision: number; payable: number; paid: number }>()
    for (const row of selectedRows) {
      const key = `${row.currencyCode ?? ''}|${row.decimalPrecision}`
      const group = grouped.get(key) ?? { currencyCode: row.currencyCode, decimalPrecision: row.decimalPrecision, payable: 0, paid: 0 }
      group.payable += Number(row.payableAmount)
      group.paid += Number(row.paidAmount)
      grouped.set(key, group)
    }
    return [...grouped.values()].sort((left, right) => (left.currencyCode ?? '').localeCompare(right.currencyCode ?? ''))
  }, [selectedRows])
  const pageSelectedCount = pageRows.filter(row => selectedKeys.includes(ledgerRowKey(row))).length
  const pageSelection = pageRows.length > 0 && pageSelectedCount === pageRows.length ? 'all' : pageSelectedCount > 0 ? 'some' : 'none'

  function toggleRow(row: (typeof combinedRows)[number], checked: boolean) {
    const key = ledgerRowKey(row)
    setSelectedKeys(previous => checked ? [...new Set([...previous, key])] : previous.filter(candidate => candidate !== key))
  }

  function togglePage(checked: boolean) {
    const keys = pageRows.map(ledgerRowKey)
    setSelectedKeys(previous => checked ? [...new Set([...previous, ...keys])] : previous.filter(candidate => !keys.includes(candidate)))
  }

  /** Selects every filtered row across all pages, not just the visible page. */
  function selectAllPages() {
    setSelectedKeys(filteredRows.map(ledgerRowKey))
  }

  const allFilteredSelected = filteredRows.length > 0 && filteredRows.every(row => selectedKeys.includes(ledgerRowKey(row)))

  function exportSelected() {
    const selected = combinedRows.filter(row => selectedKeys.includes(ledgerRowKey(row)))
    const sheets = selectedLedgerSheets({
      rowsSheetName: t('suppliers.balancesPage.exportRowsSheet'),
      summarySheetName: t('suppliers.balancesPage.exportSummarySheet'),
      rows: selected,
      language,
      formatDate: iso => formatBusinessDate(iso, language),
      kindLabel: kind => t(`suppliers.ledger.types.${kind}`),
      labels: {
        supplier: t('suppliers.supplier'), supplierNumber: t('common.number'),
        date: t('suppliers.ledger.date'), projectCode: t('suppliers.ledger.exportProjectCode'),
        projectTitle: t('suppliers.ledger.project'), type: t('suppliers.ledger.type'),
        currency: t('suppliers.balancesPage.currency'), previousBalance: t('suppliers.ledger.previousBalance'),
        payable: t('suppliers.ledger.projectPayable'), paidToday: t('suppliers.ledger.paidToday'),
        balance: t('suppliers.balancesPage.supplierBalanceAfter'), rowsCount: t('suppliers.balancesPage.exportRowsCount'),
        payableTotal: t('suppliers.balancesPage.selectedPayable'), paidTotal: t('suppliers.balancesPage.selectedPaid'),
        netTotal: t('suppliers.balancesPage.selectedNet'),
      },
    })
    downloadXlsxFile(`supplier-balances-selected-${selected.length}.xlsx`, buildXlsxWorkbook(sheets))
  }

  const perSupplierSummaries = useMemo(
    () => suppliersWithBalance
      .filter(item => (currencyFilter === 'ALL' || item.balance.currencyCode === currencyFilter) && matchesSupplier(item.supplier.name, item.supplier.supplierNumber))
      .map(item => {
        const entries = entriesBySupplier.get(item.supplier.id) ?? []
        const summary = summarizeSupplierLedger(entries, from, to, item.balance.decimalPrecision)
        return { ...item, entriesCount: summary.count, totalPayable: summary.totalPayable, totalPaid: summary.totalPaid, closingBalance: summary.closingBalance }
      })
      .sort((left, right) => Math.abs(Number(right.closingBalance)) - Math.abs(Number(left.closingBalance))),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- matchesSupplier/queryText are derived from search state
    [suppliersWithBalance, entriesBySupplier, from, to, currencyFilter, queryText],
  )

  function applyPreset(next: Exclude<Preset, 'CUSTOM'>, days: number | null) {
    setPreset(next)
    setPage(1)
    setTo(fullRange.to)
    setFrom(days == null ? fullRange.from : addCalendarDays(fullRange.to, -(days - 1)))
  }

  function changeRange(nextFrom: string, nextTo: string) {
    setPreset('CUSTOM')
    setPage(1)
    setFrom(nextFrom)
    setTo(nextTo)
  }

  function money(value: string, precision: number) {
    return formatCashAmount(value, precision, language)
  }

  function sumFiltered(pick: (row: (typeof filteredRows)[number]) => string, precision: number) {
    return filteredRows.reduce((total, row) => total + Number(pick(row)), 0).toFixed(precision)
  }

  return <main className="app-shell supplier-balances-page">
    <PageHeader title={t('suppliers.balancesPage.title')} subtitle={t('suppliers.balancesPage.subtitle')} actions={<Link className="button" to="/mrp/supply/suppliers">{t('suppliers.balancesPage.backToSuppliers')}</Link>} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {anyFake && !loading && <p className="supplier-ledger-demo-note" role="note">{t('suppliers.ledger.demoNotice')}</p>}

    {currencyTotals.length > 0 && <section className="supplier-balances-section">
      <div className="supplier-balances-section__header"><div><h2>{t('suppliers.balancesPage.currencyTotalsTitle')}</h2><p>{t('suppliers.balancesPage.currencyTotalsHint')}</p></div></div>
      <div className="supplier-ledger-summary supplier-balances-currency-grid">
        {currencyTotals.map(total => <div className="supplier-ledger-summary__card" key={total.currencyCode ?? 'none'}>
          <span>{t('suppliers.ledger.totalBalance')} · <bdi>{total.currencyCode ?? '—'}</bdi></span>
          <strong dir="ltr" className={total.totalAmount.startsWith('-') ? 'is-negative' : undefined}>{money(total.totalAmount, total.decimalPrecision)}{total.currencyCode ? ` ${total.currencyCode}` : ''}</strong>
          <small>{t('suppliers.balancesPage.suppliersInCurrency', { count: total.supplierCount })}{total.fakeCount > 0 ? ` · ${t('suppliers.ledger.demoBadge')}` : ''}</small>
        </div>)}
      </div>
    </section>}

    <div className="supplier-ledger-filters">
      <div className="supplier-ledger-filters__dates">
        <LedgerDateField label={t('suppliers.ledger.fromDate')} value={from} persian={persian} onChange={value => changeRange(value, to)} />
        <LedgerDateField label={t('suppliers.ledger.toDate')} value={to} persian={persian} onChange={value => changeRange(from, value)} />
      </div>
      <div className="supplier-balances-filters__row">
        <FormField label={t('common.search')}>
          <input value={search} placeholder={t('suppliers.balancesPage.searchPlaceholder')} onChange={event => { setSearch(event.target.value); setPage(1) }} />
        </FormField>
        <FormField label={t('suppliers.balancesPage.currency')}>
          <select value={currencyFilter} onChange={event => { setCurrencyFilter(event.target.value); setPage(1) }}>
            <option value="ALL">{t('suppliers.balancesPage.allCurrencies')}</option>
            {currencyOptions.map(code => <option key={code} value={code}>{code}</option>)}
          </select>
        </FormField>
        <FormField label={t('suppliers.ledger.type')}>
          <select value={kindFilter} onChange={event => { setKindFilter(event.target.value); setPage(1) }}>
            <option value="ALL">{t('suppliers.balancesPage.allTypes')}</option>
            {LEDGER_KINDS.map(kind => <option key={kind} value={kind}>{t(`suppliers.ledger.types.${kind}`)}</option>)}
          </select>
        </FormField>
      </div>
      <div className="supplier-ledger-presets" role="group" aria-label={t('suppliers.ledger.quickRanges')}>
        <CalendarRange aria-hidden="true" />
        {PRESETS.map(item => <button key={item.id} type="button" className={`supplier-ledger-preset${preset === item.id ? ' is-active' : ''}`} aria-pressed={preset === item.id} onClick={() => applyPreset(item.id, item.days)}>{t(`suppliers.ledger.presets.${item.id}`)}</button>)}
      </div>
    </div>

    <section className="supplier-balances-section">
      <div className="supplier-balances-section__header">
        <div><h2>{t('suppliers.balancesPage.combinedTitle')}</h2><p>{t('suppliers.balancesPage.combinedHint')}</p></div>
        <span className="ui-count">{t('suppliers.ledger.transactionsCount', { count: filteredRows.length })}</span>
      </div>
      {selectedRows.length > 0 && <div className="supplier-balances-selection" role="region" aria-label={t('suppliers.balancesPage.selectedCount', { count: selectedRows.length })}>
        <strong><ListChecks aria-hidden="true" />{t('suppliers.balancesPage.selectedCount', { count: selectedRows.length })}</strong>
        <div className="supplier-balances-selection__totals">
          {selectedTotals.map(total => <span className="supplier-balances-selection__group" key={`${total.currencyCode ?? 'none'}-${total.decimalPrecision}`}>
            <bdi>{total.currencyCode ?? '—'}</bdi>
            <span>{t('suppliers.balancesPage.selectedPayable')}: <span className="ui-number" dir="ltr">{money(total.payable.toFixed(total.decimalPrecision), total.decimalPrecision)}</span></span>
            <span>{t('suppliers.balancesPage.selectedPaid')}: <span className="ui-number" dir="ltr">{money(total.paid.toFixed(total.decimalPrecision), total.decimalPrecision)}</span></span>
            <span>{t('suppliers.balancesPage.selectedNet')}: <span className="ui-number" dir="ltr">{money((total.payable - total.paid).toFixed(total.decimalPrecision), total.decimalPrecision)}</span></span>
          </span>)}
        </div>
        <div className="supplier-balances-selection__actions">
          <button type="button" className="button" onClick={exportSelected}><FileSpreadsheet aria-hidden="true" />{t('suppliers.ledger.exportExcel')}</button>
          {!allFilteredSelected && <button type="button" className="button" onClick={selectAllPages}>{t('suppliers.balancesPage.selectAllFiltered', { count: filteredRows.length })}</button>}
          <button type="button" className="button" onClick={() => setSelectedKeys([])}><X aria-hidden="true" />{t('suppliers.balancesPage.clearSelection')}</button>
        </div>
      </div>}
      {pageSelection === 'all' && !allFilteredSelected && <div className="supplier-balances-select-all-note" role="note">
        <span>{t('suppliers.balancesPage.pageFullySelected', { pageCount: pageRows.length })}</span>
        <button type="button" className="supplier-balances-select-all-note__button" onClick={selectAllPages}>{t('suppliers.balancesPage.selectAllPages', { count: filteredRows.length })}</button>
      </div>}
      {allFilteredSelected && filteredRows.length > pageRows.length && <div className="supplier-balances-select-all-note" role="note">
        <span>{t('suppliers.balancesPage.allPagesSelected', { count: filteredRows.length })}</span>
      </div>}
      <div className="supplier-ledger-table-wrap">
        <table className="ui-data-table supplier-ledger-table supplier-balances-table">
          <thead><tr>
            <th scope="col" className="supplier-balances-select"><input type="checkbox" aria-label={t('suppliers.balancesPage.selectPage')} checked={pageSelection === 'all'} ref={node => { if (node) node.indeterminate = pageSelection === 'some' }} onChange={event => togglePage(event.target.checked)} /></th>
            <th scope="col">{t('suppliers.ledger.date')}</th>
            <th scope="col">{t('suppliers.supplier')}</th>
            <th scope="col">{t('suppliers.ledger.project')}</th>
            <th scope="col">{t('suppliers.balancesPage.currency')}</th>
            <th scope="col">{t('suppliers.ledger.previousBalance')}</th>
            <th scope="col">{t('suppliers.ledger.projectPayable')}</th>
            <th scope="col">{t('suppliers.ledger.paidToday')}</th>
            <th scope="col">{t('suppliers.balancesPage.supplierBalanceAfter')}</th>
          </tr></thead>
          <tbody>
            {loading
              ? <tr><td colSpan={9}><LoadingState label={t('common.loading')} compact /></td></tr>
              : pageRows.length === 0
                ? <tr><td colSpan={9}><div className="supplier-ledger-empty"><strong>{t('suppliers.ledger.emptyTitle')}</strong><span>{t('suppliers.ledger.emptyDescription')}</span></div></td></tr>
                : pageRows.map(row => <tr key={`${row.supplierId}-${row.id}`} className={selectedKeys.includes(ledgerRowKey(row)) ? 'is-selected' : undefined}>
                  <td className="supplier-balances-select"><input type="checkbox" aria-label={`${t('suppliers.balancesPage.selectRow')} — ${row.supplierName}`} checked={selectedKeys.includes(ledgerRowKey(row))} onChange={event => toggleRow(row, event.target.checked)} /></td>
                  <td><span className="supplier-ledger-date" dir="ltr">{formatBusinessDate(row.date, language)}</span></td>
                  <td className="supplier-balances-supplier"><Link to={`/mrp/suppliers/${row.supplierId}`}>{row.supplierName}</Link><small className="ui-technical">{row.supplierNumber}</small></td>
                  <td className="supplier-ledger-project"><strong>{persian ? row.project.titleFa : row.project.titleEn}</strong><small><span className="ui-technical">{row.project.code}</span><span className={`supplier-ledger-kind supplier-ledger-kind--${row.kind.toLowerCase()}`}>{t(`suppliers.ledger.types.${row.kind}`)}</span></small></td>
                  <td className="ui-technical">{row.currencyCode ?? '—'}</td>
                  <td className="ui-number">{money(row.previousBalance, row.decimalPrecision)}</td>
                  <td className="ui-number">{Number(row.payableAmount) === 0 ? '—' : money(row.payableAmount, row.decimalPrecision)}</td>
                  <td className="ui-number">{Number(row.paidAmount) === 0 ? '—' : money(row.paidAmount, row.decimalPrecision)}</td>
                  <td className={`ui-number supplier-ledger-running${row.balance.startsWith('-') ? ' is-negative' : ''}`}>{money(row.balance, row.decimalPrecision)}</td>
                </tr>)}
          </tbody>
          {singleCurrency && <tfoot><tr>
            <td colSpan={6}>{t('suppliers.ledger.periodTotal')} · <bdi>{singleCurrency.currencyCode ?? ''}</bdi></td>
            <td className="ui-number">{money(sumFiltered(row => row.payableAmount, singleCurrency.decimalPrecision), singleCurrency.decimalPrecision)}</td>
            <td className="ui-number">{money(sumFiltered(row => row.paidAmount, singleCurrency.decimalPrecision), singleCurrency.decimalPrecision)}</td>
            <td className="ui-number">—</td>
          </tr></tfoot>}
        </table>
      </div>
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={filteredRows.length} onPageChange={setPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.page', { page, pages: Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE)), count: filteredRows.length })} />
    </section>

    <section className="supplier-balances-section">
      <div className="supplier-balances-section__header">
        <div><h2>{t('suppliers.balancesPage.summaryTitle')}</h2><p>{t('suppliers.balancesPage.summaryHint')}</p></div>
        <span className="ui-count">{t('common.count', { count: perSupplierSummaries.length })}</span>
      </div>
      <div className="supplier-ledger-table-wrap">
        <table className="ui-data-table supplier-ledger-table supplier-balances-summary-table">
          <thead><tr>
            <th scope="col">{t('suppliers.supplier')}</th>
            <th scope="col">{t('suppliers.balancesPage.currency')}</th>
            <th scope="col">{t('suppliers.ledger.periodPayable')}</th>
            <th scope="col">{t('suppliers.ledger.periodPaid')}</th>
            <th scope="col">{t('suppliers.ledger.closingBalance')}</th>
            <th scope="col">{t('suppliers.ledger.totalBalance')}</th>
            <th scope="col">{t('suppliers.balancesPage.entriesCount')}</th>
            <th scope="col">{t('common.actions')}</th>
          </tr></thead>
          <tbody>
            {loading
              ? <tr><td colSpan={8}><LoadingState label={t('common.loading')} compact /></td></tr>
              : perSupplierSummaries.length === 0
                ? <tr><td colSpan={8}><div className="supplier-ledger-empty"><strong>{t('suppliers.ledger.emptyTitle')}</strong><span>{t('suppliers.ledger.emptyDescription')}</span></div></td></tr>
                : perSupplierSummaries.map(item => {
                  const balanceLabel = `${money(item.balance.amount, item.balance.decimalPrecision)}${item.balance.currencyCode ? ` ${item.balance.currencyCode}` : ''}`
                  return <tr key={item.supplier.id}>
                    <td className="supplier-balances-supplier"><Link to={`/mrp/suppliers/${item.supplier.id}`}>{item.supplier.name}</Link><small className="ui-technical">{item.supplier.supplierNumber}</small></td>
                    <td className="ui-technical">{item.balance.currencyCode ?? '—'}</td>
                    <td className="ui-number">{money(item.totalPayable, item.balance.decimalPrecision)}</td>
                    <td className="ui-number">{money(item.totalPaid, item.balance.decimalPrecision)}</td>
                    <td className={`ui-number supplier-ledger-running${item.closingBalance.startsWith('-') ? ' is-negative' : ''}`}>{money(item.closingBalance, item.balance.decimalPrecision)}</td>
                    <td className="ui-number"><button type="button" className="supplier-balance-button" onClick={() => setSelected(item)} aria-label={`${t('suppliers.ledger.viewDetails')}: ${item.supplier.name} — ${balanceLabel}`} title={t('suppliers.ledger.viewDetails')}><ReceiptText aria-hidden="true" /><span className="supplier-balance-button__amount" dir="ltr">{balanceLabel}</span>{item.balance.isFake && <span className="supplier-balance-button__badge">{t('suppliers.ledger.demoBadge')}</span>}</button></td>
                    <td className="ui-number">{item.entriesCount}</td>
                    <td className="actions"><Link className="button button--quiet" to={`/mrp/suppliers/${item.supplier.id}`}>{t('common.details')}</Link></td>
                  </tr>
                })}
          </tbody>
        </table>
      </div>
    </section>

    {selected && <SupplierFinancialLedgerDrawer key={selected.supplier.id} supplier={selected.supplier} balance={selected.balance} onClose={() => setSelected(null)} />}
  </main>
}

async function fetchSuppliersPage(page: number) {
  const query = new URLSearchParams({ includeInactive: 'true', page: String(page), pageSize: '100' })
  const response = await fetch(`${api}/suppliers?${query}`)
  if (!response.ok) throw new Error(await problem(response))
  return await response.json() as { items: SupplierApiItem[]; totalCount: number }
}

async function problem(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; code?: string; errors?: Record<string, string[]> } | null
  return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()].filter(Boolean).join(' — ') || `HTTP ${response.status}`
}
