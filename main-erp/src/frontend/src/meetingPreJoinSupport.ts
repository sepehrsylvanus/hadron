export type MeetingDeviceKind = 'audioinput' | 'audiooutput' | 'videoinput'

export type MeetingMediaDeviceLike = Readonly<{
  deviceId: string
  kind: MeetingDeviceKind
  label: string
}>

export type MeetingDeviceOption = Readonly<{
  deviceId: string
  label: string
}>

export type MeetingMediaIssue =
  | 'PERMISSION_DENIED'
  | 'DEVICE_MISSING'
  | 'DEVICE_BUSY'
  | 'MEDIA_UNAVAILABLE'
  | 'UNKNOWN'

export type MeetingPreJoinConstraintInput = Readonly<{
  microphone: boolean
  camera: boolean
  canUseMicrophone: boolean
  canUseCamera: boolean
  microphoneDeviceId?: string
  cameraDeviceId?: string
}>

export type MeetingJoinMediaSelection = Readonly<{
  microphone: boolean
  camera: boolean
  microphoneDeviceId?: string
  cameraDeviceId?: string
  speakerDeviceId?: string
}>

type MeetingJoinMediaSelectionInput = Readonly<{
  withMedia: boolean
  microphone: boolean
  camera: boolean
  canUseMicrophone: boolean
  canUseCamera: boolean
  microphoneDeviceId?: string
  cameraDeviceId?: string
  speakerDeviceId?: string
}>

/** Produces non-empty, deterministic labels without exposing device identifiers as UI text. */
export function meetingDeviceOptions(
  devices: readonly MeetingMediaDeviceLike[],
  kind: MeetingDeviceKind,
  fallbackLabel: string,
) {
  const seen = new Set<string>()
  let fallbackIndex = 0
  return devices.flatMap<MeetingDeviceOption>(device => {
    if (device.kind !== kind || seen.has(device.deviceId)) return []
    seen.add(device.deviceId)
    fallbackIndex += 1
    return [{
      deviceId: device.deviceId,
      label: device.label.trim() || `${fallbackLabel} ${fallbackIndex}`,
    }]
  })
}

/** Capability checks are repeated here so UI state can never request a disallowed source. */
export function meetingPreJoinMediaConstraints(input: MeetingPreJoinConstraintInput) {
  return {
    audio: selectedConstraint(
      input.microphone && input.canUseMicrophone,
      input.microphoneDeviceId,
    ),
    video: selectedConstraint(
      input.camera && input.canUseCamera,
      input.cameraDeviceId,
    ),
  }
}

/** Keeps the user's tested device choices in memory for the ensuing room connection. */
export function meetingJoinMediaSelection(input: MeetingJoinMediaSelectionInput): MeetingJoinMediaSelection {
  const microphone = input.withMedia && input.microphone && input.canUseMicrophone
  const camera = input.withMedia && input.camera && input.canUseCamera
  const microphoneDeviceId = microphone ? normalizedDeviceId(input.microphoneDeviceId) : undefined
  const cameraDeviceId = camera ? normalizedDeviceId(input.cameraDeviceId) : undefined
  const speakerDeviceId = normalizedDeviceId(input.speakerDeviceId)
  return {
    microphone,
    camera,
    ...(microphoneDeviceId ? { microphoneDeviceId } : {}),
    ...(cameraDeviceId ? { cameraDeviceId } : {}),
    ...(speakerDeviceId ? { speakerDeviceId } : {}),
  }
}

export function classifyMeetingMediaIssue(error: unknown): MeetingMediaIssue {
  const candidate = error && typeof error === 'object'
    ? error as { name?: unknown; message?: unknown }
    : null
  const name = typeof candidate?.name === 'string' ? candidate.name : ''
  const message = typeof candidate?.message === 'string' ? candidate.message : ''

  if (message === 'MEDIA_UNAVAILABLE' || name === 'TypeError') return 'MEDIA_UNAVAILABLE'
  if (name === 'NotAllowedError' || name === 'SecurityError' || name === 'PermissionDismissedError')
    return 'PERMISSION_DENIED'
  if (name === 'NotFoundError' || name === 'DevicesNotFoundError' || name === 'OverconstrainedError')
    return 'DEVICE_MISSING'
  if (name === 'NotReadableError' || name === 'TrackStartError' || name === 'AbortError')
    return 'DEVICE_BUSY'
  return 'UNKNOWN'
}

export function meetingMediaIssueTranslationKey(issue: MeetingMediaIssue) {
  switch (issue) {
    case 'PERMISSION_DENIED': return 'collaborationMeetings.mediaPermissionGuidance'
    case 'DEVICE_MISSING': return 'collaborationMeetings.mediaDeviceMissingGuidance'
    case 'DEVICE_BUSY': return 'collaborationMeetings.mediaDeviceBusyGuidance'
    case 'MEDIA_UNAVAILABLE': return 'collaborationMeetings.mediaUnavailableGuidance'
    default: return 'collaborationMeetings.mediaUnknownGuidance'
  }
}

/** Converts byte time-domain samples into a calm 0-100 microphone activity indication. */
export function meetingMicrophoneLevel(samples: ArrayLike<number>) {
  if (samples.length === 0) return 0
  let squaredTotal = 0
  for (let index = 0; index < samples.length; index += 1) {
    const normalized = (samples[index] - 128) / 128
    squaredTotal += normalized * normalized
  }
  const rootMeanSquare = Math.sqrt(squaredTotal / samples.length)
  return Math.round(Math.min(1, rootMeanSquare * 5) * 100)
}

function selectedConstraint(enabled: boolean, deviceId?: string) {
  if (!enabled) return false
  return deviceId ? { deviceId: { exact: deviceId } } : true
}

function normalizedDeviceId(deviceId?: string) {
  const normalized = deviceId?.trim()
  return normalized || undefined
}
