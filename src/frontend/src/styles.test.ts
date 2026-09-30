import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const stylesheet = readFileSync(new URL('./styles.css', import.meta.url), 'utf8')
const collaborationCenter = readFileSync(new URL('./CollaborationNotificationCenter.tsx', import.meta.url), 'utf8')

test('every CSS custom-property reference resolves or declares a fallback', () => {
  const definitions = new Set(Array.from(stylesheet.matchAll(/--([a-z0-9-]+)\s*:/gi), match => match[1]))
  const unresolved = Array.from(stylesheet.matchAll(/var\(\s*--([a-z0-9-]+)(\s*,)?/gi))
    .filter(match => !definitions.has(match[1]) && !match[2])
    .map(match => `--${match[1]}`)

  assert.deepEqual([...new Set(unresolved)].sort(), [])
})

test('compatibility aliases reuse approved canonical design tokens', () => {
  assert.match(stylesheet, /--surface-muted:\s*var\(--surface-page\)/)
  assert.match(stylesheet, /--surface-subtle:\s*var\(--surface-page\)/)
  assert.match(stylesheet, /--text-muted:\s*var\(--text-secondary\)/)
  assert.match(stylesheet, /--semantic-negative:\s*var\(--brand-red\)/)
})

test('the idle collaboration launcher has explicit disclosure and dialog accessibility semantics', () => {
  assert.match(collaborationCenter, /className="collaboration-dock__launcher"/)
  assert.match(collaborationCenter, /aria-expanded=\{dockOpen\}/)
  assert.match(collaborationCenter, /aria-controls="collaboration-global-dock"/)
  assert.match(collaborationCenter, /id="collaboration-global-dock"[\s\S]*role="dialog" aria-modal="false"/)
  assert.match(collaborationCenter, /className="collaboration-dock__actions" aria-label=/)
  assert.match(collaborationCenter, /aria-label=\{t\('collaborationHub\.notificationCenter\.dock\.close'\)\}/)
  assert.match(collaborationCenter, /role="group" aria-label=\{t\('collaborationHub\.notificationCenter\.filters'\)\}/)
  assert.match(collaborationCenter, /aria-pressed=\{filter === tab\.id\}/)
})

test('the collaboration launcher is desktop-only while the top-shell control remains available on narrow screens', () => {
  assert.match(stylesheet, /\.collaboration-dock__launcher \{[^}]*min-height:\s*48px;/)
  assert.match(stylesheet, /\.collaboration-dock__header-actions button \{[^}]*width:\s*44px;[^}]*height:\s*44px;/)
  assert.match(stylesheet, /@media \(max-width:\s*760px\)[\s\S]*\.collaboration-dock \{[^}]*display:\s*none;/)
  assert.doesNotMatch(stylesheet, /@media \(max-width:\s*760px\)[\s\S]*\.shell-communication \{[^}]*display:\s*none;/)
})
