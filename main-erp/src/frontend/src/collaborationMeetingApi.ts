import { apiRoot } from './apiClient'
import { collaborationError } from './collaborationSupport'

export class MeetingRequestError extends Error {
  readonly status: number | null

  constructor(message: string, status: number | null) {
    super(message)
    this.name = 'MeetingRequestError'
    this.status = status
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, init)
  } catch {
    throw new MeetingRequestError('Meeting request failed.', null)
  }
  if (response.ok) return response.status === 204 ? undefined as T : await response.json() as T
  const body = await response.json().catch(() => null)
  throw new MeetingRequestError(
    collaborationError(response.status, body, 'Meeting request failed.'),
    response.status,
  )
}

export function meetingRequest<T>(path = '', init?: RequestInit) {
  return request<T>(`${apiRoot}/collaboration/meetings${path}`, init)
}

export function guestMeetingRequest<T>(path: string, body: unknown) {
  return request<T>(`${apiRoot}/collaboration/meeting-guests${path}`, jsonRequest('POST', body))
}

export function collaborationRequest<T>(path: string, init?: RequestInit) {
  return request<T>(`${apiRoot}/collaboration${path}`, init)
}

export function jsonRequest(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }
}
