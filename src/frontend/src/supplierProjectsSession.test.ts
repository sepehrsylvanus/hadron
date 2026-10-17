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
  supplierProjectManualNote,
  supplierProjectsFor,
  updateSupplierProject,
} from './supplierProjectsSession.ts'

beforeEach(() => resetSupplierProjectsSession())

test('project display name is product - order year - repeat, manual info is the note below', () => {
  const project = {
    manualInfo: 'پروژه خط تولید', productName: 'پنکه ایستاده', orderYear: '1404', orderRepeat: 2,
  }
  assert.equal(supplierProjectDisplayName(project), 'پنکه ایستاده - 1404 - 2')
  assert.equal(supplierProjectManualNote(project), '(پروژه خط تولید)')

  // Seeded projects (no manual info yet) get a clean name and no note.
  const seeded = { manualInfo: '', productName: 'موتور فن', orderYear: '1402', orderRepeat: 1 }
  assert.equal(supplierProjectDisplayName(seeded), 'موتور فن - 1402 - 1')
  assert.equal(supplierProjectManualNote(seeded), '')

  // Missing leading parts drop out; the repeat number always closes the name.
  assert.equal(supplierProjectDisplayName({ productName: '', orderYear: '', orderRepeat: 1 }), '1')
})

test('projects are created per supplier, edited, ended, and removed in session only', () => {
  ensureSupplierProjects('supplier-1', [{
    ...blankSupplierProjectDraft(), productName: 'موتور فن', pieceSerial: 'PART-30',
  }])
  let projects = supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1')
  assert.equal(projects.length, 1)
  assert.equal(projects[0].status, 'current')

  // Seeding again must not duplicate or wipe session edits.
  updateSupplierProject('supplier-1', projects[0].id, { productName: 'موتور فن جدید' })
  ensureSupplierProjects('supplier-1', [{ ...blankSupplierProjectDraft(), productName: 'نباید اضافه شود' }])
  projects = supplierProjectsFor(getSupplierProjectsSessionState(), 'supplier-1')
  assert.equal(projects.length, 1)
  assert.equal(projects[0].productName, 'موتور فن جدید')

  const created = addSupplierProject('supplier-1', {
    ...blankSupplierProjectDraft(), manualInfo: 'پروژه خط تولید', productName: 'پنکه ایستاده',
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
