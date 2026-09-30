import assert from 'node:assert/strict'
import test from 'node:test'
import { createMaterialPlanningApiError, formatMaterialPlanningProblem } from './materialPlanningProblem.ts'

const translate = (key: string, options?: Record<string, unknown>) => key.startsWith('materialPlanning.errors.endpoints.')
  ? `translated:${key}`
  : `${key}:${String(options?.endpoint ?? '')}:${String(options?.traceId ?? options?.status ?? '')}`

test('generic RFC problem is converted to a safe endpoint-aware message', async () => {
  const response = new Response(JSON.stringify({
    type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
    title: 'An error occurred while processing your request.',
    status: 500,
    traceId: 'trace-123',
  }), { status: 500, headers: { 'Content-Type': 'application/problem+json' } })

  const error = await createMaterialPlanningApiError(response, '/api/material-planning/runs/1/products?page=1')
  assert.equal(formatMaterialPlanningProblem(error, translate),
    'materialPlanning.errors.server:translated:materialPlanning.errors.endpoints.products:trace-123')
  assert.equal(error.message, 'HTTP 500')
})

test('network failures do not expose browser-specific Failed to fetch text', () => {
  assert.equal(formatMaterialPlanningProblem(new TypeError('Failed to fetch'), translate),
    'materialPlanning.errors.network::')
})
