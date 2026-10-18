import assert from 'node:assert/strict'
import test from 'node:test'
import { canOpenMeetingWorklist, capNotificationCount, collaborationDockActions, collaborationError, conversationMeetingEntry, displayConversationName, filterCollaborationNotifications, formatCollaborationTime, statusTone, type CollaborationNotificationItem } from './collaborationSupport.ts'
import { normalizeJalaliDateInput } from './mrpRunUiSupport.ts'

test('direct conversation displays the other member while channels keep their name', () => {
  const members = [{ userId: 'me', displayName: 'Me', role: 'OWNER', joinedAtUtc: '' }, { userId: 'other', displayName: 'Sara', role: 'MEMBER', joinedAtUtc: '' }]
  assert.equal(displayConversationName({ type: 'DIRECT', name: 'legacy', members }, 'me'), 'Sara')
  assert.equal(displayConversationName({ type: 'CHANNEL', name: 'Planning', members }, 'me'), 'Planning')
})

test('status tones keep operational distinctions', () => {
  assert.equal(statusTone('DRAFT'), 'warning')
  assert.equal(statusTone('ISSUED'), 'information')
  assert.equal(statusTone('CLOSED'), 'positive')
})

test('API validation exposes field-specific messages', () => {
  assert.equal(collaborationError(400, { errors: { subject: ['Subject required.'] } }, 'fallback'), 'Subject required.')
  assert.equal(collaborationError(500, null, 'fallback'), 'fallback')
})

test('invalid timestamps never leak Invalid Date to the interface', () => {
  assert.equal(formatCollaborationTime('invalid', 'en'), '—')
})

test('official letters accept the ERP Jalali YYYYMMDD input contract', () => {
  assert.equal(normalizeJalaliDateInput('۱۴۰۵۰۶۲۴'), '14050624')
  assert.equal(normalizeJalaliDateInput('1405/06/24'), '14050624')
  assert.equal(normalizeJalaliDateInput('14051301'), null)
})

test('global communication center filters mixed operational notifications and caps large badges', () => {
  const base = { conversationId: null, preview: null, actorDisplayName: null, occurredAtUtc: '2026-09-08T10:00:00Z', count: 1, requiresAction: false, videoEnabled: false }
  const items: CollaborationNotificationItem[] = [
    { ...base, kind: 'MESSAGE', entityId: 'm1', title: 'پیام', targetUrl: '/collaboration/messages/c1' },
    { ...base, kind: 'LETTER', entityId: 'l1', title: 'نامه', targetUrl: '/collaboration/correspondence/l1' },
    { ...base, kind: 'CALL', entityId: 'c1', title: 'تماس', targetUrl: '/collaboration/messages/c1' },
  ]
  assert.equal(filterCollaborationNotifications(items, 'ALL').length, 3)
  assert.deepEqual(filterCollaborationNotifications(items, 'LETTER').map(item => item.entityId), ['l1'])
  assert.equal(capNotificationCount(0), '0')
  assert.equal(capNotificationCount(99), '99')
  assert.equal(capNotificationCount(128), '99+')
})

test('meeting entry points are explicit and follow the effective meeting permissions', () => {
  const permissions = new Set(['collaboration.meeting.view', 'collaboration.meeting.schedule'])
  const has = (permission: string) => permissions.has(permission)

  assert.equal(canOpenMeetingWorklist(has), true)
  assert.deepEqual(conversationMeetingEntry(has), {
    path: '/collaboration/meetings/new',
    labelKey: 'collaborationMeetings.newMeeting',
    mode: 'SCHEDULED',
  })

  permissions.add('collaboration.meeting.start')
  assert.deepEqual(conversationMeetingEntry(has), {
    path: '/collaboration/meetings/new?mode=instant',
    labelKey: 'collaborationMeetings.instantMeeting',
    mode: 'INSTANT',
  })

  permissions.clear()
  assert.equal(canOpenMeetingWorklist(has), false)
  assert.equal(conversationMeetingEntry(has), null)

  permissions.add('collaboration.meeting.start')
  assert.equal(conversationMeetingEntry(has), null, 'an action must not link into a route the user cannot view')

  permissions.add('collaboration.meeting.view')
  assert.equal(conversationMeetingEntry(has), null, 'instant creation also requires the backend schedule permission')
})

test('global collaboration dock exposes only destinations the current user can actually open', () => {
  const permissions = new Set(['collaboration.meeting.view', 'collaboration.meeting.schedule'])
  const has = (permission: string) => permissions.has(permission)

  assert.deepEqual(collaborationDockActions(has).map(action => [action.id, action.path]), [
    ['MEETINGS', '/collaboration/meetings'],
    ['NEW_MEETING', '/collaboration/meetings/new'],
  ])

  permissions.add('collaboration.workspace.view')
  permissions.add('correspondence.letter.view')
  assert.deepEqual(collaborationDockActions(has).map(action => action.id), ['MESSAGES', 'MEETINGS', 'NEW_MEETING', 'LETTERS'])

  permissions.clear()
  permissions.add('collaboration.meeting.start')
  assert.deepEqual(collaborationDockActions(has), [], 'an action permission cannot expose a route without view permission')

  permissions.add('collaboration.meeting.view')
  assert.deepEqual(collaborationDockActions(has).map(action => action.id), ['MEETINGS'], 'start alone cannot expose instant creation without schedule permission')
})
