import assert from 'node:assert/strict'
import test, { after } from 'node:test'
import * as React from 'react'
import { act, create, type ReactTestRenderer } from 'react-test-renderer'
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom'
import { createServer, type ViteDevServer } from 'vite'

type Listener = (event: never) => void
const documentListeners = new Map<string, Listener>()
const windowListeners = new Map<string, Listener>()
const historyGoCalls: number[] = []

Object.assign(globalThis, {
  IS_REACT_ACT_ENVIRONMENT: true,
  document: {
    activeElement: { focus: () => undefined },
    body: { style: { overflow: '' } },
    addEventListener: (name: string, listener: Listener) => documentListeners.set(name, listener),
    removeEventListener: (name: string, listener: Listener) => {
      if (documentListeners.get(name) === listener) documentListeners.delete(name)
    },
  },
  window: {
    location: { href: 'http://localhost/start', origin: 'http://localhost', pathname: '/start', search: '' },
    history: { state: { idx: 2 }, go: (delta: number) => historyGoCalls.push(delta) },
    addEventListener: (name: string, listener: Listener) => windowListeners.set(name, listener),
    removeEventListener: (name: string, listener: Listener) => {
      if (windowListeners.get(name) === listener) windowListeners.delete(name)
    },
  },
})

let server: ViteDevServer | undefined
let Guard: typeof import('./UnsavedChangesGuard.tsx').UnsavedChangesGuard | undefined
after(async () => server?.close())

function Harness({ when }: { when: boolean }) {
  const location = useLocation()
  const navigate = useNavigate()
  return React.createElement(React.Fragment, null,
    React.createElement(Guard!, { when }),
    React.createElement('output', null, location.pathname),
    React.createElement('button', { type: 'button', onClick: () => navigate('/saved') }, 'programmatic'),
  )
}

test('dirty forms intercept ordinary internal links but allow explicit programmatic navigation', async () => {
  Guard = await guardModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(MemoryRouter, { initialEntries: ['/start'] }, React.createElement(Harness, { when: true })), {
      createNodeMock: element => element.type === 'section' ? { querySelector: () => null }
        : element.type === 'button' ? { focus: () => undefined }
        : {},
    })
  })

  let prevented = false
  const click = documentListeners.get('click')
  assert.ok(click)
  await act(async () => click({
    defaultPrevented: false, button: 0, metaKey: false, ctrlKey: false, shiftKey: false, altKey: false,
    target: { closest: () => ({ href: 'http://localhost/next?view=all', target: '', hasAttribute: () => false }) },
    preventDefault: () => { prevented = true },
  } as never))
  assert.equal(prevented, true)
  assert.equal(renderer.root.findByProps({ role: 'alertdialog' }).props['aria-modal'], 'true')
  assert.equal(renderer.root.findByType('output').children.join(''), '/start')

  await act(async () => renderer.root.findAllByType('button').find(button => String(button.props.className).includes('button--danger'))!.props.onClick())
  assert.equal(renderer.root.findByType('output').children.join(''), '/next')

  await act(async () => renderer.root.findAllByType('button').find(button => button.children.includes('programmatic'))!.props.onClick())
  assert.equal(renderer.root.findByType('output').children.join(''), '/saved')
  await act(async () => renderer.unmount())
})

test('dirty forms register and clean up the native refresh protection', async () => {
  Guard = await guardModule()
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(MemoryRouter, null, React.createElement(Harness, { when: true })))
  })
  const beforeUnload = windowListeners.get('beforeunload')
  assert.ok(beforeUnload)
  let prevented = false
  beforeUnload({ preventDefault: () => { prevented = true }, returnValue: undefined } as never)
  assert.equal(prevented, true)
  await act(async () => renderer.unmount())
  assert.equal(windowListeners.has('beforeunload'), false)
})

test('dirty forms restore browser Back/Forward navigation and continue only after explicit discard', async () => {
  Guard = await guardModule()
  historyGoCalls.length = 0
  let renderer!: ReactTestRenderer
  await act(async () => {
    renderer = create(React.createElement(MemoryRouter, { initialEntries: ['/start'] }, React.createElement(Harness, { when: true })), {
      createNodeMock: element => element.type === 'section' ? { querySelector: () => null }
        : element.type === 'button' ? { focus: () => undefined }
        : {},
    })
  })

  const popstate = windowListeners.get('popstate')
  assert.ok(popstate)
  let prevented = false
  let stopped = false
  await act(async () => popstate({
    state: { idx: 1 },
    preventDefault: () => { prevented = true },
    stopImmediatePropagation: () => { stopped = true },
  } as never))
  assert.equal(prevented, true)
  assert.equal(stopped, true)
  assert.deepEqual(historyGoCalls, [1], 'Back is immediately reversed so the dirty form remains mounted')
  assert.equal(renderer.root.findByProps({ role: 'alertdialog' }).props['aria-modal'], 'true')

  // The compensating POP returns to the guarded entry and must pass through to BrowserRouter.
  await act(async () => popstate({ state: { idx: 2 }, preventDefault: () => undefined, stopImmediatePropagation: () => undefined } as never))
  await act(async () => renderer.root.findAllByType('button').find(button => String(button.props.className).includes('button--danger'))!.props.onClick())
  assert.deepEqual(historyGoCalls, [1, -1], 'confirmed discard repeats the original Back navigation exactly once')
  await act(async () => renderer.unmount())

  // Mount a fresh guarded entry and verify Forward uses the same reversible path.
  ;(window.history as unknown as { state: { idx: number } }).state = { idx: 2 }
  await act(async () => {
    renderer = create(React.createElement(MemoryRouter, { initialEntries: ['/start'] }, React.createElement(Harness, { when: true })), {
      createNodeMock: element => element.type === 'section' ? { querySelector: () => null }
        : element.type === 'button' ? { focus: () => undefined }
        : {},
    })
  })
  const forwardPopstate = windowListeners.get('popstate')
  assert.ok(forwardPopstate)
  await act(async () => forwardPopstate({ state: { idx: 3 }, preventDefault: () => undefined, stopImmediatePropagation: () => undefined } as never))
  assert.deepEqual(historyGoCalls, [1, -1, -1], 'Forward is also reversed while confirmation is pending')
  await act(async () => forwardPopstate({ state: { idx: 2 }, preventDefault: () => undefined, stopImmediatePropagation: () => undefined } as never))
  const dialog = renderer.root.findByProps({ role: 'alertdialog' })
  await act(async () => dialog.findAllByType('button').find(button => !String(button.props.className).includes('button--danger'))!.props.onClick())
  assert.deepEqual(historyGoCalls, [1, -1, -1], 'keeping the form cancels Forward without losing its data')
  assert.equal(renderer.root.findAllByProps({ role: 'alertdialog' }).length, 0)
  await act(async () => renderer.unmount())
})

async function guardModule() {
  if (Guard) return Guard
  server = await createServer({ root: process.cwd(), server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' })
  const module = await server.ssrLoadModule('/src/ui/UnsavedChangesGuard.tsx') as typeof import('./UnsavedChangesGuard.tsx')
  return module.UnsavedChangesGuard
}
