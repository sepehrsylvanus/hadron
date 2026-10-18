import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { Bell, CalendarPlus, Mail, MessageSquareText, Mic, PhoneIncoming, RefreshCw, Video, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { createLatestRequestGate, focusOnNextFrame } from './collaborationNotificationCenterSupport'
import { useMeetingCall } from './MeetingCallContext'
import {
  capNotificationCount, type CollaborationNotificationFilter, type CollaborationNotificationItem,
  type CollaborationNotificationKind, type CollaborationNotificationSummary,
  collaborationDockActions, type CollaborationDockAction, filterCollaborationNotifications, formatCollaborationTime,
} from './collaborationSupport'

type NotificationEvent = { kind?: string; entityId?: string; conversationId?: string | null }

const emptySummary: CollaborationNotificationSummary = {
  totalCount: 0, unreadMessageCount: 0, pendingLetterCount: 0, activeCallCount: 0,
  generatedAtUtc: '', items: [],
}

export default function CollaborationNotificationCenter() {
  const { t, i18n } = useTranslation()
  const { has } = useAuth()
  const { activeMeetingId } = useMeetingCall()
  const canLoadNotifications = has('collaboration.workspace.view')
  const dockActions = collaborationDockActions(has)
  const enabled = dockActions.length > 0
  const [summary, setSummary] = useState<CollaborationNotificationSummary>(emptySummary)
  const [filter, setFilter] = useState<CollaborationNotificationFilter>('ALL')
  const [open, setOpen] = useState(false)
  const [dockOpen, setDockOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [unavailable, setUnavailable] = useState(false)
  const [toastKind, setToastKind] = useState<CollaborationNotificationKind | null>(null)
  const root = useRef<HTMLDivElement>(null)
  const notificationLauncher = useRef<HTMLButtonElement>(null)
  const notificationFirstControl = useRef<HTMLButtonElement>(null)
  const dockLauncher = useRef<HTMLButtonElement>(null)
  const dockFirstControl = useRef<HTMLAnchorElement>(null)
  const requestGate = useRef(createLatestRequestGate())

  const load = useCallback(async () => {
    if (!canLoadNotifications) return
    const requestId = requestGate.current.begin()
    setLoading(true)
    try {
      const response = await fetch(`${apiRoot}/collaboration/notifications?limit=12`)
      if (!response.ok) throw new Error(String(response.status))
      const nextSummary = await response.json() as CollaborationNotificationSummary
      if (!requestGate.current.isLatest(requestId)) return
      setSummary(nextSummary)
      setUnavailable(false)
    } catch {
      if (!requestGate.current.isLatest(requestId)) return
      setUnavailable(true)
    } finally {
      if (requestGate.current.isLatest(requestId)) setLoading(false)
    }
  }, [canLoadNotifications])

  useEffect(() => {
    const gate = requestGate.current
    return () => gate.invalidate()
  }, [canLoadNotifications])

  useEffect(() => {
    if (!canLoadNotifications) return
    const initial = window.setTimeout(() => void load(), 0)
    const interval = window.setInterval(() => void load(), 60_000)
    const focus = () => void load()
    window.addEventListener('focus', focus)
    return () => { window.clearTimeout(initial); window.clearInterval(interval); window.removeEventListener('focus', focus) }
  }, [canLoadNotifications, load])

  useEffect(() => {
    if (!canLoadNotifications) return
    const connection = new HubConnectionBuilder().withUrl('/hubs/collaboration').withAutomaticReconnect()
      .configureLogging(LogLevel.Warning).build()
    connection.on('notificationUpdated', (event: NotificationEvent) => {
      if (event.kind && event.kind !== 'READ') {
        const kind = event.kind as CollaborationNotificationKind
        if (kind === 'MESSAGE' || kind === 'LETTER' || kind === 'CALL') setToastKind(kind)
      }
      void load()
    })
    connection.onreconnected(() => void load())
    void connection.start().catch(() => setUnavailable(true))
    return () => { void connection.stop() }
  }, [canLoadNotifications, load])

  useEffect(() => {
    if (!toastKind) return
    const timer = window.setTimeout(() => setToastKind(null), 5_000)
    return () => window.clearTimeout(timer)
  }, [toastKind])

  useEffect(() => {
    if (open) focusOnNextFrame(notificationFirstControl.current)
  }, [open])

  useEffect(() => {
    if (dockOpen) focusOnNextFrame(dockFirstControl.current)
  }, [dockOpen])

  const closeNotifications = useCallback((restoreFocus = true) => {
    setOpen(false)
    if (restoreFocus) focusOnNextFrame(notificationLauncher.current)
  }, [setOpen])

  const closeDock = useCallback((restoreFocus = true) => {
    setDockOpen(false)
    if (restoreFocus) focusOnNextFrame(dockLauncher.current)
  }, [setDockOpen])

  useEffect(() => {
    if (!open && !dockOpen) return
    const pointer = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) {
        if (open) closeNotifications()
        if (dockOpen) closeDock()
      }
    }
    const keyboard = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (open) closeNotifications()
        if (dockOpen) closeDock()
      }
    }
    document.addEventListener('pointerdown', pointer)
    document.addEventListener('keydown', keyboard)
    return () => { document.removeEventListener('pointerdown', pointer); document.removeEventListener('keydown', keyboard) }
  }, [closeDock, closeNotifications, dockOpen, open])

  const visibleItems = useMemo(() => filterCollaborationNotifications(summary.items, filter), [filter, summary.items])
  if (!enabled) return null

  const tabs: { id: CollaborationNotificationFilter; count: number }[] = [
    { id: 'ALL', count: summary.totalCount },
    { id: 'MESSAGE', count: summary.unreadMessageCount },
    { id: 'LETTER', count: summary.pendingLetterCount },
    { id: 'CALL', count: summary.activeCallCount },
  ]

  return <div className="shell-communication" ref={root}>
    {canLoadNotifications && <button ref={notificationLauncher} type="button" className={`shell-communication__trigger${open ? ' is-open' : ''}`}
      aria-label={t('collaborationHub.notificationCenter.title')} aria-expanded={open}
      aria-controls="collaboration-notification-popover" aria-haspopup="dialog"
      onClick={() => {
        if (open) closeNotifications()
        else { setOpen(true); setDockOpen(false); void load() }
      }}>
      <Bell aria-hidden="true" />
      {summary.totalCount > 0 && <span className="shell-communication__badge" aria-label={t('collaborationHub.notificationCenter.total', { count: summary.totalCount })}>{capNotificationCount(summary.totalCount)}</span>}
    </button>}
    {canLoadNotifications && open && <section id="collaboration-notification-popover" className="shell-communication__popover" role="dialog" aria-modal="false"
      aria-label={t('collaborationHub.notificationCenter.title')}>
      <header><div><strong>{t('collaborationHub.notificationCenter.title')}</strong><small>{t('collaborationHub.notificationCenter.subtitle')}</small></div><button type="button" onClick={() => void load()} disabled={loading} title={t('collaborationHub.refresh')}><RefreshCw className={loading ? 'is-spinning' : ''} /></button></header>
      <div className="shell-communication__tabs" role="group" aria-label={t('collaborationHub.notificationCenter.filters')}>
        {tabs.map(tab => <button key={tab.id} ref={tab.id === 'ALL' ? notificationFirstControl : undefined} type="button" aria-pressed={filter === tab.id}
          className={filter === tab.id ? 'is-active' : ''} onClick={() => setFilter(tab.id)}>
          {t(`collaborationHub.notificationCenter.${tab.id.toLowerCase()}`)}<span>{capNotificationCount(tab.count)}</span>
        </button>)}
      </div>
      {unavailable ? <div className="shell-communication__state" role="status"><strong>{t('collaborationHub.notificationCenter.unavailable')}</strong><button type="button" onClick={() => void load()}>{t('collaborationHub.retry')}</button></div>
        : visibleItems.length === 0 ? <div className="shell-communication__state"><Bell aria-hidden="true" /><strong>{t('collaborationHub.notificationCenter.empty')}</strong><span>{t('collaborationHub.notificationCenter.emptyHint')}</span></div>
          : <div className="shell-communication__list">{visibleItems.map(item => <NotificationItem key={`${item.kind}-${item.entityId}`} item={item} onOpen={() => closeNotifications(false)} />)}</div>}
      <footer><Link to="/collaboration/messages" onClick={() => closeNotifications(false)}>{t('collaborationHub.notificationCenter.openWorkspace')}</Link><span>{summary.generatedAtUtc ? t('collaborationHub.notificationCenter.updated', { date: formatCollaborationTime(summary.generatedAtUtc, i18n.language) }) : ''}</span></footer>
    </section>}
    {!activeMeetingId && <aside className={`collaboration-dock${dockOpen ? ' is-open' : ''}`} aria-label={t('collaborationHub.notificationCenter.dock.title')}>
      <button ref={dockLauncher} type="button" className="collaboration-dock__launcher"
        aria-label={t('collaborationHub.notificationCenter.dock.open')} aria-expanded={dockOpen}
        aria-controls="collaboration-global-dock" aria-haspopup="dialog"
        onClick={() => {
          if (dockOpen) closeDock()
          else { setDockOpen(true); setOpen(false); if (canLoadNotifications) void load() }
        }}>
        <MessageSquareText aria-hidden="true" />
        <span>{t('collaborationHub.notificationCenter.dock.title')}</span>
        {summary.totalCount > 0 && <span className="collaboration-dock__badge" aria-label={t('collaborationHub.notificationCenter.total', { count: summary.totalCount })}>{capNotificationCount(summary.totalCount)}</span>}
      </button>
      {dockOpen && <section id="collaboration-global-dock" className="collaboration-dock__panel" role="dialog" aria-modal="false"
        aria-label={t('collaborationHub.notificationCenter.dock.title')}>
        <header className="collaboration-dock__header">
          <div><strong>{t('collaborationHub.notificationCenter.dock.title')}</strong><small>{t('collaborationHub.notificationCenter.dock.subtitle')}</small></div>
          <div className="collaboration-dock__header-actions">
            {canLoadNotifications && <button type="button" onClick={() => void load()} disabled={loading} aria-label={t('collaborationHub.refresh')} title={t('collaborationHub.refresh')}><RefreshCw className={loading ? 'is-spinning' : ''} /></button>}
            <button type="button" onClick={() => closeDock()} aria-label={t('collaborationHub.notificationCenter.dock.close')} title={t('collaborationHub.notificationCenter.dock.close')}><X /></button>
          </div>
        </header>
        <nav className="collaboration-dock__actions" aria-label={t('collaborationHub.notificationCenter.dock.actions')}>
          {dockActions.map((action, index) => <Link key={action.id} ref={index === 0 ? dockFirstControl : undefined} to={action.path}
            className={`collaboration-dock__action collaboration-dock__action--${action.id.toLowerCase()}`}
            onClick={() => closeDock(false)}>
            <DockActionIcon action={action} />
            <span>{t(action.labelKey)}</span>
          </Link>)}
        </nav>
        {canLoadNotifications && <>
          <div className="collaboration-dock__section-title">
            <strong>{t('collaborationHub.notificationCenter.dock.recent')}</strong>
            {summary.generatedAtUtc && <time>{formatCollaborationTime(summary.generatedAtUtc, i18n.language)}</time>}
          </div>
          <div className="shell-communication__tabs" role="group" aria-label={t('collaborationHub.notificationCenter.filters')}>
            {tabs.map(tab => <button key={tab.id} type="button" aria-pressed={filter === tab.id}
              className={filter === tab.id ? 'is-active' : ''} onClick={() => setFilter(tab.id)}>
              {t(`collaborationHub.notificationCenter.${tab.id.toLowerCase()}`)}<span>{capNotificationCount(tab.count)}</span>
            </button>)}
          </div>
          {unavailable ? <div className="shell-communication__state" role="status"><strong>{t('collaborationHub.notificationCenter.unavailable')}</strong><button type="button" onClick={() => void load()}>{t('collaborationHub.retry')}</button></div>
            : visibleItems.length === 0 ? <div className="shell-communication__state collaboration-dock__empty"><Bell aria-hidden="true" /><strong>{t('collaborationHub.notificationCenter.empty')}</strong><span>{t('collaborationHub.notificationCenter.dock.empty')}</span></div>
              : <div className="shell-communication__list collaboration-dock__list">{visibleItems.slice(0, 6).map(item => <NotificationItem key={`${item.kind}-${item.entityId}`} item={item} onOpen={() => closeDock(false)} />)}</div>}
        </>}
      </section>}
    </aside>}
    {toastKind && !open && !dockOpen && <div className="shell-communication__toast" role="status" aria-live="polite">
      <NotificationIcon kind={toastKind} video={false} />
      <div><strong>{t(`collaborationHub.notificationCenter.toast.${toastKind.toLowerCase()}`)}</strong><button type="button" onClick={() => { setOpen(true); setDockOpen(false); setToastKind(null); void load() }}>{t('collaborationHub.notificationCenter.view')}</button></div>
      <button type="button" className="shell-communication__toast-close" aria-label={t('common.close')} onClick={() => setToastKind(null)}>×</button>
    </div>}
  </div>
}

function DockActionIcon({ action }: { action: CollaborationDockAction }) {
  if (action.id === 'MESSAGES') return <MessageSquareText aria-hidden="true" />
  if (action.id === 'LETTERS') return <Mail aria-hidden="true" />
  if (action.id === 'NEW_MEETING') return <CalendarPlus aria-hidden="true" />
  return <Video aria-hidden="true" />
}

function NotificationItem({ item, onOpen }: { item: CollaborationNotificationItem; onOpen: () => void }) {
  const { t, i18n } = useTranslation()
  return <Link className={`shell-communication__item shell-communication__item--${item.kind.toLowerCase()}`}
    to={item.targetUrl} onClick={onOpen}>
    <span className="shell-communication__icon"><NotificationIcon kind={item.kind} video={item.videoEnabled} /></span>
    <span className="shell-communication__copy"><strong>{item.title}</strong><small>{item.actorDisplayName ? `${item.actorDisplayName} · ` : ''}{item.preview}</small><time>{formatCollaborationTime(item.occurredAtUtc, i18n.language)}</time></span>
    <span className="shell-communication__meta">{item.count > 1 ? capNotificationCount(item.count) : item.requiresAction ? t('collaborationHub.notificationCenter.action') : ''}</span>
  </Link>
}

function NotificationIcon({ kind, video }: { kind: CollaborationNotificationKind; video: boolean }) {
  if (kind === 'MESSAGE') return <MessageSquareText aria-hidden="true" />
  if (kind === 'LETTER') return <Mail aria-hidden="true" />
  if (video) return <Video aria-hidden="true" />
  return <><PhoneIncoming aria-hidden="true" /><Mic className="shell-communication__mic" aria-hidden="true" /></>
}
