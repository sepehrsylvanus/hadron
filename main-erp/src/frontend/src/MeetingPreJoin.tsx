import { Camera, CameraOff, Mic, MicOff, ShieldCheck, Volume2 } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { MeetingJoinCapabilities } from './collaborationMeetingSupport'
import {
  classifyMeetingMediaIssue,
  meetingDeviceOptions,
  meetingJoinMediaSelection,
  meetingMediaIssueTranslationKey,
  meetingMicrophoneLevel,
  meetingPreJoinMediaConstraints,
  type MeetingDeviceOption,
  type MeetingJoinMediaSelection,
} from './meetingPreJoinSupport'
import { MessageStrip } from './ui'

type Props = {
  meetingTitle: string
  displayName: string
  capabilities: MeetingJoinCapabilities
  onCancel: () => void
  onJoin: (selection: MeetingJoinMediaSelection) => void
}

type OutputSelectableAudioContext = AudioContext & {
  setSinkId?: (sinkId: string) => Promise<void>
}

export default function MeetingPreJoin({ meetingTitle, displayName, capabilities, onCancel, onJoin }: Props) {
  const { t } = useTranslation()
  const [microphone, setMicrophone] = useState(capabilities.microphone)
  const [camera, setCamera] = useState(capabilities.camera)
  const [microphones, setMicrophones] = useState<MeetingDeviceOption[]>([])
  const [cameras, setCameras] = useState<MeetingDeviceOption[]>([])
  const [speakers, setSpeakers] = useState<MeetingDeviceOption[]>([])
  const [selectedMicrophoneId, setSelectedMicrophoneId] = useState('')
  const [selectedCameraId, setSelectedCameraId] = useState('')
  const [selectedSpeakerId, setSelectedSpeakerId] = useState('')
  const [devicesEnumerated, setDevicesEnumerated] = useState(false)
  const [testing, setTesting] = useState(false)
  const [speakerTesting, setSpeakerTesting] = useState(false)
  const [tested, setTested] = useState(false)
  const [hasVideoPreview, setHasVideoPreview] = useState(false)
  const [microphoneLevel, setMicrophoneLevel] = useState(0)
  const [meterUnavailable, setMeterUnavailable] = useState(false)
  const [speakerNotice, setSpeakerNotice] = useState('')
  const [error, setError] = useState('')
  const dialogRef = useRef<HTMLElement>(null)
  const onCancelRef = useRef(onCancel)
  const streamRef = useRef<MediaStream | null>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const meterContextRef = useRef<AudioContext | null>(null)
  const meterSourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const meterAnalyserRef = useRef<AnalyserNode | null>(null)
  const meterFrameRef = useRef<number | null>(null)
  const speakerContextRef = useRef<AudioContext | null>(null)
  const speakerOscillatorRef = useRef<OscillatorNode | null>(null)
  const speakerGainRef = useRef<GainNode | null>(null)
  const speakerFallbackTimerRef = useRef<number | null>(null)
  const deviceTestVersionRef = useRef(0)
  const speakerTestVersionRef = useRef(0)
  const microphoneEnabledRef = useRef(microphone)

  const stopMicrophoneMeter = useCallback((resetLevel: boolean) => {
    if (meterFrameRef.current !== null) window.cancelAnimationFrame(meterFrameRef.current)
    meterFrameRef.current = null
    meterSourceRef.current?.disconnect()
    meterAnalyserRef.current?.disconnect()
    meterSourceRef.current = null
    meterAnalyserRef.current = null
    const context = meterContextRef.current
    meterContextRef.current = null
    if (context && context.state !== 'closed') void context.close().catch(() => undefined)
    if (resetLevel) setMicrophoneLevel(0)
  }, [])

  const stopMediaPreview = useCallback((resetLevel: boolean) => {
    stopMicrophoneMeter(resetLevel)
    streamRef.current?.getTracks().forEach(track => track.stop())
    streamRef.current = null
    if (videoRef.current) videoRef.current.srcObject = null
    if (resetLevel) setHasVideoPreview(false)
  }, [stopMicrophoneMeter])

  const stopSpeakerTest = useCallback((updateState: boolean) => {
    if (speakerFallbackTimerRef.current !== null) window.clearTimeout(speakerFallbackTimerRef.current)
    speakerFallbackTimerRef.current = null
    const oscillator = speakerOscillatorRef.current
    speakerOscillatorRef.current = null
    if (oscillator) {
      oscillator.onended = null
      try { oscillator.stop() } catch { /* The scheduled tone may already have ended. */ }
      oscillator.disconnect()
    }
    speakerGainRef.current?.disconnect()
    speakerGainRef.current = null
    const context = speakerContextRef.current
    speakerContextRef.current = null
    if (context && context.state !== 'closed') void context.close().catch(() => undefined)
    if (updateState) setSpeakerTesting(false)
  }, [])

  useEffect(() => () => {
    deviceTestVersionRef.current += 1
    speakerTestVersionRef.current += 1
    stopMediaPreview(false)
    stopSpeakerTest(false)
  }, [stopMediaPreview, stopSpeakerTest])

  useEffect(() => { onCancelRef.current = onCancel }, [onCancel])

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const dialog = dialogRef.current
    const initialFocus = dialog?.querySelector<HTMLElement>('button:not([disabled])')
    if (initialFocus) initialFocus.focus()
    else dialog?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCancelRef.current()
        return
      }
      if (event.key !== 'Tab' || !dialog) return
      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
        'button:not([disabled]), select:not([disabled]), input:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
      )).filter(element => !element.hasAttribute('hidden'))
      if (focusable.length === 0) {
        event.preventDefault()
        dialog.focus()
        return
      }
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    dialog?.addEventListener('keydown', handleKeyDown)
    return () => {
      dialog?.removeEventListener('keydown', handleKeyDown)
      if (previousFocus?.isConnected) previousFocus.focus()
    }
  }, [])

  useEffect(() => {
    microphoneEnabledRef.current = microphone
    streamRef.current?.getAudioTracks().forEach(track => { track.enabled = microphone })
  }, [microphone])

  useEffect(() => {
    streamRef.current?.getVideoTracks().forEach(track => { track.enabled = camera })
  }, [camera])

  async function enumerateAvailableDevices(testVersion: number) {
    if (!navigator.mediaDevices?.enumerateDevices) return
    const available = await navigator.mediaDevices.enumerateDevices()
    if (testVersion !== deviceTestVersionRef.current) return
    const microphoneOptions = meetingDeviceOptions(available, 'audioinput', t('collaborationMeetings.microphoneDeviceFallback'))
    const cameraOptions = meetingDeviceOptions(available, 'videoinput', t('collaborationMeetings.cameraDeviceFallback'))
    const speakerOptions = meetingDeviceOptions(available, 'audiooutput', t('collaborationMeetings.speakerDeviceFallback'))
    setMicrophones(microphoneOptions)
    setCameras(cameraOptions)
    setSpeakers(speakerOptions)
    setSelectedMicrophoneId(current => optionOrFirst(current, microphoneOptions))
    setSelectedCameraId(current => optionOrFirst(current, cameraOptions))
    setSelectedSpeakerId(current => optionOrFirst(current, speakerOptions))
    setDevicesEnumerated(true)
  }

  async function startMicrophoneMeter(stream: MediaStream, testVersion: number) {
    if (!stream.getAudioTracks().length) {
      setMicrophoneLevel(0)
      return
    }
    const AudioContextConstructor = browserAudioContext()
    if (!AudioContextConstructor) {
      setMeterUnavailable(true)
      return
    }
    let context: AudioContext | null = null
    let source: MediaStreamAudioSourceNode | null = null
    let analyser: AnalyserNode | null = null
    try {
      context = new AudioContextConstructor()
      source = context.createMediaStreamSource(new MediaStream(stream.getAudioTracks()))
      analyser = context.createAnalyser()
      analyser.fftSize = 256
      analyser.smoothingTimeConstant = 0.75
      source.connect(analyser)
      await context.resume()
      if (testVersion !== deviceTestVersionRef.current) {
        source.disconnect()
        analyser.disconnect()
        void context.close().catch(() => undefined)
        return
      }
      meterContextRef.current = context
      meterSourceRef.current = source
      meterAnalyserRef.current = analyser
      const activeAnalyser = analyser
      const samples = new Uint8Array(activeAnalyser.fftSize)
      let lastUpdateAt = 0
      const measure = (now: number) => {
        if (testVersion !== deviceTestVersionRef.current || meterAnalyserRef.current !== activeAnalyser) return
        activeAnalyser.getByteTimeDomainData(samples)
        if (now - lastUpdateAt >= 80) {
          setMicrophoneLevel(microphoneEnabledRef.current ? meetingMicrophoneLevel(samples) : 0)
          lastUpdateAt = now
        }
        meterFrameRef.current = window.requestAnimationFrame(measure)
      }
      meterFrameRef.current = window.requestAnimationFrame(measure)
    } catch {
      source?.disconnect()
      analyser?.disconnect()
      if (context && context.state !== 'closed') void context.close().catch(() => undefined)
      if (testVersion === deviceTestVersionRef.current) setMeterUnavailable(true)
    }
  }

  async function testDevices(
    microphoneDeviceId = selectedMicrophoneId,
    cameraDeviceId = selectedCameraId,
  ) {
    const testVersion = deviceTestVersionRef.current + 1
    deviceTestVersionRef.current = testVersion
    setTesting(true)
    setTested(false)
    setMeterUnavailable(false)
    setError('')
    stopMediaPreview(true)
    try {
      if (!navigator.mediaDevices?.getUserMedia) throw new Error('MEDIA_UNAVAILABLE')
      const constraints = meetingPreJoinMediaConstraints({
        microphone,
        camera,
        canUseMicrophone: capabilities.microphone,
        canUseCamera: capabilities.camera,
        microphoneDeviceId,
        cameraDeviceId,
      })
      if (constraints.audio === false && constraints.video === false) {
        if (testVersion === deviceTestVersionRef.current) setTested(true)
        return
      }
      const stream = await navigator.mediaDevices.getUserMedia(constraints)
      if (testVersion !== deviceTestVersionRef.current) {
        stream.getTracks().forEach(track => track.stop())
        return
      }
      streamRef.current = stream
      if (videoRef.current) videoRef.current.srcObject = stream
      setHasVideoPreview(stream.getVideoTracks().length > 0)
      await enumerateAvailableDevices(testVersion)
      await startMicrophoneMeter(stream, testVersion)
      if (testVersion === deviceTestVersionRef.current) setTested(true)
    } catch (exception) {
      if (testVersion !== deviceTestVersionRef.current) return
      console.error('Meeting pre-join device test failed.', exception)
      stopMediaPreview(true)
      const issue = classifyMeetingMediaIssue(exception)
      setError(t(meetingMediaIssueTranslationKey(issue)))
    } finally {
      if (testVersion === deviceTestVersionRef.current) setTesting(false)
    }
  }

  async function testSpeaker() {
    if (speakerTesting) return
    stopSpeakerTest(false)
    const testVersion = speakerTestVersionRef.current + 1
    speakerTestVersionRef.current = testVersion
    setSpeakerTesting(true)
    setSpeakerNotice('')
    const AudioContextConstructor = browserAudioContext()
    if (!AudioContextConstructor) {
      setSpeakerTesting(false)
      setSpeakerNotice(t('collaborationMeetings.speakerTestUnavailable'))
      return
    }
    try {
      const context = new AudioContextConstructor() as OutputSelectableAudioContext
      speakerContextRef.current = context
      if (selectedSpeakerId && context.setSinkId) await context.setSinkId(selectedSpeakerId)
      else if (selectedSpeakerId && !context.setSinkId)
        setSpeakerNotice(t('collaborationMeetings.speakerUsesSystemDefault'))
      await context.resume()
      if (testVersion !== speakerTestVersionRef.current) return
      const oscillator = context.createOscillator()
      const gain = context.createGain()
      speakerOscillatorRef.current = oscillator
      speakerGainRef.current = gain
      oscillator.type = 'sine'
      oscillator.frequency.setValueAtTime(660, context.currentTime)
      gain.gain.setValueAtTime(0.0001, context.currentTime)
      gain.gain.exponentialRampToValueAtTime(0.12, context.currentTime + 0.025)
      gain.gain.setValueAtTime(0.12, context.currentTime + 0.18)
      gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.32)
      oscillator.connect(gain)
      gain.connect(context.destination)
      oscillator.onended = () => stopSpeakerTest(true)
      oscillator.start()
      oscillator.stop(context.currentTime + 0.34)
      speakerFallbackTimerRef.current = window.setTimeout(() => stopSpeakerTest(true), 600)
    } catch {
      if (testVersion !== speakerTestVersionRef.current) return
      stopSpeakerTest(false)
      setSpeakerTesting(false)
      setSpeakerNotice(t('collaborationMeetings.speakerTestUnavailable'))
    }
  }

  function toggleMicrophone() {
    if (!capabilities.microphone) return
    setMicrophone(value => {
      const next = !value
      microphoneEnabledRef.current = next
      if (!next) setMicrophoneLevel(0)
      if (next && !streamRef.current?.getAudioTracks().length) setTested(false)
      return next
    })
  }

  function toggleCamera() {
    if (!capabilities.camera) return
    setCamera(value => {
      const next = !value
      if (next && !streamRef.current?.getVideoTracks().length) setTested(false)
      return next
    })
  }

  function closePreJoin() {
    deviceTestVersionRef.current += 1
    speakerTestVersionRef.current += 1
    stopMediaPreview(false)
    stopSpeakerTest(false)
    onCancel()
  }

  function joinMeeting(withMedia: boolean) {
    deviceTestVersionRef.current += 1
    speakerTestVersionRef.current += 1
    stopMediaPreview(false)
    stopSpeakerTest(false)
    onJoin(meetingJoinMediaSelection({
      withMedia,
      microphone,
      camera,
      canUseMicrophone: capabilities.microphone,
      canUseCamera: capabilities.camera,
      microphoneDeviceId: selectedMicrophoneId,
      cameraDeviceId: selectedCameraId,
      speakerDeviceId: selectedSpeakerId,
    }))
  }

  return <div className="meeting-prejoin-overlay" role="presentation">
    <section ref={dialogRef} className="meeting-prejoin" role="dialog" aria-modal="true" aria-labelledby="meeting-prejoin-title" aria-describedby="meeting-prejoin-description" tabIndex={-1}>
      <header><span><ShieldCheck aria-hidden="true" />{t('collaborationMeetings.prejoinTitle')}</span><h1 id="meeting-prejoin-title">{meetingTitle}</h1><p id="meeting-prejoin-description">{t('collaborationMeetings.prejoinHint')}</p></header>
      {error && <MessageStrip tone="warning"><strong>{t('collaborationMeetings.deviceCheckNeedsAttention')}</strong><p>{error}</p></MessageStrip>}
      <div className="meeting-prejoin__body">
        <div className="meeting-prejoin__preview">
          <video ref={videoRef} autoPlay muted playsInline hidden={!tested || !camera || !hasVideoPreview} aria-label={t('collaborationMeetings.cameraPreviewLabel')} />
          {(!tested || !camera || !hasVideoPreview) && <div className="meeting-prejoin__identity"><span aria-hidden="true">{displayName.trim().charAt(0).toUpperCase() || 'H'}</span><strong>{displayName}</strong></div>}
          <small>{tested
            ? microphone || camera ? t('collaborationMeetings.devicesReady') : t('collaborationMeetings.mediaOffReady')
            : t('collaborationMeetings.devicePreview')}</small>
        </div>
        <div className="meeting-prejoin__settings">
          <div className="meeting-prejoin__toggles">
            <button type="button" className={microphone ? 'is-active' : ''} disabled={!capabilities.microphone} aria-pressed={microphone} onClick={toggleMicrophone}>{microphone ? <Mic aria-hidden="true" /> : <MicOff aria-hidden="true" />}<span><strong>{t('collaborationMeetings.microphone')}</strong><small>{capabilities.microphone ? microphone ? t('collaborationMeetings.enabled') : t('collaborationMeetings.disabled') : t('collaborationMeetings.disabledByPolicy')}</small></span></button>
            <button type="button" className={camera ? 'is-active' : ''} disabled={!capabilities.camera} aria-pressed={camera} onClick={toggleCamera}>{camera ? <Camera aria-hidden="true" /> : <CameraOff aria-hidden="true" />}<span><strong>{t('collaborationMeetings.camera')}</strong><small>{capabilities.camera ? camera ? t('collaborationMeetings.enabled') : t('collaborationMeetings.disabled') : t('collaborationMeetings.disabledByPolicy')}</small></span></button>
          </div>

          {capabilities.microphone && <div className="meeting-prejoin__meter-group">
            <div className="meeting-prejoin__setting-label"><span>{t('collaborationMeetings.microphoneLevel')}</span><strong>{microphoneLevel}%</strong></div>
            <div className="meeting-prejoin__meter" role="meter" aria-label={t('collaborationMeetings.microphoneLevel')} aria-valuemin={0} aria-valuemax={100} aria-valuenow={microphoneLevel} aria-valuetext={t('collaborationMeetings.microphoneLevelValue', { level: microphoneLevel })}><span style={{ inlineSize: `${microphoneLevel}%` }} /></div>
            {meterUnavailable && <small>{t('collaborationMeetings.microphoneMeterUnavailable')}</small>}
          </div>}

          {devicesEnumerated && <div className="meeting-prejoin__device-fields">
            {capabilities.microphone && <DeviceSelector id="meeting-microphone-device" label={t('collaborationMeetings.microphoneDevice')} emptyLabel={t('collaborationMeetings.noMicrophoneFound')} options={microphones} value={selectedMicrophoneId} disabled={testing} onChange={value => { setSelectedMicrophoneId(value); if (microphone) void testDevices(value, selectedCameraId) }} />}
            {capabilities.camera && <DeviceSelector id="meeting-camera-device" label={t('collaborationMeetings.cameraDevice')} emptyLabel={t('collaborationMeetings.noCameraFound')} options={cameras} value={selectedCameraId} disabled={testing} onChange={value => { setSelectedCameraId(value); if (camera) void testDevices(selectedMicrophoneId, value) }} />}
            <DeviceSelector id="meeting-speaker-device" label={t('collaborationMeetings.speakerDevice')} emptyLabel={t('collaborationMeetings.systemDefaultSpeaker')} options={speakers} value={selectedSpeakerId} disabled={speakerTesting} onChange={setSelectedSpeakerId} />
          </div>}

          <div className="meeting-prejoin__test-actions">
            <button type="button" className="button" disabled={testing} onClick={() => void testDevices()}>{testing ? t('common.loading') : t('collaborationMeetings.testDevices')}</button>
            <button type="button" className="button" disabled={speakerTesting} onClick={() => void testSpeaker()}><Volume2 aria-hidden="true" />{speakerTesting ? t('collaborationMeetings.testingSpeaker') : t('collaborationMeetings.testSpeaker')}</button>
          </div>
          {speakerNotice && <small className="meeting-prejoin__device-note" role="status">{speakerNotice}</small>}
          {!tested && <small className="meeting-prejoin__permission-hint">{t('collaborationMeetings.permissionRequestHint')}</small>}
        </div>
      </div>
      <footer><button type="button" className="button button--primary" onClick={() => joinMeeting(true)}>{t('collaborationMeetings.joinWithCurrentSettings')}</button><button type="button" className="button" onClick={() => joinMeeting(false)}><MicOff aria-hidden="true" />{t('collaborationMeetings.joinWithoutMedia')}</button><button type="button" className="button button--quiet" onClick={closePreJoin}>{t('common.cancel')}</button></footer>
    </section>
  </div>
}

function DeviceSelector({ id, label, emptyLabel, options, value, disabled, onChange }: {
  id: string
  label: string
  emptyLabel: string
  options: MeetingDeviceOption[]
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  return <label className="meeting-prejoin__device-field" htmlFor={id}><span>{label}</span><select id={id} value={value} disabled={disabled || options.length === 0} onChange={event => onChange(event.target.value)}>{options.length === 0
    ? <option value="">{emptyLabel}</option>
    : options.map(option => <option key={option.deviceId || option.label} value={option.deviceId}>{option.label}</option>)}</select></label>
}

function optionOrFirst(current: string, options: MeetingDeviceOption[]) {
  return options.some(option => option.deviceId === current) ? current : options[0]?.deviceId ?? ''
}

function browserAudioContext() {
  return window.AudioContext
    ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
}
