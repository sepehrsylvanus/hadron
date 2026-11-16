import type { FormEvent, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { FormField, Section } from './ui'
import type { OrderProject, OrderValues } from './tradingOrderSupport'
import fields from './tradingOrderFields.json'
import './tradingProjectSetup.css'
import './tradingOrders.css'

export function TradingOrderView({ values, project, language, dateLanguage = language, editing = false, annotated = false, saving, ready,
  currencyPicker, onChange, onSubmit, onCancel }: {
  values: OrderValues; project: OrderProject; language: string; dateLanguage?: string; editing?: boolean; annotated?: boolean;
  saving: boolean; ready: boolean; currencyPicker: ReactNode;
  onChange: (key: keyof OrderValues, value: string) => void; onSubmit: (event: FormEvent) => void; onCancel: () => void
}) {
  const { t } = useTranslation()
  const en = language.startsWith('en')
  const dateEn = dateLanguage.startsWith('en')
  const label = (key: string) => t(`trading.orders.fields.${key}`)
  const input = (key: keyof OrderValues, maxLength?: number) => <input name={key} dir={key === 'registrationNumber' ? 'ltr' : undefined} required maxLength={maxLength} value={values[key]} onChange={e => onChange(key, e.target.value)} />
  return <form className="trade-order-form trade-setup ui-object-form" onSubmit={onSubmit} aria-label={t(editing ? 'trading.orders.edit' : 'trading.orders.create')}>
    <fieldset className="trade-setup-fields" disabled={saving}>
      <Section title={t(editing ? 'trading.orders.edit' : 'trading.orders.create')} description={t('trading.orders.formHint')}>
        <div className="trade-setup-grid">
          {fields.map(field => {
            let control: ReactNode
            switch (field.key) {
              case 'projectId': control = <input name="projectId" readOnly value={`${project.projectNumber} — ${en ? project.titleEn || project.titleFa : project.titleFa}`} />; break
              case 'registrationNumber': control = input('registrationNumber', 80); break
              case 'registrationDate': control = <input name="registrationDate" required type={dateEn ? 'date' : 'text'} dir="ltr" inputMode={dateEn ? undefined : 'numeric'} placeholder={dateEn ? undefined : 'YYYYMMDD'} maxLength={dateEn ? undefined : 10} value={values.registrationDate} onChange={e => onChange('registrationDate', e.target.value)} />; break
              case 'currencyId': control = currencyPicker; break
              case 'registeredAmount': control = <input name="registeredAmount" required inputMode="decimal" dir="ltr" maxLength={40} value={values.registeredAmount} onChange={e => onChange('registeredAmount', e.target.value)} />; break
              case 'status': control = <input name="status" readOnly value={t('trading.codes.DRAFT')} />; break
              case 'notes': control = <textarea name="notes" rows={4} maxLength={2000} value={values.notes} onChange={e => onChange('notes', e.target.value)} />; break
            }
            return <div key={field.key} data-order-field={field.key} className={`trade-setup-field${field.key === 'notes' ? ' trade-setup-field--wide' : ''}`}>
              {field.key === 'currencyId' ? control : <FormField label={label(field.key)} required={['registrationNumber', 'registrationDate', 'registeredAmount'].includes(field.key)}
                hint={field.key === 'registrationDate' ? t(dateEn ? 'trading.setup.dateHintEn' : 'trading.setup.dateHintFa') : field.key === 'registeredAmount' ? t('trading.orders.amountHint') : undefined}>{control}</FormField>}
              {annotated && <div className="trade-setup-binding"><span>ذخیرهٔ فعلی: <code dir="ltr">{field.actual}</code></span><span>مدل پیشنهادی: <code dir="ltr">{field.proposed}</code></span><small>{field.source}</small></div>}
            </div>
          })}
          {editing && <FormField className="trade-setup-field--wide" label={t('common.reason')} required>{input('reason', 1000)}{annotated && <code dir="ltr">trading.audit_events.Reason</code>}</FormField>}
        </div>
      </Section>
    </fieldset>
    <div className="ui-form-actions trade-setup-actions"><small>{t('trading.orders.scopeHint')}</small><div>
      <button className="button button--primary" type="submit" disabled={saving || !ready}>{t(saving ? 'common.saving' : editing ? 'common.save' : 'trading.orders.create')}</button>
      <button className="button" type="button" disabled={saving} onClick={onCancel}>{t('common.cancel')}</button>
    </div></div>
  </form>
}
