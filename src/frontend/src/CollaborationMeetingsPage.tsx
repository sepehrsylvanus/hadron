import {
  Ban,
  CalendarClock,
  Check,
  ClipboardCopy,
  DoorOpen,
  FileClock,
  LockKeyhole,
  MessageSquareText,
  MonitorUp,
  Plus,
  RefreshCw,
  Search,
  ShieldCheck,
  UnlockKeyhole,
  UserRoundCheck,
  UserRoundX,
  Users,
  Video,
} from 'lucide-react'
import { FormEvent, lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from './AuthContext'
import { collaborationRequest, jsonRequest, meetingRequest } from './collaborationMeetingApi'
import {
  buildGuestMeetingInviteUrl,
  buildMeetingDetailRoute,
  isMeetingCreateSubmitDisabled,
  meetingAdmissionTone,
  meetingAdmissionTranslationKey,
  meetingCreateHeaderKeys,
  meetingLifecycleActions,
  meetingListRoute,
  meetingLocalDateTimeToUtc,
  meetingUtcToLocalDateTime,
  meetingRoleTone,
  meetingRoleTranslationKey,
  meetingRequestErrorText,
  MeetingRequestGenerationGate,
  meetingStatusTone,
  meetingStatusTranslationKey,
  newMeetingRoute,
  shouldShowMeetingListFailure,
  type CreateMeetingRequest,
  type MeetingDetail,
  type MeetingGuestInviteResponse,
  type MeetingInternalParticipantInput,
  type MeetingJoinResponse,
  type MeetingListResponse,
  type MeetingParticipant,
  type MeetingPolicyInput,
  type MeetingReportResponse,
  type MeetingRole,
  type MeetingStatus,
  type UpdateMeetingRequest,
} from './collaborationMeetingSupport'
import { formatCollaborationTime, type ApiPage, type DirectoryUser } from './collaborationSupport'
import { formatBusinessDate } from './i18n/businessDate'
import { useMeetingCall } from './MeetingCallContext'
import { isPersianLanguage, normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport'
import {
  ConfirmationDialog,
  DataTable,
  Definition,
  DefinitionGrid,
  EmptyState,
  FilterBar,
  FormField,
  LoadingState,
  MessageStrip,
  PageHeader,
  Pagination,
  Section,
  StatusBadge,
} from './ui'

const MeetingPreJoin = lazy(() => import('./MeetingPreJoin'))

const defaultPolicy: MeetingPolicyInput = {
  attendeeMicrophone: true,
  attendeeCamera: true,
  attendeeScreenShare: false,
  guestMicrophone: false,
  guestCamera: false,
  guestScreenShare: false,
  chatEnabled: true,
  fileSharingEnabled: false,
  ephemeralWhiteboardEnabled: false,
}

type SelectedPerson = DirectoryUser & { role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'> }

type ResolvedMeetingDateTime = {
  jalaliDate: string
  localTime: string
  gregorianDate: string
  utcDateTimeUtc: string
  timeZoneId: 'Asia/Tehran' | string
}

function editableMeetingPolicy(policy: MeetingDetail['policy']): MeetingPolicyInput {
  return {
    attendeeMicrophone: policy.attendeeMicrophone,
    attendeeCamera: policy.attendeeCamera,
    attendeeScreenShare: policy.attendeeScreenShare,
    guestMicrophone: policy.guestMicrophone,
    guestCamera: policy.guestCamera,
    guestScreenShare: policy.guestScreenShare,
    chatEnabled: policy.chatEnabled,
    fileSharingEnabled: false,
    ephemeralWhiteboardEnabled: policy.ephemeralWhiteboardEnabled,
  }
}

export default function CollaborationMeetingsPage() {
  const location = useLocation()
  const { meetingId } = useParams()
  if (location.pathname === newMeetingRoute) return <MeetingCreatePage />
  if (meetingId) return <MeetingObjectPage meetingId={meetingId} />
  return <MeetingListPage />
}

function MeetingListPage() {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const { meetingRevision } = useMeetingCall()
  const [data, setData] = useState<MeetingListResponse>({ items: [], page: 1, pageSize: 25, totalCount: 0 })
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<MeetingStatus | ''>('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const requestGate = useRef(new MeetingRequestGenerationGate())
  const filtered = Boolean(search || status)

  const load = useCallback(async (page: number) => {
    const query = new URLSearchParams({ search, status, page: String(page), pageSize: '25' })
    const requestScope = requestGate.current.transition(query.toString())
    setLoading(true)
    setError('')
    try {
      const result = await meetingRequest<MeetingListResponse>(`?${query}`)
      if (requestGate.current.isCurrent(requestScope)) setData(result)
    } catch (exception) {
      if (requestGate.current.isCurrent(requestScope)) setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized')))
    } finally {
      if (requestGate.current.isCurrent(requestScope)) setLoading(false)
    }
  }, [search, status, t])

  useEffect(() => {
    const gate = requestGate.current
    const timer = window.setTimeout(() => void load(1), 220)
    return () => { window.clearTimeout(timer); gate.transition(null) }
  }, [load, meetingRevision])

  const listUnavailable = shouldShowMeetingListFailure(loading, error, data.items.length)
  function clearFilters() {
    setSearch('')
    setStatus('')
  }

  return <main className="collaboration-page collaboration-meetings">
    <PageHeader eyebrow={t('collaborationMeetings.eyebrow')} title={t('collaborationMeetings.title')} subtitle={t('collaborationMeetings.subtitle')} actions={has('collaboration.meeting.schedule') ? <Link className="button button--primary" to={newMeetingRoute}><Plus />{t('collaborationMeetings.newMeeting')}</Link> : undefined} />
    {error && <MessageStrip tone="critical"><span>{error}</span><button type="button" className="button button--quiet" onClick={() => void load(data.page)}>{t('common.retry')}</button></MessageStrip>}
    <FilterBar label={t('common.filterBar')}>
      <FormField label={t('collaborationMeetings.search')}><span className="meeting-search-field"><Search aria-hidden="true" /><input value={search} onChange={event => setSearch(event.target.value)} /></span></FormField>
      <FormField label={t('collaborationMeetings.statusFilter')}><select value={status} onChange={event => setStatus(event.target.value as MeetingStatus | '')}><option value="">{t('common.all')}</option>{(['SCHEDULED', 'LIVE', 'ENDED', 'CANCELLED'] as const).map(value => <option value={value} key={value}>{t(meetingStatusTranslationKey(value))}</option>)}</select></FormField>
    </FilterBar>
    <Section title={t('collaborationMeetings.meetingList')} description={t('collaborationMeetings.meetingListHint')} actions={<div className="meeting-row-actions">
        <span className="ui-count">{data.totalCount}</span>
        {filtered && <button type="button" className="button button--quiet" disabled={loading} onClick={clearFilters}>{t('collaborationMeetings.clearFilters')}</button>}
        <button type="button" className="button button--quiet" disabled={loading} onClick={() => void load(data.page)}><RefreshCw aria-hidden="true" />{t('common.refresh')}</button>
      </div>}>
      {listUnavailable ? <EmptyState title={t('collaborationMeetings.listUnavailable')} description={t('collaborationMeetings.listUnavailableHint')} compact /> : <>
        <DataTable headers={[t('collaborationMeetings.meetingTitle'), t('collaborationMeetings.schedule'), t('collaborationMeetings.participants'), t('collaborationMeetings.statusLabel'), t('common.actions')]} loading={loading} empty={!data.items.length} filtered={filtered} loadingLabel={t('common.loading')} emptyTitle={t('collaborationMeetings.noMeetings')} emptyDescription={t('collaborationMeetings.noMeetingsHint')} noResultTitle={t('common.noResultsTitle')} noResultDescription={t('common.noResultsDescription')}>
          {data.items.map(item => <tr key={item.id} className={item.status === 'LIVE' ? 'meeting-row--live' : undefined}>
            <td><Link className="meeting-title-link" to={buildMeetingDetailRoute(item.id)}><strong>{item.title}</strong><small dir="ltr">{item.id.slice(0, 8)}</small></Link></td>
            <td dir="ltr">{formatCollaborationTime(item.scheduledStartAtUtc ?? item.startedAtUtc, i18n.language)}</td>
            <td><span className="meeting-count"><Users aria-hidden="true" />{item.participantCount}{item.waitingCount > 0 && <em>{item.waitingCount}</em>}</span></td>
            <td><StatusBadge text={t(meetingStatusTranslationKey(item.status))} tone={meetingStatusTone(item.status)} />{item.isLocked && <small className="meeting-locked"><LockKeyhole />{t('collaborationMeetings.locked')}</small>}</td>
            <td><Link className="button button--quiet" to={buildMeetingDetailRoute(item.id)}>{t('collaborationMeetings.openMeeting')}</Link></td>
          </tr>)}
        </DataTable>
        <Pagination page={data.page} pageSize={data.pageSize} totalCount={data.totalCount} onPageChange={page => void load(page)} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.paginationSummary', { page: data.page, count: data.totalCount })} />
      </>}
    </Section>
  </main>
}

function MeetingCreatePage() {
  const { t, i18n } = useTranslation()
  const location = useLocation()
  const { user, has } = useAuth()
  const navigate = useNavigate()
  const persian = isPersianLanguage(i18n.resolvedLanguage ?? i18n.language)
  const canStartInstant = has('collaboration.meeting.start')
  const [mode, setMode] = useState<'SCHEDULED' | 'INSTANT'>(() => new URLSearchParams(location.search).get('mode') === 'instant' && canStartInstant ? 'INSTANT' : 'SCHEDULED')
  const [title, setTitle] = useState('')
  const [date, setDate] = useState('')
  const [resolvedDate, setResolvedDate] = useState('')
  const [time, setTime] = useState('09:00')
  const [duration, setDuration] = useState('60')
  const [policy, setPolicy] = useState<MeetingPolicyInput>(defaultPolicy)
  const [people, setPeople] = useState<ApiPage<DirectoryUser>>({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  const [selected, setSelected] = useState<Record<string, SelectedPerson>>({})
  const [peopleSearch, setPeopleSearch] = useState('')
  const [peopleLoading, setPeopleLoading] = useState(false)
  const [peopleError, setPeopleError] = useState('')
  const peopleRequestGate = useRef(new MeetingRequestGenerationGate())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [dateError, setDateError] = useState('')
  const createHeader = meetingCreateHeaderKeys(mode)

  const loadPeople = useCallback(async (page: number) => {
    const query = new URLSearchParams({ search: peopleSearch, page: String(page), pageSize: '20' })
    const requestScope = peopleRequestGate.current.transition(query.toString())
    setPeopleLoading(true)
    try {
      const result = await collaborationRequest<ApiPage<DirectoryUser>>(`/directory/users?${query}`)
      if (peopleRequestGate.current.isCurrent(requestScope)) {
        setPeople({ ...result, items: result.items.filter(person => person.id !== user.id) })
        setPeopleError('')
      }
    } catch (exception) {
      if (peopleRequestGate.current.isCurrent(requestScope)) setPeopleError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized')))
    } finally {
      if (peopleRequestGate.current.isCurrent(requestScope)) setPeopleLoading(false)
    }
  }, [peopleSearch, t, user.id])

  useEffect(() => {
    const gate = peopleRequestGate.current
    const timer = window.setTimeout(() => void loadPeople(1), 220)
    return () => { window.clearTimeout(timer); gate.transition(null) }
  }, [loadPeople])

  async function resolveDate() {
    setDateError('')
    if (!date) { setResolvedDate(''); return null }
    if (!persian) {
      const utcDateTimeUtc = meetingLocalDateTimeToUtc(`${date}T${time}`)
      setResolvedDate(date)
      return utcDateTimeUtc ? { gregorianDate: date, utcDateTimeUtc } : null
    }
    const normalized = normalizeJalaliDateInput(date)
    if (!normalized) { setDateError(t('collaborationMeetings.dateInvalid')); return null }
    try {
      const result = await meetingRequest<ResolvedMeetingDateTime>('/resolve-jalali-datetime', jsonRequest('POST', { jalaliDate: normalized, localTime: time }))
      setResolvedDate(result.gregorianDate)
      return result
    } catch (exception) { setDateError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); return null }
  }

  function togglePerson(person: DirectoryUser) {
    setSelected(current => {
      if (current[person.id]) { const next = { ...current }; delete next[person.id]; return next }
      return { ...current, [person.id]: { ...person, role: 'ATTENDEE' } }
    })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setDateError('')
    setBusy(true)
    try {
      let scheduledStartAtUtc: string | null = null
      let scheduledEndAtUtc: string | null = null
      if (mode === 'SCHEDULED') {
        const resolved = await resolveDate()
        const minutes = Number(duration)
        scheduledStartAtUtc = resolved?.utcDateTimeUtc ?? null
        if (!scheduledStartAtUtc || !Number.isInteger(minutes) || minutes < 15 || minutes > 480) {
          if (!scheduledStartAtUtc) setDateError(t('collaborationMeetings.dateInvalid'))
          return
        }
        scheduledEndAtUtc = new Date(new Date(scheduledStartAtUtc).getTime() + minutes * 60_000).toISOString()
      }
      const participants: MeetingInternalParticipantInput[] = Object.values(selected).map(person => ({ userId: person.id, role: person.role }))
      const request: CreateMeetingRequest = { title: title.trim(), scheduledStartAtUtc, scheduledEndAtUtc, participants, policy }
      const created = await meetingRequest<MeetingDetail>('', jsonRequest('POST', request))
      navigate(buildMeetingDetailRoute(created.id), { replace: true })
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))) }
    finally { setBusy(false) }
  }

  return <main className="collaboration-page collaboration-meetings">
    <PageHeader eyebrow={t('collaborationMeetings.eyebrow')} title={t(createHeader.title)} subtitle={t(createHeader.subtitle)} backTo={meetingListRoute} backLabel={t('collaborationMeetings.backToMeetings')} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <form className="meeting-create-form" onSubmit={event => void submit(event)}>
      <Section title={t('collaborationMeetings.meetingDetails')}>
        <div className="meeting-mode-selector" role="group" aria-label={t('collaborationMeetings.meetingDetails')}>
          <button type="button" className={mode === 'SCHEDULED' ? 'is-active' : ''} onClick={() => setMode('SCHEDULED')}><CalendarClock />{t('collaborationMeetings.newMeeting')}</button>
          {canStartInstant && <button type="button" className={mode === 'INSTANT' ? 'is-active' : ''} onClick={() => setMode('INSTANT')}><Video />{t('collaborationMeetings.instantMeeting')}</button>}
        </div>
        <div className="ui-form-grid meeting-details-grid">
          <FormField label={t('collaborationMeetings.titleLabel')} hint={t('collaborationMeetings.titleHint')} required><input value={title} maxLength={200} autoFocus required onChange={event => setTitle(event.target.value)} /></FormField>
          {mode === 'SCHEDULED' && <>
            <FormField label={t('collaborationMeetings.dateLabel')} hint={persian ? `${t('collaborationMeetings.dateJalaliHint')}${resolvedDate ? ` · ${formatBusinessDate(resolvedDate, 'en')}` : ''}` : undefined} error={dateError} required><input type={persian ? 'text' : 'date'} inputMode={persian ? 'numeric' : undefined} dir="ltr" value={date} placeholder={persian ? '14050618' : undefined} required onChange={event => { setDate(persian ? toAsciiDigits(event.target.value).replace(/[^0-9]/g, '').slice(0, 8) : event.target.value); setResolvedDate(''); setDateError('') }} onBlur={() => void resolveDate()} /></FormField>
            <FormField label={t('collaborationMeetings.timeLabel')} required><input type="time" dir="ltr" value={time} required onChange={event => setTime(event.target.value)} /></FormField>
            <FormField label={t('collaborationMeetings.durationLabel')} required><select value={duration} onChange={event => setDuration(event.target.value)}>{[30, 45, 60, 90, 120, 180, 240].map(value => <option value={value} key={value}>{t('collaborationMeetings.durationMinutes', { count: value })}</option>)}</select></FormField>
          </>}
        </div>
      </Section>

      <Section title={t('collaborationMeetings.participantSelection')} description={t('collaborationMeetings.participantSelectionHint')}>
        {peopleError && <MessageStrip tone="critical"><span>{peopleError}</span><button type="button" className="button button--quiet" onClick={() => void loadPeople(people.page)}>{t('common.retry')}</button></MessageStrip>}
        <div className="meeting-person-picker">
          <label className="meeting-search-field"><Search aria-hidden="true" /><input value={peopleSearch} placeholder={t('collaborationMeetings.searchPeople')} onChange={event => setPeopleSearch(event.target.value)} /></label>
          <span>{t('collaborationMeetings.selectedPeople', { count: Object.keys(selected).length })}</span>
          <div className="meeting-person-results" aria-busy={peopleLoading || undefined}>{peopleLoading ? <LoadingState label={t('common.loading')} compact /> : people.items.length === 0 ? <EmptyState title={t('collaborationMeetings.noPeople')} filtered={Boolean(peopleSearch)} compact /> : people.items.map(person => <label key={person.id} className={selected[person.id] ? 'is-selected' : ''}><input type="checkbox" checked={Boolean(selected[person.id])} onChange={() => togglePerson(person)} /><img src={person.photoUrl} alt="" /><span><strong>{person.fullName}</strong><small>{person.organizationalUnit}</small></span></label>)}</div>
          <div className="meeting-person-paging"><button type="button" className="button button--quiet" disabled={people.page <= 1} onClick={() => void loadPeople(people.page - 1)}>{t('collaborationMeetings.previousPeople')}</button><span>{people.page} / {Math.max(1, Math.ceil(people.totalCount / people.pageSize))}</span><button type="button" className="button button--quiet" disabled={people.page * people.pageSize >= people.totalCount} onClick={() => void loadPeople(people.page + 1)}>{t('collaborationMeetings.nextPeople')}</button></div>
        </div>
        {Object.values(selected).length > 0 && <div className="meeting-selected-people">{Object.values(selected).map(person => <div key={person.id}><span><strong>{person.fullName}</strong><small>{person.organizationalUnit}</small></span><select value={person.role} aria-label={`${t('collaborationMeetings.changeRole')}: ${person.fullName}`} onChange={event => setSelected(current => ({ ...current, [person.id]: { ...current[person.id], role: event.target.value as SelectedPerson['role'] } }))}><option value="ATTENDEE">{t(meetingRoleTranslationKey('ATTENDEE'))}</option><option value="PRESENTER">{t(meetingRoleTranslationKey('PRESENTER'))}</option><option value="COHOST">{t(meetingRoleTranslationKey('COHOST'))}</option></select></div>)}</div>}
      </Section>

      <MeetingPolicyEditor policy={policy} onChange={setPolicy} />
      <div className="meeting-create-actions"><button type="submit" className="button button--primary" disabled={isMeetingCreateSubmitDisabled({ busy, title, mode, date, resolvedDate, dateError })}>{busy ? t('collaborationMeetings.creating') : mode === 'INSTANT' ? t('collaborationMeetings.instantMeeting') : t('collaborationMeetings.scheduleMeeting')}</button><Link className="button" to={meetingListRoute}>{t('common.cancel')}</Link></div>
    </form>
  </main>
}

type EditableMeetingPerson = {
  userId: string
  displayName: string
  organizationalUnit?: string
  photoUrl?: string
  role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>
}

function MeetingEditForm({ meeting, onCancel, onSaved }: { meeting: MeetingDetail; onCancel: () => void; onSaved: (meeting: MeetingDetail) => void }) {
  const { t, i18n } = useTranslation()
  const persian = isPersianLanguage(i18n.resolvedLanguage ?? i18n.language)
  const localStart = meetingUtcToLocalDateTime(meeting.scheduledStartAtUtc ?? '')
  const gregorianDate = localStart?.slice(0, 10) ?? ''
  const initialDate = persian
    ? toAsciiDigits(formatBusinessDate(gregorianDate, 'fa-IR')).replace(/[^0-9]/g, '').slice(0, 8)
    : gregorianDate
  const initialMinutes = meeting.scheduledStartAtUtc && meeting.scheduledEndAtUtc
    ? Math.max(15, Math.round((new Date(meeting.scheduledEndAtUtc).getTime() - new Date(meeting.scheduledStartAtUtc).getTime()) / 60_000))
    : 60
  const [title, setTitle] = useState(meeting.title)
  const [date, setDate] = useState(initialDate)
  const [resolvedDate, setResolvedDate] = useState(gregorianDate)
  const [time, setTime] = useState(localStart?.slice(11, 16) ?? '09:00')
  const [duration, setDuration] = useState(String(initialMinutes))
  const [policy, setPolicy] = useState<MeetingPolicyInput>(() => editableMeetingPolicy(meeting.policy))
  const [selected, setSelected] = useState<Record<string, EditableMeetingPerson>>(() => Object.fromEntries(meeting.participants
    .filter(participant => participant.userId && participant.role !== 'HOST' && participant.role !== 'EXTERNAL_GUEST')
    .map(participant => [participant.userId!, {
      userId: participant.userId!, displayName: participant.displayName,
      role: participant.role as EditableMeetingPerson['role'],
    }])))
  const [people, setPeople] = useState<ApiPage<DirectoryUser>>({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  const [peopleSearch, setPeopleSearch] = useState('')
  const [peopleLoading, setPeopleLoading] = useState(false)
  const [peopleError, setPeopleError] = useState('')
  const peopleRequestGate = useRef(new MeetingRequestGenerationGate())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [dateError, setDateError] = useState('')

  const loadPeople = useCallback(async (page: number) => {
    const query = new URLSearchParams({ search: peopleSearch, page: String(page), pageSize: '20' })
    const requestScope = peopleRequestGate.current.transition(query.toString())
    setPeopleLoading(true)
    try {
      const result = await collaborationRequest<ApiPage<DirectoryUser>>(`/directory/users?${query}`)
      if (peopleRequestGate.current.isCurrent(requestScope)) {
        setPeople({ ...result, items: result.items.filter(person => person.id !== meeting.createdByUserId) })
        setPeopleError('')
      }
    } catch (exception) {
      if (peopleRequestGate.current.isCurrent(requestScope)) setPeopleError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized')))
    } finally {
      if (peopleRequestGate.current.isCurrent(requestScope)) setPeopleLoading(false)
    }
  }, [meeting.createdByUserId, peopleSearch, t])

  useEffect(() => {
    const gate = peopleRequestGate.current
    const timer = window.setTimeout(() => void loadPeople(1), 220)
    return () => { window.clearTimeout(timer); gate.transition(null) }
  }, [loadPeople])

  async function resolveDate() {
    setDateError('')
    if (!date) return null
    if (!persian) {
      const utcDateTimeUtc = meetingLocalDateTimeToUtc(`${date}T${time}`)
      setResolvedDate(date)
      return utcDateTimeUtc ? { gregorianDate: date, utcDateTimeUtc } : null
    }
    const normalized = normalizeJalaliDateInput(date)
    if (!normalized) { setDateError(t('collaborationMeetings.dateInvalid')); return null }
    try {
      const result = await meetingRequest<ResolvedMeetingDateTime>('/resolve-jalali-datetime', jsonRequest('POST', { jalaliDate: normalized, localTime: time }))
      setResolvedDate(result.gregorianDate)
      return result
    } catch (exception) { setDateError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); return null }
  }

  function togglePerson(person: DirectoryUser) {
    setSelected(current => {
      if (current[person.id]) { const next = { ...current }; delete next[person.id]; return next }
      return { ...current, [person.id]: { userId: person.id, displayName: person.fullName, organizationalUnit: person.organizationalUnit, photoUrl: person.photoUrl, role: 'ATTENDEE' } }
    })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const resolved = await resolveDate()
      const scheduledStartAtUtc = resolved?.utcDateTimeUtc ?? null
      const minutes = Number(duration)
      if (!scheduledStartAtUtc || !Number.isInteger(minutes) || minutes < 15 || minutes > 480) return
      const scheduledEndAtUtc = new Date(new Date(scheduledStartAtUtc).getTime() + minutes * 60_000).toISOString()
      const request: UpdateMeetingRequest = {
        title: title.trim(), scheduledStartAtUtc, scheduledEndAtUtc,
        participants: Object.values(selected).map(person => ({ userId: person.userId, role: person.role })),
        policy, expectedVersion: meeting.version,
      }
      const updated = await meetingRequest<MeetingDetail>(`/${meeting.id}`, jsonRequest('PUT', request))
      onSaved(updated)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))) }
    finally { setBusy(false) }
  }

  return <form className="meeting-edit-form" onSubmit={event => void submit(event)}>
    <Section title={t('collaborationMeetings.editTitle')} description={t('collaborationMeetings.editScheduledHint')}>
      {error && <MessageStrip tone="critical">{error}</MessageStrip>}
      <div className="ui-form-grid meeting-details-grid">
        <FormField label={t('collaborationMeetings.titleLabel')} required><input value={title} maxLength={200} required onChange={event => setTitle(event.target.value)} /></FormField>
        <FormField label={t('collaborationMeetings.dateLabel')} hint={persian && resolvedDate ? `${t('collaborationMeetings.dateJalaliHint')} · ${formatBusinessDate(resolvedDate, 'en')}` : undefined} error={dateError} required><input type={persian ? 'text' : 'date'} inputMode={persian ? 'numeric' : undefined} dir="ltr" value={date} placeholder={persian ? '14050618' : undefined} required onChange={event => { setDate(persian ? toAsciiDigits(event.target.value).replace(/[^0-9]/g, '').slice(0, 8) : event.target.value); setResolvedDate(''); setDateError('') }} onBlur={() => void resolveDate()} /></FormField>
        <FormField label={t('collaborationMeetings.timeLabel')} required><input type="time" dir="ltr" value={time} required onChange={event => setTime(event.target.value)} /></FormField>
        <FormField label={t('collaborationMeetings.durationLabel')} required><select value={duration} onChange={event => setDuration(event.target.value)}>{[30, 45, 60, 90, 120, 180, 240, initialMinutes].filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b).map(value => <option value={value} key={value}>{t('collaborationMeetings.durationMinutes', { count: value })}</option>)}</select></FormField>
      </div>
      {peopleError && <MessageStrip tone="critical"><span>{peopleError}</span><button type="button" className="button button--quiet" onClick={() => void loadPeople(people.page)}>{t('common.retry')}</button></MessageStrip>}
      <div className="meeting-person-picker meeting-person-picker--edit">
        <label className="meeting-search-field"><Search aria-hidden="true" /><input value={peopleSearch} placeholder={t('collaborationMeetings.searchPeople')} onChange={event => setPeopleSearch(event.target.value)} /></label>
        <span>{t('collaborationMeetings.selectedPeople', { count: Object.keys(selected).length })}</span>
        <div className="meeting-person-results" aria-busy={peopleLoading || undefined}>{peopleLoading ? <LoadingState label={t('common.loading')} compact /> : people.items.length === 0 ? <EmptyState title={t('collaborationMeetings.noPeople')} filtered={Boolean(peopleSearch)} compact /> : people.items.map(person => <label key={person.id} className={selected[person.id] ? 'is-selected' : ''}><input type="checkbox" checked={Boolean(selected[person.id])} onChange={() => togglePerson(person)} /><img src={person.photoUrl} alt="" /><span><strong>{person.fullName}</strong><small>{person.organizationalUnit}</small></span></label>)}</div>
      </div>
      <div className="meeting-selected-people">{Object.values(selected).map(person => <div key={person.userId}><span><strong>{person.displayName}</strong><small>{person.organizationalUnit}</small></span><select value={person.role} aria-label={`${t('collaborationMeetings.changeRole')}: ${person.displayName}`} onChange={event => setSelected(current => ({ ...current, [person.userId]: { ...current[person.userId], role: event.target.value as EditableMeetingPerson['role'] } }))}><option value="ATTENDEE">{t(meetingRoleTranslationKey('ATTENDEE'))}</option><option value="PRESENTER">{t(meetingRoleTranslationKey('PRESENTER'))}</option><option value="COHOST">{t(meetingRoleTranslationKey('COHOST'))}</option></select></div>)}</div>
    </Section>
    <MeetingPolicyEditor policy={policy} onChange={setPolicy} />
    <div className="meeting-create-actions"><button type="submit" className="button button--primary" disabled={busy || !title.trim()}>{busy ? t('collaborationMeetings.saving') : t('collaborationMeetings.saveChanges')}</button><button type="button" className="button" onClick={onCancel}>{t('common.cancel')}</button></div>
  </form>
}

function MeetingPolicyEditor({ policy, onChange }: { policy: MeetingPolicyInput; onChange: (policy: MeetingPolicyInput) => void }) {
  const { t } = useTranslation()
  const items: { key: keyof MeetingPolicyInput; label: string; icon: typeof Video }[] = [
    { key: 'attendeeMicrophone', label: t('collaborationMeetings.attendeeMicrophone'), icon: Video },
    { key: 'attendeeCamera', label: t('collaborationMeetings.attendeeCamera'), icon: Video },
    { key: 'attendeeScreenShare', label: t('collaborationMeetings.attendeeScreen'), icon: MonitorUp },
    { key: 'guestMicrophone', label: t('collaborationMeetings.guestMicrophone'), icon: Video },
    { key: 'guestCamera', label: t('collaborationMeetings.guestCamera'), icon: Video },
    { key: 'guestScreenShare', label: t('collaborationMeetings.guestScreen'), icon: MonitorUp },
    { key: 'chatEnabled', label: t('collaborationMeetings.chatEnabled'), icon: MessageSquareText },
    { key: 'ephemeralWhiteboardEnabled', label: t('collaborationMeetings.whiteboardEnabled'), icon: FileClock },
  ]
  return <Section title={t('collaborationMeetings.mediaPolicy')} description={t('collaborationMeetings.mediaPolicyHint')}>
    <div className="meeting-policy-editor">{items.map(item => { const Icon = item.icon; return <label key={item.key} className={policy[item.key] ? 'is-enabled' : ''}><input type="checkbox" checked={policy[item.key]} onChange={event => onChange({ ...policy, [item.key]: event.target.checked })} /><Icon aria-hidden="true" /><span>{item.label}</span><strong>{t(policy[item.key] ? 'collaborationMeetings.enabled' : 'collaborationMeetings.disabled')}</strong></label> })}</div>
    <MessageStrip tone="information"><span>{t('collaborationMeetings.meetingFilesConversationHint')}</span></MessageStrip>
    <MessageStrip tone="information"><strong>{t('collaborationMeetings.recordingUnavailable')}</strong><span>{t('collaborationMeetings.recordingUnavailableHint')}</span></MessageStrip>
  </Section>
}

function MeetingObjectPage({ meetingId }: { meetingId: string }) {
  const { t, i18n } = useTranslation()
  const { user, has } = useAuth()
  const { activeMeetingId, activateMeeting, restoreMeeting, meetingRevision } = useMeetingCall()
  const [meeting, setMeeting] = useState<MeetingDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyAction, setBusyAction] = useState('')
  const [confirm, setConfirm] = useState<'START' | 'CANCEL' | 'END' | null>(null)
  const [inviteName, setInviteName] = useState('')
  const [inviteExpiryHours, setInviteExpiryHours] = useState('24')
  const [inviteResult, setInviteResult] = useState<MeetingGuestInviteResponse | null>(null)
  const [inviteCopied, setInviteCopied] = useState(false)
  const [join, setJoin] = useState<MeetingJoinResponse | null>(null)
  const [prejoinOpen, setPrejoinOpen] = useState(false)
  const [report, setReport] = useState<MeetingReportResponse | null>(null)
  const [editOpen, setEditOpen] = useState(false)

  const load = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true)
    try { setMeeting(await meetingRequest<MeetingDetail>(`/${meetingId}`)); if (!quiet) setError('') }
    catch (exception) { if (!quiet) setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))) }
    finally { if (!quiet) setLoading(false) }
  }, [meetingId, t])

  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load, meetingRevision])
  useEffect(() => {
    if (!meeting || !['SCHEDULED', 'LIVE'].includes(meeting.status)) return
    const timer = window.setInterval(() => void load(true), activeMeetingId === meetingId ? 3000 : 7000)
    return () => window.clearInterval(timer)
  }, [activeMeetingId, load, meeting, meetingId])

  const currentParticipant = useMemo(() => meeting?.participants.find(participant => participant.userId === user.id) ?? null, [meeting, user.id])
  const lifecycle = meeting && currentParticipant ? meetingLifecycleActions(meeting, currentParticipant.role, currentParticipant.admissionStatus) : null
  async function meetingAction(action: 'start' | 'cancel' | 'lock' | 'unlock' | 'end') {
    if (!meeting) return
    setBusyAction(action)
    setError('')
    try {
      const result = await meetingRequest<MeetingDetail>(`/${meeting.id}/${action}`, jsonRequest('POST', { expectedVersion: meeting.version }))
      setMeeting(result)
      if (action === 'end') setJoin(null)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); await load(true) }
    finally { setBusyAction(''); setConfirm(null) }
  }

  async function participantAction(participant: MeetingParticipant, action: 'admit' | 'deny' | 'revoke') {
    if (!meeting) return
    setBusyAction(`${action}-${participant.id}`)
    try {
      const result = await meetingRequest<MeetingDetail>(`/${meeting.id}/participants/${participant.id}/${action}`, { method: 'POST' })
      setMeeting(result)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); await load(true) }
    finally { setBusyAction('') }
  }

  async function changeRole(participant: MeetingParticipant, role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>) {
    if (!meeting) return
    setBusyAction(`role-${participant.id}`)
    try {
      const result = await meetingRequest<MeetingDetail>(`/${meeting.id}/participants/${participant.id}/role`, jsonRequest('POST', { role, expectedVersion: participant.version }))
      setMeeting(result)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); await load(true) }
    finally { setBusyAction('') }
  }

  async function createInvite(event: FormEvent) {
    event.preventDefault()
    if (!meeting) return
    const expiryHours = Number(inviteExpiryHours)
    if (![1, 24, 168].includes(expiryHours)) { setError(t('collaborationMeetings.dateInvalid')); return }
    const expiresAtUtc = new Date(Date.now() + expiryHours * 60 * 60_000).toISOString()
    setBusyAction('invite')
    try {
      const result = await meetingRequest<MeetingGuestInviteResponse>(`/${meeting.id}/guest-invites`, jsonRequest('POST', { guestDisplayName: inviteName.trim(), expiresAtUtc }))
      setInviteResult(result); setInviteName(''); setInviteCopied(false); await load(true)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))) }
    finally { setBusyAction('') }
  }

  async function copyInvite() {
    if (!inviteResult) return
    const link = buildGuestMeetingInviteUrl(
      inviteResult.inviteToken,
      window.location.origin,
      import.meta.env.VITE_PUBLIC_APP_ORIGIN,
    )
    try { await navigator.clipboard.writeText(link); setInviteCopied(true) }
    catch { setError(link) }
  }

  async function requestJoin() {
    if (!meeting) return
    if (activeMeetingId === meeting.id) { restoreMeeting(); return }
    if (activeMeetingId) { setError(t('collaborationMeetings.anotherMeetingActive')); return }
    setBusyAction('join')
    try {
      const response = await meetingRequest<MeetingJoinResponse>(`/${meeting.id}/join`, { method: 'POST' })
      setJoin(response); setPrejoinOpen(true)
    } catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))); await load(true) }
    finally { setBusyAction('') }
  }

  async function loadReport() {
    setBusyAction('report')
    try { setReport(await meetingRequest<MeetingReportResponse>(`/${meetingId}/report`)) }
    catch (exception) { setError(meetingRequestErrorText(exception, t('collaborationMeetings.serviceUnavailable'), t('collaborationMeetings.unauthorized'))) }
    finally { setBusyAction('') }
  }

  if (loading) return <main className="collaboration-page collaboration-meetings"><PageHeader eyebrow={t('collaborationMeetings.eyebrow')} title={t('collaborationMeetings.title')} backTo={meetingListRoute} backLabel={t('collaborationMeetings.backToMeetings')} /><LoadingState label={t('common.loading')} /></main>
  if (!meeting) return <main className="collaboration-page"><PageHeader title={t('collaborationMeetings.title')} /><MessageStrip tone="critical">{error || t('collaborationMeetings.notFound')}</MessageStrip><Link to={meetingListRoute} className="button">{t('collaborationMeetings.backToMeetings')}</Link></main>

  const canStart = Boolean(lifecycle?.canStart && has('collaboration.meeting.start'))
  const canEdit = Boolean(lifecycle?.canEdit && has('collaboration.meeting.schedule'))
  const canJoin = Boolean(lifecycle?.canJoin && has('collaboration.meeting.join'))
  const canManage = Boolean(lifecycle?.canManageParticipants && has('collaboration.meeting.manage_participants'))
  const canInvite = Boolean(lifecycle?.canInviteGuest && has('collaboration.meeting.invite_guest'))
  const canEnd = Boolean(lifecycle?.canEnd && has('collaboration.meeting.end'))

  return <main className="collaboration-page collaboration-meetings">
    <PageHeader eyebrow={t('collaborationMeetings.eyebrow')} title={meeting.title} subtitle={`${meeting.createdByDisplayName} · ${formatCollaborationTime(meeting.scheduledStartAtUtc ?? meeting.startedAtUtc, i18n.language)}`} backTo={meetingListRoute} backLabel={t('collaborationMeetings.backToMeetings')} status={<><StatusBadge text={t(meetingStatusTranslationKey(meeting.status))} tone={meetingStatusTone(meeting.status)} />{meeting.isLocked && <StatusBadge text={t('collaborationMeetings.locked')} tone="warning" />}</>} actions={<div className="meeting-object-actions">{canEdit && <button type="button" className="button" onClick={() => setEditOpen(value => !value)}>{t('collaborationMeetings.editMeeting')}</button>}{canStart && <button type="button" className="button button--primary" onClick={() => setConfirm('START')}><Video />{t('collaborationMeetings.startMeeting')}</button>}{canJoin && <button type="button" className="button button--primary" disabled={busyAction === 'join'} onClick={() => void requestJoin()}><DoorOpen />{t(activeMeetingId === meeting.id ? 'collaborationMeetings.restoreMeeting' : 'collaborationMeetings.joinMeeting')}</button>}{lifecycle?.canLock && canManage && <button type="button" className="button" onClick={() => void meetingAction('lock')}><LockKeyhole />{t('collaborationMeetings.lockMeeting')}</button>}{lifecycle?.canUnlock && canManage && <button type="button" className="button" onClick={() => void meetingAction('unlock')}><UnlockKeyhole />{t('collaborationMeetings.unlockMeeting')}</button>}{canEnd && <button type="button" className="button button--danger" onClick={() => setConfirm('END')}>{t('collaborationMeetings.endMeeting')}</button>}{lifecycle?.canCancel && has('collaboration.meeting.schedule') && <button type="button" className="button button--danger" onClick={() => setConfirm('CANCEL')}><Ban />{t('collaborationMeetings.cancelMeeting')}</button>}</div>} />
    {error && <MessageStrip tone="critical"><span>{error}</span><button type="button" className="button button--quiet" onClick={() => void load()}>{t('common.retry')}</button></MessageStrip>}

    {editOpen && canEdit && <MeetingEditForm meeting={meeting} onCancel={() => setEditOpen(false)} onSaved={updated => { setMeeting(updated); setEditOpen(false) }} />}

    <Section title={t('collaborationMeetings.overview')}>
      <DefinitionGrid>
        <Definition label={t('collaborationMeetings.meetingNumber')} technical>{meeting.id}</Definition>
        <Definition label={t('collaborationMeetings.organizer')}>{meeting.createdByDisplayName}</Definition>
        <Definition label={t('collaborationMeetings.schedule')}>{formatCollaborationTime(meeting.scheduledStartAtUtc ?? meeting.startedAtUtc, i18n.language)}</Definition>
        <Definition label={t('collaborationMeetings.duration')}>{meeting.scheduledStartAtUtc && meeting.scheduledEndAtUtc ? t('collaborationMeetings.durationMinutes', { count: Math.round((new Date(meeting.scheduledEndAtUtc).getTime() - new Date(meeting.scheduledStartAtUtc).getTime()) / 60_000) }) : '—'}</Definition>
        <Definition label={t('collaborationMeetings.statusLabel')}><StatusBadge text={t(meetingStatusTranslationKey(meeting.status))} tone={meetingStatusTone(meeting.status)} /></Definition>
        <Definition label={t('collaborationMeetings.locked')}>{t(meeting.isLocked ? 'collaborationMeetings.locked' : 'collaborationMeetings.unlocked')}</Definition>
      </DefinitionGrid>
    </Section>

    <MeetingParticipantsSection meeting={meeting} canManage={canManage} canChangeRoles={Boolean(lifecycle?.canChangeRoles && canManage)} busyAction={busyAction} onAdmit={participant => participantAction(participant, 'admit')} onDeny={participant => participantAction(participant, 'deny')} onRevoke={participant => participantAction(participant, 'revoke')} onChangeRole={changeRole} />

    <Section title={t('collaborationMeetings.policySummary')} description={t('collaborationMeetings.mediaPolicyHint')}>
      <div className="meeting-policy-summary">{([
        ['attendeeMicrophone', t('collaborationMeetings.attendeeMicrophone')], ['attendeeCamera', t('collaborationMeetings.attendeeCamera')], ['attendeeScreenShare', t('collaborationMeetings.attendeeScreen')],
        ['guestMicrophone', t('collaborationMeetings.guestMicrophone')], ['guestCamera', t('collaborationMeetings.guestCamera')], ['guestScreenShare', t('collaborationMeetings.guestScreen')],
        ['chatEnabled', t('collaborationMeetings.chatEnabled')], ['fileSharingEnabled', t('collaborationMeetings.filesEnabled')], ['ephemeralWhiteboardEnabled', t('collaborationMeetings.whiteboardEnabled')],
      ] as const).map(([key, label]) => <div key={key}><span>{label}</span><StatusBadge text={t(meeting.policy[key] ? 'collaborationMeetings.enabled' : 'collaborationMeetings.disabled')} tone={meeting.policy[key] ? 'positive' : 'neutral'} /></div>)}</div>
      <MessageStrip tone="information"><span>{t('collaborationMeetings.meetingFilesConversationHint')}</span></MessageStrip>
      <MessageStrip tone="information"><strong>{t('collaborationMeetings.recordingUnavailable')}</strong><span>{t('collaborationMeetings.recordingUnavailableHint')}</span></MessageStrip>
    </Section>

    {canInvite && <Section title={t('collaborationMeetings.inviteGuests')} description={t('collaborationMeetings.inviteGuestsHint')}>
      <form className="meeting-invite-form" onSubmit={event => void createInvite(event)}><FormField label={t('collaborationMeetings.guestName')} required><input value={inviteName} maxLength={160} required onChange={event => setInviteName(event.target.value)} /></FormField><FormField label={t('collaborationMeetings.inviteExpiry')} hint={t('collaborationMeetings.inviteExpiryHint')} required><select value={inviteExpiryHours} onChange={event => setInviteExpiryHours(event.target.value)}><option value="1">{t('collaborationMeetings.expiryOneHour')}</option><option value="24">{t('collaborationMeetings.expiryOneDay')}</option><option value="168">{t('collaborationMeetings.expirySevenDays')}</option></select></FormField><button type="submit" className="button button--primary" disabled={busyAction === 'invite' || !inviteName.trim()}><Plus />{t('collaborationMeetings.createInvite')}</button></form>
      {inviteResult && <div className="meeting-invite-result" role="status"><ShieldCheck aria-hidden="true" /><div><strong>{t('collaborationMeetings.inviteCreated')}</strong><p>{inviteResult.participant.displayName} · {formatCollaborationTime(inviteResult.expiresAtUtc, i18n.language)}</p><small>{t('collaborationMeetings.inviteSecretHint')}</small></div><button type="button" className="button" onClick={() => void copyInvite()}>{inviteCopied ? <Check /> : <ClipboardCopy />}{t(inviteCopied ? 'collaborationMeetings.inviteCopied' : 'collaborationMeetings.copyInvite')}</button></div>}
    </Section>}

    {has('collaboration.meeting.report') && <Section title={t('collaborationMeetings.report')} actions={<button type="button" className="button" disabled={busyAction === 'report'} onClick={() => void loadReport()}><FileClock />{t('collaborationMeetings.openReport')}</button>}>
      {report ? <MeetingReport report={report} /> : <p className="meeting-section-hint">{t('collaborationMeetings.noAttendance')}</p>}
    </Section>}

    {meeting.conversationId && has('collaboration.workspace.view') && <Link className="button meeting-open-conversation" to={`/collaboration/messages/${meeting.conversationId}`}><MessageSquareText />{t('collaborationHub.openMessages')}</Link>}

    <ConfirmationDialog open={confirm !== null} title={t(confirm === 'START' ? 'collaborationMeetings.startConfirmTitle' : confirm === 'CANCEL' ? 'collaborationMeetings.cancelConfirmTitle' : 'collaborationMeetings.endConfirmTitle')} description={t(confirm === 'START' ? 'collaborationMeetings.startConfirmHint' : confirm === 'CANCEL' ? 'collaborationMeetings.cancelConfirmHint' : 'collaborationMeetings.endConfirmHint')} confirmLabel={t(confirm === 'START' ? 'collaborationMeetings.startMeeting' : confirm === 'CANCEL' ? 'collaborationMeetings.cancelMeeting' : 'collaborationMeetings.endMeeting')} cancelLabel={t('common.cancel')} tone={confirm === 'START' ? 'normal' : 'critical'} confirmDisabled={Boolean(busyAction)} onCancel={() => setConfirm(null)} onConfirm={() => void meetingAction(confirm === 'START' ? 'start' : confirm === 'CANCEL' ? 'cancel' : 'end')} />

    {prejoinOpen && join && <Suspense fallback={<div className="meeting-prejoin-overlay"><LoadingState label={t('common.loading')} /></div>}><MeetingPreJoin meetingTitle={join.meeting.title} displayName={join.participant.displayName} capabilities={join.capabilities} onCancel={() => { setPrejoinOpen(false); setJoin(null) }} onJoin={media => { const activated = activateMeeting(join, media); setPrejoinOpen(false); setJoin(null); if (!activated) setError(t('collaborationMeetings.anotherMeetingActive')) }} /></Suspense>}
  </main>
}

function MeetingParticipantsSection({ meeting, canManage, canChangeRoles, busyAction, onAdmit, onDeny, onRevoke, onChangeRole }: { meeting: MeetingDetail; canManage: boolean; canChangeRoles: boolean; busyAction: string; onAdmit: (participant: MeetingParticipant) => void; onDeny: (participant: MeetingParticipant) => void; onRevoke: (participant: MeetingParticipant) => void; onChangeRole: (participant: MeetingParticipant, role: Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>) => void }) {
  const { t } = useTranslation()
  return <Section title={t('collaborationMeetings.participants')} description={meeting.participants.some(participant => participant.admissionStatus === 'WAITING') ? t('collaborationMeetings.waitingRoomHint') : undefined}>
    <DataTable headers={[t('collaborationMeetings.participants'), t('collaborationMeetings.changeRole'), t('collaborationMeetings.statusLabel'), t('common.actions')]} loading={false} empty={!meeting.participants.length} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('collaborationMeetings.noPeople')} noResultTitle={t('common.noResultsTitle')}>
      {meeting.participants.map(participant => <tr key={participant.id} className={participant.admissionStatus === 'WAITING' ? 'meeting-participant--waiting' : undefined}><td><span className="meeting-participant-name"><span>{participant.displayName.trim().charAt(0).toUpperCase() || 'H'}</span><strong>{participant.displayName}</strong></span></td><td>{canChangeRoles && !['HOST', 'EXTERNAL_GUEST'].includes(participant.role) ? <select value={participant.role} disabled={busyAction === `role-${participant.id}`} aria-label={`${t('collaborationMeetings.changeRole')}: ${participant.displayName}`} onChange={event => onChangeRole(participant, event.target.value as Exclude<MeetingRole, 'HOST' | 'EXTERNAL_GUEST'>)}><option value="ATTENDEE">{t(meetingRoleTranslationKey('ATTENDEE'))}</option><option value="PRESENTER">{t(meetingRoleTranslationKey('PRESENTER'))}</option><option value="COHOST">{t(meetingRoleTranslationKey('COHOST'))}</option></select> : <StatusBadge text={t(meetingRoleTranslationKey(participant.role))} tone={meetingRoleTone(participant.role)} />}</td><td><StatusBadge text={t(meetingAdmissionTranslationKey(participant.admissionStatus))} tone={meetingAdmissionTone(participant.admissionStatus)} /></td><td>{canManage && <div className="meeting-row-actions">{participant.admissionStatus === 'WAITING' && <><button type="button" className="button button--primary" disabled={busyAction.endsWith(participant.id)} onClick={() => onAdmit(participant)}><UserRoundCheck />{t('collaborationMeetings.admit')}</button><button type="button" className="button" disabled={busyAction.endsWith(participant.id)} onClick={() => onDeny(participant)}><UserRoundX />{t('collaborationMeetings.deny')}</button></>}{participant.role !== 'HOST' && participant.admissionStatus === 'ADMITTED' && <button type="button" className="button button--quiet" disabled={busyAction.endsWith(participant.id)} onClick={() => onRevoke(participant)}>{t('collaborationMeetings.revoke')}</button>}</div>}</td></tr>)}
    </DataTable>
  </Section>
}

function MeetingReport({ report }: { report: MeetingReportResponse }) {
  const { t, i18n } = useTranslation()
  return <div className="meeting-report">
    <DataTable headers={[t('collaborationMeetings.participants'), t('collaborationMeetings.joinedAt'), t('collaborationMeetings.leftAt'), t('collaborationMeetings.duration'), t('collaborationMeetings.connectionSource')]} loading={false} empty={!report.participants.some(item => item.attendanceIntervals.length > 0)} filtered={false} loadingLabel={t('common.loading')} emptyTitle={t('collaborationMeetings.noAttendance')} noResultTitle={t('common.noResultsTitle')}>
      {report.participants.flatMap(item => item.attendanceIntervals.map(interval => { const seconds = Math.max(0, ((interval.disconnectedAtUtc ? new Date(interval.disconnectedAtUtc).getTime() : Date.now()) - new Date(interval.connectedAtUtc).getTime()) / 1000); return <tr key={interval.id}><td>{item.participant.displayName}</td><td dir="ltr">{formatCollaborationTime(interval.connectedAtUtc, i18n.language)}</td><td dir="ltr">{formatCollaborationTime(interval.disconnectedAtUtc, i18n.language)}</td><td>{t('collaborationMeetings.durationMinutes', { count: Math.round(seconds / 60) })}</td><td dir="ltr">{interval.source}</td></tr> }))}
    </DataTable>
  </div>
}
