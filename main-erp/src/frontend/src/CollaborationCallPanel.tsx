import {
  Room,
  RoomEvent,
  createLocalAudioTrack,
  createLocalVideoTrack,
  type LocalAudioTrack,
  type LocalTrack,
  type LocalVideoTrack,
  type RemoteTrack,
} from 'livekit-client'
import { Archive, Mic, MicOff, PhoneOff, RefreshCw, Users, Video, VideoOff, Volume2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { callBootstrapFailureTranslationKey, callMediaFailureTranslationKey } from './collaborationCallSupport'
import type { CallJoin } from './collaborationSupport'
import { LoadingState, MessageStrip } from './ui'

type CallState = 'CONNECTING' | 'MEDIA' | 'CONNECTED' | 'RECONNECTING' | 'FAILED'

type Props = {
  join: CallJoin
  onLeave: () => void
  onEnd?: () => void
  onBootstrapFailure?: (message: string) => void | Promise<void>
  onBootstrapAbort?: () => void | Promise<void>
}

export default function CollaborationCallPanel({ join, onLeave, onEnd, onBootstrapFailure, onBootstrapAbort }: Props) {
  const { t } = useTranslation()
  const hostInitial = join.call.hostDisplayName.trim().charAt(0).toUpperCase() || 'H'
  const [state, setState] = useState<CallState>('CONNECTING')
  const [participants, setParticipants] = useState(1)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)
  const [microphoneEnabled, setMicrophoneEnabled] = useState(true)
  const [cameraEnabled, setCameraEnabled] = useState(join.call.videoEnabled)
  const [audioPlaybackBlocked, setAudioPlaybackBlocked] = useState(false)
  const stage = useRef<HTMLDivElement>(null)
  const roomRef = useRef<Room | null>(null)
  const localTracks = useRef<LocalTrack[]>([])
  const microphoneTrack = useRef<LocalAudioTrack | null>(null)
  const cameraTrack = useRef<LocalVideoTrack | null>(null)
  const establishedCallId = useRef<string | null>(null)
  const leaveRequested = useRef(false)
  const bootstrapFailureRef = useRef(onBootstrapFailure)

  useEffect(() => { bootstrapFailureRef.current = onBootstrapFailure }, [onBootstrapFailure])

  useEffect(() => {
    let active = true
    let intentionalDisconnect = false
    let mediaPublished = false
    let bootstrapFailureReported = false
    let bootstrapPhase: 'SIGNALING' | 'MEDIA' = 'SIGNALING'
    const room = new Room({ adaptiveStream: true, dynacast: true })
    roomRef.current = room

    const stopLocalTracks = () => {
      localTracks.current.forEach(track => {
        track.stop()
        track.detach().forEach(element => element.remove())
      })
      localTracks.current = []
      microphoneTrack.current = null
      cameraTrack.current = null
    }
    const reportBootstrapFailure = (message: string) => {
      if (mediaPublished || establishedCallId.current === join.call.id || bootstrapFailureReported) return
      bootstrapFailureReported = true
      void bootstrapFailureRef.current?.(message)
    }
    const updateParticipants = () => { if (active) setParticipants(room.remoteParticipants.size + 1) }
    const attach = (track: RemoteTrack) => {
      const element = track.attach()
      element.className = 'collaboration-call-media'
      element.autoplay = true
      if (element instanceof HTMLVideoElement) element.playsInline = true
      stage.current?.append(element)
    }
    const detach = (track: RemoteTrack) => track.detach().forEach(element => element.remove())
    const onReconnecting = () => { if (active) setState('RECONNECTING') }
    const onReconnected = () => {
      if (!active) return
      setState(mediaPublished ? 'CONNECTED' : 'MEDIA')
      setError('')
    }
    const onDisconnected = () => {
      if (!active || intentionalDisconnect) return
      stopLocalTracks()
      const message = t('collaborationHub.callDisconnected')
      setState('FAILED')
      setError(message)
      reportBootstrapFailure(message)
    }
    const onAudioPlaybackChanged = () => { if (active) setAudioPlaybackBlocked(!room.canPlaybackAudio) }
    const onMediaDevicesError = (exception: Error) => {
      if (active) setError(t(callMediaFailureTranslationKey(exception)))
    }

    room
      .on(RoomEvent.TrackSubscribed, attach)
      .on(RoomEvent.TrackUnsubscribed, detach)
      .on(RoomEvent.ParticipantConnected, updateParticipants)
      .on(RoomEvent.ParticipantDisconnected, updateParticipants)
      .on(RoomEvent.Reconnecting, onReconnecting)
      .on(RoomEvent.Reconnected, onReconnected)
      .on(RoomEvent.Disconnected, onDisconnected)
      .on(RoomEvent.AudioPlaybackStatusChanged, onAudioPlaybackChanged)
      .on(RoomEvent.MediaDevicesError, onMediaDevicesError)

    const connect = async () => {
      try {
        setState('CONNECTING')
        setError('')
        setAudioPlaybackBlocked(false)
        setMicrophoneEnabled(true)
        setCameraEnabled(join.call.videoEnabled)
        await room.connect(join.serverUrl, join.accessToken)
        if (!active) return

        bootstrapPhase = 'MEDIA'
        setState('MEDIA')
        const audio = await createLocalAudioTrack()
        const tracks: LocalTrack[] = [audio]
        let video: LocalVideoTrack | null = null
        try {
          if (join.call.videoEnabled) {
            video = await createLocalVideoTrack()
            tracks.push(video)
          }
        } catch (exception) {
          audio.stop()
          throw exception
        }
        if (!active) {
          tracks.forEach(track => track.stop())
          return
        }

        localTracks.current = tracks
        microphoneTrack.current = audio
        cameraTrack.current = video
        for (const track of tracks) await room.localParticipant.publishTrack(track)
        if (video) {
          const element = video.attach()
          element.className = 'collaboration-call-media collaboration-call-media--local'
          element.autoplay = true
          element.muted = true
          if (element instanceof HTMLVideoElement) element.playsInline = true
          stage.current?.append(element)
        }
        mediaPublished = true
        establishedCallId.current = join.call.id
        setState('CONNECTED')
        setError('')
        setAudioPlaybackBlocked(!room.canPlaybackAudio)
        updateParticipants()
      } catch (exception) {
        stopLocalTracks()
        intentionalDisconnect = true
        await room.disconnect().catch(() => undefined)
        if (!active) return
        console.error('Collaboration call connection failed.', exception)
        const message = t(callBootstrapFailureTranslationKey(exception, bootstrapPhase))
        setState('FAILED')
        setError(message)
        reportBootstrapFailure(message)
      }
    }

    void connect()
    return () => {
      active = false
      intentionalDisconnect = true
      stopLocalTracks()
      if (roomRef.current === room) roomRef.current = null
      void room.disconnect()
      room.removeAllListeners()
    }
  }, [attempt, join.accessToken, join.call.id, join.call.videoEnabled, join.serverUrl, t])

  async function toggleMicrophone() {
    const track = microphoneTrack.current
    if (!track) return
    try {
      if (microphoneEnabled) await track.mute()
      else await track.unmute()
      setMicrophoneEnabled(value => !value)
    } catch (exception) {
      setError(t(callMediaFailureTranslationKey(exception)))
    }
  }

  async function toggleCamera() {
    const track = cameraTrack.current
    if (!track) return
    try {
      if (cameraEnabled) await track.mute()
      else await track.unmute()
      setCameraEnabled(value => !value)
    } catch (exception) {
      setError(t(callMediaFailureTranslationKey(exception)))
    }
  }

  async function enableAudioPlayback() {
    try {
      await roomRef.current?.startAudio()
      setAudioPlaybackBlocked(false)
    } catch {
      setError(t('collaborationHub.callAudioPlaybackFailed'))
    }
  }

  function leaveCall() {
    if (leaveRequested.current) return
    leaveRequested.current = true
    if ((state === 'CONNECTING' || state === 'MEDIA') && onBootstrapAbort) {
      void onBootstrapAbort()
      return
    }
    onLeave()
  }

  return <div className="collaboration-overlay" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) leaveCall() }}>
    <section className="collaboration-dialog" role="dialog" aria-modal="true" aria-label={t('collaborationHub.activeCall')}>
      <div className="collaboration-dialog__header"><h2>{t('collaborationHub.activeCall')}</h2><button type="button" className="button button--quiet" onClick={leaveCall} aria-label={t('common.close')}>×</button></div>
      <div className="collaboration-call-stage" ref={stage}>
        {state === 'CONNECTING' && <LoadingState label={t('collaborationHub.callConnecting')} />}
        {state === 'MEDIA' && <LoadingState label={t('collaborationHub.callAwaitingMedia')} />}
        {state === 'RECONNECTING' && <LoadingState label={t('collaborationHub.callReconnecting')} />}
        {!join.call.videoEnabled && state === 'CONNECTED' && <div className="collaboration-call-audio-card" role="status">
          <span aria-hidden="true">{hostInitial}</span>
          <strong>{join.call.hostDisplayName}</strong>
          <p><Mic aria-hidden="true" />{t('collaborationHub.voiceCallConnected')}</p>
        </div>}
        {state === 'FAILED' && <MessageStrip tone="critical"><span>{error}</span><button className="button button--quiet" type="button" onClick={() => { setState('CONNECTING'); setError(''); setAttempt(value => value + 1) }}><RefreshCw />{t('collaborationHub.retry')}</button></MessageStrip>}
        {error && state !== 'FAILED' && <MessageStrip tone="warning"><span>{error}</span></MessageStrip>}
        {audioPlaybackBlocked && <MessageStrip tone="warning"><span>{t('collaborationHub.callAudioPlaybackBlocked')}</span><button className="button button--quiet" type="button" onClick={() => void enableAudioPlayback()}><Volume2 />{t('collaborationHub.enableCallAudio')}</button></MessageStrip>}
      </div>
      <div className="collaboration-call-footer">
        <span><Users aria-hidden="true" />{t('collaborationHub.callParticipants')}: {participants}</span>
        <div className="collaboration-call-controls">
          {state === 'CONNECTED' && <button className="button button--icon" type="button" aria-pressed={!microphoneEnabled} title={t(microphoneEnabled ? 'collaborationHub.muteMicrophone' : 'collaborationHub.unmuteMicrophone')} onClick={() => void toggleMicrophone()}>{microphoneEnabled ? <Mic /> : <MicOff />}</button>}
          {state === 'CONNECTED' && join.call.videoEnabled && <button className="button button--icon" type="button" aria-pressed={!cameraEnabled} title={t(cameraEnabled ? 'collaborationHub.disableCamera' : 'collaborationHub.enableCamera')} onClick={() => void toggleCamera()}>{cameraEnabled ? <Video /> : <VideoOff />}</button>}
        </div>
        <div><button className="button" type="button" onClick={leaveCall}><PhoneOff />{t('collaborationHub.leaveCall')}</button>{onEnd && <button className="button button--danger" type="button" onClick={onEnd}><Archive />{t('collaborationHub.endCall')}</button>}</div>
      </div>
    </section>
  </div>
}
