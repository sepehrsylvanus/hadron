import { lazy, ReactNode, Suspense, createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from './AuthContext'
import { jsonRequest, meetingRequest } from './collaborationMeetingApi'
import {
  activeMeetingDetailPollingProfile,
  meetingLifecycleActions,
  meetingPollingDelayMs,
  MeetingRequestGenerationGate,
  type MeetingDetail,
  type MeetingGuestInviteResponse,
  type MeetingJoinResponse,
  type MeetingParticipant,
} from './collaborationMeetingSupport'
import { ConfirmationDialog, LoadingState, MessageStrip } from './ui'
import type { MeetingJoinMediaSelection } from './meetingPreJoinSupport'

const CollaborationMeetingRoom = lazy(() => import('./CollaborationMeetingRoom'))

type ActiveMeeting = {
  join: MeetingJoinResponse
  media: MeetingJoinMediaSelection
}

type MeetingCallValue = {
  activeMeetingId: string | null
  meetingRevision: number
  activateMeeting: (join: MeetingJoinResponse, media: MeetingJoinMediaSelection) => boolean
  restoreMeeting: () => void
}

const MeetingCallContext = createContext<MeetingCallValue | null>(null)

// eslint-disable-next-line react-refresh/only-export-components
export function useMeetingCall() {
  const value = useContext(MeetingCallContext)
  if (!value) throw new Error('MeetingCallContext is not available')
  return value
}

export function MeetingCallProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const { user, has } = useAuth()
  const [active, setActive] = useState<ActiveMeeting | null>(null)
  const [detail, setDetail] = useState<MeetingDetail | null>(null)
  const [minimized, setMinimized] = useState(false)
  const [endConfirm, setEndConfirm] = useState(false)
  const [actionError, setActionError] = useState('')
  const [meetingRevision, setMeetingRevision] = useState(0)
  const requestGateRef = useRef(new MeetingRequestGenerationGate())
  const activeMeetingId = active?.join.meeting.id ?? null

  const transitionActive = useCallback((next: ActiveMeeting | null) => {
    requestGateRef.current.transition(next?.join.meeting.id ?? null, next?.join.meeting.version ?? null)
    setActive(next)
  }, [])

  const refresh = useCallback(async () => {
    if (!activeMeetingId) return 'INACTIVE' as const
    const scope = requestGateRef.current.capture()
    if (scope.key !== activeMeetingId) return 'STALE' as const
    try {
      const next = await meetingRequest<MeetingDetail>(`/${activeMeetingId}`)
      if (!requestGateRef.current.isCurrent(scope)) return 'STALE' as const
      if (!requestGateRef.current.tryCommitVersion(scope, next.id, next.version)) return 'SUCCESS' as const
      if (next.status === 'ENDED' || next.status === 'CANCELLED') {
        transitionActive(null)
        setDetail(null)
        setEndConfirm(false)
        return 'TERMINAL' as const
      }
      setDetail(next)
      return 'SUCCESS' as const
    } catch {
      if (!requestGateRef.current.isCurrent(scope)) return 'STALE' as const
      // A transient worklist refresh must not tear down an active media connection.
      return 'RETRY' as const
    }
  }, [activeMeetingId, transitionActive])

  useEffect(() => {
    if (!active) return
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
      }, meetingPollingDelayMs(activeMeetingDetailPollingProfile, consecutiveFailures, Math.random()))
    }

    async function poll() {
      if (!canPoll() || inFlight) return
      inFlight = true
      const result = await refresh()
      inFlight = false
      if (disposed || result === 'TERMINAL' || result === 'INACTIVE' || result === 'STALE') return
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

    const initial = window.setTimeout(refreshNow, 0)
    document.addEventListener('visibilitychange', handleVisibilityChange)
    window.addEventListener('focus', refreshNow)
    return () => {
      disposed = true
      window.clearTimeout(initial)
      clearTimer()
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      window.removeEventListener('focus', refreshNow)
    }
  }, [active, refresh])

  const currentMeeting = detail?.id === activeMeetingId ? detail : active?.join.meeting ?? null
  const participant = currentMeeting?.participants.find(item => item.userId === user.id) ?? null
  const lifecycle = currentMeeting && participant
    ? meetingLifecycleActions(currentMeeting, participant.role, participant.admissionStatus)
    : null
  const canManage = Boolean(lifecycle?.canManageParticipants && has('collaboration.meeting.manage_participants'))
  const canInvite = Boolean(lifecycle?.canInviteGuest && has('collaboration.meeting.invite_guest'))
  const canEnd = Boolean(lifecycle?.canEnd && has('collaboration.meeting.end'))
  const waiting = currentMeeting?.participants.filter(item => item.admissionStatus === 'WAITING') ?? []

  const value = useMemo<MeetingCallValue>(() => ({
    activeMeetingId,
    meetingRevision,
    activateMeeting: (join, media) => {
      const currentKey = requestGateRef.current.currentKey()
      if (currentKey && currentKey !== join.meeting.id) return false
      transitionActive({ join, media })
      setDetail(join.meeting)
      setMinimized(false)
      setActionError('')
      return true
    },
    restoreMeeting: () => setMinimized(false),
  }), [activeMeetingId, meetingRevision, transitionActive])

  const participantAction = useCallback(async (item: MeetingParticipant, action: 'admit' | 'deny') => {
    if (!activeMeetingId) return
    const scope = requestGateRef.current.capture()
    if (scope.key !== activeMeetingId) return
    setActionError('')
    try {
      const next = await meetingRequest<MeetingDetail>(`/${activeMeetingId}/participants/${item.id}/${action}`, { method: 'POST' })
      if (!requestGateRef.current.isCurrent(scope)) return
      if (!requestGateRef.current.tryCommitVersion(scope, next.id, next.version)) return
      setDetail(next)
    } catch {
      if (!requestGateRef.current.isCurrent(scope)) return
      setActionError(t('collaborationMeetings.serviceUnavailable'))
      await refresh()
    }
  }, [activeMeetingId, refresh, t])

  const createGuestInvite = useCallback(async (guestDisplayName: string, expiryHours: number) => {
    if (!activeMeetingId || !canInvite || ![1, 24, 168].includes(expiryHours)) throw new Error('MEETING_GUEST_INVITE_NOT_ALLOWED')
    const scope = requestGateRef.current.capture()
    if (scope.key !== activeMeetingId) throw new Error('MEETING_GUEST_INVITE_STALE')
    const result = await meetingRequest<MeetingGuestInviteResponse>(
      `/${activeMeetingId}/guest-invites`,
      jsonRequest('POST', {
        guestDisplayName: guestDisplayName.trim(),
        expiresAtUtc: new Date(Date.now() + expiryHours * 60 * 60_000).toISOString(),
      }),
    )
    if (!requestGateRef.current.isCurrent(scope)) throw new Error('MEETING_GUEST_INVITE_STALE')
    await refresh()
    return result
  }, [activeMeetingId, canInvite, refresh])

  const reportConnected = useCallback(async () => {
    if (!activeMeetingId) return
    const scope = requestGateRef.current.capture()
    if (scope.key !== activeMeetingId) return
    await meetingRequest(`/${activeMeetingId}/connected`, { method: 'POST' })
  }, [activeMeetingId])
  const reportDisconnected = useCallback(async () => {
    if (!activeMeetingId) return
    await meetingRequest(`/${activeMeetingId}/disconnected`, { method: 'POST' })
  }, [activeMeetingId])

  async function endMeeting() {
    if (!activeMeetingId || !currentMeeting) return
    const scope = requestGateRef.current.capture()
    if (scope.key !== activeMeetingId) return
    setActionError('')
    try {
      await meetingRequest(`/${activeMeetingId}/end`, jsonRequest('POST', { expectedVersion: currentMeeting.version }))
      if (!requestGateRef.current.isCurrent(scope)) return
      setEndConfirm(false)
      transitionActive(null)
      setDetail(null)
      setMeetingRevision(value => value + 1)
    } catch {
      if (!requestGateRef.current.isCurrent(scope)) return
      setMinimized(false)
      setActionError(t('collaborationMeetings.serviceUnavailable'))
      setEndConfirm(false)
    }
  }

  return <MeetingCallContext.Provider value={value}>
    {children}
    {active && <Suspense fallback={<div className="meeting-room-overlay"><LoadingState label={t('collaborationMeetings.connecting')} /></div>}>
      <CollaborationMeetingRoom
        join={active.join}
        initialMedia={active.media}
        meetingParticipants={currentMeeting?.participants}
        waitingParticipants={waiting}
        canManageParticipants={canManage}
        canInviteGuests={canInvite}
        canEndMeeting={canEnd}
        minimized={minimized}
        onMinimize={() => setMinimized(true)}
        onRestore={() => setMinimized(false)}
        onAdmit={item => participantAction(item, 'admit')}
        onDeny={item => participantAction(item, 'deny')}
        onCreateGuestInvite={createGuestInvite}
        onConnected={reportConnected}
        onDisconnected={reportDisconnected}
        onLeave={() => { transitionActive(null); setDetail(null); setEndConfirm(false) }}
        onEnd={() => { setMinimized(true); setEndConfirm(true) }}
      />
    </Suspense>}
    {active && actionError && <div className="meeting-call-global-error" role="alert"><MessageStrip tone="warning"><span>{actionError}</span><button type="button" className="button button--quiet" onClick={() => setActionError('')}>{t('common.close')}</button></MessageStrip></div>}
    <ConfirmationDialog open={endConfirm} title={t('collaborationMeetings.endConfirmTitle')} description={t('collaborationMeetings.endConfirmHint')} confirmLabel={t('collaborationMeetings.endMeeting')} cancelLabel={t('common.cancel')} tone="critical" onCancel={() => setEndConfirm(false)} onConfirm={() => void endMeeting()} />
  </MeetingCallContext.Provider>
}
