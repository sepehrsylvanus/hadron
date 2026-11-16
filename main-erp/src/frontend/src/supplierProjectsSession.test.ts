import assert from 'node:assert/strict'
import test, { beforeEach } from 'node:test'
import {
  addSupplierProject,
  blankSupplierProjectDraft,
  ensureSupplierProjects,
  getSupplierProjectsSessionState,
  removeSupplierProject,
  resetSupplierProjectsSession,
  setSupplierProjectStatus,
  supplierProjectDisplayName,
  supplierProjectsFor,
  updateSupplierProject,
} from './supplierProjectsSession.ts'

beforeEach(() => resetSupplierProjectsSession())

test('project display name follows the standard formula', () => {
  const name = supplierProjectDisplayName({
    manualInfo: 'پروژه خط تولید', shamsiYear: '1404', orderCount: 3,
    pieceName: 'پره فن', productName: 'پنکه ایستاده', orderYear: '1404', orderRepeat: 2,
  })
  assert.equal(name, 'پروژه خط تولید 1404 - 3 - پره فن-پنکه ایستاده-1404-2')

  // Seeded projects (no manual info yet) still produce a clean name.
  const seeded = supplierProjectDisplayName({
    manualInfo: '', shamsiYear: '1402', orderCount: 1,
    pieceName: 'موتور فن', productName: '', orderYear: '1402', orderRepeat: 1,
  })
  assert.equal(seeded, '1402 - 1 - موتور فن-1402-1')
})

test('projects are created per supplier, edited, ended, and removed in session only', () => {
  ensureSupplierProjects('supplier-1', [{
    ...blankSupplierProjectDraft(), pieceName: 'موتور فن', pieceSerial: 'PART-30',
  }])
  let projects = supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1')
  assert.equal(projects.length, 1)
  assert.equal(projects[0].status, 'current')

  // Seeding again must not duplicate or wipe session edits.
  updateSupplierProject('supplier-1', projects[0].id, { pieceName: 'موتور فن جدید' })
  ensureSupplierProjects('supplier-1', [{ ...blankSupplierProjectDraft(), pieceName: 'نباید اضافه شود' }])
  projects = supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1')
  assert.equal(projects.length, 1)
  assert.equal(projects[0].pieceName, 'موتور فن جدید')

  const created = addSupplierProject('supplier-1', {
    ...blankSupplierProjectDraft(), manualInfo: 'پروژه خط تولید', orderCount: 2,
  })
  assert.equal(supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1').length, 2)
  setSupplierProjectStatus('supplier-1', created.id, 'previous')
  const after = supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1')
  assert.equal(after.filter(project => project.status === 'current').length, 1)
  assert.equal(after.filter(project => project.status === 'previous').length, 1)

  removeSupplierProject('supplier-1', created.id)
  assert.equal(supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1').length, 1)
  assert.equal(supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-2').length, 0, 'other suppliers stay untouched')
})
