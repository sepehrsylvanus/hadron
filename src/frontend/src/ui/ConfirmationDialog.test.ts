import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { act, create, type ReactTestRenderer } from 'react-test-renderer'
import { createServer, type ViteDevServer } from 'vite'

let server: ViteDevServer | undefined
let Module: typeof import('./index.tsx') | undefined
type KeyEvent = { key: string; shiftKey?: boolean; preventDefault: () => void }
let escapeHandler: ((event: KeyEvent) => void) | undefined

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  document: {
    activeElement: { focus: () => undefined },
    body: { style: { overflow: '' } },
    addEventListener: (name: string, handler: (event: KeyEvent) => void) => {
      if (name === 'keydown') escapeHandler = handler
    },
    removeEventListener: (name: string, handler: (event: KeyEvent) => void) => {
      if (name === 'keydown' && escapeHandler === handler) escapeHandler = undefined
    },
  },
})

after(async () => server?.close())

test('dialog focus is initialized once and an inline cancel callback cannot steal focus on every controlled-input render', async () => {
  const { ConfirmationDialog } = await dialogModule()
  let confirmFocusCount = 0
  let oldCancelCount = 0
  let latestCancelCount = 0
  let renderer!: ReactTestRenderer
  const dialog = (onCancel: () => void) => React.createElement(ConfirmationDialog, {
    open: true,
    title: 'Confirm',
    confirmLabel: 'Save',
    cancelLabel: 'Cancel',
    tone: 'normal' as const,
    onConfirm: () => undefined,
    onCancel,
  })

  await act(async () => {
    renderer = create(dialog(() => { oldCancelCount++ }), {
      createNodeMock: element => element.type === 'button' && String(element.props.className).includes('button--primary')
        ? { focus: () => { confirmFocusCount++ } }
        : element.type === 'section' ? { querySelector: () => null }
        : {},
    })
  })
  assert.equal(confirmFocusCount, 1)

  await act(async () => renderer.update(dialog(() => { latestCancelCount++ })))
  assert.equal(confirmFocusCount, 1, 'a controlled input render must not refocus the confirm button')
  let escapePrevented = false
  escapeHandler?.({ key: 'Escape', preventDefault: () => { escapePrevented = true } })
  assert.equal(escapePrevented, true)
  assert.equal(oldCancelCount, 0)
  assert.equal(latestCancelCount, 1, 'Escape must use the latest inline callback without reinstalling the focus effect')
  await act(async () => renderer.unmount())
})

test('form fields expose required, hint and error relationships to assistive technology', async () => {
  const { FormField } = await dialogModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(FormField, { label: 'Quantity', hint: 'Use kilograms', required: true }, React.createElement('input')))
  })
  const input = renderer.root.findByType('input')
  const hint = renderer.root.findByType('small')
  assert.equal(input.props['aria-describedby'], hint.props.id)
  assert.equal(input.props['aria-invalid'], undefined)
  assert.equal(input.props['aria-required'], true)

  await act(async () => {
    renderer.update(React.createElement(FormField, { label: 'Quantity', error: 'Required' }, React.createElement('input')))
  })
  const invalidInput = renderer.root.findByType('input')
  const error = renderer.root.findByType('small')
  assert.equal(invalidInput.props['aria-describedby'], error.props.id)
  assert.equal(invalidInput.props['aria-invalid'], true)
  assert.equal(error.props.role, 'alert')
  await act(async () => renderer.unmount())
})

test('data-table headers identify their column scope and expose busy state', async () => {
  const { DataTable } = await dialogModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(DataTable, {
      headers: ['Code', 'Name'], loadingLabel: 'Loading', emptyTitle: 'Empty', noResultTitle: 'No results',
    }, React.createElement('tr', null, React.createElement('td', null, 'P-1'), React.createElement('td', null, 'Product'))))
  })
  assert.deepEqual(renderer.root.findAllByType('th').map(header => header.props.scope), ['col', 'col'])
  assert.equal(renderer.root.findByType('table').props['aria-busy'], undefined)
  await act(async () => renderer.update(React.createElement(DataTable, {
    headers: ['Code', 'Name'], loading: true, loadingLabel: 'Loading', emptyTitle: 'Empty', noResultTitle: 'No results',
  }, null)))
  assert.equal(renderer.root.findByType('table').props['aria-busy'], true)
  await act(async () => renderer.unmount())
})

test('critical dialogs focus the least-destructive action and lock background scrolling', async () => {
  const { ConfirmationDialog } = await dialogModule()
  let confirmFocusCount = 0
  let cancelFocusCount = 0
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(ConfirmationDialog, {
      open: true,
      title: 'Delete record?',
      confirmLabel: 'Delete',
      cancelLabel: 'Cancel',
      onConfirm: () => undefined,
      onCancel: () => undefined,
    }), {
      createNodeMock: element => element.type === 'section' ? { querySelector: () => null }
        : element.type === 'button' && String(element.props.className).includes('button--danger') ? { focus: () => { confirmFocusCount++ } }
        : element.type === 'button' ? { focus: () => { cancelFocusCount++ } }
        : {},
    })
  })
  assert.equal(confirmFocusCount, 0)
  assert.equal(cancelFocusCount, 1)
  assert.equal(document.body.style.overflow, 'hidden')
  await act(async () => renderer.unmount())
  assert.equal(document.body.style.overflow, '')
})

test('a form dialog does not override its auto-focused field with the confirm button', async () => {
  const { ConfirmationDialog } = await dialogModule()
  let confirmFocusCount = 0
  let inputFocusCount = 0
  let renderer!: ReactTestRenderer
  const dialog = (value: string) => React.createElement(ConfirmationDialog, {
    open: true,
    title: 'Edit forecast',
    confirmLabel: 'Save',
    cancelLabel: 'Cancel',
    tone: 'normal' as const,
    onConfirm: () => undefined,
    onCancel: () => undefined,
  }, React.createElement('input', { autoFocus: true, value, onChange: () => undefined }))

  await act(async () => {
    renderer = create(dialog('1'), {
      createNodeMock: element => element.type === 'button' && String(element.props.className).includes('button--primary')
        ? { focus: () => { confirmFocusCount++ } }
        : element.type === 'section' ? { querySelector: () => ({ focus: () => { inputFocusCount++ } }) }
        : {},
    })
  })
  await act(async () => renderer.update(dialog('12')))
  assert.equal(confirmFocusCount, 0, 'typing a second digit must not move focus away from the form field')
  assert.equal(inputFocusCount, 1, 'the form field is focused once when the dialog opens')
  await act(async () => renderer.unmount())
})

async function dialogModule() {
  if (Module) return Module
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  Module = await server.ssrLoadModule('/src/ui/index.tsx') as typeof import('./index.tsx')
  return Module
}
