import {
  ConnectionQuality,
  ConnectionState,
  Room,
  RoomEvent,
  Track,
  DisconnectReason,
  createLocalAudioTrack,
  createLocalVideoTrack,
  type LocalAudioTrack,
  type LocalTrack,
  type LocalVideoTrack,
  type RemoteParticipant,
  type RemoteTrack,
  type RemoteTrackPublication,
} from 'livekit-client'
import {
  Check,
  ClipboardCopy,
  LockKeyhole,
  Maximize2,
  MessageSquareText,
  Mic,
  MicOff,
  Minimize2,
  MonitorOff,
  MonitorUp,
  PanelRightClose,
  Pencil,
  PhoneOff,
  Plus,
  Search,
  Send,
  ShieldCheck,
  Square,
  Trash2,
  Users,
  Video,
  VideoOff,
  Volume2,
} from 'lucide-react'
import { FormEvent, PointerEvent as ReactPointerEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  buildGuestMeetingInviteUrl,
  meetingRoleTranslationKey,
  type MeetingAdmissionStatus,
  type MeetingGuestInviteResponse,
  type MeetingParticipant,
  type MeetingRoomJoin,
  type MeetingRoomRosterParticipant,
  type MeetingRole,
} from './collaborationMeetingSupport'
import {
  MAX_EPHEMERAL_CHAT_ENTRIES,
  MAX_STAGE_VISUAL_TILES,
  MAX_WHITEBOARD_STROKES,
  WHITEBOARD_MIN_UPDATE_INTERVAL_MS,
  MeetingMediaOperationGate,
  appendBoundedUnique,
  canonicalMeetingChatTimestamp,
  canEmitWhiteboardUpdate,
  connectionQualityPresentation,
  createMeetingParticipantIndex,
  formatMeetingChatTime,
  participantMatchesMeetingSearch,
  reportMeetingAttendanceWithRetry,
  resolveMeetingParticipant,
  resolveMeetingShortcut,
  selectStageMediaTrackSids,
  selectStageParticipantPlaceholders,
  type StageMediaSubscriptionCandidate,
  type StageVisualTile,
} from './meetingRoomScaleSupport'
import { MessageStrip, StatusBadge } from './ui'
import type { MeetingJoinMediaSelection } from './meetingPreJoinSupport'

type MeetingChatMessage = {
  id: string
  sender: string
  text: string
  sentAtUtc: string
}

type WhiteboardStroke = {
  id: string
  fromX: number
  fromY: number
  toX: number
  toY: number
  author: string
}

type MeetingRoomParticipant = {
  key: string
  name: string
  role: MeetingRole
  admissionStatus: MeetingAdmissionStatus
  connected: boolean
  microphone: boolean
  camera: boolean
  cameraVisible: boolean
  screenShare: boolean
  isLocal: boolean
}

type Props = {
  join: MeetingRoomJoin
  initialMedia: MeetingJoinMediaSelection
  meetingParticipants?: readonly MeetingParticipant[]
  waitingParticipants?: MeetingParticipant[]
  canManageParticipants?: boolean
  canInviteGuests?: boolean
  canEndMeeting?: boolean
  minimized?: boolean
  onAdmit?: (participant: MeetingParticipant) => void | Promise<void>
  onDeny?: (participant: MeetingParticipant) => void | Promise<void>
  onCreateGuestInvite?: (guestDisplayName: string, expiryHours: number) => Promise<MeetingGuestInviteResponse>
  onConnected?: () => void | Promise<void>
  onDisconnected?: () => void | Promise<void>
  onLeave: () => void
  onEnd?: () => void
  onMinimize?: () => void
  onRestore?: () => void
}

const meetingChatTopic = 'hadron.meeting.chat.v1'
const meetingWhiteboardTopic = 'hadron.meeting.whiteboard.v1'

function selectedDeviceConstraint(deviceId?: string) {
  return deviceId ? { deviceId: { exact: deviceId } } : undefined
}

function remoteParticipantName(participant: RemoteParticipant | undefined) {
  return participant?.name?.trim() || participant?.identity || '—'
}

function disconnectMessageKey(reason?: DisconnectReason) {
  if (reason === DisconnectReason.PARTICIPANT_REMOVED) return 'collaborationMeetings.revokedTitle'
  if (reason === DisconnectReason.ROOM_DELETED || reason === DisconnectReason.ROOM_CLOSED) return 'collaborationMeetings.callEnded'
  if (reason === DisconnectReason.DUPLICATE_IDENTITY) return 'collaborationMeetings.anotherMeetingActive'
  return 'collaborationHub.callDisconnected'
}

function mediaTileKey(participantKey: string, mediaKind: 'screen' | 'camera') {
  return `${participantKey}:${mediaKind}`
}

function removeMediaElement(element: Element, mediaTiles: Map<string, HTMLElement>) {
  const tile = element.closest<HTMLElement>('.meeting-room-tile')
  if (!tile) {
    element.remove()
    return
  }
  const participantKey = tile.dataset.meetingParticipant
  const mediaKind = tile.dataset.meetingMedia
  if (participantKey && (mediaKind === 'screen' || mediaKind === 'camera')) {
    const key = mediaTileKey(participantKey, mediaKind)
    if (mediaTiles.get(key) === tile) mediaTiles.delete(key)
  }
  tile.remove()
}

function disposeLocalMeetingTrack(track: LocalTrack, mediaTiles: Map<string, HTMLElement>) {
  track.stop()
  track.detach().forEach(element => removeMediaElement(element, mediaTiles))
}

function appendMediaTile(
  stage: HTMLDivElement | null,
  mediaTiles: Map<string, HTMLElement>,
  element: HTMLMediaElement,
  labelText: string,
  screen: boolean,
  participantKey: string,
) {
  if (!stage) return
  const mediaKind = screen ? 'screen' : 'camera' as const
  const key = mediaTileKey(participantKey, mediaKind)
  mediaTiles.get(key)?.remove()
  const tile = document.createElement('div')
  tile.className = `meeting-room-tile${screen ? ' meeting-room-tile--screen' : ''}`
  tile.dataset.meetingParticipant = participantKey
  tile.dataset.meetingMedia = mediaKind
  if (screen) tile.dataset.meetingScreen = 'true'
  const label = document.createElement('span')
  label.className = 'meeting-room-tile__label'
  label.textContent = labelText
  tile.append(element, label)
  stage.append(tile)
  mediaTiles.set(key, tile)
}

export default function CollaborationMeetingRoom({
  join,
  initialMedia,
  meetingParticipants,
  waitingParticipants = [],
  canManageParticipants = false,
  canInviteGuests = false,
  canEndMeeting = false,
  minimized = false,
  onAdmit,
  onDeny,
  onCreateGuestInvite,
  onConnected,
  onDisconnected,
  onLeave,
  onEnd,
  onMinimize,
  onRestore,
}: Props) {
  const { t, i18n } = useTranslation()
  const [connection, setConnection] = useState<'CONNECTING' | 'CONNECTED' | 'RECONNECTING' | 'FAILED'>('CONNECTING')
  const [participants, setParticipants] = useState<MeetingRoomParticipant[]>([{
    key: join.participant.id,
    name: join.participant.displayName,
    role: join.participant.role,
    admissionStatus: join.participant.admissionStatus,
    connected: false,
    microphone: false,
    camera: false,
    cameraVisible: false,
    screenShare: false,
    isLocal: true,
  }])
  const [microphoneEnabled, setMicrophoneEnabled] = useState(initialMedia.microphone && join.capabilities.microphone)
  const [cameraEnabled, setCameraEnabled] = useState(initialMedia.camera && join.capabilities.camera)
  const [screenSharing, setScreenSharing] = useState(false)
  const [audioPlaybackBlocked, setAudioPlaybackBlocked] = useState(false)
  const [sidePanel, setSidePanel] = useState<'PEOPLE' | 'CHAT' | 'WHITEBOARD' | null>(null)
  const [participantSearch, setParticipantSearch] = useState('')
  const [participantActionBusy, setParticipantActionBusy] = useState('')
  const [inviteName, setInviteName] = useState('')
  const [inviteExpiryHours, setInviteExpiryHours] = useState('24')
  const [inviteBusy, setInviteBusy] = useState(false)
  const [inviteResult, setInviteResult] = useState<MeetingGuestInviteResponse | null>(null)
  const [inviteCopied, setInviteCopied] = useState(false)
  const [chatMessages, setChatMessages] = useState<MeetingChatMessage[]>([])
  const [chatDraft, setChatDraft] = useState('')
  const [chatAnnouncement, setChatAnnouncement] = useState<{ id: string; text: string } | null>(null)
  const [whiteboardStrokes, setWhiteboardStrokes] = useState<WhiteboardStroke[]>([])
  const [stageVisualTiles, setStageVisualTiles] = useState<StageVisualTile[]>([])
  const [connectionQuality, setConnectionQuality] = useState<ConnectionQuality>(ConnectionQuality.Unknown)
  const [error, setError] = useState('')
  const roomRef = useRef<Room | null>(null)
  const stageRef = useRef<HTMLDivElement>(null)
  const mediaTilesRef = useRef(new Map<string, HTMLElement>())
  const inviteLinkTextRef = useRef<HTMLElement>(null)
  const recentChatMessageIdsRef = useRef(new Set<string>())
  const microphoneRef = useRef<LocalAudioTrack | null>(null)
  const cameraRef = useRef<LocalVideoTrack | null>(null)
  const screenTrackRef = useRef<LocalTrack | null>(null)
  const localTracksRef = useRef<LocalTrack[]>([])
  const mediaOperationGateRef = useRef(new MeetingMediaOperationGate())
  const attendanceQueueRef = useRef<Promise<unknown>>(Promise.resolve())
  const resyncRemoteSubscriptionsRef = useRef<() => void>(() => undefined)
  const leavingRef = useRef(false)
  const drawingPointRef = useRef<{ x: number; y: number } | null>(null)
  const pendingWhiteboardStrokesRef = useRef<WhiteboardStroke[]>([])
  const whiteboardFlushTimerRef = useRef<number | null>(null)
  const lastWhiteboardStateUpdateAtRef = useRef(Number.NEGATIVE_INFINITY)
  const lastWhiteboardOutboundAtRef = useRef(Number.NEGATIVE_INFINITY)
  const onConnectedRef = useRef(onConnected)
  const onDisconnectedRef = useRef(onDisconnected)
  const tRef = useRef(t)
  const rosterRef = useRef<readonly MeetingRoomRosterParticipant[]>(meetingParticipants ?? join.meeting.participants)
  const joinParticipantRef = useRef(join.participant)
  const participantIndex = useMemo(
    () => createMeetingParticipantIndex(meetingParticipants ?? join.meeting.participants),
    [join.meeting.participants, meetingParticipants],
  )
  const participantIndexRef = useRef(participantIndex)

  const appendChatMessage = useCallback((item: MeetingChatMessage) => {
    const recentIds = recentChatMessageIdsRef.current
    if (recentIds.has(item.id)) return
    recentIds.add(item.id)
    if (recentIds.size > MAX_EPHEMERAL_CHAT_ENTRIES) {
      const oldestId = recentIds.values().next().value as string | undefined
      if (oldestId) recentIds.delete(oldestId)
    }
    setChatMessages(current => appendBoundedUnique(current, [item], MAX_EPHEMERAL_CHAT_ENTRIES))
    setChatAnnouncement({ id: item.id, text: `${item.sender}: ${item.text}` })
  }, [])

  const discardLocalTrack = useCallback((track: LocalTrack, room: Room, requestUnpublish: boolean) => {
    localTracksRef.current = localTracksRef.current.filter(item => item !== track)
    if (microphoneRef.current === track) microphoneRef.current = null
    if (cameraRef.current === track) cameraRef.current = null
    if (screenTrackRef.current === track) screenTrackRef.current = null
    disposeLocalMeetingTrack(track, mediaTilesRef.current)
    if (requestUnpublish) void room.localParticipant.unpublishTrack(track, false).catch(() => undefined)
  }, [])

  useEffect(() => { onConnectedRef.current = onConnected }, [onConnected])
  useEffect(() => { onDisconnectedRef.current = onDisconnected }, [onDisconnected])
  useEffect(() => { tRef.current = t }, [t])
  useEffect(() => {
    participantIndexRef.current = participantIndex
    rosterRef.current = meetingParticipants ?? join.meeting.participants
    joinParticipantRef.current = join.participant
  }, [join.meeting.participants, join.participant, meetingParticipants, participantIndex])

  const enqueueAttendance = useCallback((operation: () => Promise<boolean>) => {
    const pending = attendanceQueueRef.current.then(operation, operation)
    attendanceQueueRef.current = pending.then(() => undefined, () => undefined)
    return pending
  }, [])

  const flushWhiteboardStrokes = useCallback(() => {
    whiteboardFlushTimerRef.current = null
    const pending = pendingWhiteboardStrokesRef.current
    pendingWhiteboardStrokesRef.current = []
    if (pending.length === 0) return
    lastWhiteboardStateUpdateAtRef.current = performance.now()
    setWhiteboardStrokes(current => appendBoundedUnique(current, pending, MAX_WHITEBOARD_STROKES))
  }, [])

  const queueWhiteboardStroke = useCallback((stroke: WhiteboardStroke) => {
    pendingWhiteboardStrokesRef.current = appendBoundedUnique(
      pendingWhiteboardStrokesRef.current,
      [stroke],
      MAX_WHITEBOARD_STROKES,
    )
    if (whiteboardFlushTimerRef.current !== null) return
    const elapsed = performance.now() - lastWhiteboardStateUpdateAtRef.current
    const delay = Number.isFinite(elapsed) ? Math.max(0, WHITEBOARD_MIN_UPDATE_INTERVAL_MS - elapsed) : 0
    whiteboardFlushTimerRef.current = window.setTimeout(flushWhiteboardStrokes, delay)
  }, [flushWhiteboardStrokes])

  const clearQueuedWhiteboard = useCallback(() => {
    if (whiteboardFlushTimerRef.current !== null) window.clearTimeout(whiteboardFlushTimerRef.current)
    whiteboardFlushTimerRef.current = null
    pendingWhiteboardStrokesRef.current = []
    setWhiteboardStrokes([])
  }, [])

  const refreshParticipants = useCallback((room: Room | null = roomRef.current) => {
    if (!room) return
    const roster = rosterRef.current
    const joinParticipant = joinParticipantRef.current
    const connectedByParticipantId = new Map<string, MeetingRoomParticipant>()
    const providerOnlyParticipants: MeetingRoomParticipant[] = []

    Array.from(room.remoteParticipants.values()).forEach(participant => {
      const meetingParticipant = resolveMeetingParticipant(participantIndexRef.current, {
        identity: participant.identity,
        displayName: remoteParticipantName(participant),
        metadata: participant.metadata,
      })
      const cameraPublication = participant.getTrackPublication(Track.Source.Camera)
      const screenPublication = participant.getTrackPublication(Track.Source.ScreenShare)
      const key = meetingParticipant?.id ?? `provider:${participant.identity}`
      const connectedParticipant: MeetingRoomParticipant = {
        key,
        name: meetingParticipant?.displayName ?? remoteParticipantName(participant),
        role: meetingParticipant?.role ?? 'ATTENDEE',
        admissionStatus: meetingParticipant?.admissionStatus ?? 'ADMITTED',
        connected: true,
        microphone: participant.isMicrophoneEnabled,
        camera: participant.isCameraEnabled,
        cameraVisible: Boolean(participant.isCameraEnabled && cameraPublication?.isSubscribed && cameraPublication.isDesired),
        screenShare: Boolean(participant.isScreenShareEnabled || (screenPublication && !screenPublication.isMuted)),
        isLocal: false,
      }
      if (!meetingParticipant) {
        providerOnlyParticipants.push(connectedParticipant)
        return
      }
      const previous = connectedByParticipantId.get(meetingParticipant.id)
      connectedByParticipantId.set(meetingParticipant.id, previous ? {
        ...connectedParticipant,
        microphone: previous.microphone || connectedParticipant.microphone,
        camera: previous.camera || connectedParticipant.camera,
        cameraVisible: previous.cameraVisible || connectedParticipant.cameraVisible,
        screenShare: previous.screenShare || connectedParticipant.screenShare,
      } : connectedParticipant)
    })

    const localConnected = room.state === ConnectionState.Connected || room.state === ConnectionState.Reconnecting
    const snapshot = roster.map<MeetingRoomParticipant>(participant => {
      if (participant.id === joinParticipant.id) return {
        key: participant.id,
        name: participant.displayName,
        role: participant.role,
        admissionStatus: participant.admissionStatus,
        connected: localConnected,
        microphone: localConnected && room.localParticipant.isMicrophoneEnabled,
        camera: localConnected && room.localParticipant.isCameraEnabled,
        cameraVisible: localConnected && room.localParticipant.isCameraEnabled,
        screenShare: localConnected && room.localParticipant.isScreenShareEnabled,
        isLocal: true,
      }
      return connectedByParticipantId.get(participant.id) ?? {
        key: participant.id,
        name: participant.displayName,
        role: participant.role,
        admissionStatus: participant.admissionStatus,
        connected: false,
        microphone: false,
        camera: false,
        cameraVisible: false,
        screenShare: false,
        isLocal: false,
      }
    })
    if (!snapshot.some(participant => participant.key === joinParticipant.id)) {
      snapshot.unshift({
        key: joinParticipant.id,
        name: joinParticipant.displayName,
        role: joinParticipant.role,
        admissionStatus: joinParticipant.admissionStatus,
        connected: localConnected,
        microphone: localConnected && room.localParticipant.isMicrophoneEnabled,
        camera: localConnected && room.localParticipant.isCameraEnabled,
        cameraVisible: localConnected && room.localParticipant.isCameraEnabled,
        screenShare: localConnected && room.localParticipant.isScreenShareEnabled,
        isLocal: true,
      })
    }
    snapshot.push(...providerOnlyParticipants)
    setParticipants(snapshot)
    const snapshotByKey = new Map(snapshot.map(participant => [participant.key, participant]))
    mediaTilesRef.current.forEach((tile, tileKey) => {
      const participant = snapshotByKey.get(tile.dataset.meetingParticipant ?? '')
      if (!participant) {
        tile.remove()
        mediaTilesRef.current.delete(tileKey)
        return
      }
      const label = tile.querySelector<HTMLElement>('.meeting-room-tile__label')
      if (!label) return
      if (tile.dataset.meetingMedia === 'camera') tile.hidden = !participant.cameraVisible
      if (tile.dataset.meetingMedia === 'screen') tile.hidden = !participant.screenShare
      const translate = tRef.current
      const role = translate(meetingRoleTranslationKey(participant.role))
      const microphone = translate(participant.microphone ? 'collaborationMeetings.enabled' : 'collaborationMeetings.disabled')
      const camera = translate(participant.camera ? 'collaborationMeetings.enabled' : 'collaborationMeetings.disabled')
      const screen = tile.dataset.meetingScreen === 'true' ? ` · ${translate('collaborationMeetings.screenShare')}` : ''
      label.textContent = `${participant.name} · ${role} · ${translate('collaborationMeetings.microphone')}: ${microphone} · ${translate('collaborationMeetings.camera')}: ${camera}${screen}`
    })
    setStageVisualTiles(Array.from(mediaTilesRef.current.values()).flatMap<StageVisualTile>(tile => {
      const participantKey = tile.dataset.meetingParticipant
      const mediaKind = tile.dataset.meetingMedia
      return tile.isConnected && !tile.hidden && participantKey && (mediaKind === 'screen' || mediaKind === 'camera')
        ? [{ participantKey, mediaKind }]
        : []
    }))
  }, [])

  useEffect(() => {
    let mounted = true
    let intentionalDisconnect = false
    let activeSpeakerIdentities: string[] = []
    let selectedRemoteVisualTrackSids: string[] = []
    let connectedReport: Promise<boolean> | null = null
    let disconnectedEnqueued = false
    const connectedCallback = onConnectedRef.current
    const disconnectedCallback = onDisconnectedRef.current
    const mediaOperationGate = mediaOperationGateRef.current
    const lifecycleGeneration = mediaOperationGate.startLifecycle()
    const room = new Room({ adaptiveStream: true, dynacast: true })
    const mediaTiles = mediaTilesRef.current
    roomRef.current = room
    const isCurrentLifecycle = () => mounted
      && mediaOperationGate.isCurrent(lifecycleGeneration)
      && roomRef.current === room

    const meetingParticipantForRemote = (participant: RemoteParticipant) => resolveMeetingParticipant(participantIndexRef.current, {
      identity: participant.identity,
      displayName: remoteParticipantName(participant),
      metadata: participant.metadata,
    })
    const resyncRemoteSubscriptions = () => {
      if (!isCurrentLifecycle()) return
      const candidates: StageMediaSubscriptionCandidate[] = []
      const publications: RemoteTrackPublication[] = []
      room.remoteParticipants.forEach(participant => {
        const role = meetingParticipantForRemote(participant)?.role ?? 'ATTENDEE'
        participant.trackPublications.forEach(publication => {
          publications.push(publication)
          const mediaKind = publication.source === Track.Source.ScreenShare
            ? 'screen'
            : publication.source === Track.Source.Camera ? 'camera' : null
          if (!mediaKind || publication.kind !== Track.Kind.Video) return
          candidates.push({
            trackSid: publication.trackSid,
            participantIdentity: participant.identity,
            role,
            muted: publication.isMuted,
            mediaKind,
          })
        })
      })
      const localVisualTileCount = (['camera', 'screen'] as const).reduce((count, mediaKind) => {
        const tile = mediaTiles.get(mediaTileKey(join.participant.id, mediaKind))
        return count + (tile?.isConnected && !tile.hidden ? 1 : 0)
      }, 0)
      selectedRemoteVisualTrackSids = selectStageMediaTrackSids(
        candidates,
        activeSpeakerIdentities,
        localVisualTileCount,
        MAX_STAGE_VISUAL_TILES,
        selectedRemoteVisualTrackSids,
      )
      const selectedVisualTrackSids = new Set(selectedRemoteVisualTrackSids)
      const shouldSubscribe = (publication: RemoteTrackPublication) => publication.kind === Track.Kind.Audio
        || selectedVisualTrackSids.has(publication.trackSid)
      // Release cameras outside the window before adding replacements, so the
      // desired camera set never transiently exceeds its hard limit.
      publications.forEach(publication => {
        if (publication.isDesired && !shouldSubscribe(publication)) publication.setSubscribed(false)
      })
      publications.forEach(publication => {
        const desired = shouldSubscribe(publication)
        if (!publication.isDesired && desired) publication.setSubscribed(true)
      })
    }
    resyncRemoteSubscriptionsRef.current = resyncRemoteSubscriptions
    const refreshMountedParticipants = () => { if (mounted) refreshParticipants(room) }
    const attachRemote = (track: RemoteTrack, publication: RemoteTrackPublication, participant: RemoteParticipant) => {
      if (track.kind !== Track.Kind.Audio && !selectedRemoteVisualTrackSids.includes(publication.trackSid)) {
        publication.setSubscribed(false)
        track.detach()
        return
      }
      const element = track.attach()
      element.className = 'meeting-room-media'
      element.autoplay = true
      if (element instanceof HTMLVideoElement) element.playsInline = true
      if (track.kind === Track.Kind.Audio) stageRef.current?.append(element)
      else {
        const meetingParticipant = meetingParticipantForRemote(participant)
        appendMediaTile(
          stageRef.current,
          mediaTilesRef.current,
          element,
          meetingParticipant?.displayName ?? remoteParticipantName(participant),
          track.source === Track.Source.ScreenShare,
          meetingParticipant?.id ?? `provider:${participant.identity}`,
        )
      }
      refreshMountedParticipants()
    }
    const detachRemote = (track: RemoteTrack) => track.detach().forEach(element => {
      removeMediaElement(element, mediaTilesRef.current)
      refreshMountedParticipants()
    })
    const receiveData = (payload: Uint8Array, participant?: RemoteParticipant, _kind?: unknown, topic?: string) => {
      if (!mounted) return
      try {
        const parsed = JSON.parse(new TextDecoder().decode(payload)) as Record<string, unknown>
        if (topic === meetingChatTopic && join.capabilities.chat) {
          if (typeof parsed.id !== 'string' || typeof parsed.text !== 'string' || parsed.text.trim().length === 0 || parsed.text.length > 2000) return
          const id = parsed.id
          const text = parsed.text.trim()
          const receivedAtUtc = new Date().toISOString()
          appendChatMessage({
            id, sender: remoteParticipantName(participant), text,
            sentAtUtc: canonicalMeetingChatTimestamp(parsed.sentAtUtc, receivedAtUtc),
          })
          return
        }
        if (topic === meetingWhiteboardTopic && join.capabilities.ephemeralWhiteboard) {
          if (parsed.type === 'clear') { clearQueuedWhiteboard(); return }
          const values = [parsed.fromX, parsed.fromY, parsed.toX, parsed.toY]
          if (parsed.type !== 'stroke' || typeof parsed.id !== 'string'
            || !values.every(value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1)) return
          const stroke: WhiteboardStroke = {
            id: parsed.id,
            fromX: parsed.fromX as number, fromY: parsed.fromY as number,
            toX: parsed.toX as number, toY: parsed.toY as number,
            author: remoteParticipantName(participant),
          }
          queueWhiteboardStroke(stroke)
        }
      } catch {
        // Unknown data topics/payloads are ignored instead of affecting media.
      }
    }
    const reportConnected = () => {
      if (connectedReport) return
      connectedReport = enqueueAttendance(() => reportMeetingAttendanceWithRetry(connectedCallback))
      void connectedReport.then(reported => {
        if (!reported && isCurrentLifecycle()) setError(tRef.current('collaborationMeetings.serviceUnavailable'))
      })
    }
    const reportDisconnected = () => {
      if (disconnectedEnqueued || !connectedReport) return
      disconnectedEnqueued = true
      const connected = connectedReport
      void enqueueAttendance(async () => {
        if (!await connected) return false
        return reportMeetingAttendanceWithRetry(disconnectedCallback)
      })
    }
    const clearLocal = () => {
      const tracks = localTracksRef.current
      localTracksRef.current = []
      microphoneRef.current = null
      cameraRef.current = null
      screenTrackRef.current = null
      tracks.forEach(track => disposeLocalMeetingTrack(track, mediaTilesRef.current))
      stageRef.current?.querySelectorAll('[data-local-meeting-media]').forEach(element => removeMediaElement(element, mediaTilesRef.current))
    }
    const subscriptionTopologyChanged = () => {
      resyncRemoteSubscriptions()
      refreshMountedParticipants()
    }

    room
      .on(RoomEvent.TrackSubscribed, attachRemote)
      .on(RoomEvent.TrackUnsubscribed, detachRemote)
      .on(RoomEvent.ParticipantConnected, subscriptionTopologyChanged)
      .on(RoomEvent.ParticipantDisconnected, subscriptionTopologyChanged)
      .on(RoomEvent.ParticipantMetadataChanged, subscriptionTopologyChanged)
      .on(RoomEvent.TrackMuted, subscriptionTopologyChanged)
      .on(RoomEvent.TrackUnmuted, subscriptionTopologyChanged)
      .on(RoomEvent.TrackPublished, subscriptionTopologyChanged)
      .on(RoomEvent.TrackUnpublished, subscriptionTopologyChanged)
      .on(RoomEvent.ActiveSpeakersChanged, speakers => {
        activeSpeakerIdentities = speakers.map(participant => participant.identity)
        resyncRemoteSubscriptions()
      })
      .on(RoomEvent.ConnectionQualityChanged, (quality, participant) => {
        if (mounted && participant === room.localParticipant) setConnectionQuality(quality)
      })
      .on(RoomEvent.DataReceived, receiveData)
      .on(RoomEvent.Reconnecting, () => { if (mounted) { setConnection('RECONNECTING'); setConnectionQuality(ConnectionQuality.Unknown) } })
      .on(RoomEvent.Reconnected, () => {
        if (mounted) {
          setConnection('CONNECTED')
          setConnectionQuality(room.localParticipant.connectionQuality)
          setError('')
          subscriptionTopologyChanged()
        }
      })
      .on(RoomEvent.AudioPlaybackStatusChanged, () => { if (mounted) setAudioPlaybackBlocked(!room.canPlaybackAudio) })
      .on(RoomEvent.Disconnected, (reason?: DisconnectReason) => {
        if (!mounted || intentionalDisconnect) return
        clearLocal()
        reportDisconnected()
        setConnection('FAILED')
        setError(tRef.current(disconnectMessageKey(reason)))
      })

    const connect = async () => {
      try {
        await room.connect(join.serverUrl, join.accessToken, { autoSubscribe: false })
        if (!isCurrentLifecycle()) {
          await room.disconnect().catch(() => undefined)
          return
        }
        if (initialMedia.speakerDeviceId) {
          try {
            await room.switchActiveDevice('audiooutput', initialMedia.speakerDeviceId)
            if (!isCurrentLifecycle()) return
          } catch {
            if (isCurrentLifecycle()) setError(tRef.current('collaborationMeetings.speakerTestUnavailable'))
          }
        }
        resyncRemoteSubscriptions()
        const mediaErrors: unknown[] = []
        if (initialMedia.microphone && join.capabilities.microphone) {
          let audio: LocalAudioTrack | null = null
          try {
            audio = await createLocalAudioTrack(selectedDeviceConstraint(initialMedia.microphoneDeviceId))
            if (!isCurrentLifecycle()) {
              disposeLocalMeetingTrack(audio, mediaTilesRef.current)
              return
            }
            microphoneRef.current = audio
            localTracksRef.current.push(audio)
            await room.localParticipant.publishTrack(audio)
            if (!isCurrentLifecycle()) {
              discardLocalTrack(audio, room, true)
              return
            }
          } catch (exception) {
            if (audio) discardLocalTrack(audio, room, true)
            if (!isCurrentLifecycle()) return
            mediaErrors.push(exception)
            setMicrophoneEnabled(false)
          }
        }
        if (initialMedia.camera && join.capabilities.camera) {
          let video: LocalVideoTrack | null = null
          try {
            video = await createLocalVideoTrack(selectedDeviceConstraint(initialMedia.cameraDeviceId))
            if (!isCurrentLifecycle()) {
              disposeLocalMeetingTrack(video, mediaTilesRef.current)
              return
            }
            cameraRef.current = video
            localTracksRef.current.push(video)
            await room.localParticipant.publishTrack(video)
            if (!isCurrentLifecycle()) {
              discardLocalTrack(video, room, true)
              return
            }
            const element = video.attach()
            element.className = 'meeting-room-media meeting-room-media--local'
            element.autoplay = true
            element.muted = true
            if (element instanceof HTMLVideoElement) element.playsInline = true
            element.dataset.localMeetingMedia = 'camera'
            appendMediaTile(stageRef.current, mediaTilesRef.current, element, join.participant.displayName, false, join.participant.id)
          } catch (exception) {
            if (video) discardLocalTrack(video, room, true)
            if (!isCurrentLifecycle()) return
            mediaErrors.push(exception)
            setCameraEnabled(false)
          }
        }
        if (!isCurrentLifecycle()) return
        resyncRemoteSubscriptions()
        if (mediaErrors.length > 0) setError(tRef.current('collaborationMeetings.mediaPermissionFailed'))
        setConnection('CONNECTED')
        setConnectionQuality(room.localParticipant.connectionQuality)
        setAudioPlaybackBlocked(!room.canPlaybackAudio)
        refreshMountedParticipants()
        reportConnected()
      } catch (exception) {
        console.error('Meeting connection failed.', exception)
        if (!isCurrentLifecycle()) return
        clearLocal()
        setConnection('FAILED')
        setError(tRef.current('collaborationHub.callConnectionFailed'))
      }
    }

    void connect()
    return () => {
      mounted = false
      intentionalDisconnect = true
      mediaOperationGate.stopLifecycle(lifecycleGeneration)
      clearLocal()
      clearQueuedWhiteboard()
      mediaTiles.forEach(tile => tile.remove())
      mediaTiles.clear()
      reportDisconnected()
      if (resyncRemoteSubscriptionsRef.current === resyncRemoteSubscriptions) {
        resyncRemoteSubscriptionsRef.current = () => undefined
      }
      if (roomRef.current === room) roomRef.current = null
      void room.disconnect()
      room.removeAllListeners()
    }
  }, [appendChatMessage, clearQueuedWhiteboard, discardLocalTrack, enqueueAttendance, initialMedia.camera, initialMedia.cameraDeviceId, initialMedia.microphone, initialMedia.microphoneDeviceId, initialMedia.speakerDeviceId, join.accessToken, join.capabilities.camera, join.capabilities.chat, join.capabilities.ephemeralWhiteboard, join.capabilities.microphone, join.participant.displayName, join.participant.id, join.serverUrl, queueWhiteboardStroke, refreshParticipants])

  useEffect(() => {
    resyncRemoteSubscriptionsRef.current()
    refreshParticipants()
  }, [participantIndex, refreshParticipants])

  useEffect(() => {
    refreshParticipants()
  }, [refreshParticipants, t])

  const toggleMicrophone = useCallback(async () => {
    if (!join.capabilities.microphone) return
    const gate = mediaOperationGateRef.current
    const generation = gate.currentGeneration()
    if (!gate.tryStart('MICROPHONE', generation)) return
    const room = roomRef.current
    if (!room) { gate.finish('MICROPHONE', generation); return }
    const isCurrent = () => gate.isCurrent(generation) && roomRef.current === room
    let createdTrack: LocalAudioTrack | null = null
    try {
      if (!microphoneRef.current) {
        createdTrack = await createLocalAudioTrack(selectedDeviceConstraint(initialMedia.microphoneDeviceId))
        if (!isCurrent()) {
          disposeLocalMeetingTrack(createdTrack, mediaTilesRef.current)
          return
        }
        microphoneRef.current = createdTrack
        localTracksRef.current.push(createdTrack)
        await room.localParticipant.publishTrack(createdTrack)
        if (!isCurrent()) {
          discardLocalTrack(createdTrack, room, true)
          return
        }
        setMicrophoneEnabled(true)
        refreshParticipants(room)
        return
      }
      const track = microphoneRef.current
      if (microphoneEnabled) await track.mute()
      else await track.unmute()
      if (!isCurrent()) return
      setMicrophoneEnabled(!microphoneEnabled)
      refreshParticipants(room)
    } catch {
      if (createdTrack) discardLocalTrack(createdTrack, room, true)
      if (isCurrent()) setError(t('collaborationMeetings.mediaPermissionFailed'))
    } finally {
      gate.finish('MICROPHONE', generation)
    }
  }, [discardLocalTrack, initialMedia.microphoneDeviceId, join.capabilities.microphone, microphoneEnabled, refreshParticipants, t])

  const toggleCamera = useCallback(async () => {
    if (!join.capabilities.camera) return
    const gate = mediaOperationGateRef.current
    const generation = gate.currentGeneration()
    if (!gate.tryStart('CAMERA', generation)) return
    const room = roomRef.current
    if (!room) { gate.finish('CAMERA', generation); return }
    const isCurrent = () => gate.isCurrent(generation) && roomRef.current === room
    let createdTrack: LocalVideoTrack | null = null
    try {
      if (!cameraRef.current) {
        createdTrack = await createLocalVideoTrack(selectedDeviceConstraint(initialMedia.cameraDeviceId))
        if (!isCurrent()) {
          disposeLocalMeetingTrack(createdTrack, mediaTilesRef.current)
          return
        }
        cameraRef.current = createdTrack
        localTracksRef.current.push(createdTrack)
        await room.localParticipant.publishTrack(createdTrack)
        if (!isCurrent()) {
          discardLocalTrack(createdTrack, room, true)
          return
        }
        const element = createdTrack.attach()
        element.className = 'meeting-room-media meeting-room-media--local'
        element.autoplay = true
        element.muted = true
        if (element instanceof HTMLVideoElement) element.playsInline = true
        element.dataset.localMeetingMedia = 'camera'
        appendMediaTile(stageRef.current, mediaTilesRef.current, element, join.participant.displayName, false, join.participant.id)
        setCameraEnabled(true)
        resyncRemoteSubscriptionsRef.current()
        refreshParticipants(room)
        return
      }
      const track = cameraRef.current
      if (cameraEnabled) await track.mute()
      else await track.unmute()
      if (!isCurrent()) return
      const localCameraTile = mediaTilesRef.current.get(mediaTileKey(join.participant.id, 'camera'))
      if (localCameraTile) localCameraTile.hidden = cameraEnabled
      setCameraEnabled(!cameraEnabled)
      resyncRemoteSubscriptionsRef.current()
      refreshParticipants(room)
    } catch {
      if (createdTrack) discardLocalTrack(createdTrack, room, true)
      if (isCurrent()) setError(t('collaborationMeetings.mediaPermissionFailed'))
    } finally {
      gate.finish('CAMERA', generation)
    }
  }, [cameraEnabled, discardLocalTrack, initialMedia.cameraDeviceId, join.capabilities.camera, join.participant.displayName, join.participant.id, refreshParticipants, t])

  const toggleScreenShare = useCallback(async () => {
    const room = roomRef.current
    if (!room || !join.capabilities.screenShare || !navigator.mediaDevices?.getDisplayMedia) {
      setError(t('collaborationMeetings.shareUnavailable'))
      return
    }
    const gate = mediaOperationGateRef.current
    const generation = gate.currentGeneration()
    if (!gate.tryStart('SCREEN_SHARE', generation)) return
    const isCurrent = () => gate.isCurrent(generation) && roomRef.current === room
    let createdTrack: LocalTrack | null = null
    try {
      if (screenSharing) {
        await room.localParticipant.setScreenShareEnabled(false)
        if (!isCurrent()) return
        if (screenTrackRef.current) discardLocalTrack(screenTrackRef.current, room, false)
        stageRef.current?.querySelectorAll('[data-local-meeting-media="screen"]').forEach(element => removeMediaElement(element, mediaTilesRef.current))
        setScreenSharing(false)
        resyncRemoteSubscriptionsRef.current()
        refreshParticipants(room)
        return
      }
      const publication = await room.localParticipant.setScreenShareEnabled(true, {
        audio: true,
        contentHint: 'detail',
        selfBrowserSurface: 'exclude',
        surfaceSwitching: 'include',
        systemAudio: 'include',
      })
      const screenTrack = publication?.track
      if (!screenTrack) throw new Error('SCREEN_SHARE_TRACK_UNAVAILABLE')
      createdTrack = screenTrack
      if (!isCurrent()) {
        disposeLocalMeetingTrack(screenTrack, mediaTilesRef.current)
        await room.localParticipant.setScreenShareEnabled(false).catch(() => undefined)
        return
      }
      screenTrackRef.current = screenTrack
      localTracksRef.current.push(screenTrack)
      const element = screenTrack.attach()
      element.className = 'meeting-room-media meeting-room-media--screen'
      element.autoplay = true
      element.muted = true
      if (element instanceof HTMLVideoElement) element.playsInline = true
      element.dataset.localMeetingMedia = 'screen'
      appendMediaTile(stageRef.current, mediaTilesRef.current, element, join.participant.displayName, true, join.participant.id)
      screenTrack.once('ended', () => {
        localTracksRef.current = localTracksRef.current.filter(item => item !== screenTrack)
        if (screenTrackRef.current === screenTrack) screenTrackRef.current = null
        removeMediaElement(element, mediaTilesRef.current)
        if (!isCurrent()) return
        setScreenSharing(false)
        resyncRemoteSubscriptionsRef.current()
        refreshParticipants(room)
      })
      setScreenSharing(true)
      resyncRemoteSubscriptionsRef.current()
      refreshParticipants(room)
    } catch (exception) {
      if (createdTrack) discardLocalTrack(createdTrack, room, true)
      if (!screenSharing) await room.localParticipant.setScreenShareEnabled(false).catch(() => undefined)
      if (isCurrent()) {
        console.error('Meeting screen sharing failed.', exception)
        if (!screenSharing) setScreenSharing(false)
        setError(t('collaborationMeetings.screenShareFailed'))
      }
    } finally {
      gate.finish('SCREEN_SHARE', generation)
    }
  }, [discardLocalTrack, join.capabilities.screenShare, join.participant.displayName, join.participant.id, refreshParticipants, screenSharing, t])

  async function enableAudioPlayback() {
    try { await roomRef.current?.startAudio(); setAudioPlaybackBlocked(false) }
    catch { setError(t('collaborationHub.callAudioPlaybackFailed')) }
  }

  async function sendChat(event: FormEvent) {
    event.preventDefault()
    const text = chatDraft.trim()
    const room = roomRef.current
    if (!room || !join.capabilities.chat || !text) return
    const item: MeetingChatMessage = {
      id: crypto.randomUUID(),
      sender: join.participant.displayName,
      text: text.slice(0, 2000),
      sentAtUtc: new Date().toISOString(),
    }
    try {
      await room.localParticipant.publishData(new TextEncoder().encode(JSON.stringify(item)), { reliable: true, topic: meetingChatTopic })
      appendChatMessage(item)
      setChatDraft('')
    } catch { setError(t('collaborationMeetings.serviceUnavailable')) }
  }

  function whiteboardPoint(event: ReactPointerEvent<SVGSVGElement>) {
    const bounds = event.currentTarget.getBoundingClientRect()
    return {
      x: Math.min(1, Math.max(0, (event.clientX - bounds.left) / bounds.width)),
      y: Math.min(1, Math.max(0, (event.clientY - bounds.top) / bounds.height)),
    }
  }

  function startWhiteboardStroke(event: ReactPointerEvent<SVGSVGElement>) {
    if (!join.capabilities.ephemeralWhiteboard) return
    event.currentTarget.setPointerCapture(event.pointerId)
    drawingPointRef.current = whiteboardPoint(event)
  }

  function continueWhiteboardStroke(event: ReactPointerEvent<SVGSVGElement>) {
    const from = drawingPointRef.current
    const room = roomRef.current
    if (!from || !room || !join.capabilities.ephemeralWhiteboard) return
    const now = performance.now()
    if (!canEmitWhiteboardUpdate(lastWhiteboardOutboundAtRef.current, now)) return
    lastWhiteboardOutboundAtRef.current = now
    const to = whiteboardPoint(event)
    const stroke: WhiteboardStroke = {
      id: crypto.randomUUID(), fromX: from.x, fromY: from.y, toX: to.x, toY: to.y,
      author: join.participant.displayName,
    }
    drawingPointRef.current = to
    queueWhiteboardStroke(stroke)
    void room.localParticipant.publishData(new TextEncoder().encode(JSON.stringify({ ...stroke, type: 'stroke' })), {
      reliable: false, topic: meetingWhiteboardTopic,
    }).catch(() => setError(t('collaborationMeetings.serviceUnavailable')))
  }

  function stopWhiteboardStroke(event: ReactPointerEvent<SVGSVGElement>) {
    drawingPointRef.current = null
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
  }

  async function clearWhiteboard() {
    if (!roomRef.current || !join.capabilities.ephemeralWhiteboard) return
    clearQueuedWhiteboard()
    try {
      await roomRef.current.localParticipant.publishData(new TextEncoder().encode(JSON.stringify({ type: 'clear' })), {
        reliable: true, topic: meetingWhiteboardTopic,
      })
    } catch { setError(t('collaborationMeetings.serviceUnavailable')) }
  }

  async function updateWaitingParticipant(participant: MeetingParticipant, action: 'admit' | 'deny') {
    const callback = action === 'admit' ? onAdmit : onDeny
    if (!callback || participantActionBusy) return
    setParticipantActionBusy(`${action}:${participant.id}`)
    try {
      await callback(participant)
    } finally {
      setParticipantActionBusy('')
    }
  }

  async function createGuestInvite(event: FormEvent) {
    event.preventDefault()
    const name = inviteName.trim()
    const expiryHours = Number(inviteExpiryHours)
    if (!canInviteGuests || !onCreateGuestInvite || !name || ![1, 24, 168].includes(expiryHours)) return
    setInviteBusy(true)
    setInviteCopied(false)
    setError('')
    try {
      const result = await onCreateGuestInvite(name, expiryHours)
      setInviteResult(result)
      setInviteName('')
    } catch {
      setError(t('collaborationMeetings.serviceUnavailable'))
    } finally {
      setInviteBusy(false)
    }
  }

  const inviteLink = useMemo(() => {
    if (!inviteResult) return ''
    try {
      return buildGuestMeetingInviteUrl(
        inviteResult.inviteToken,
        window.location.origin,
        import.meta.env.VITE_PUBLIC_APP_ORIGIN,
      )
    } catch {
      return ''
    }
  }, [inviteResult])

  async function copyGuestInvite() {
    if (!inviteLink) return
    try {
      await navigator.clipboard.writeText(inviteLink)
      setInviteCopied(true)
    } catch {
      setInviteCopied(false)
      const inviteLinkText = inviteLinkTextRef.current
      const selection = window.getSelection()
      if (inviteLinkText && selection) {
        const range = document.createRange()
        range.selectNodeContents(inviteLinkText)
        selection.removeAllRanges()
        selection.addRange(range)
      }
    }
  }

  useEffect(() => {
    const handleMeetingShortcut = (event: KeyboardEvent) => {
      const target = event.target
      const editableTarget = target instanceof HTMLElement
        && (target.isContentEditable || target.closest('input, textarea, select, [contenteditable="true"]') !== null)
      const shortcut = resolveMeetingShortcut({
        key: event.key,
        ctrlKey: event.ctrlKey,
        shiftKey: event.shiftKey,
        altKey: event.altKey,
        metaKey: event.metaKey,
        repeat: event.repeat,
        editableTarget,
      })
      if (!shortcut || connection !== 'CONNECTED') return
      if (shortcut === 'MICROPHONE' && join.capabilities.microphone) {
        event.preventDefault()
        void toggleMicrophone()
      } else if (shortcut === 'CAMERA' && join.capabilities.camera) {
        event.preventDefault()
        void toggleCamera()
      } else if (shortcut === 'SCREEN_SHARE' && join.capabilities.screenShare) {
        event.preventDefault()
        void toggleScreenShare()
      }
    }
    window.addEventListener('keydown', handleMeetingShortcut)
    return () => window.removeEventListener('keydown', handleMeetingShortcut)
  }, [connection, join.capabilities.camera, join.capabilities.microphone, join.capabilities.screenShare, toggleCamera, toggleMicrophone, toggleScreenShare])

  function leaveMeeting() {
    if (leavingRef.current) return
    leavingRef.current = true
    onLeave()
  }

  const connected = connection === 'CONNECTED'
  const qualityPresentation = connectionQualityPresentation(connectionQuality)
  const stageParticipantPresentation = selectStageParticipantPlaceholders(
    participants.filter(participant => participant.connected),
    MAX_STAGE_VISUAL_TILES,
    stageVisualTiles,
  )
  const visibleParticipants = useMemo(() => participants.filter(participant => participantMatchesMeetingSearch(
    participant.name,
    t(meetingRoleTranslationKey(participant.role)),
    participantSearch,
  )), [participantSearch, participants, t])
  const connectedParticipantCount = participants.filter(participant => participant.connected).length
  const offlineParticipantCount = participants.length - connectedParticipantCount
  return <div className={`meeting-room-overlay${minimized ? ' is-minimized' : ''}`} role="presentation">
    <section className="meeting-room" role="dialog" aria-modal={!minimized} aria-labelledby="meeting-room-title">
      <header className="meeting-room__header">
        <div><span><ShieldCheck aria-hidden="true" />{t('collaborationMeetings.roomTitle')}</span><h1 id="meeting-room-title">{join.meeting.title}</h1></div>
        <div>
          <StatusBadge text={connection === 'CONNECTED' ? t('collaborationMeetings.connected') : connection === 'RECONNECTING' ? t('collaborationMeetings.reconnecting') : connection === 'FAILED' ? t('collaborationMeetings.disconnected') : t('collaborationMeetings.connecting')} tone={connected ? 'positive' : connection === 'FAILED' ? 'critical' : 'information'} />
          {connected && <div className="meeting-room__quality" role="status" aria-live="polite" aria-label={t('collaborationMeetings.connectionQuality')}>
            <StatusBadge text={t(qualityPresentation.labelKey)} tone={qualityPresentation.tone} />
            <small>{t(qualityPresentation.guidanceKey)}</small>
          </div>}
          <span className="meeting-room__privacy"><LockKeyhole aria-hidden="true" />{t('collaborationMeetings.recordingUnavailable')}</span>
          {minimized ? <button type="button" className="meeting-room__window-action" onClick={onRestore} aria-label={t('collaborationMeetings.restoreMeeting')}><Maximize2 /></button> : onMinimize && <button type="button" className="meeting-room__window-action" onClick={onMinimize} aria-label={t('collaborationMeetings.minimizeMeeting')}><Minimize2 /></button>}
        </div>
      </header>
      <p className="meeting-room__chat-announcement" role="status" aria-live="polite" aria-atomic="true">{chatAnnouncement?.text ?? ''}</p>

      <div className={`meeting-room__body${sidePanel ? ' has-panel' : ''}`}>
        <div className="meeting-room__stage" ref={stageRef} aria-label={t('collaborationMeetings.devicePreview')}>
          {!connected && <div className="meeting-room__connection" role="status"><span className="ui-spinner" aria-hidden="true" /><strong>{connection === 'FAILED' ? error : connection === 'RECONNECTING' ? t('collaborationMeetings.reconnecting') : t('collaborationMeetings.connecting')}</strong></div>}
          {connected && stageParticipantPresentation.visibleParticipants.map(participant => <div className="meeting-room__identity-tile" key={`identity-${participant.key}`} data-meeting-participant-placeholder={participant.key}><span>{participant.name.trim().charAt(0).toUpperCase() || 'H'}</span><strong>{participant.name}{participant.isLocal ? ` (${t('collaborationMeetings.you')})` : ''}</strong><small>{t(meetingRoleTranslationKey(participant.role))} · {t('collaborationMeetings.microphone')}: {t(participant.microphone ? 'collaborationMeetings.enabled' : 'collaborationMeetings.disabled')} · {t('collaborationMeetings.camera')}: {t(participant.camera ? 'collaborationMeetings.cameraSubscriptionPaused' : 'collaborationMeetings.disabled')}</small></div>)}
          {connected && stageParticipantPresentation.summarizedParticipantCount > 0 && <div className="meeting-room__identity-tile meeting-room__identity-tile--summary" role="status" data-meeting-participant-summary="true"><span><Users aria-hidden="true" /></span><strong>{t('collaborationMeetings.additionalParticipants', { count: stageParticipantPresentation.summarizedParticipantCount })}</strong><small>{t('collaborationMeetings.additionalParticipantsHint')}</small></div>}
          {error && connected && <div className="meeting-room__message"><MessageStrip tone="warning">{error}</MessageStrip></div>}
          {audioPlaybackBlocked && <div className="meeting-room__message"><MessageStrip tone="warning"><span>{t('collaborationHub.callAudioPlaybackBlocked')}</span><button type="button" className="button button--quiet" onClick={() => void enableAudioPlayback()}><Volume2 />{t('collaborationMeetings.enableAudio')}</button></MessageStrip></div>}
        </div>

        {sidePanel === 'PEOPLE' && <aside className="meeting-room__side meeting-room__people" aria-label={t('collaborationMeetings.people.title')}>
          <header className="meeting-room__people-header"><div className="meeting-room__people-header-copy"><h2>{t('collaborationMeetings.people.title')}</h2><small>{t('collaborationMeetings.people.participantCount', { count: participants.length })}</small></div><button type="button" className="button button--icon" onClick={() => setSidePanel(null)} aria-label={t('common.close')}><PanelRightClose /></button></header>
          <div className="meeting-room__people-counts" role="status" aria-live="polite">
            <span className="meeting-room__people-count is-connected">{t('collaborationMeetings.people.connectedCount', { count: connectedParticipantCount })}</span>
            <span className="meeting-room__people-count">{t('collaborationMeetings.people.offlineCount', { count: offlineParticipantCount })}</span>
            <span className="meeting-room__people-count is-waiting">{t('collaborationMeetings.people.waitingCount', { count: waitingParticipants.length })}</span>
          </div>
          <label className="meeting-room__participant-search">
            <span>{t('collaborationMeetings.searchRoomParticipants')}</span>
            <span><Search aria-hidden="true" /><input type="search" value={participantSearch} placeholder={t('collaborationMeetings.searchRoomParticipantsPlaceholder')} onChange={event => setParticipantSearch(event.target.value)} /></span>
          </label>
          <div className="meeting-room__people-scroll">
            <ul className="meeting-room__people-list">{visibleParticipants.map(participant => {
              const connectionLabel = t(`collaborationMeetings.people.connection.${participant.connected ? 'connected' : 'offline'}`)
              const microphoneLabel = t(`collaborationMeetings.people.media.${participant.microphone ? 'microphoneOn' : 'microphoneOff'}`)
              const cameraLabel = t(`collaborationMeetings.people.media.${participant.camera ? 'cameraOn' : 'cameraOff'}`)
              const screenShareLabel = t(`collaborationMeetings.people.media.${participant.screenShare ? 'screenShareOn' : 'screenShareOff'}`)
              return <li className="meeting-room__people-person" key={participant.key}>
                <span className={`meeting-room__people-avatar${participant.connected ? ' is-connected' : ''}`} title={connectionLabel}>{participant.name.trim().charAt(0).toUpperCase() || 'H'}</span>
                <span className="meeting-room__people-identity"><strong>{participant.name}{participant.isLocal ? ` (${t('collaborationMeetings.you')})` : ''}</strong><small>{t(meetingRoleTranslationKey(participant.role))} · {connectionLabel}</small><span className={`meeting-room__people-admission is-${participant.admissionStatus.toLowerCase()}`}>{t(`collaborationMeetings.people.admission.${participant.admissionStatus.toLowerCase()}`)}</span></span>
                <span className="meeting-room__people-media">
                  <span className={`meeting-room__people-media-status ${participant.microphone ? 'is-active' : 'is-off'}`} role="img" aria-label={microphoneLabel} title={microphoneLabel}>{participant.microphone ? <Mic aria-hidden="true" /> : <MicOff aria-hidden="true" />}<span>{microphoneLabel}</span></span>
                  <span className={`meeting-room__people-media-status ${participant.camera ? 'is-active' : 'is-off'}`} role="img" aria-label={cameraLabel} title={cameraLabel}>{participant.camera ? <Video aria-hidden="true" /> : <VideoOff aria-hidden="true" />}<span>{cameraLabel}</span></span>
                  <span className={`meeting-room__people-media-status ${participant.screenShare ? 'is-active' : 'is-off'}`} role="img" aria-label={screenShareLabel} title={screenShareLabel}>{participant.screenShare ? <MonitorUp aria-hidden="true" /> : <MonitorOff aria-hidden="true" />}<span>{screenShareLabel}</span></span>
                </span>
              </li>
            })}</ul>
            {visibleParticipants.length === 0 && <p className="meeting-room__people-empty">{t('collaborationMeetings.noRoomParticipants')}</p>}
            {canManageParticipants && <section className="meeting-room__people-waiting"><header><h3>{t('collaborationMeetings.waitingRoom')}</h3><span>{t('collaborationMeetings.people.waitingCount', { count: waitingParticipants.length })}</span></header>{waitingParticipants.length === 0 ? <p>{t('collaborationMeetings.noWaitingGuests')}</p> : <div className="meeting-room__people-waiting-list">{waitingParticipants.map(participant => {
              const admitBusy = participantActionBusy === `admit:${participant.id}`
              const denyBusy = participantActionBusy === `deny:${participant.id}`
              return <div className="meeting-room__people-waiting-person" key={participant.id}><span className="meeting-room__people-identity"><strong>{participant.displayName}</strong><small>{t(`collaborationMeetings.people.admission.${participant.admissionStatus.toLowerCase()}`)}</small></span><span className="meeting-room__people-waiting-actions"><button type="button" className="button button--primary" disabled={Boolean(participantActionBusy)} onClick={() => void updateWaitingParticipant(participant, 'admit')}>{admitBusy ? t('common.loading') : t('collaborationMeetings.admit')}</button><button type="button" className="button" disabled={Boolean(participantActionBusy)} onClick={() => void updateWaitingParticipant(participant, 'deny')}>{denyBusy ? t('common.loading') : t('collaborationMeetings.deny')}</button></span></div>
            })}</div>}</section>}
            {canInviteGuests && onCreateGuestInvite && <details className="meeting-room__people-invite"><summary><span>{t('collaborationMeetings.people.invite.title')}</span><Plus aria-hidden="true" /></summary><div className="meeting-room__people-invite-body"><p className="meeting-room__people-invite-hint">{t('collaborationMeetings.people.invite.hint')}</p><form className="meeting-room__people-invite-form" onSubmit={event => void createGuestInvite(event)}><label><span>{t('collaborationMeetings.people.invite.displayName')}</span><input value={inviteName} maxLength={160} required autoComplete="off" onChange={event => { setInviteName(event.target.value); setInviteResult(null); setInviteCopied(false) }} /></label><label><span>{t('collaborationMeetings.people.invite.expiryLabel')}</span><select value={inviteExpiryHours} onChange={event => setInviteExpiryHours(event.target.value)}><option value="1">{t('collaborationMeetings.people.invite.expiryOneHour')}</option><option value="24">{t('collaborationMeetings.people.invite.expiryOneDay')}</option><option value="168">{t('collaborationMeetings.people.invite.expirySevenDays')}</option></select></label><button type="submit" className="button button--primary" disabled={inviteBusy || !inviteName.trim()}><Plus aria-hidden="true" />{t(inviteBusy ? 'collaborationMeetings.people.invite.creating' : 'collaborationMeetings.people.invite.create')}</button></form>{inviteResult && inviteLink && <><div className="meeting-room__people-invite-result" role="status"><code ref={inviteLinkTextRef}>{inviteLink}</code><button type="button" className="button" onClick={() => void copyGuestInvite()}>{inviteCopied ? <Check aria-hidden="true" /> : <ClipboardCopy aria-hidden="true" />}{t(inviteCopied ? 'collaborationMeetings.people.invite.copied' : 'collaborationMeetings.people.invite.copyLink')}</button></div><small className="meeting-room__people-invite-secret">{t('collaborationMeetings.people.invite.secretHint')}</small></>}</div></details>}
          </div>
        </aside>}

        {sidePanel === 'CHAT' && <aside className="meeting-room__side meeting-room__chat" aria-label={t('collaborationMeetings.chatPanel')}>
          <header><h2>{t('collaborationMeetings.chatPanel')}</h2><button type="button" className="button button--icon" onClick={() => setSidePanel(null)} aria-label={t('common.close')}><PanelRightClose /></button></header>
          <small>{t('collaborationMeetings.ephemeralChatHint')}</small>
          <div className="meeting-room__chat-stream" role="log" aria-live="off" aria-relevant="additions" aria-label={t('collaborationMeetings.chatPanel')} tabIndex={0}>{chatMessages.length === 0 ? <p>{t('collaborationMeetings.noChatMessages')}</p> : chatMessages.map(item => <article key={item.id}><strong>{item.sender}</strong><p dir="auto">{item.text}</p><time dateTime={item.sentAtUtc} dir="ltr">{formatMeetingChatTime(item.sentAtUtc, i18n.resolvedLanguage ?? i18n.language)}</time></article>)}</div>
          <form onSubmit={event => void sendChat(event)}><textarea value={chatDraft} maxLength={2000} placeholder={t('collaborationMeetings.messagePlaceholder')} aria-label={t('collaborationMeetings.messagePlaceholder')} onChange={event => setChatDraft(event.target.value)} /><button type="submit" className="button button--primary" disabled={!chatDraft.trim()}><Send />{t('collaborationMeetings.sendMessage')}</button></form>
        </aside>}

        {sidePanel === 'WHITEBOARD' && <aside className="meeting-room__side meeting-room__whiteboard" aria-label={t('collaborationMeetings.whiteboardPanel')}>
          <header><div><h2>{t('collaborationMeetings.whiteboardPanel')}</h2><small>{t('collaborationMeetings.ephemeralWhiteboardHint')}</small></div><button type="button" className="button button--icon" onClick={() => setSidePanel(null)} aria-label={t('common.close')}><PanelRightClose /></button></header>
          <svg className="meeting-room__whiteboard-canvas" viewBox="0 0 1000 600" role="img" aria-label={t('collaborationMeetings.whiteboardCanvas')} onPointerDown={startWhiteboardStroke} onPointerMove={continueWhiteboardStroke} onPointerUp={stopWhiteboardStroke} onPointerCancel={stopWhiteboardStroke}>
            {whiteboardStrokes.map(stroke => <line key={stroke.id} x1={stroke.fromX * 1000} y1={stroke.fromY * 600} x2={stroke.toX * 1000} y2={stroke.toY * 600}><title>{stroke.author}</title></line>)}
          </svg>
          <button type="button" className="button" onClick={() => void clearWhiteboard()}><Trash2 />{t('collaborationMeetings.clearWhiteboard')}</button>
        </aside>}
      </div>

      <footer className="meeting-room__footer">
        <div className="meeting-room__controls">
          <button type="button" className="meeting-room-control" disabled={!connected || !join.capabilities.microphone} aria-pressed={microphoneEnabled} aria-keyshortcuts="Control+D" onClick={() => void toggleMicrophone()} title={`${t('collaborationMeetings.microphone')} · ${t('collaborationMeetings.shortcutMicrophone')}`}>{microphoneEnabled ? <Mic /> : <MicOff />}<span>{t('collaborationMeetings.microphone')}</span><kbd>Ctrl+D</kbd></button>
          <button type="button" className="meeting-room-control" disabled={!connected || !join.capabilities.camera} aria-pressed={cameraEnabled} aria-keyshortcuts="Control+E" onClick={() => void toggleCamera()} title={`${t('collaborationMeetings.camera')} · ${t('collaborationMeetings.shortcutCamera')}`}>{cameraEnabled ? <Video /> : <VideoOff />}<span>{t('collaborationMeetings.camera')}</span><kbd>Ctrl+E</kbd></button>
          <button type="button" className="meeting-room-control" disabled={!connected || !join.capabilities.screenShare} aria-pressed={screenSharing} aria-keyshortcuts="Control+Shift+S" onClick={() => void toggleScreenShare()} title={`${t(screenSharing ? 'collaborationMeetings.stopScreenShare' : 'collaborationMeetings.startScreenShare')} · ${t('collaborationMeetings.shortcutScreenShare')}`}>{screenSharing ? <Square /> : <MonitorUp />}<span>{t(screenSharing ? 'collaborationMeetings.stopScreenShare' : 'collaborationMeetings.startScreenShare')}</span><kbd>Ctrl+Shift+S</kbd></button>
          <button type="button" className={`meeting-room-control${sidePanel === 'PEOPLE' ? ' is-active' : ''}`} aria-pressed={sidePanel === 'PEOPLE'} onClick={() => setSidePanel(value => value === 'PEOPLE' ? null : 'PEOPLE')}><Users /><span>{t('collaborationMeetings.peoplePanel')}</span>{waitingParticipants.length > 0 && <span className="meeting-room-control__badge" aria-label={t('collaborationMeetings.people.waitingCount', { count: waitingParticipants.length })}>{waitingParticipants.length}</span>}</button>
          {join.capabilities.chat && <button type="button" className={`meeting-room-control${sidePanel === 'CHAT' ? ' is-active' : ''}`} aria-pressed={sidePanel === 'CHAT'} onClick={() => setSidePanel(value => value === 'CHAT' ? null : 'CHAT')}><MessageSquareText /><span>{t('collaborationMeetings.chatPanel')}</span></button>}
          {join.capabilities.ephemeralWhiteboard && <button type="button" className={`meeting-room-control${sidePanel === 'WHITEBOARD' ? ' is-active' : ''}`} aria-pressed={sidePanel === 'WHITEBOARD'} onClick={() => setSidePanel(value => value === 'WHITEBOARD' ? null : 'WHITEBOARD')}><Pencil /><span>{t('collaborationMeetings.whiteboardPanel')}</span></button>}
        </div>
        <div className="meeting-room__exit"><button type="button" className="button" onClick={leaveMeeting}><PhoneOff />{t('collaborationMeetings.leave')}</button>{canEndMeeting && onEnd && <button type="button" className="button button--danger" onClick={onEnd}><PhoneOff />{t('collaborationMeetings.end')}</button>}</div>
      </footer>
    </section>
  </div>
}
