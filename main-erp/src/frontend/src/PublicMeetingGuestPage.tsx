import {
  CalendarClock,
  CircleX,
  DoorOpen,
  Hourglass,
  Languages,
  LockKeyhole,
  ShieldCheck,
} from 'lucide-react'
import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import officialHadronLogo from './assets/brand/hadron-logo-red.svg'
import { guestMeetingRequest } from './collaborationMeetingApi'
import {
  extractMeetingInviteToken,
  guestMeetingStatusPollingProfile,
  isGuestAccessTicketExpired,
  meetingAdmissionTone,
  meetingAdmissionTranslationKey,
  meetingPollingDelayMs,
  meetingStatusTone,
  meetingStatusTranslationKey,
  shouldDiscardGuestMeetingTicket,
  type MeetingGuestPreviewResponse,
  type MeetingGuestRedeemResponse,
  type MeetingGuestJoinResponse,
  type MeetingGuestStatusResponse,
} from './collaborationMeetingSupport'
import { formatCollaborationTime } from './collaborationSupport'
import { GuestMeetingRequestGate, scrubGuestMeetingCredentialFromUrl } from './guestMeetingFlowSupport'
import { languageDirection, normalizeLanguage, type SupportedLanguage } from './i18n'
import { LoadingState, MessageStrip, StatusBadge } from './ui'
import type { MeetingJoinMediaSelection } from './meetingPreJoinSupport'

const MeetingPreJoin = lazy(() => import('./MeetingPreJoin'))
const CollaborationMeetingRoom = lazy(() => import('./CollaborationMeetingRoom'))

const guestTicketStorageKey = 'hadron.meeting.guest.ticket.v1'

type StoredGuestTicket = {
  accessTicket: string
  expiresAtUtc: string
}

type GuestStatusRefreshResult = 'SUCCESS' | 'RETRY' | 'TERMINAL'
type GuestErrorKey = 'collaborationMeetings.invitationExpired' | 'collaborationMeetings.serviceUnavailable' | 'collaborationMeetings.conflict'
type GuestRetryAction = 'PREVIEW' | 'REDEEM' | 'STATUS' | null

function readGuestTicket(): StoredGuestTicket | null {
  try {
    const value = JSON.parse(window.sessionStorage.getItem(guestTicketStorageKey) ?? 'null') as Partial<StoredGuestTicket> | null
    if (!value || typeof value.accessTicket !== 'string' || typeof value.expiresAtUtc !== 'string'
      || isGuestAccessTicketExpired(value.expiresAtUtc)) {
      window.sessionStorage.removeItem(guestTicketStorageKey)
      return null
    }
    return { accessTicket: value.accessTicket, expiresAtUtc: value.expiresAtUtc }
  } catch {
    window.sessionStorage.removeItem(guestTicketStorageKey)
    return null
  }
}

function saveGuestTicket(ticket: StoredGuestTicket) {
  window.sessionStorage.setItem(guestTicketStorageKey, JSON.stringify(ticket))
}

function clearGuestTicketIfCurrent(expectedAccessTicket: string) {
  try {
    const current = JSON.parse(window.sessionStorage.getItem(guestTicketStorageKey) ?? 'null') as Partial<StoredGuestTicket> | null
    if (current?.accessTicket === expectedAccessTicket) window.sessionStorage.removeItem(guestTicketStorageKey)
  } catch {
    window.sessionStorage.removeItem(guestTicketStorageKey)
  }
}

export default function PublicMeetingGuestPage() {
  const { t, i18n } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [inviteToken, setInviteToken] = useState<string | null>(null)
  const [ticket, setTicket] = useState<StoredGuestTicket | null>(null)
  const [preview, setPreview] = useState<MeetingGuestPreviewResponse | null>(null)
  const [status, setStatus] = useState<MeetingGuestStatusResponse | null>(null)
  const [join, setJoin] = useState<MeetingGuestJoinResponse | null>(null)
  const [initialMedia, setInitialMedia] = useState<MeetingJoinMediaSelection>({ microphone: false, camera: false })
  const [prejoinOpen, setPrejoinOpen] = useState(false)
  const [roomOpen, setRoomOpen] = useState(false)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [errorKey, setErrorKey] = useState<GuestErrorKey | null>(null)
  const [retryAction, setRetryAction] = useState<GuestRetryAction>(null)
  const [refreshNoticeKey, setRefreshNoticeKey] = useState<'collaborationMeetings.statusRefreshDelayed' | null>(null)
  const bootstrappedRef = useRef(false)
  const inviteTokenRef = useRef<string | null>(null)
  const ticketRef = useRef<StoredGuestTicket | null>(null)
  const requestGateRef = useRef(new GuestMeetingRequestGate())

  const selectInvitation = useCallback((token: string) => {
    requestGateRef.current.selectCredential('invite', token)
    inviteTokenRef.current = token
    ticketRef.current = null
    setInviteToken(token)
    setTicket(null)
    setPreview(null)
    setStatus(null)
    setJoin(null)
    setPrejoinOpen(false)
    setRoomOpen(false)
    setBusy(false)
    setErrorKey(null)
    setRetryAction(null)
    setRefreshNoticeKey(null)
  }, [setBusy, setErrorKey, setInviteToken, setJoin, setPrejoinOpen, setPreview, setRefreshNoticeKey, setRetryAction, setRoomOpen, setStatus, setTicket])

  const selectTicket = useCallback((value: StoredGuestTicket, persist = false) => {
    requestGateRef.current.selectCredential('ticket', value.accessTicket)
    inviteTokenRef.current = null
    ticketRef.current = value
    if (persist) saveGuestTicket(value)
    setInviteToken(null)
    setTicket(value)
    setJoin(null)
    setPrejoinOpen(false)
    setRoomOpen(false)
    setBusy(false)
    setErrorKey(null)
    setRetryAction(null)
    setRefreshNoticeKey(null)
  }, [setBusy, setErrorKey, setInviteToken, setJoin, setPrejoinOpen, setRefreshNoticeKey, setRetryAction, setRoomOpen, setTicket])

  const invalidateInvitation = useCallback((token: string) => {
    if (!requestGateRef.current.clearCredentialIfCurrent('invite', token)) return false
    inviteTokenRef.current = null
    setInviteToken(null)
    setPreview(null)
    setLoading(false)
    setErrorKey('collaborationMeetings.invitationExpired')
    setRetryAction(null)
    return true
  }, [setErrorKey, setInviteToken, setLoading, setPreview, setRetryAction])

  const invalidateTicket = useCallback((accessTicket: string) => {
    if (!requestGateRef.current.clearCredentialIfCurrent('ticket', accessTicket)) return false
    clearGuestTicketIfCurrent(accessTicket)
    ticketRef.current = null
    setTicket(null)
    setStatus(null)
    setJoin(null)
    setPrejoinOpen(false)
    setRoomOpen(false)
    setLoading(false)
    setRefreshNoticeKey(null)
    setErrorKey('collaborationMeetings.invitationExpired')
    setRetryAction(null)
    return true
  }, [setErrorKey, setJoin, setLoading, setPrejoinOpen, setRefreshNoticeKey, setRetryAction, setRoomOpen, setStatus, setTicket])

  const previewInvitation = useCallback(async (token: string) => {
    const requestToken = requestGateRef.current.begin('preview', 'invite', token)
    if (!requestToken) return
    setLoading(true)
    setErrorKey(null)
    setRetryAction(null)
    try {
      const result = await guestMeetingRequest<MeetingGuestPreviewResponse>('/preview', { inviteToken: token })
      if (!requestGateRef.current.isCurrent(requestToken)) return
      setPreview(result)
      setErrorKey(null)
      setRetryAction(null)
    } catch (exception) {
      if (!requestGateRef.current.isCurrent(requestToken)) return
      if (shouldDiscardGuestMeetingTicket(exception)) {
        invalidateInvitation(token)
      } else {
        setErrorKey('collaborationMeetings.serviceUnavailable')
        setRetryAction('PREVIEW')
      }
    } finally {
      if (requestGateRef.current.isCurrent(requestToken)) setLoading(false)
    }
  }, [invalidateInvitation, setErrorKey, setLoading, setPreview, setRetryAction])

  const refreshStatus = useCallback(async (
    value: StoredGuestTicket,
    quiet = false,
  ): Promise<GuestStatusRefreshResult> => {
    const requestToken = requestGateRef.current.begin('status', 'ticket', value.accessTicket)
    if (!requestToken) return 'TERMINAL'
    if (isGuestAccessTicketExpired(value.expiresAtUtc)) {
      invalidateTicket(value.accessTicket)
      return 'TERMINAL'
    }
    if (!quiet) setLoading(true)
    try {
      const next = await guestMeetingRequest<MeetingGuestStatusResponse>('/status', { accessTicket: value.accessTicket })
      if (!requestGateRef.current.isCurrent(requestToken)) return 'TERMINAL'
      setStatus(next)
      setPreview(next.meeting)
      setErrorKey(null)
      setRetryAction(null)
      setRefreshNoticeKey(null)
      if (next.participant.admissionStatus === 'DENIED' || next.participant.admissionStatus === 'REVOKED'
        || next.meeting.status === 'ENDED' || next.meeting.status === 'CANCELLED') {
        return 'TERMINAL'
      }
      return 'SUCCESS'
    } catch (exception) {
      if (!requestGateRef.current.isCurrent(requestToken)) return 'TERMINAL'
      if (shouldDiscardGuestMeetingTicket(exception)) {
        invalidateTicket(value.accessTicket)
        return 'TERMINAL'
      }
      setRefreshNoticeKey('collaborationMeetings.statusRefreshDelayed')
      setRetryAction('STATUS')
      return 'RETRY'
    } finally {
      if (!quiet && requestGateRef.current.isCurrent(requestToken)) setLoading(false)
    }
  }, [invalidateTicket, setErrorKey, setLoading, setPreview, setRefreshNoticeKey, setRetryAction, setStatus])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (bootstrappedRef.current) return
      bootstrappedRef.current = true
      const token = extractMeetingInviteToken(window.location.href)
      window.history.replaceState(window.history.state, '', scrubGuestMeetingCredentialFromUrl(window.location.href))
      const stored = readGuestTicket()
      if (token) {
        selectInvitation(token)
        void previewInvitation(token)
        return
      }
      if (stored) {
        selectTicket(stored)
        void refreshStatus(stored)
        return
      }
      setErrorKey('collaborationMeetings.invitationExpired')
      setLoading(false)
    }, 0)
    return () => window.clearTimeout(timer)
  }, [previewInvitation, refreshStatus, selectInvitation, selectTicket])

  useEffect(() => {
    if (!ticket || roomOpen) return
    const polledTicket = ticket
    let disposed = false
    let timer: number | undefined
    let inFlight = false
    let consecutiveFailures = 0

    function clearTimer() {
      if (timer !== undefined) window.clearTimeout(timer)
      timer = undefined
    }

    function canPoll() {
      return !disposed && document.visibilityState !== 'hidden'
    }

    function schedule() {
      clearTimer()
      if (!canPoll()) return
      timer = window.setTimeout(() => {
        timer = undefined
        void poll()
      }, meetingPollingDelayMs(guestMeetingStatusPollingProfile, consecutiveFailures, Math.random()))
    }

    async function poll() {
      if (!canPoll() || inFlight) return
      inFlight = true
      const result = await refreshStatus(polledTicket, true)
      inFlight = false
      if (disposed || result === 'TERMINAL') return
      consecutiveFailures = result === 'RETRY' ? Math.min(6, consecutiveFailures + 1) : 0
      schedule()
    }

    function refreshNow() {
      if (!canPoll() || inFlight) return
      clearTimer()
      void poll()
    }

    function handleVisibilityChange() {
      if (document.visibilityState === 'hidden') clearTimer()
      else refreshNow()
    }

    document.addEventListener('visibilitychange', handleVisibilityChange)
    window.addEventListener('focus', refreshNow)
    schedule()
    return () => {
      disposed = true
      clearTimer()
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      window.removeEventListener('focus', refreshNow)
    }
  }, [refreshStatus, roomOpen, ticket])

  async function redeemInvitation() {
    const token = inviteTokenRef.current
    if (!token) return
    const requestToken = requestGateRef.current.begin('redeem', 'invite', token)
    if (!requestToken) return
    setBusy(true)
    setErrorKey(null)
    setRetryAction(null)
    try {
      const result = await guestMeetingRequest<MeetingGuestRedeemResponse>('/redeem', { inviteToken: token })
      if (!requestGateRef.current.isCurrent(requestToken)) return
      const nextTicket = { accessTicket: result.accessTicket, expiresAtUtc: result.accessTicketExpiresAtUtc }
      selectTicket(nextTicket, true)
      setPreview(result.meeting)
      setStatus({ meeting: result.meeting, participant: result.participant, canJoin: false })
      setErrorKey(null)
      setRetryAction(null)
    } catch (exception) {
      if (!requestGateRef.current.isCurrent(requestToken)) return
      if (shouldDiscardGuestMeetingTicket(exception)) {
        invalidateInvitation(token)
      } else {
        setErrorKey('collaborationMeetings.serviceUnavailable')
        setRetryAction('REDEEM')
      }
    } finally {
      if (requestGateRef.current.isCurrent(requestToken)) setBusy(false)
    }
  }

  async function requestJoin() {
    const currentTicket = ticketRef.current
    if (!currentTicket) return
    const requestToken = requestGateRef.current.begin('join', 'ticket', currentTicket.accessTicket)
    if (!requestToken) return
    setBusy(true)
    setErrorKey(null)
    setRetryAction(null)
    try {
      const response = await guestMeetingRequest<MeetingGuestJoinResponse>('/join', { accessTicket: currentTicket.accessTicket })
      if (!requestGateRef.current.isCurrent(requestToken)) return
      setJoin(response)
      setPrejoinOpen(true)
    } catch (exception) {
      if (!requestGateRef.current.isCurrent(requestToken)) return
      if (shouldDiscardGuestMeetingTicket(exception)) {
        invalidateTicket(currentTicket.accessTicket)
        return
      }
      setErrorKey('collaborationMeetings.conflict')
      await refreshStatus(currentTicket, true)
    } finally {
      if (requestGateRef.current.isCurrent(requestToken)) setBusy(false)
    }
  }

  function retryGuestAction() {
    if (retryAction === 'PREVIEW' && inviteTokenRef.current) {
      void previewInvitation(inviteTokenRef.current)
    } else if (retryAction === 'REDEEM') {
      void redeemInvitation()
    } else if (retryAction === 'STATUS' && ticketRef.current) {
      void refreshStatus(ticketRef.current)
    }
  }

  const reportConnected = useCallback(async () => {
    if (ticket) await guestMeetingRequest('/connected', { accessTicket: ticket.accessTicket })
  }, [ticket])
  const reportDisconnected = useCallback(async () => {
    if (ticket) await guestMeetingRequest('/disconnected', { accessTicket: ticket.accessTicket })
  }, [ticket])

  const participant = status?.participant
  const meeting = status?.meeting ?? preview
  const terminal = meeting?.status === 'ENDED' || meeting?.status === 'CANCELLED'

  return <main className="meeting-guest-shell" dir={languageDirection(language)} lang={language}>
    <header className="meeting-guest-shell__header">
      <a href="/login" aria-label={t('productName')}><img src={officialHadronLogo} alt="" /></a>
      <label className="meeting-guest-language"><Languages aria-hidden="true" /><span>{t('language.label')}</span><select value={language} aria-label={t('language.selectAria')} onChange={event => void i18n.changeLanguage(event.target.value as SupportedLanguage)}><option value="fa-IR">{t('language.persian')}</option><option value="en">{t('language.english')}</option></select></label>
    </header>

    <section className="meeting-guest-card" aria-labelledby="guest-meeting-title">
      <div className="meeting-guest-card__trust"><ShieldCheck aria-hidden="true" /><span>{t('collaborationMeetings.publicEyebrow')}</span></div>
      {loading ? <LoadingState label={t('common.loading')} /> : <>
        <h1 id="guest-meeting-title">{meeting?.title ?? t('collaborationMeetings.publicTitle')}</h1>
        {meeting && <div className="meeting-guest-summary">
          <div><CalendarClock aria-hidden="true" /><span>{t('collaborationMeetings.schedule')}</span><strong dir="ltr">{formatCollaborationTime(meeting.scheduledStartAtUtc, i18n.language)}</strong></div>
          <div><LockKeyhole aria-hidden="true" /><span>{t('collaborationMeetings.statusLabel')}</span><StatusBadge text={t(meetingStatusTranslationKey(meeting.status))} tone={meetingStatusTone(meeting.status)} /></div>
          {participant && <div><DoorOpen aria-hidden="true" /><span>{participant.displayName}</span><StatusBadge text={t(meetingAdmissionTranslationKey(participant.admissionStatus))} tone={meetingAdmissionTone(participant.admissionStatus)} /></div>}
        </div>}
        {errorKey && <MessageStrip tone={errorKey === 'collaborationMeetings.serviceUnavailable' ? 'warning' : 'critical'}><span>{t(errorKey)}</span>{retryAction && <button type="button" className="button button--quiet" disabled={busy} onClick={retryGuestAction}>{t('common.retry')}</button>}</MessageStrip>}
        {refreshNoticeKey && <MessageStrip tone="information"><span>{t(refreshNoticeKey)}</span>{retryAction === 'STATUS' && <button type="button" className="button button--quiet" disabled={busy} onClick={retryGuestAction}>{t('common.retry')}</button>}</MessageStrip>}

        {inviteToken && preview && <div className="meeting-guest-state">
          <ShieldCheck aria-hidden="true" /><h2>{t('collaborationMeetings.publicInvitation')}</h2><p>{t('collaborationMeetings.publicInviteHint')}</p>
          <button type="button" className="button button--primary" disabled={busy} onClick={() => void redeemInvitation()}>{t('collaborationMeetings.acceptInvitation')}</button>
        </div>}

        {!errorKey && ticket && participant?.admissionStatus === 'WAITING' && <div className="meeting-guest-state" role="status" aria-live="polite">
          <Hourglass aria-hidden="true" /><h2>{t('collaborationMeetings.waitingTitle')}</h2><p>{t('collaborationMeetings.waitingHint')}</p><span className="meeting-guest-pulse" aria-hidden="true" />
        </div>}

        {!errorKey && ticket && participant?.admissionStatus === 'INVITED' && <div className="meeting-guest-state" role="status">
          <Hourglass aria-hidden="true" /><h2>{t('collaborationMeetings.meetingNotLive')}</h2><p>{t('collaborationMeetings.waitingHint')}</p>
        </div>}

        {!errorKey && ticket && participant?.admissionStatus === 'ADMITTED' && !terminal && <div className="meeting-guest-state meeting-guest-state--ready">
          <DoorOpen aria-hidden="true" /><h2>{status?.canJoin ? t('collaborationMeetings.joinNow') : t('collaborationMeetings.meetingNotLive')}</h2><p>{status?.canJoin ? t('collaborationMeetings.prejoinHint') : t('collaborationMeetings.waitingHint')}</p>
          {status?.canJoin && <button type="button" className="button button--primary" disabled={busy} onClick={() => void requestJoin()}>{t('collaborationMeetings.joinNow')}</button>}
        </div>}

        {!errorKey && participant?.admissionStatus === 'DENIED' && <div className="meeting-guest-state meeting-guest-state--blocked"><CircleX aria-hidden="true" /><h2>{t('collaborationMeetings.deniedTitle')}</h2></div>}
        {!errorKey && participant?.admissionStatus === 'REVOKED' && <div className="meeting-guest-state meeting-guest-state--blocked"><CircleX aria-hidden="true" /><h2>{t('collaborationMeetings.revokedTitle')}</h2></div>}
        {!errorKey && terminal && <div className="meeting-guest-state"><CircleX aria-hidden="true" /><h2>{t('collaborationMeetings.callEnded')}</h2></div>}
      </>}
      <footer><LockKeyhole aria-hidden="true" /><p>{t('collaborationMeetings.guestPrivacy')}</p><a href="/login">{t('collaborationMeetings.returnToSignIn')}</a></footer>
    </section>

    {prejoinOpen && join && <Suspense fallback={<div className="meeting-prejoin-overlay"><LoadingState label={t('common.loading')} /></div>}><MeetingPreJoin meetingTitle={join.meeting.title} displayName={join.participant.displayName} capabilities={join.capabilities} onCancel={() => { setPrejoinOpen(false); setJoin(null) }} onJoin={media => { setInitialMedia(media); setPrejoinOpen(false); setRoomOpen(true) }} /></Suspense>}
    {roomOpen && join && <Suspense fallback={<div className="meeting-room-overlay"><LoadingState label={t('collaborationMeetings.connecting')} /></div>}><CollaborationMeetingRoom join={join} initialMedia={initialMedia} onConnected={reportConnected} onDisconnected={reportDisconnected} onLeave={() => { setRoomOpen(false); setJoin(null); if (ticket) void refreshStatus(ticket, true) }} /></Suspense>}
  </main>
}
