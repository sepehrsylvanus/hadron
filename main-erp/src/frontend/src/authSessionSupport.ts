export type InitialAuthentication<TUser> =
  | { kind: 'authenticated'; user: TUser }
  | { kind: 'setup-required' }
  | { kind: 'unauthenticated' }
  | { kind: 'unavailable' }

type AuthFetch = (input: string, init?: RequestInit) => Promise<Response>

const sessionTerminationCodes = new Set([
  'AUTHENTICATION_REQUIRED',
  'INVALID_SESSION',
  'SESSION_REVOKED',
])

export async function loadInitialAuthentication<TUser>(
  root: string,
  fetcher: AuthFetch = fetch,
): Promise<InitialAuthentication<TUser>> {
  try {
    const statusResponse = await fetcher(`${root}/auth/bootstrap-status`)
    if (!statusResponse.ok) return { kind: 'unavailable' }
    const status = await statusResponse.json() as { initialized?: unknown }
    if (typeof status.initialized !== 'boolean') return { kind: 'unavailable' }
    if (!status.initialized) return { kind: 'setup-required' }

    const currentResponse = await fetcher(`${root}/auth/me`)
    if (currentResponse.ok) {
      return { kind: 'authenticated', user: await currentResponse.json() as TUser }
    }
    return currentResponse.status === 401
      ? { kind: 'unauthenticated' }
      : { kind: 'unavailable' }
  } catch {
    return { kind: 'unavailable' }
  }
}

export async function isSessionTerminationResponse(response: Response): Promise<boolean> {
  if (response.status !== 401 || !response.headers.get('content-type')?.includes('json')) return false
  const body = await response.clone().json().catch(() => null) as { code?: unknown } | null
  return typeof body?.code === 'string' && sessionTerminationCodes.has(body.code)
}
