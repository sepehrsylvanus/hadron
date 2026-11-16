import assert from 'node:assert/strict'
import test from 'node:test'
import { isApiUrl, normalizeDevelopmentApiUrl } from './apiUrlSupport.ts'

test('configured development API URLs use the same-origin proxy', () => {
  const configured = 'http://localhost:5080/api'
  const request = `${configured}/suppliers?includeInactive=false`

  assert.equal(isApiUrl(request, '/api', configured), true)
  assert.equal(
    normalizeDevelopmentApiUrl(request, '/api', configured, true),
    '/api/suppliers?includeInactive=false',
  )
})

test('production keeps its configured absolute API URL', () => {
  const request = 'https://erp.example.com/api/suppliers'
  assert.equal(
    normalizeDevelopmentApiUrl(request, 'https://erp.example.com/api', 'https://erp.example.com/api', false),
    request,
  )
})
