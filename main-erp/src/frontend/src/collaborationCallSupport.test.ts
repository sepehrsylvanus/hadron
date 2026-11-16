import assert from 'node:assert/strict'
import test from 'node:test'
import {
  assessCallReadiness,
  callBootstrapFailureTranslationKey,
  callMediaFailureTranslationKey,
  callReadinessTranslationKey,
  type BrowserCallEnvironment,
} from './collaborationCallSupport.ts'

const secureBrowser: BrowserCallEnvironment = {
  secureContext: true,
  mediaDevicesAvailable: true,
  pageProtocol: 'https:',
}

test('call readiness requires an available provider', () => {
  const result = assessCallReadiness({ available: false, serverUrl: null }, secureBrowser)
  assert.deepEqual(result, { ready: false, code: 'PROVIDER_UNAVAILABLE' })
})

test('call readiness rejects LAN HTTP before a call session is created', () => {
  const result = assessCallReadiness(
    { available: true, serverUrl: 'ws://192.168.0.175:7880' },
    { secureContext: false, mediaDevicesAvailable: false, pageProtocol: 'http:' },
  )
  assert.deepEqual(result, { ready: false, code: 'INSECURE_CONTEXT' })
  assert.equal(callReadinessTranslationKey(result.code), 'collaborationHub.callRequiresHttps')
})

test('call readiness allows loopback development when the browser grants media APIs', () => {
  const result = assessCallReadiness(
    { available: true, serverUrl: 'ws://127.0.0.1:7880' },
    { secureContext: true, mediaDevicesAvailable: true, pageProtocol: 'http:' },
  )
  assert.deepEqual(result, { ready: true, code: 'READY' })
})

test('call readiness blocks mixed-content signaling on HTTPS', () => {
  const result = assessCallReadiness({ available: true, serverUrl: 'ws://rtc.hadron.test' }, secureBrowser)
  assert.deepEqual(result, { ready: false, code: 'SECURE_SIGNALING_REQUIRED' })
})

test('call readiness accepts secure signaling on HTTPS', () => {
  assert.deepEqual(
    assessCallReadiness({ available: true, serverUrl: 'wss://rtc.hadron.test' }, secureBrowser),
    { ready: true, code: 'READY' },
  )
})

test('media failures are converted into actionable localized messages', () => {
  assert.equal(callMediaFailureTranslationKey({ name: 'NotAllowedError' }), 'collaborationHub.callMediaPermissionDenied')
  assert.equal(callMediaFailureTranslationKey({ name: 'NotFoundError' }), 'collaborationHub.callMediaDeviceMissing')
  assert.equal(callMediaFailureTranslationKey({ name: 'NotReadableError' }), 'collaborationHub.callMediaDeviceBusy')
  assert.equal(callMediaFailureTranslationKey({ name: 'OverconstrainedError' }), 'collaborationHub.callMediaUnavailable')
  assert.equal(callMediaFailureTranslationKey(new Error('network')), 'collaborationHub.callMediaAccessFailed')
})

test('bootstrap failures distinguish signaling from local media setup', () => {
  assert.equal(
    callBootstrapFailureTranslationKey(new Error('websocket unreachable'), 'SIGNALING'),
    'collaborationHub.callConnectionFailed',
  )
  assert.equal(
    callBootstrapFailureTranslationKey({ name: 'NotAllowedError' }, 'MEDIA'),
    'collaborationHub.callMediaPermissionDenied',
  )
})
