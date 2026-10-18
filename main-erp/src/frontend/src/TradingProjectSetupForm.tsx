import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { canonicalTradingDate, tradingRequest } from './tradingApi'
import { emptySetup, projectSetupPayload } from './tradingProjectSetupSupport'
import { MessageStrip } from './ui'
import { UnsavedChangesGuard } from './ui/UnsavedChangesGuard'
import { SetupLookupItem, SetupLookupKind, SetupPickerView, TradingProjectSetupView } from './TradingProjectSetupView'

type LookupPage = { items: SetupLookupItem[]; page: number; pageSize: number; totalCount: number }

function SetupLookup({ kind, label, required, value, onSelect, onReady, disabled }: {
  kind: SetupLookupKind; label: string; required: boolean; value: string; onSelect: (value: string) => void;
  onReady: (kind: SetupLookupKind, ready: boolean) => void; disabled: boolean
}) {
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [retry, setRetry] = useState(0)
  const [data, setData] = useState<LookupPage | null>(null)
  const [selected, setSelected] = useState<SetupLookupItem | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setLoading(true); setError(''); onReady(kind, false)
      const qs = new URLSearchParams({ kind, search: query.trim(), page: String(page), pageSize: '25' })
      tradingRequest<LookupPage>(`/project-setup/lookups?${qs}`, 'GET', undefined, controller.signal).then(result => {
        if (controller.signal.aborted) return
        setData(result); onReady(kind, true)
      }).catch((e: Error) => { if (!controller.signal.aborted) { setError(e.message); onReady(kind, false) } })
        .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    }, query ? 250 : 0)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [kind, query, page, retry, onReady])
  const items = selected && selected.id === value && !data?.items.some(x => x.id === value)
    ? [selected, ...(data?.items ?? [])] : data?.items ?? []
  return <SetupPickerView kind={kind} label={label} required={required} value={value} query={query} items={items}
    page={page} totalCount={data?.totalCount ?? 0} loading={loading} error={error} disabled={disabled}
    onSearch={next => { setQuery(next); setPage(1) }} onPage={setPage} onRetry={() => setRetry(x => x + 1)}
    onSelect={next => { setSelected(items.find(x => x.id === next) ?? null); onSelect(next) }} />
}

export function TradingProjectSetupForm({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [form, setForm] = useState({ ...emptySetup })
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const [ready, setReady] = useState<Record<SetupLookupKind, boolean>>({ CUSTOMER: false, BUSINESS_CARD: false, ASSIGNEE: false })
  const onReady = useCallback((kind: SetupLookupKind, loaded: boolean) => setReady(current => ({ ...current, [kind]: loaded })), [])
  const dirty = JSON.stringify(form) !== JSON.stringify(emptySetup)
  const cancel = () => { if (!dirty || window.confirm(t('trading.unsavedConfirm'))) onCancel() }
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (saving || !Object.values(ready).every(Boolean)) return
    setSaving(true); setError('')
    try {
      if (!form.titleFa.trim()) throw new Error(t('trading.setup.titleRequired'))
      const startDate = await canonicalTradingDate(form.startDate, language, true, '/trading/project-setup/resolve-jalali-date')
      const result = await tradingRequest<{ project: { id: string } }>('/project-setup', 'POST', {
        ...projectSetupPayload(form, startDate!),
      })
      setSaved(true); onCreated(result.project.id)
    } catch (e) { setError((e as Error).message) } finally { setSaving(false) }
  }
  return <>
    <UnsavedChangesGuard when={dirty && !saved} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <TradingProjectSetupView values={form} language={language} saving={saving} canSubmit={Object.values(ready).every(Boolean)}
      onChange={(key, value) => setForm(current => ({ ...current, [key]: value }))} onSubmit={submit} onCancel={cancel}
      renderLookup={(kind, label, required, value, onSelect) => <SetupLookup key={kind} kind={kind} label={label} required={required}
        value={value} onSelect={onSelect} onReady={onReady} disabled={saving} />} />
  </>
}
