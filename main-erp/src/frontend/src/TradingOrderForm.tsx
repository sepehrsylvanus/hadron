import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { canonicalTradingDate, tradingDateInput, tradingRequest } from './tradingApi'
import { emptyOrder, normalizeOrderAmount, orderPayload } from './tradingOrderSupport'
import type { ManagedOrder, OrderProject, OrderValues } from './tradingOrderSupport'
import { MessageStrip } from './ui'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'
import { TradingOrderView } from './TradingOrderView'
import { TradingOrderPicker } from './TradingOrderPicker'

export function TradingOrderForm({ project, editing, onSaved, onCancel, onDirty, onBusy }: {
  project: OrderProject; editing?: ManagedOrder; onSaved: () => void; onCancel: () => void;
  onDirty: (dirty: boolean) => void; onBusy: (busy: boolean) => void
}) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  // Keep the entered date's calendar stable if the shell language changes mid-draft.
  const [dateLanguage] = useState(language)
  const [initial] = useState<OrderValues>(() => editing ? { registrationNumber: editing.registrationNumber,
    registrationDate: tradingDateInput(editing.registrationDate, language), registeredAmount: editing.registeredAmount,
    currencyId: editing.currencyId, notes: editing.notes ?? '', reason: '' } : { ...emptyOrder })
  const [values, setValues] = useState(initial)
  const [ready, setReady] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const dirty = JSON.stringify(values) !== JSON.stringify(initial)
  useEffect(() => { onDirty(dirty); return () => onDirty(false) }, [dirty, onDirty])
  useEffect(() => { onBusy(saving); return () => onBusy(false) }, [saving, onBusy])
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (saving || !ready) return
    setSaving(true); setError('')
    try {
      if (!values.registrationNumber.trim()) throw new Error(t('trading.orders.numberRequired'))
      if (!normalizeOrderAmount(values.registeredAmount)) throw new Error(t('trading.orders.amountHint'))
      if (editing && !values.reason.trim()) throw new Error(t('trading.orders.reasonRequired'))
      const date = await canonicalTradingDate(values.registrationDate, dateLanguage, true, '/trading/order-registrations/resolve-jalali-date')
      await tradingRequest(editing ? `/order-registrations/${editing.id}` : '/order-registrations', editing ? 'PUT' : 'POST', orderPayload(values, project.id, date!, editing))
      onSaved()
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }
  return <>
    <UnsavedChangesGuard when={dirty} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <TradingOrderView values={values} project={project} language={language} dateLanguage={dateLanguage} editing={!!editing} saving={saving} ready={ready}
      onChange={(key, value) => setValues(x => ({ ...x, [key]: value }))} onSubmit={submit}
      onCancel={() => { if (!dirty || window.confirm(t('trading.unsavedConfirm'))) onCancel() }}
      currencyPicker={<TradingOrderPicker mode="currency" resource="/order-registrations/currencies" value={values.currencyId}
        existing={editing ? { id: editing.currencyId, code: editing.currencyCode, nameFa: editing.currencyNameFa, nameEn: editing.currencyNameEn } : undefined}
        onReady={setReady} disabled={saving} onSelect={x => setValues(v => ({ ...v, currencyId: x?.id ?? '' }))} />} />
  </>
}
