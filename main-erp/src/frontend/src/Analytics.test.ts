import assert from 'node:assert/strict'
import test from 'node:test'
import { analyticsDisplayName, analyticsExportUrl } from './analyticsUi.ts'

test('analytics uses canonical bilingual name fallback without changing stored data', () => {
  assert.equal(analyticsDisplayName('نام فارسی', 'English Name', 'fa-IR'), 'نام فارسی')
  assert.equal(analyticsDisplayName('نام فارسی', 'English Name', 'en'), 'English Name')
  assert.equal(analyticsDisplayName('نام فارسی', undefined, 'en'), 'نام فارسی')
  assert.equal(analyticsDisplayName('نام فارسی', '   ', 'en'), 'نام فارسی')
})

test('analytics Excel URL preserves the authoritative filter and historical provenance context', () => {
  const url = new URL(analyticsExportUrl({
    report: 'commercial-commitments',
    evaluationDate: '2026-08-25',
    language: 'fa-IR',
    maximumRows: 10000,
    search: 'PO-000123',
    dataCutoffUtc: '2026-08-25T09:30:00.000Z',
    runId: '11111111-1111-1111-1111-111111111111',
    generationId: '22222222-2222-2222-2222-222222222222',
    fromDate: '2026-08-01',
    toDate: '2026-08-25',
  }), 'http://localhost')

  assert.equal(url.pathname, '/api/v1/analytics/exports/commercial-commitments/xlsx')
  assert.equal(url.searchParams.get('search'), 'PO-000123')
  assert.equal(url.searchParams.get('dataCutoffUtc'), '2026-08-25T09:30:00.000Z')
  assert.equal(url.searchParams.get('runId'), '11111111-1111-1111-1111-111111111111')
  assert.equal(url.searchParams.get('generationId'), '22222222-2222-2222-2222-222222222222')
  assert.equal(url.searchParams.get('fromDate'), '2026-08-01')
  assert.equal(url.searchParams.get('toDate'), '2026-08-25')
  assert.equal(url.searchParams.get('maximumRows'), '10000')
})
