import { isApiUrl, normalizeDevelopmentApiUrl } from './apiUrlSupport'
import { isSessionTerminationResponse } from './authSessionSupport'

const nativeFetch = window.fetch.bind(window)
const configuredApiRoot = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
// Keep development authentication same-origin through Vite's proxy. Mixing
// localhost and 127.0.0.1 prevents SameSite antiforgery/session cookies from
// being sent, even when CORS itself is configured correctly.
const apiRoot = import.meta.env.DEV ? '/api' : configuredApiRoot
let csrfToken: string | null = null

function isUnsafe(method: string) {
  return !['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase())
}

function isApiRequest(input: RequestInfo | URL) {
  const value = input instanceof Request ? input.url : String(input)
  return isApiUrl(value, apiRoot, configuredApiRoot)
}

function normalizeApiRequest(input: RequestInfo | URL) {
  const value = input instanceof Request ? input.url : String(input)
  const sameOriginUrl = normalizeDevelopmentApiUrl(value, apiRoot, configuredApiRoot, import.meta.env.DEV)
  if (sameOriginUrl === value) return input
  return input instanceof Request ? new Request(sameOriginUrl, input) : sameOriginUrl
}

async function token() {
  if (csrfToken) return csrfToken
  const response = await nativeFetch(`${apiRoot}/auth/csrf`, { credentials: 'include' })
  if (!response.ok) throw new Error('CSRF bootstrap failed')
  csrfToken = (await response.json() as { token: string }).token
  return csrfToken
}

export function installApiClient() {
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    if (!isApiRequest(input)) return nativeFetch(input, init)
    const normalizedInput = normalizeApiRequest(input)
    const method = init?.method ?? (normalizedInput instanceof Request ? normalizedInput.method : 'GET')
    const headers = new Headers(normalizedInput instanceof Request ? normalizedInput.headers : undefined)
    new Headers(init?.headers).forEach((value, key) => headers.set(key, value))
    if (isUnsafe(method) && !String(input).includes('/auth/bootstrap')) headers.set('X-CSRF-TOKEN', await token())
    const response = await nativeFetch(normalizedInput, { ...init, headers, credentials: 'include' })
    const requestUrl = String(input)
    if (response.ok && ['/auth/login', '/auth/logout', '/auth/change-password'].some(path => requestUrl.includes(path))) {
      csrfToken = null
    }
    if (response.status === 400 && response.headers.get('content-type')?.includes('json')) {
      const clone = response.clone(); const body = await clone.json().catch(() => null)
      if (body?.code === 'CSRF_VALIDATION_FAILED') csrfToken = null
    }
    if (await isSessionTerminationResponse(response)) {
      csrfToken = null
      window.dispatchEvent(new CustomEvent('hadron:authentication-required'))
    }
    return response
  }
}

export { apiRoot }
