export type CallReadinessCode =
  | 'READY'
  | 'PROVIDER_UNAVAILABLE'
  | 'INSECURE_CONTEXT'
  | 'MEDIA_UNAVAILABLE'
  | 'INVALID_SERVER_URL'
  | 'SECURE_SIGNALING_REQUIRED'

export type BrowserCallEnvironment = {
  secureContext: boolean
  mediaDevicesAvailable: boolean
  pageProtocol: string
}

export type CallProviderConfiguration = {
  available: boolean
  serverUrl?: string | null
}

export type CallReadiness = {
  ready: boolean
  code: CallReadinessCode
}

export function readBrowserCallEnvironment(): BrowserCallEnvironment {
  if (typeof window === 'undefined') {
    return { secureContext: false, mediaDevicesAvailable: false, pageProtocol: '' }
  }

  return {
    secureContext: window.isSecureContext,
    mediaDevicesAvailable: typeof window.navigator.mediaDevices?.getUserMedia === 'function',
    pageProtocol: window.location.protocol,
  }
}

export function assessCallReadiness(
  configuration: CallProviderConfiguration | null | undefined,
  environment: BrowserCallEnvironment,
): CallReadiness {
  if (!configuration?.available || !configuration.serverUrl) {
    return { ready: false, code: 'PROVIDER_UNAVAILABLE' }
  }
  if (!environment.secureContext) {
    return { ready: false, code: 'INSECURE_CONTEXT' }
  }
  if (!environment.mediaDevicesAvailable) {
    return { ready: false, code: 'MEDIA_UNAVAILABLE' }
  }

  let signalingUrl: URL
  try {
    signalingUrl = new URL(configuration.serverUrl)
  } catch {
    return { ready: false, code: 'INVALID_SERVER_URL' }
  }
  if (signalingUrl.protocol !== 'ws:' && signalingUrl.protocol !== 'wss:') {
    return { ready: false, code: 'INVALID_SERVER_URL' }
  }
  if (environment.pageProtocol === 'https:' && signalingUrl.protocol !== 'wss:') {
    return { ready: false, code: 'SECURE_SIGNALING_REQUIRED' }
  }
  return { ready: true, code: 'READY' }
}

export function callReadinessTranslationKey(code: CallReadinessCode) {
  switch (code) {
    case 'INSECURE_CONTEXT': return 'collaborationHub.callRequiresHttps'
    case 'MEDIA_UNAVAILABLE': return 'collaborationHub.callMediaUnavailable'
    case 'INVALID_SERVER_URL': return 'collaborationHub.callConfigurationInvalid'
    case 'SECURE_SIGNALING_REQUIRED': return 'collaborationHub.callRequiresSecureSignaling'
    case 'PROVIDER_UNAVAILABLE': return 'collaborationHub.callUnavailable'
    default: return 'collaborationHub.callReady'
  }
}

export function callMediaFailureTranslationKey(error: unknown) {
  const name = error instanceof DOMException
    ? error.name
    : typeof error === 'object' && error !== null && 'name' in error
      ? String((error as { name?: unknown }).name ?? '')
      : ''
  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
      return 'collaborationHub.callMediaPermissionDenied'
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return 'collaborationHub.callMediaDeviceMissing'
    case 'NotReadableError':
    case 'TrackStartError':
      return 'collaborationHub.callMediaDeviceBusy'
    case 'SecurityError':
      return 'collaborationHub.callRequiresHttps'
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
      return 'collaborationHub.callMediaUnavailable'
    default:
      return 'collaborationHub.callMediaAccessFailed'
  }
}

export function callBootstrapFailureTranslationKey(error: unknown, phase: 'SIGNALING' | 'MEDIA') {
  return phase === 'SIGNALING'
    ? 'collaborationHub.callConnectionFailed'
    : callMediaFailureTranslationKey(error)
}
