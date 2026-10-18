import { FormEvent, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { toAsciiDigits } from './mrpRunUiSupport'
import { FormField, Section } from './ui'
import fieldDefinitions from './tradingProjectSetupFields.json'
type SetupFieldDefinition = { key: string; type: string; kind?: string; required?: boolean; wide?: boolean; maxLength?: number; actual: string; proposed: string; source: string }
const fields: { title: string; fields: SetupFieldDefinition[] }[] = fieldDefinitions
import './tradingProjectSetup.css'

export type SetupValues = { titleFa: string; startDate: string; customerId: string; businessCardId: string; responsibleUserId: string; notes: string }
export type SetupLookupItem = { id: string; code: string; nameFa: string; nameEn?: string }
export type SetupLookupKind = 'CUSTOMER' | 'BUSINESS_CARD' | 'ASSIGNEE'

export function SetupPickerView({ kind, label, required, value, query, items, page, totalCount, loading, error,
  onSelect, onSearch, onPage, onRetry, disabled = false }: {
  kind: SetupLookupKind; label: string; required?: boolean; value: string; query: string; items: SetupLookupItem[];
  page: number; totalCount: number; loading: boolean; error: string;
  onSelect: (value: string) => void; onSearch: (value: string) => void; onPage: (page: number) => void; onRetry: () => void; disabled?: boolean
}) {
  const { t, i18n } = useTranslation()
  const english = (i18n.resolvedLanguage ?? i18n.language).startsWith('en')
  return <div className="trade-setup-picker" data-picker={kind}>
    <FormField label={t('trading.setup.searchFor', { label })}>
      <input type="search" autoComplete="off" maxLength={200} value={query} disabled={disabled} onChange={e => onSearch(e.target.value)} />
    </FormField>
    <FormField label={label} required={required} error={error || undefined} hint={loading ? t('common.loading') : totalCount === 0 ? t('trading.setup.noOptions') : undefined}>
      <select name={kind === 'CUSTOMER' ? 'customerId' : kind === 'BUSINESS_CARD' ? 'businessCardId' : 'responsibleUserId'} value={value} required={required} disabled={disabled || (loading && !value)} aria-busy={loading} onChange={e => onSelect(e.target.value)}>
        <option value="">{t('trading.setup.choose')}</option>
        {items.map(item => <option key={item.id} value={item.id}>{item.code ? `${item.code} — ` : ''}{english ? item.nameEn || item.nameFa : item.nameFa}</option>)}
      </select>
    </FormField>
    {error ? <button className="button button--compact" type="button" disabled={loading || disabled} onClick={onRetry}>{t('trading.setup.retry')}</button> : <div className="trade-setup-picker__paging">
      <button className="button button--compact" type="button" disabled={page <= 1 || loading || disabled} onClick={() => onPage(page - 1)}>{t('common.previous')}</button>
      <small>{t('trading.setup.lookupPage', { page, count: totalCount })}</small>
      <button className="button button--compact" type="button" disabled={page * 25 >= totalCount || loading || disabled} onClick={() => onPage(page + 1)}>{t('common.next')}</button>
    </div>}
  </div>
}

// Both the production form and exported annotated HTML use this exact view and field definition.
export function TradingProjectSetupView({ values, language, saving, canSubmit = true, annotated = false,
  onChange, onSubmit, onCancel, renderLookup }: {
  values: SetupValues; language: string; saving: boolean; canSubmit?: boolean; annotated?: boolean;
  onChange: (key: keyof SetupValues, value: string) => void; onSubmit: (event: FormEvent) => void;
  onCancel: () => void;
  renderLookup: (kind: SetupLookupKind, label: string, required: boolean, value: string, onSelect: (value: string) => void) => ReactNode
}) {
  const { t } = useTranslation()
  const english = language.startsWith('en')
  return <form className="trade-setup ui-object-form" onSubmit={onSubmit} aria-label={t('trading.setup.formLabel')}>
    <fieldset disabled={saving} className="trade-setup-fields">
      {fields.map(group => <Section key={group.title} title={t(`trading.setup.sections.${group.title}`)}>
        <div className="trade-setup-grid">
          {group.fields.map(field => {
            const key = field.key as keyof SetupValues
            const label = t(`trading.setup.fields.${field.key}`)
            const ro = field.type === 'readonly'
            let control: ReactNode
            if (field.type === 'lookup') control = renderLookup(field.kind as SetupLookupKind, label, !!field.required, values[key], value => onChange(key, value))
            else control = <FormField label={label} required={!!field.required} hint={field.key === 'projectNumber' ? t('trading.generatedNumber') : field.type === 'date' ? t(english ? 'trading.setup.dateHintEn' : 'trading.setup.dateHintFa') : field.key === 'status' ? t('trading.setup.statusHint') : undefined}>
              {field.type === 'textarea'
                ? <textarea name={key} maxLength={field.maxLength} rows={4} value={values[key]} onChange={e => onChange(key, e.target.value)} />
                : <input name={field.key} required={!!field.required} readOnly={ro} type={field.type === 'date' && english ? 'date' : 'text'}
                    inputMode={field.type === 'date' && !english ? 'numeric' : undefined}
                    dir={field.type === 'date' ? 'ltr' : undefined}
                    maxLength={field.type === 'date' && !english ? 10 : field.maxLength}
                    placeholder={field.type === 'date' && !english ? 'YYYYMMDD' : undefined}
                    value={ro ? t(field.key === 'status' ? 'trading.codes.DRAFT' : 'trading.setup.numberAfterSave') : values[key]}
                    onChange={e => !ro && onChange(key, field.type === 'date' && !english ? toAsciiDigits(e.target.value).replace(/[^0-9/-]/g, '') : e.target.value)} />}
            </FormField>
            return <div className={`trade-setup-field${field.wide ? ' trade-setup-field--wide' : ''}`} key={field.key} data-setup-field={field.key}>
              {control}
              {annotated && <div className="trade-setup-binding"><span>ذخیرهٔ فعلی: <code dir="ltr">{field.actual}</code></span><span>مدل پیشنهادی: <code dir="ltr">{field.proposed}</code></span><small>{field.source}</small></div>}
            </div>
          })}
        </div>
      </Section>)}
    </fieldset>
    <div className="ui-form-actions trade-setup-actions">
      <small>{t('trading.setup.scopeHint')}</small>
      <div><button type="submit" className="button button--primary" disabled={saving || !canSubmit}>{saving ? t('common.saving') : t('trading.setup.create')}</button>
        <button type="button" className="button" disabled={saving} onClick={onCancel}>{t('common.cancel')}</button></div>
    </div>
  </form>
}
