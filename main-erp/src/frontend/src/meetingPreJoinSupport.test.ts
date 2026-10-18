import assert from 'node:assert/strict'
import test from 'node:test'
import {
  classifyMeetingMediaIssue,
  meetingDeviceOptions,
  meetingJoinMediaSelection,
  meetingMediaIssueTranslationKey,
  meetingMicrophoneLevel,
  meetingPreJoinMediaConstraints,
  type MeetingMediaDeviceLike,
} from './meetingPreJoinSupport.ts'

const devices: MeetingMediaDeviceLike[] = [
  { deviceId: 'mic-default', kind: 'audioinput', label: 'Office microphone' },
  { deviceId: 'camera-usb', kind: 'videoinput', label: '' },
  { deviceId: 'speaker-default', kind: 'audiooutput', label: 'Office speaker' },
  { deviceId: 'camera-usb', kind: 'videoinput', label: 'Duplicate camera' },
]

test('device options filter, deduplicate and provide stable non-secret fallback labels', () => {
  assert.deepEqual(meetingDeviceOptions(devices, 'videoinput', 'Camera'), [
    { deviceId: 'camera-usb', label: 'Camera 1' },
  ])
  assert.deepEqual(meetingDeviceOptions(devices, 'audioinput', 'Microphone'), [
    { deviceId: 'mic-default', label: 'Office microphone' },
  ])
})

test('prejoin constraints preserve role capability enforcement', () => {
  assert.deepEqual(meetingPreJoinMediaConstraints({
    microphone: true,
    camera: true,
    canUseMicrophone: false,
    canUseCamera: true,
    microphoneDeviceId: 'forbidden-mic',
    cameraDeviceId: 'camera-usb',
  }), {
    audio: false,
    video: { deviceId: { exact: 'camera-usb' } },
  })
})

test('prejoin constraints use browser defaults until the user selects a device', () => {
  assert.deepEqual(meetingPreJoinMediaConstraints({
    microphone: true,
    camera: false,
    canUseMicrophone: true,
    canUseCamera: true,
  }), { audio: true, video: false })
})

test('join selection carries the tested devices into the room without bypassing policy', () => {
  assert.deepEqual(meetingJoinMediaSelection({
    withMedia: true,
    microphone: true,
    camera: true,
    canUseMicrophone: true,
    canUseCamera: false,
    microphoneDeviceId: ' mic-usb ',
    cameraDeviceId: 'forbidden-camera',
    speakerDeviceId: ' speaker-usb ',
  }), {
    microphone: true,
    camera: false,
    microphoneDeviceId: 'mic-usb',
    speakerDeviceId: 'speaker-usb',
  })
})

test('joining without media drops capture-device ids while retaining the selected output', () => {
  assert.deepEqual(meetingJoinMediaSelection({
    withMedia: false,
    microphone: true,
    camera: true,
    canUseMicrophone: true,
    canUseCamera: true,
    microphoneDeviceId: 'mic-usb',
    cameraDeviceId: 'camera-usb',
    speakerDeviceId: 'speaker-usb',
  }), {
    microphone: false,
    camera: false,
    speakerDeviceId: 'speaker-usb',
  })
})

test('media errors map to actionable and stable guidance categories', () => {
  assert.equal(classifyMeetingMediaIssue({ name: 'NotAllowedError' }), 'PERMISSION_DENIED')
  assert.equal(classifyMeetingMediaIssue({ name: 'NotFoundError' }), 'DEVICE_MISSING')
  assert.equal(classifyMeetingMediaIssue({ name: 'NotReadableError' }), 'DEVICE_BUSY')
  assert.equal(classifyMeetingMediaIssue(new Error('MEDIA_UNAVAILABLE')), 'MEDIA_UNAVAILABLE')
  assert.equal(classifyMeetingMediaIssue(new Error('unexpected')), 'UNKNOWN')
  assert.equal(
    meetingMediaIssueTranslationKey('PERMISSION_DENIED'),
    'collaborationMeetings.mediaPermissionGuidance',
  )
})

test('microphone meter is deterministic, bounded and silent at the center line', () => {
  assert.equal(meetingMicrophoneLevel(new Uint8Array()), 0)
  assert.equal(meetingMicrophoneLevel(new Uint8Array([128, 128, 128, 128])), 0)
  assert.equal(meetingMicrophoneLevel(new Uint8Array([0, 255])), 100)
  const moderate = meetingMicrophoneLevel(new Uint8Array([116, 128, 140, 128]))
  assert.ok(moderate > 0 && moderate < 100)
})
