export type ApiPage<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
export type DirectoryUser = { id: string; userName: string; fullName: string; organizationalUnit: string; photoUrl: string }
export type Member = { userId: string; displayName: string; role: string; joinedAtUtc: string; lastReadAtUtc?: string | null }
export type Attachment = { id: string; fileName: string; contentType: string; size: number; sha256: string; validationStatus: string; downloadUrl: string; uploadedByUserId: string; uploadedByDisplayName: string; createdAtUtc: string }
export type Reaction = { code: string; count: number; reactedByCurrentUser: boolean }
export type CallSession = { id: string; conversationId: string; provider: string; roomName: string; status: string; hostUserId: string; hostDisplayName: string; videoEnabled: boolean; startedAtUtc: string; endedAtUtc?: string | null; version: number }
export type ConversationSummary = { id: string; type: string; name: string; description?: string | null; isPrivate: boolean; isArchived: boolean; memberCount: number; unreadCount: number; lastMessageAtUtc?: string | null; lastMessagePreview?: string | null; version: number }
export type Conversation = ConversationSummary & { createdByUserId: string; createdByDisplayName: string; createdAtUtc: string; updatedAtUtc: string; members: Member[]; activeCall?: CallSession | null }
export type Message = { id: string; conversationId: string; authorUserId: string; authorDisplayName: string; type: string; body?: string | null; stickerCode?: string | null; replyToMessageId?: string | null; officialLetterId?: string | null; createdAtUtc: string; editedAtUtc?: string | null; deletedAtUtc?: string | null; version: number; reactions: Reaction[]; attachments: Attachment[] }
export type LetterSummary = { id: string; number: string; subject: string; classification: string; status: string; senderUserId: string; senderDisplayName: string; dueDate?: string | null; issuedAtUtc?: string | null; createdAtUtc: string; version: number }
export type Letter = LetterSummary & { body: string; acknowledgedAtUtc?: string | null; closedAtUtc?: string | null; updatedAtUtc: string; recipients: { userId: string; displayName: string; type: string }[]; events: { id: string; eventType: string; actorUserId: string; actorDisplayName: string; note?: string | null; occurredAtUtc: string }[]; attachments: Attachment[] }
export type CallConfiguration = { available: boolean; serverUrl?: string | null; provider: string; reason?: string | null }
export type CallJoin = { call: CallSession; serverUrl: string; accessToken: string; createdNew: boolean }
export type CollaborationNotificationKind = 'MESSAGE' | 'LETTER' | 'CALL'
export type CollaborationNotificationItem = {
  kind: CollaborationNotificationKind; entityId: string; conversationId?: string | null
  title: string; preview?: string | null; actorDisplayName?: string | null; occurredAtUtc: string
  count: number; requiresAction: boolean; videoEnabled: boolean; targetUrl: string
}
export type CollaborationNotificationSummary = {
  totalCount: number; unreadMessageCount: number; pendingLetterCount: number; activeCallCount: number
  generatedAtUtc: string; items: CollaborationNotificationItem[]
}

export type CollaborationNotificationFilter = 'ALL' | CollaborationNotificationKind

export type ConversationMeetingEntry = {
  path: '/collaboration/meetings/new' | '/collaboration/meetings/new?mode=instant'
  labelKey: 'collaborationMeetings.newMeeting' | 'collaborationMeetings.instantMeeting'
  mode: 'SCHEDULED' | 'INSTANT'
}

export type CollaborationDockAction = {
  id: 'MESSAGES' | 'MEETINGS' | 'NEW_MEETING' | 'LETTERS'
  path: string
  labelKey: string
}

export function collaborationDockActions(hasPermission: (permission: string) => boolean): CollaborationDockAction[] {
  const actions: CollaborationDockAction[] = []

  if (hasPermission('collaboration.workspace.view')) {
    actions.push({ id: 'MESSAGES', path: '/collaboration/messages', labelKey: 'collaborationHub.messagesTitle' })
  }

  if (hasPermission('collaboration.meeting.view')) {
    actions.push({ id: 'MEETINGS', path: '/collaboration/meetings', labelKey: 'collaborationMeetings.title' })
    const meetingEntry = conversationMeetingEntry(hasPermission)
    if (meetingEntry) {
      actions.push({ id: 'NEW_MEETING', path: meetingEntry.path, labelKey: meetingEntry.labelKey })
    }
  }

  if (hasPermission('correspondence.letter.view')) {
    actions.push({ id: 'LETTERS', path: '/collaboration/correspondence', labelKey: 'collaborationHub.lettersTitle' })
  }

  return actions
}

export function canOpenMeetingWorklist(hasPermission: (permission: string) => boolean) {
  return hasPermission('collaboration.meeting.view')
}

export function conversationMeetingEntry(hasPermission: (permission: string) => boolean): ConversationMeetingEntry | null {
  if (!hasPermission('collaboration.meeting.view')) return null

  if (
    hasPermission('collaboration.meeting.schedule')
    && hasPermission('collaboration.meeting.start')
  ) {
    return {
      path: '/collaboration/meetings/new?mode=instant',
      labelKey: 'collaborationMeetings.instantMeeting',
      mode: 'INSTANT',
    }
  }

  if (hasPermission('collaboration.meeting.schedule')) {
    return {
      path: '/collaboration/meetings/new',
      labelKey: 'collaborationMeetings.newMeeting',
      mode: 'SCHEDULED',
    }
  }

  return null
}

export function filterCollaborationNotifications(items: CollaborationNotificationItem[], filter: CollaborationNotificationFilter) {
  return filter === 'ALL' ? items : items.filter(item => item.kind === filter)
}

export function capNotificationCount(count: number) {
  return count > 99 ? '99+' : String(Math.max(0, count))
}

export const stickerCodes = ['HELLO', 'DONE', 'CHECKING', 'WAIT', 'SENDING', 'SORRY', 'GREAT'] as const
export type StickerCode = typeof stickerCodes[number]
export const reactionCodes = ['LIKE', 'HEART', 'THANKS', 'DONE', 'CHECK'] as const

export function displayConversationName(conversation: Pick<Conversation, 'type' | 'name' | 'members'>, currentUserId: string) {
  if (conversation.type !== 'DIRECT') return conversation.name
  return conversation.members.find(member => member.userId !== currentUserId)?.displayName ?? conversation.name
}

export function formatCollaborationTime(value: string | null | undefined, language: string, withTime = true) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  const locale = language.startsWith('fa') ? 'fa-IR-u-ca-persian' : 'en-GB'
  return new Intl.DateTimeFormat(locale, withTime
    ? { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Tehran' }
    : { dateStyle: 'medium', timeZone: 'Asia/Tehran' }).format(date)
}

export function statusTone(status: string): 'positive' | 'critical' | 'warning' | 'information' | 'neutral' {
  if (status === 'CLOSED' || status === 'ACKNOWLEDGED') return 'positive'
  if (status === 'ISSUED' || status === 'ACTIVE') return 'information'
  if (status === 'DRAFT') return 'warning'
  return 'neutral'
}

export function collaborationError(status: number, body: unknown, fallback: string) {
  const problem = body as { title?: string; detail?: string; errors?: Record<string, string[]> } | null
  const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : ''
  if (validation) return validation
  return problem?.detail || problem?.title || (status === 403 ? 'Forbidden' : fallback)
}
