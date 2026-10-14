import { useState, useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { History, RotateCcw } from 'lucide-react'
import { formatBusinessDate } from './i18n/businessDate'
import { ConfirmationDialog, FormField, MessageStrip, Section, StatusBadge } from './ui'
import {
  DEMO_BOM_FAMILIES,
  activateBomRevision,
  activeRevisionId,
  getBomVersionSessionState,
  subscribeBomVersionSession,
  type BomVersionSessionState,
  type DemoBomFamily,
  type DemoBomRevision,
} from './bomVersionSession'

type PendingActivation = { family: DemoBomFamily; revision: DemoBomRevision }

/**
 * Demo panel on the BOM page: fake multi-version BOMs where a previous
 * version can be re-activated (the newer active one is deactivated). Every
 * activation asks for a mandatory reason in a confirmation modal. The state
 * lives in module memory only — nothing is sent to the backend and a page
 * refresh restores the default active versions.
 */
export default function BomVersionActivation() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const english = language.toLowerCase().startsWith('en')
  const session = useSyncExternalStore(subscribeBomVersionSession, getBomVersionSessionState, getBomVersionSessionState)
  const [pending, setPending] = useState<PendingActivation | null>(null)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState('')
  const [notice, setNotice] = useState('')

  const pendingFamily = pending?.family
  const pendingCurrent = pendingFamily
    ? pendingFamily.revisions.find(item => item.id === activeRevisionId(pendingFamily, session))
    : undefined
  const reasonValid = reason.trim().length > 0

  function familyName(family: DemoBomFamily) {
    return english ? family.nameEn : family.nameFa
  }

  function requestActivation(family: DemoBomFamily, revision: DemoBomRevision) {
    setPending({ family, revision })
    setReason('')
    setReasonError('')
  }

  function confirmActivation() {
    if (!pending) return
    const result = activateBomRevision(pending.family.id, pending.revision.id, reason)
    if (!result.ok) {
      if (result.error === 'REASON_REQUIRED') setReasonError(t('bom.versionActivation.reasonRequired'))
      return
    }
    const previous = pending.family.revisions.find(item => item.id === result.deactivatedRevisionId)
    setNotice(t('bom.versionActivation.activatedMessage', {
      target: pending.revision.code,
      family: familyName(pending.family),
      previous: previous?.code ?? '—',
    }))
    setPending(null)
    setReason('')
    setReasonError('')
  }

  return <Section
    title={t('bom.versionActivation.title')}
    description={t('bom.versionActivation.description')}
    actions={<span className="supplier-ledger-demo-badge">{t('bom.versionActivation.demoBadge')}</span>}
  >
    <div className="bom-version-panel">
      <p className="bom-version-session-note"><History aria-hidden="true" />{t('bom.versionActivation.sessionNote')}</p>
      {notice && <MessageStrip tone="positive">{notice}</MessageStrip>}
      <div className="bom-version-families">
        {DEMO_BOM_FAMILIES.map(family => (
          <FamilyVersions
            key={family.id}
            family={family}
            session={session}
            english={english}
            language={language}
            onRequestActivate={requestActivation}
          />
        ))}
      </div>
    </div>

    <ConfirmationDialog
      open={Boolean(pending)}
      title={pending ? t('bom.versionActivation.activateTitle', { code: pending.revision.code }) : ''}
      confirmLabel={t('bom.versionActivation.confirmActivate')}
      cancelLabel={t('common.cancel')}
      tone="normal"
      confirmDisabled={!reasonValid}
      onConfirm={confirmActivation}
      onCancel={() => setPending(null)}
    >
      {pending && pendingCurrent && <div className="bom-version-dialog">
        <MessageStrip tone="warning">
          {t('bom.versionActivation.warning', { target: pending.revision.code, current: pendingCurrent.code, family: familyName(pending.family) })}
        </MessageStrip>
        <FormField label={t('bom.versionActivation.reasonLabel')} error={reasonError || undefined}>
          <textarea
            className="bom-version-reason"
            rows={3}
            value={reason}
            placeholder={t('bom.versionActivation.reasonPlaceholder')}
            onChange={event => { setReason(event.target.value); setReasonError('') }}
          />
        </FormField>
        {!reasonValid && <p className="bom-version-reason-hint">{t('bom.versionActivation.reasonRequired')}</p>}
      </div>}
    </ConfirmationDialog>
  </Section>
}

function FamilyVersions({ family, session, english, language, onRequestActivate }: {
  family: DemoBomFamily
  session: BomVersionSessionState
  english: boolean
  language: string
  onRequestActivate: (family: DemoBomFamily, revision: DemoBomRevision) => void
}) {
  const { t } = useTranslation()
  const activeId = activeRevisionId(family, session)
  const active = family.revisions.find(item => item.id === activeId)
  const newestFirst = [...family.revisions].reverse()
  const familyLog = session.log.filter(record => record.familyId === family.id)
  const revisionCode = (id: string | null) => family.revisions.find(item => item.id === id)?.code ?? '—'
  const timeFormat = new Intl.DateTimeFormat(language, { hour: '2-digit', minute: '2-digit' })

  return <article className="bom-version-family">
    <header className="bom-version-family__header">
      <div className="bom-version-family__identity">
        <strong>{english ? family.nameEn : family.nameFa}</strong>
        <span><bdi className="ui-technical">{family.parentCode}</bdi> · {t(`bom.versionActivation.itemTypes.${family.itemType}`)}</span>
      </div>
      <span className="bom-version-family__active">{t('bom.versionActivation.currentActive')}: <bdi className="ui-technical">{active?.code}</bdi></span>
    </header>

    <ol className="bom-version-timeline">
      {newestFirst.map(item => {
        const isActive = item.id === activeId
        return <li key={item.id} className={`bom-version-entry${isActive ? ' is-active' : ''}`}>
          <span className="bom-version-entry__node" aria-hidden="true" />
          <div className="bom-version-entry__main">
            <div className="bom-version-entry__title">
              <strong className="ui-technical">{item.code}</strong>
              <StatusBadge text={t(isActive ? 'bom.versionActivation.active' : 'bom.versionActivation.inactive')} tone={isActive ? 'positive' : 'neutral'} />
            </div>
            <span className="bom-version-entry__meta">
              {english ? item.noteEn : item.noteFa} · {t('bom.lineCount', { count: item.lineCount })} · {t('bom.versionActivation.createdAt', { date: formatBusinessDate(item.createdAt, language) })}
            </span>
          </div>
          {!isActive && <button type="button" className="button button--quiet bom-version-activate" onClick={() => onRequestActivate(family, item)}>
            <RotateCcw aria-hidden="true" />{t('bom.versionActivation.activate')}
          </button>}
        </li>
      })}
    </ol>

    {familyLog.length > 0 && <div className="bom-version-log">
      <strong>{t('bom.versionActivation.changeLogTitle')}</strong>
      <ul>
        {familyLog.map(record => <li key={record.seq}>
          <span>{t('bom.versionActivation.changeLogEntry', { from: revisionCode(record.deactivatedRevisionId), to: revisionCode(record.activatedRevisionId) })}</span>
          <span className="bom-version-log__reason">{record.reason}</span>
          <time>{timeFormat.format(new Date(record.at))}</time>
        </li>)}
      </ul>
    </div>}
  </article>
}
