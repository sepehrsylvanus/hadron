import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { CalendarClock, FilePlus2, MessageSquarePlus, Paperclip, Pencil, Phone, Plus, RefreshCw, Search, Send, SmilePlus, Trash2, Video } from 'lucide-react'
import { FormEvent, lazy, ReactNode, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { formatBusinessDate } from './i18n/businessDate'
import { isPersianLanguage, normalizeJalaliDateInput, toAsciiDigits } from './mrpRunUiSupport'
import helloSticker from './assets/brand/stickers/hadron-hello.webp'
import doneSticker from './assets/brand/stickers/hadron-done.webp'
import checkingSticker from './assets/brand/stickers/hadron-checking.webp'
import waitSticker from './assets/brand/stickers/hadron-wait.webp'
import sendingSticker from './assets/brand/stickers/hadron-sending.webp'
import sorrySticker from './assets/brand/stickers/hadron-sorry.webp'
import greatSticker from './assets/brand/stickers/hadron-great.webp'
import {
  type ApiPage, type Attachment, type CallConfiguration, type CallJoin, type CallSession, type Conversation, type ConversationSummary,
  type DirectoryUser, type Letter, type LetterSummary, type Message, type StickerCode, collaborationError,
  canOpenMeetingWorklist, conversationMeetingEntry, displayConversationName, formatCollaborationTime, reactionCodes, statusTone, stickerCodes,
} from './collaborationSupport'
import { assessCallReadiness, callReadinessTranslationKey, readBrowserCallEnvironment } from './collaborationCallSupport'
import { ConfirmationDialog, DataTable, EmptyState, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge } from './ui'

const stickerAssets: Record<StickerCode, string> = {
  HELLO: helloSticker, DONE: doneSticker, CHECKING: checkingSticker, WAIT: waitSticker,
  SENDING: sendingSticker, SORRY: sorrySticker, GREAT: greatSticker,
}

const CollaborationCallPanel = lazy(() => import('./CollaborationCallPanel'))

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiRoot}/collaboration${path}`, init)
  if (response.ok) return response.status === 204 ? undefined as T : await response.json() as T
  const body = await response.json().catch(() => null)
  throw new Error(collaborationError(response.status, body, 'Collaboration request failed.'))
}

function json(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}

function Overlay({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  const { t } = useTranslation()
  const dialogRef = useRef<HTMLElement>(null)
  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const handleKeyDown = (event: globalThis.KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', handleKeyDown)
    return () => { document.removeEventListener('keydown', handleKeyDown); previousFocus?.focus() }
  }, [onClose])
  return <div className="collaboration-overlay" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <section ref={dialogRef} className="collaboration-dialog" role="dialog" aria-modal="true" aria-label={title}>
      <div className="collaboration-dialog__header"><h2>{title}</h2><button type="button" className="button button--quiet" onClick={onClose} aria-label={t('common.close')}>×</button></div>
      {children}
    </section>
  </div>
}

function formatBytes(size: number) {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

function AttachmentView({ attachment }: { attachment: Attachment }) {
  const { t } = useTranslation()
  const isImage = attachment.contentType.startsWith('image/')
  return <a className={`collaboration-attachment${isImage ? ' collaboration-attachment--image' : ''}`} href={attachment.downloadUrl} target="_blank" rel="noreferrer" aria-label={t('collaborationHub.downloadFile', { name: attachment.fileName })}>
    {isImage ? <img src={attachment.downloadUrl} alt={attachment.fileName} loading="lazy" /> : <Paperclip aria-hidden="true" />}
    <span><strong>{attachment.fileName}</strong><small>{formatBytes(attachment.size)} · {t('collaborationHub.fileValidated')}</small></span>
  </a>
}

function NewConversationDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (conversation: Conversation) => void }) {
  const { t } = useTranslation()
  const { user, has } = useAuth()
  const canSend = has('collaboration.message.send')
  const canManageChannels = has('collaboration.channel.manage')
  const [mode, setMode] = useState<'CHANNEL' | 'DIRECT'>(canSend ? 'DIRECT' : 'CHANNEL')
  const [directory, setDirectory] = useState<DirectoryUser[]>([])
  const [selected, setSelected] = useState<string[]>([])
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  useEffect(() => { const timer = window.setTimeout(() => void api<ApiPage<DirectoryUser>>(`/directory/users?search=${encodeURIComponent(search)}&pageSize=50`).then(x => setDirectory(x.items.filter(person => person.id !== user.id))).catch(error => setError(error.message)), 220); return () => window.clearTimeout(timer) }, [search, user.id])
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError('')
    const form = new FormData(event.currentTarget)
    try {
      const result = mode === 'DIRECT'
        ? await api<Conversation>('/conversations/direct', json('POST', { otherUserId: selected[0] }))
        : await api<Conversation>('/conversations/channels', json('POST', { name: form.get('name'), description: form.get('description'), isPrivate: form.get('isPrivate') === 'on', memberUserIds: selected }))
      onCreated(result)
    } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } finally { setBusy(false) }
  }
  return <Overlay title={t('collaborationHub.newConversation')} onClose={onClose}>
    <form className="collaboration-form" onSubmit={submit}>
      <div className="collaboration-segmented" role="group" aria-label={t('collaborationHub.newConversation')}>
        <button type="button" className={mode === 'DIRECT' ? 'is-active' : ''} onClick={() => { setMode('DIRECT'); setSelected([]) }} disabled={!canSend}>{t('collaborationHub.newDirect')}</button>
        <button type="button" className={mode === 'CHANNEL' ? 'is-active' : ''} onClick={() => { setMode('CHANNEL'); setSelected([]) }} disabled={!canManageChannels}>{t('collaborationHub.newChannel')}</button>
      </div>
      {error && <MessageStrip tone="critical">{error}</MessageStrip>}
      {mode === 'CHANNEL' && <div className="ui-form-grid"><FormField label={t('collaborationHub.conversationName')} required><input name="name" maxLength={160} required autoFocus /></FormField><FormField label={t('collaborationHub.description')}><input name="description" maxLength={1000} /></FormField><label className="collaboration-check"><input type="checkbox" name="isPrivate" defaultChecked />{t('collaborationHub.privateChannel')}</label></div>}
      <FormField label={t('collaborationHub.searchPeople')}><input value={search} onChange={event => setSearch(event.target.value)} /></FormField>
      <div className="collaboration-people-list">
        {directory.map(person => <label key={person.id} className={selected.includes(person.id) ? 'is-selected' : ''}>
          <input type={mode === 'DIRECT' ? 'radio' : 'checkbox'} name="people" checked={selected.includes(person.id)} onChange={() => setSelected(current => mode === 'DIRECT' ? [person.id] : current.includes(person.id) ? current.filter(id => id !== person.id) : [...current, person.id])} />
          <img src={person.photoUrl} alt="" /><span><strong>{person.fullName}</strong><small>{person.organizationalUnit}</small></span>
        </label>)}
      </div>
      <div className="ui-dialog-actions"><button type="submit" className="button button--primary" disabled={busy || (mode === 'DIRECT' && selected.length === 0) || (mode === 'DIRECT' ? !canSend : !canManageChannels)}>{busy ? t('common.saving') : t('collaborationHub.createConversation')}</button><button type="button" className="button" onClick={onClose}>{t('common.cancel')}</button></div>
    </form>
  </Overlay>
}

function MessageCard({ message, onReload }: { message: Message; onReload: () => void }) {
  const { t, i18n } = useTranslation()
  const { user, has } = useAuth()
  const [editing, setEditing] = useState(false)
  const [body, setBody] = useState(message.body ?? '')
  const [deleting, setDeleting] = useState(false)
  const mine = message.authorUserId === user.id
  const canSend = has('collaboration.message.send')
  const canChange = !message.deletedAtUtc && message.type === 'TEXT' && ((mine && canSend) || has('collaboration.message.moderate'))
  async function saveEdit() { await api(`/conversations/${message.conversationId}/messages/${message.id}`, json('PUT', { body, expectedVersion: message.version })); setEditing(false); onReload() }
  async function remove() { await api(`/conversations/${message.conversationId}/messages/${message.id}/delete`, json('POST', { expectedVersion: message.version })); setDeleting(false); onReload() }
  async function react(code: string) { await api(`/conversations/${message.conversationId}/messages/${message.id}/reactions`, json('POST', { code })); onReload() }
  if (message.type === 'SYSTEM' || message.type === 'CALL_EVENT') return <article className="collaboration-event"><Phone aria-hidden="true" /><span>{message.body}</span><time>{formatCollaborationTime(message.createdAtUtc, i18n.language)}</time></article>
  return <article className={`collaboration-message${mine ? ' is-mine' : ''}`}>
    <header><strong>{message.authorDisplayName}</strong><time>{formatCollaborationTime(message.createdAtUtc, i18n.language)}{message.editedAtUtc ? ` · ${t('collaborationHub.edited')}` : ''}</time></header>
    {message.deletedAtUtc ? <p className="collaboration-message__deleted">{t('collaborationHub.messageDeleted')}</p>
      : message.type === 'STICKER' && message.stickerCode && stickerAssets[message.stickerCode as StickerCode]
        ? <img className="collaboration-message__sticker" src={stickerAssets[message.stickerCode as StickerCode]} alt={t(`collaborationHub.stickers.${message.stickerCode}`)} loading="lazy" />
        : editing ? <div className="collaboration-inline-edit"><textarea value={body} onChange={event => setBody(event.target.value)} autoFocus /><button className="button button--primary" type="button" onClick={() => void saveEdit()}>{t('common.save')}</button><button className="button" type="button" onClick={() => setEditing(false)}>{t('common.cancel')}</button></div>
          : <p>{message.body}</p>}
    {message.attachments.map(attachment => <AttachmentView key={attachment.id} attachment={attachment} />)}
    {!message.deletedAtUtc && <footer>{canSend && <div className="collaboration-reactions">{reactionCodes.map(code => { const reaction = message.reactions.find(item => item.code === code); return <button key={code} type="button" className={reaction?.reactedByCurrentUser ? 'is-active' : ''} onClick={() => void react(code)} aria-label={`${t('collaborationHub.reactions')} ${code}`}>{code === 'LIKE' ? '👍' : code === 'HEART' ? '❤️' : code === 'THANKS' ? '🙏' : code === 'DONE' ? '✅' : '👀'}{reaction?.count ? <span>{reaction.count}</span> : null}</button> })}</div>}{canChange && <div className="collaboration-message-actions"><button type="button" onClick={() => setEditing(true)} title={t('collaborationHub.editMessage')}><Pencil /></button><button type="button" onClick={() => setDeleting(true)} title={t('collaborationHub.deleteMessage')}><Trash2 /></button></div>}</footer>}
    <ConfirmationDialog open={deleting} title={t('collaborationHub.deleteMessageTitle')} description={t('collaborationHub.deleteMessageHint')} confirmLabel={t('common.delete')} cancelLabel={t('common.cancel')} onConfirm={() => void remove()} onCancel={() => setDeleting(false)} />
  </article>
}

export function CollaborationMessagesPage() {
  const { t } = useTranslation()
  const { user, has } = useAuth()
  const { conversationId } = useParams()
  const navigate = useNavigate()
  const [list, setList] = useState<ConversationSummary[]>([])
  const [conversation, setConversation] = useState<Conversation | null>(null)
  const [messages, setMessages] = useState<Message[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [composer, setComposer] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [stickersOpen, setStickersOpen] = useState(false)
  const [creating, setCreating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [callJoin, setCallJoin] = useState<CallJoin | null>(null)
  const [startedCallId, setStartedCallId] = useState<string | null>(null)
  const [callBusy, setCallBusy] = useState(false)
  const connectionRef = useRef<HubConnection | null>(null)
  const loadList = useCallback(async () => { const data = await api<ApiPage<ConversationSummary>>(`/conversations?search=${encodeURIComponent(search)}&pageSize=50`); setList(data.items) }, [search])
  const loadMessages = useCallback(async () => { if (!conversationId) return; const [detail, page] = await Promise.all([api<Conversation>(`/conversations/${conversationId}`), api<ApiPage<Message>>(`/conversations/${conversationId}/messages?pageSize=100`)]); setConversation(detail); setMessages(page.items); await api(`/conversations/${conversationId}/read`, { method: 'POST' }); await loadList() }, [conversationId, loadList])
  const refreshAll = useCallback(async () => { setLoading(true); setError(''); try { if (conversationId) await loadMessages(); else await loadList() } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } finally { setLoading(false) } }, [conversationId, loadList, loadMessages, t])
  useEffect(() => { const timer = window.setTimeout(() => void loadList().catch(exception => setError(exception.message)).finally(() => setLoading(false)), 220); return () => window.clearTimeout(timer) }, [loadList])
  useEffect(() => {
    const initialLoadTimer = window.setTimeout(() => void loadMessages().catch(exception => setError(exception.message)), 0)
    if (!conversationId) return () => window.clearTimeout(initialLoadTimer)

    let disposed = false
    let retryTimer: number | undefined
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/collaboration')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connectionRef.current = connection

    const reload = () => void loadMessages()
    const reloadList = () => void loadList()
    const callEnded = (call?: CallSession) => {
      if (call) setCallJoin(current => current?.call.id === call.id ? null : current)
      reload()
    }
    const scheduleStart = () => {
      if (disposed || retryTimer !== undefined) return
      retryTimer = window.setTimeout(() => {
        retryTimer = undefined
        void startConnection()
      }, 1_500)
    }
    const startConnection = async () => {
      if (disposed || connection.state !== HubConnectionState.Disconnected) return
      try {
        await connection.start()
        if (disposed) {
          await connection.stop()
          return
        }
        await connection.invoke('JoinConversation', conversationId)
      } catch (exception) {
        if (disposed) return
        console.warn('Collaboration real-time connection failed; retrying.', exception)
        await connection.stop().catch(() => undefined)
        scheduleStart()
      }
    }

    connection.on('messageCreated', reload)
    connection.on('messageUpdated', reload)
    connection.on('messageDeleted', reload)
    connection.on('messageReactionsUpdated', reload)
    connection.on('callStarted', reload)
    connection.on('callEnded', callEnded)
    connection.on('notificationUpdated', reloadList)
    connection.onreconnected(() => {
      if (!disposed) void connection.invoke('JoinConversation', conversationId).catch(async exception => {
        console.warn('Collaboration group rejoin failed; reconnecting.', exception)
        await connection.stop().catch(() => undefined)
        scheduleStart()
      })
    })
    connection.onclose(scheduleStart)
    void startConnection()

    return () => {
      disposed = true
      window.clearTimeout(initialLoadTimer)
      if (retryTimer !== undefined) window.clearTimeout(retryTimer)
      if (connectionRef.current === connection) connectionRef.current = null
      void connection.stop()
    }
  }, [conversationId, loadList, loadMessages])
  async function send(type: 'TEXT' | 'STICKER', stickerCode?: StickerCode) {
    if (!conversationId || !has('collaboration.message.send') || (file && !has('collaboration.file.share')) ||
        (type === 'TEXT' && !composer.trim() && !file)) return
    setBusy(true); setError('')
    try { const message = await api<Message>(`/conversations/${conversationId}/messages`, json('POST', { type, body: type === 'TEXT' ? composer.trim() || file?.name : null, stickerCode: stickerCode ?? null, replyToMessageId: null })); if (file) { const data = new FormData(); data.set('file', file); try { await api(`/conversations/${conversationId}/messages/${message.id}/attachments`, { method: 'POST', body: data }) } catch (exception) { await api(`/conversations/${conversationId}/messages/${message.id}/delete`, json('POST', { expectedVersion: message.version })).catch(() => undefined); throw exception } } setComposer(''); setFile(null); setStickersOpen(false); await refreshAll() } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } finally { setBusy(false) }
  }
  async function callEnvironmentReady() {
    const configuration = await api<CallConfiguration>('/calls/configuration')
    const readiness = assessCallReadiness(configuration, readBrowserCallEnvironment())
    if (!readiness.ready) {
      setError(t(callReadinessTranslationKey(readiness.code)))
      return false
    }
    return true
  }
  async function joinActive() {
    if (!conversation?.activeCall || callBusy) return
    setCallBusy(true); setError('')
    try {
      if (!await callEnvironmentReady()) return
      const result = await api<CallJoin>(`/calls/${conversation.activeCall.id}/join`, { method: 'POST' })
      setStartedCallId(null)
      setCallJoin(result)
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : t('collaborationHub.callUnavailable'))
    } finally { setCallBusy(false) }
  }
  async function endCall() {
    if (!callJoin) return
    try {
      await api(`/calls/${callJoin.call.id}/end`, json('POST', { expectedVersion: callJoin.call.version }))
      setCallJoin(null); setStartedCallId(null); await refreshAll()
    } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) }
  }
  async function cancelFailedCallBootstrap(failed: CallJoin, failureMessage: string) {
    let message = failureMessage
    try {
      await api(`/calls/${failed.call.id}/end`, json('POST', { expectedVersion: failed.call.version }))
    } catch (exception) {
      message = exception instanceof Error ? `${failureMessage} ${exception.message}` : `${failureMessage} ${t('collaborationHub.callCleanupFailed')}`
    } finally {
      setCallJoin(current => current?.call.id === failed.call.id ? null : current)
      setStartedCallId(current => current === failed.call.id ? null : current)
      await refreshAll()
      setError(message)
    }
  }
  async function abortStartedCallBootstrap(aborted: CallJoin) {
    let failure = ''
    try {
      await api(`/calls/${aborted.call.id}/end`, json('POST', { expectedVersion: aborted.call.version }))
    } catch (exception) {
      failure = exception instanceof Error ? exception.message : t('collaborationHub.callCleanupFailed')
    } finally {
      setCallJoin(current => current?.call.id === aborted.call.id ? null : current)
      setStartedCallId(current => current === aborted.call.id ? null : current)
      await refreshAll()
      if (failure) setError(failure)
    }
  }
  const title = conversation ? displayConversationName(conversation, user.id) : ''
  const meetingEntry = conversationMeetingEntry(has)
  const meetingWorklistVisible = canOpenMeetingWorklist(has)
  return <main className="collaboration-page">
    <PageHeader eyebrow={t('collaborationHub.privateWorkspace')} title={t('collaborationHub.messagesTitle')} subtitle={t('collaborationHub.messagesSubtitle')} actions={<>{meetingWorklistVisible && <Link className="button" to="/collaboration/meetings"><CalendarClock />{t('collaborationMeetings.title')}</Link>}<button className="button button--primary" type="button" onClick={() => setCreating(true)} disabled={!has('collaboration.message.send') && !has('collaboration.channel.manage')}><MessageSquarePlus />{t('collaborationHub.newConversation')}</button></>} />
    {error && <MessageStrip tone="critical"><span>{error}</span> <button className="button button--quiet" onClick={() => void refreshAll()}>{t('collaborationHub.retry')}</button></MessageStrip>}
    <section className="collaboration-chat-shell">
      <aside className="collaboration-conversations"><div className="collaboration-pane-heading"><h2>{t('collaborationHub.conversations')}</h2><button type="button" onClick={() => void refreshAll()} title={t('collaborationHub.refresh')}><RefreshCw /></button></div><label className="collaboration-search"><Search aria-hidden="true" /><input value={search} onChange={event => setSearch(event.target.value)} placeholder={t('collaborationHub.searchConversations')} /></label>{loading && !list.length ? <LoadingState label={t('common.loading')} compact /> : <div className="collaboration-conversation-list">{list.map(item => <button key={item.id} type="button" className={conversationId === item.id ? 'is-selected' : ''} onClick={() => navigate(`/collaboration/messages/${item.id}`)}><span className="collaboration-avatar">{item.type === 'CHANNEL' ? '#' : item.name.slice(0, 1)}</span><span><strong>{item.name}</strong><small>{item.lastMessagePreview ?? t('collaborationHub.noMessages')}</small></span>{item.unreadCount > 0 && <em>{item.unreadCount}</em>}</button>)}</div>}</aside>
      <section className="collaboration-thread">
        {!conversationId ? <EmptyState title={t('collaborationHub.noConversation')} description={t('collaborationHub.noConversationHint')} /> : <>
          <header className="collaboration-thread-header"><div><h2>{title}</h2><p>{conversation?.description || t('collaborationHub.memberCount', { count: conversation?.members.length ?? 0 })}</p></div><div>{conversation?.activeCall ? <button className="button button--primary" type="button" onClick={() => void joinActive()} disabled={callBusy || !has('collaboration.call.join')} title={t('collaborationMeetings.legacyActiveCall')}><Phone />{t('collaborationHub.joinCall')}</button> : meetingEntry && <Link className="button" to={meetingEntry.path} title={t('collaborationMeetings.controlledMeetingFromMessages')}>{meetingEntry.mode === 'INSTANT' ? <Video /> : <CalendarClock />}{t(meetingEntry.labelKey)}</Link>}</div></header>
          <div className="collaboration-message-stream" aria-live="polite">{!messages.length ? <EmptyState title={t('collaborationHub.noMessages')} description={t('collaborationHub.noMessagesHint')} compact /> : messages.map(message => <MessageCard key={message.id} message={message} onReload={() => void refreshAll()} />)}</div>
          {has('collaboration.message.send') && <div className="collaboration-composer">{file && <div className="collaboration-file-chip"><Paperclip />{t('collaborationHub.selectedFile', { name: file.name })}<button type="button" onClick={() => setFile(null)}>×</button></div>}{stickersOpen && <div className="collaboration-sticker-picker" aria-label={t('collaborationHub.stickerPicker')}>{stickerCodes.map(code => <button key={code} type="button" onClick={() => void send('STICKER', code)}><img src={stickerAssets[code]} alt="" /><span>{t(`collaborationHub.stickers.${code}`)}</span></button>)}</div>}<textarea value={composer} onChange={event => setComposer(event.target.value)} placeholder={t('collaborationHub.messagePlaceholder')} maxLength={10000} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void send('TEXT') } }} /><div className="collaboration-composer-actions">{has('collaboration.file.share') && <label className="button button--icon" title={t('collaborationHub.attachFile')}><FilePlus2 /><input type="file" hidden onChange={event => setFile(event.target.files?.[0] ?? null)} /></label>}<button type="button" className="button button--icon" title={t('collaborationHub.stickerPicker')} onClick={() => setStickersOpen(value => !value)}><SmilePlus /></button><button type="button" className="button button--primary" onClick={() => void send('TEXT')} disabled={busy || (!composer.trim() && !file)}><Send />{t('collaborationHub.send')}</button></div></div>}
        </>}
      </section>
      <aside className="collaboration-context"><div className="collaboration-pane-heading"><h2>{t('collaborationHub.members')}</h2></div>{conversation?.members.map(member => <div className="collaboration-member" key={member.userId}><span className="collaboration-avatar">{member.displayName.slice(0, 1)}</span><span><strong>{member.displayName}</strong><small>{member.role}</small></span></div>) ?? <p>{t('collaborationHub.noConversationHint')}</p>}</aside>
    </section>
    {creating && <NewConversationDialog onClose={() => setCreating(false)} onCreated={result => { setCreating(false); void loadList(); navigate(`/collaboration/messages/${result.id}`) }} />}
    {callJoin && <Suspense fallback={<div className="collaboration-overlay"><LoadingState label={t('collaborationHub.callConnecting')} /></div>}><CollaborationCallPanel join={callJoin} onLeave={() => setCallJoin(null)} onEnd={has('collaboration.call.start') && (callJoin.call.hostUserId === user.id || has('collaboration.message.moderate')) ? () => void endCall() : undefined} onBootstrapFailure={startedCallId === callJoin.call.id ? message => cancelFailedCallBootstrap(callJoin, message) : undefined} onBootstrapAbort={startedCallId === callJoin.call.id ? () => abortStartedCallBootstrap(callJoin) : undefined} /></Suspense>}
  </main>
}

function LetterEditor({ existing, directory, onSaved, onCancel }: { existing?: Letter | null; directory: DirectoryUser[]; onSaved: (letter: Letter) => void; onCancel: () => void }) {
  const { t, i18n } = useTranslation()
  const persian = isPersianLanguage(i18n.resolvedLanguage ?? i18n.language)
  const [selected, setSelected] = useState<Record<string, 'TO' | 'CC'>>(() => Object.fromEntries(existing?.recipients.map(x => [x.userId, x.type as 'TO' | 'CC']) ?? []))
  const [dueDate, setDueDate] = useState(() => existing?.dueDate
    ? persian ? toAsciiDigits(formatBusinessDate(existing.dueDate, 'fa')).replace(/[^0-9]/g, '') : existing.dueDate
    : '')
  const [resolvedDueDate, setResolvedDueDate] = useState<string | null>(existing?.dueDate ?? null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function canonicalDueDate() {
    if (!dueDate.trim()) return null
    if (!persian) return dueDate
    const normalized = normalizeJalaliDateInput(dueDate)
    if (!normalized) throw new Error(t('collaborationHub.dueDateInvalid'))
    const result = await api<{ gregorianDate: string }>('/date/resolve-jalali', json('POST', { jalaliDate: normalized }))
    setResolvedDueDate(result.gregorianDate)
    return result.gregorianDate
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    const form = new FormData(event.currentTarget)
    try {
      const canonicalDate = await canonicalDueDate()
      const payload = {
        subject: form.get('subject'), body: form.get('body'), classification: form.get('classification'),
        dueDate: canonicalDate,
        recipients: Object.entries(selected).map(([userId, type]) => ({ userId, type })),
        ...(existing ? { expectedVersion: existing.version } : {}),
      }
      const result = await api<Letter>(existing ? `/letters/${existing.id}` : '/letters', json(existing ? 'PUT' : 'POST', payload))
      onSaved(result)
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable'))
    } finally { setBusy(false) }
  }

  return <Overlay title={existing ? t('collaborationHub.updateDraft') : t('collaborationHub.newLetter')} onClose={onCancel}>
    <form className="collaboration-form" onSubmit={submit}>
      {error && <MessageStrip tone="critical">{error}</MessageStrip>}
      <div className="ui-form-grid">
        <FormField label={t('collaborationHub.subject')} required><input name="subject" defaultValue={existing?.subject} maxLength={300} required autoFocus /></FormField>
        <FormField label={t('collaborationHub.classification')} required><select name="classification" defaultValue={existing?.classification ?? 'NORMAL'}><option value="NORMAL">{t('collaborationHub.normal')}</option><option value="CONFIDENTIAL">{t('collaborationHub.confidential')}</option><option value="HIGHLY_CONFIDENTIAL">{t('collaborationHub.highlyConfidential')}</option></select></FormField>
        <FormField label={t('collaborationHub.dueDate')}>
          <input type={persian ? 'text' : 'date'} inputMode={persian ? 'numeric' : undefined} dir="ltr"
            value={dueDate} maxLength={persian ? 8 : undefined} placeholder={persian ? '14050616' : undefined}
            onChange={event => { setDueDate(persian ? toAsciiDigits(event.target.value).replace(/[^0-9]/g, '').slice(0, 8) : event.target.value); setResolvedDueDate(null) }}
            onBlur={() => { if (persian && dueDate.trim()) void canonicalDueDate().catch(exception => setError(exception instanceof Error ? exception.message : t('collaborationHub.dueDateInvalid'))) }} />
          {persian && <small>{t('collaborationHub.dueDateJalaliHint')}{resolvedDueDate ? ` · ${formatBusinessDate(resolvedDueDate, 'en')}` : ''}</small>}
        </FormField>
      </div>
      <FormField label={t('collaborationHub.letterBody')} required><textarea name="body" defaultValue={existing?.body} maxLength={40000} rows={10} required /></FormField>
      <fieldset className="collaboration-recipient-fieldset"><legend>{t('collaborationHub.recipients')}</legend>{directory.map(person => <div key={person.id}><span><strong>{person.fullName}</strong><small>{person.organizationalUnit}</small></span><select value={selected[person.id] ?? ''} onChange={event => setSelected(current => { const next = { ...current }; if (!event.target.value) delete next[person.id]; else next[person.id] = event.target.value as 'TO' | 'CC'; return next })}><option value="">—</option><option value="TO">{t('collaborationHub.toRecipient')}</option><option value="CC">{t('collaborationHub.ccRecipient')}</option></select></div>)}</fieldset>
      <div className="ui-dialog-actions"><button type="submit" className="button button--primary" disabled={busy || !Object.values(selected).includes('TO')}>{busy ? t('common.saving') : existing ? t('collaborationHub.updateDraft') : t('collaborationHub.saveDraft')}</button><button type="button" className="button" onClick={onCancel}>{t('common.cancel')}</button></div>
    </form>
  </Overlay>
}

export function CollaborationLettersPage() {
  const { t, i18n } = useTranslation(); const { user, has } = useAuth(); const { letterId } = useParams(); const navigate = useNavigate()
  const [letters, setLetters] = useState<ApiPage<LetterSummary>>({ items: [], page: 1, pageSize: 25, totalCount: 0 }); const [letter, setLetter] = useState<Letter | null>(null); const [directory, setDirectory] = useState<DirectoryUser[]>([]); const [search, setSearch] = useState(''); const [status, setStatus] = useState(''); const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [editing, setEditing] = useState(false)
  const load = useCallback(async (page: number) => { setLoading(true); setError(''); try { const [list, people, detail] = await Promise.all([api<ApiPage<LetterSummary>>(`/letters?search=${encodeURIComponent(search)}&status=${status}&page=${page}&pageSize=25`), api<ApiPage<DirectoryUser>>('/directory/users?pageSize=100'), letterId ? api<Letter>(`/letters/${letterId}`) : Promise.resolve(null)]); setLetters(list); setDirectory(people.items.filter(person => person.id !== user.id)); setLetter(detail) } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } finally { setLoading(false) } }, [letterId, search, status, t, user.id])
  useEffect(() => { const timer = window.setTimeout(() => void load(1), 220); return () => window.clearTimeout(timer) }, [load])
  async function transition(name: 'issue' | 'acknowledge' | 'close') { if (!letter) return; try { const result = await api<Letter>(`/letters/${letter.id}/${name}`, json('POST', { expectedVersion: letter.version, note: null })); setLetter(result); await load(letters.page) } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } }
  async function attach(file: File) { if (!letter) return; const data = new FormData(); data.set('file', file); try { await api(`/letters/${letter.id}/attachments`, { method: 'POST', body: data }); await load(letters.page) } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } }
  const canAcknowledge = letter?.status === 'ISSUED' && letter.recipients.some(recipient => recipient.userId === user.id)
  return <main className="collaboration-page"><PageHeader eyebrow={t('collaborationHub.privateWorkspace')} title={t('collaborationHub.lettersTitle')} subtitle={t('collaborationHub.lettersSubtitle')} actions={<button className="button button--primary" type="button" onClick={() => setEditing(true)} disabled={!has('correspondence.letter.manage')}><Plus />{t('collaborationHub.newLetter')}</button>} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}<div className="collaboration-letter-layout"><Section title={t('collaborationHub.letterList')} actions={<div className="collaboration-list-filters"><label className="collaboration-search"><Search /><input value={search} onChange={event => setSearch(event.target.value)} placeholder={t('collaborationHub.searchLetters')} /></label><select value={status} onChange={event => setStatus(event.target.value)}><option value="">{t('common.all')}</option><option value="DRAFT">{t('collaborationHub.draft')}</option><option value="ISSUED">{t('collaborationHub.issued')}</option><option value="ACKNOWLEDGED">{t('collaborationHub.acknowledged')}</option><option value="CLOSED">{t('collaborationHub.closed')}</option></select></div>}><DataTable headers={[t('collaborationHub.letterNumber'), t('collaborationHub.subject'), t('collaborationHub.sender'), t('collaborationHub.letterStatus'), t('common.actions')]} loading={loading} empty={!letters.items.length} filtered={Boolean(search || status)} loadingLabel={t('common.loading')} emptyTitle={t('collaborationHub.noLetters')} noResultTitle={t('common.noResultsTitle')}>{letters.items.map(item => <tr key={item.id} className={item.id === letterId ? 'is-selected' : ''}><td dir="ltr">{item.number}</td><td>{item.subject}</td><td>{item.senderDisplayName}</td><td><StatusBadge text={t(`collaborationHub.${item.status.toLowerCase()}`)} tone={statusTone(item.status)} /></td><td><button className="button button--quiet" type="button" onClick={() => navigate(`/collaboration/correspondence/${item.id}`)}>{t('common.view')}</button></td></tr>)}</DataTable><Pagination page={letters.page} pageSize={letters.pageSize} totalCount={letters.totalCount} onPageChange={page => void load(page)} previousLabel={t('common.previous')} nextLabel={t('common.next')} summary={t('common.paginationSummary', { page: letters.page, count: letters.totalCount })} /></Section>{letter && <Section title={`${letter.number} — ${letter.subject}`} description={letter.senderDisplayName} actions={<div className="collaboration-letter-actions">{letter.status === 'DRAFT' && letter.senderUserId === user.id && <><button className="button" type="button" onClick={() => setEditing(true)}><Pencil />{t('common.edit')}</button><label className="button"><Paperclip />{t('collaborationHub.attachToDraft')}<input type="file" hidden onChange={event => { const selected = event.target.files?.[0]; if (selected) void attach(selected) }} /></label><button className="button button--primary" type="button" onClick={() => void transition('issue')} disabled={!has('correspondence.letter.issue')}><Send />{t('collaborationHub.issueLetter')}</button></>}{canAcknowledge && <button className="button button--primary" type="button" onClick={() => void transition('acknowledge')} disabled={!has('correspondence.letter.acknowledge')}>{t('collaborationHub.acknowledgeLetter')}</button>}{letter.status === 'ACKNOWLEDGED' && letter.senderUserId === user.id && <button className="button button--primary" type="button" onClick={() => void transition('close')}>{t('collaborationHub.closeLetter')}</button>}</div>}><div className="collaboration-letter"><div className="collaboration-letter-meta"><div><span>{t('collaborationHub.classification')}</span><strong>{t(`collaborationHub.${letter.classification === 'HIGHLY_CONFIDENTIAL' ? 'highlyConfidential' : letter.classification.toLowerCase()}`)}</strong></div><div><span>{t('collaborationHub.letterStatus')}</span><StatusBadge text={t(`collaborationHub.${letter.status.toLowerCase()}`)} tone={statusTone(letter.status)} /></div><div><span>{t('collaborationHub.dueDate')}</span><strong>{formatCollaborationTime(letter.dueDate, i18n.language, false)}</strong></div></div>{letter.status !== 'DRAFT' && <MessageStrip tone="information">{t('collaborationHub.issuedImmutable')}</MessageStrip>}<article className="collaboration-letter-body">{letter.body}</article><h3>{t('collaborationHub.recipients')}</h3><div className="collaboration-recipient-chips">{letter.recipients.map(recipient => <span key={`${recipient.userId}-${recipient.type}`}><b>{recipient.type}</b>{recipient.displayName}</span>)}</div>{letter.attachments.length > 0 && <><h3>{t('collaborationHub.attachments')}</h3><div className="collaboration-attachments">{letter.attachments.map(attachment => <AttachmentView key={attachment.id} attachment={attachment} />)}</div></>}<h3>{t('collaborationHub.letterHistory')}</h3><ol className="collaboration-letter-history">{[...letter.events].reverse().map(event => <li key={event.id}><span>{event.eventType}</span><strong>{event.actorDisplayName}</strong><time>{formatCollaborationTime(event.occurredAtUtc, i18n.language)}</time></li>)}</ol></div></Section>}</div>{editing && <LetterEditor existing={letter?.status === 'DRAFT' ? letter : null} directory={directory} onCancel={() => setEditing(false)} onSaved={saved => { setEditing(false); setLetter(saved); navigate(`/collaboration/correspondence/${saved.id}`); void load(letters.page) }} />}</main>
}

export function CollaborationCallsPage() {
  const { t } = useTranslation(); const [configuration, setConfiguration] = useState<CallConfiguration | null>(null); const [error, setError] = useState('')
  const load = useCallback(async () => { setError(''); try { setConfiguration(await api<CallConfiguration>('/calls/configuration')) } catch (exception) { setError(exception instanceof Error ? exception.message : t('collaborationHub.serviceUnavailable')) } }, [t])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer) }, [load])
  const readiness = configuration ? assessCallReadiness(configuration, readBrowserCallEnvironment()) : null
  const ready = readiness?.ready === true
  const readinessText = ready
    ? configuration?.serverUrl
    : readiness ? t(callReadinessTranslationKey(readiness.code)) : t('common.loading')
  return <main className="collaboration-page"><PageHeader eyebrow={t('collaborationHub.privateWorkspace')} title={t('collaborationHub.callsTitle')} subtitle={t('collaborationHub.callsSubtitle')} actions={<Link className="button button--primary" to="/collaboration/messages"><MessageSquarePlus />{t('collaborationHub.openMessages')}</Link>} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}<Section title={ready ? t('collaborationHub.callReady') : t('collaborationHub.callUnavailable')} description={t('collaborationHub.callPrivacy')}><div className="collaboration-call-readiness"><span className={ready ? 'is-ready' : ''}><Video aria-hidden="true" /></span><div><strong>{configuration?.provider ?? 'LiveKit'}</strong><p>{readinessText}</p></div></div></Section></main>
}
