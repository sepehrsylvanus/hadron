import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from './AuthContext'
import { tradingRequest } from './tradingApi'
import { tradingStatusOptions } from './tradingUiSupport'
import { orderStatuses } from './tradingOrderSupport'
import type { ManagedOrder, OrderPage, OrderProject } from './tradingOrderSupport'
import { FilterBar, FormField, MessageStrip, Pagination, Section } from './ui'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'
import { TradingOrderForm } from './TradingOrderForm'
import { TradingOrderPicker } from './TradingOrderPicker'
import { TradingOrdersListView } from './TradingOrdersListView'
import './tradingOrders.css'

function OrderStatusForm({ order, onDone, onCancel, onDirty, onBusy }: {
  order: ManagedOrder; onDone: () => void; onCancel: () => void; onDirty: (dirty: boolean) => void; onBusy: (busy: boolean) => void
}) {
  const { t } = useTranslation()
  const [target, setTarget] = useState('')
  const [reason, setReason] = useState('')
  const [correction, setCorrection] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const dirty = !!(target || reason || correction)
  useEffect(() => { onDirty(dirty); return () => onDirty(false) }, [dirty, onDirty])
  useEffect(() => { onBusy(saving); return () => onBusy(false) }, [saving, onBusy])
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (saving || !target || !reason.trim()) return
    setSaving(true); setError('')
    try {
      await tradingRequest(`/order-registrations/${order.id}/status`, 'POST', { status: target, expectedVersion: order.version, reason: reason.trim(), isCorrection: correction })
      onDone()
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }
  return <Section title={`${order.registrationNumber} — ${t('trading.changeOperationalStatus')}`}>
    <UnsavedChangesGuard when={dirty} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form className="trade-order-status" onSubmit={submit} aria-label={t('trading.changeOperationalStatus')}>
      <fieldset disabled={saving} className="trade-setup-fields"><div className="trade-setup-grid">
        <FormField label={t('trading.orders.currentStatus')}><input readOnly value={t(`trading.codes.${order.status}`)} /></FormField>
        <FormField label={t('trading.orders.nextStatus')} required><select name="targetStatus" required value={target} onChange={e => setTarget(e.target.value)}><option value="">{t('trading.setup.choose')}</option>{tradingStatusOptions('ORDER', order.status, correction).map(x => <option value={x} key={x}>{t(`trading.codes.${x}`)}</option>)}</select></FormField>
        <FormField label={t('common.reason')} required><textarea name="statusReason" required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></FormField>
        {order.status !== 'DRAFT' && <FormField label={t('trading.correction')}><label className="ui-check"><input type="checkbox" checked={correction} onChange={e => { setCorrection(e.target.checked); setTarget('') }} />{t('trading.correctionHint')}</label></FormField>}
      </div></fieldset>
      <div className="ui-form-actions"><button className="button button--primary" disabled={saving || !target || !reason.trim()}>{t(saving ? 'common.saving' : 'common.save')}</button><button className="button" type="button" disabled={saving} onClick={() => { if (!dirty || window.confirm(t('trading.unsavedConfirm'))) onCancel() }}>{t('common.cancel')}</button></div>
    </form>
  </Section>
}

// This component is mounted only with order-view permission; no unauthorized lookup requests.
function ProjectOrders({ project, onDirty, onBusy, onChanged }: { project: OrderProject; onDirty: (dirty: boolean) => void; onBusy: (busy: boolean) => void; onChanged?: () => void }) {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [data, setData] = useState<OrderPage<ManagedOrder> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [editor, setEditor] = useState<'create' | ManagedOrder | null>(null)
  const [statusOrder, setStatusOrder] = useState<ManagedOrder | null>(null)
  const [session, setSession] = useState(0)
  const formRegion = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!editor && !statusOrder) return
    formRegion.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    formRegion.current?.querySelector<HTMLInputElement>('input:not([readonly])')?.focus({ preventScroll: true })
  }, [editor, statusOrder, session])
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  useEffect(() => { onDirty(dirty); return () => onDirty(false) }, [dirty, onDirty])
  useEffect(() => { onBusy(busy); return () => onBusy(false) }, [busy, onBusy])
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLoading(true); setError('')
      const qs = new URLSearchParams({ page: String(page), pageSize: '25', search: search.trim(), status })
      tradingRequest<OrderPage<ManagedOrder>>(`/projects/${project.id}/order-registrations?${qs}`, 'GET', undefined, controller.signal)
        .then(result => { if (!controller.signal.aborted) setData(result) })
        .catch((e: Error) => { if (!controller.signal.aborted) setError(e.message) })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }, search ? 250 : 0)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [project.id, page, search, status, revision])
  const canManage = has('trading.order_registration.manage') && !['CLOSED', 'CANCELLED'].includes(project.status)
  const discard = () => !busy && (!dirty || window.confirm(t('trading.unsavedConfirm')))
  const saved = () => {
    setEditor(null); setStatusOrder(null); setMessage(t('trading.orders.saved')); setSearch(''); setStatus(''); setPage(1); setRevision(x => x + 1); onChanged?.()
  }
  return <div className="trade-orders-project">
    <Section title={`${project.projectNumber} — ${t('trading.orderRegistrations')}`} description={t('trading.orders.managementHint')} actions={<div className="ui-inline-actions">
      <button type="button" className="button" onClick={() => setRevision(x => x + 1)} disabled={loading || busy}>{t('common.refresh')}</button>
      {canManage && <button className="button button--primary" type="button" disabled={busy} onClick={() => { if (discard()) { setSession(x => x + 1); setStatusOrder(null); setEditor('create'); setMessage('') } }}>{t('trading.orders.create')}</button>}
    </div>}>
      {!canManage && <MessageStrip>{t(['CLOSED', 'CANCELLED'].includes(project.status) ? 'trading.orders.closedProject' : 'trading.orders.readOnly')}</MessageStrip>}
      {message && <p role="status" className="trade-order-message">{message}</p>}
      <div ref={formRegion} className="trade-order-editor">
        {canManage && editor && <TradingOrderForm key={`${editor === 'create' ? 'new' : editor.id}-${session}`} project={project} editing={editor === 'create' ? undefined : editor} onDirty={setDirty} onBusy={setBusy} onSaved={saved} onCancel={() => setEditor(null)} />}
        {canManage && statusOrder && <OrderStatusForm key={`${statusOrder.id}-${session}`} order={statusOrder} onDirty={setDirty} onBusy={setBusy} onDone={saved} onCancel={() => setStatusOrder(null)} />}
      </div>
      <FilterBar label={t('common.filters')}><FormField label={t('trading.orders.search')}><input type="search" maxLength={200} value={search} onChange={e => { setSearch(e.target.value); setPage(1) }} /></FormField>
        <FormField label={t('common.status')}><select value={status} onChange={e => { setStatus(e.target.value); setPage(1) }}><option value="">{t('common.all')}</option>{orderStatuses.map(x => <option key={x} value={x}>{t(`trading.codes.${x}`)}</option>)}</select></FormField></FilterBar>
      {error && <MessageStrip tone="critical">{error}<button type="button" className="button button--compact" onClick={() => setRevision(x => x + 1)}>{t('trading.setup.retry')}</button></MessageStrip>}
      <TradingOrdersListView items={data?.items ?? []} language={i18n.resolvedLanguage ?? i18n.language} loading={loading} filtered={!!(search || status)} canManage={canManage} busy={busy || loading || !!error}
        onEdit={x => { if (discard()) { setSession(v => v + 1); setStatusOrder(null); setEditor(x); setMessage('') } }} onStatus={x => { if (discard()) { setSession(v => v + 1); setEditor(null); setStatusOrder(x); setMessage('') } }} />
      {data && <Pagination page={page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={setPage} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.paginationSummary', { page, count: data.totalCount })} />}
    </Section>
  </div>
}

export function TradingOrdersWorkspace({ project: fixedProject, onChanged }: { project?: OrderProject; onChanged?: () => void }) {
  const { t } = useTranslation()
  const { has } = useAuth()
  const [project, setProject] = useState<OrderProject | null>(null)
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  if (!has('trading.order_registration.view')) return <MessageStrip>{t('trading.orders.noViewPermission')}</MessageStrip>
  const selected = fixedProject ?? project
  return <section className="trade-orders" aria-label={t('trading.orders.title')}>
    {!fixedProject && <Section title={t('trading.orders.selectProject')} description={t('trading.orders.projectHint')}><div className="trade-order-project-picker">
      <TradingOrderPicker mode="project" resource="/projects" value={project?.id ?? ''} disabled={busy} onSelect={x => {
        if (x?.id === project?.id) return true
        if (dirty && !window.confirm(t('trading.unsavedConfirm'))) return false
        setProject(x ? { id: x.id, projectNumber: x.projectNumber!, titleFa: x.titleFa!, titleEn: x.titleEn, status: x.status! } : null); return true
      }} />
    </div></Section>}
    {selected ? <ProjectOrders key={selected.id} project={selected} onDirty={setDirty} onBusy={setBusy} onChanged={onChanged} /> : <MessageStrip>{t('trading.orders.chooseProjectFirst')}</MessageStrip>}
  </section>
}
