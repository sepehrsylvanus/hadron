import assert from 'node:assert/strict'
import test from 'node:test'
import { isSessionTerminationResponse, loadInitialAuthentication } from './authSessionSupport.ts'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  })
}

test('initial authentication distinguishes an anonymous user from an unavailable service', async () => {
  const anonymous = await loadInitialAuthentication('/api', async input =>
    input.endsWith('/bootstrap-status')
      ? json({ initialized: true })
      : json({ code: 'AUTHENTICATION_REQUIRED' }, 401))
  assert.deepEqual(anonymous, { kind: 'unauthenticated' })

  const serverFailure = await loadInitialAuthentication('/api', async input =>
    input.endsWith('/bootstrap-status')
      ? json({ initialized: true })
      : json({ title: 'Server error' }, 500))
  assert.deepEqual(serverFailure, { kind: 'unavailable' })

  const networkFailure = await loadInitialAuthentication('/api', async () => {
    throw new TypeError('Network request failed')
  })
  assert.deepEqual(networkFailure, { kind: 'unavailable' })
})

test('initial authentication does not call the protected endpoint before initial setup', async () => {
  const requests: string[] = []
  const result = await loadInitialAuthentication('/api', async input => {
    requests.push(input)
    return json({ initialized: false })
  })

  assert.deepEqual(result, { kind: 'setup-required' })
  assert.deepEqual(requests, ['/api/auth/bootstrap-status'])
})

test('only explicit session-termination responses request a global sign-out', async () => {
  assert.equal(await isSessionTerminationResponse(json({ code: 'AUTHENTICATION_REQUIRED' }, 401)), true)
  assert.equal(await isSessionTerminationResponse(json({ code: 'INVALID_SESSION' }, 401)), true)
  assert.equal(await isSessionTerminationResponse(json({ code: 'SESSION_REVOKED' }, 401)), true)
  assert.equal(await isSessionTerminationResponse(json({ code: 'INVALID_CREDENTIALS' }, 401)), false)
  assert.equal(await isSessionTerminationResponse(json({ title: 'unrelated unauthorized response' }, 401)), false)
  assert.equal(await isSessionTerminationResponse(json({ code: 'SESSION_REVOKED' }, 500)), false)
})
