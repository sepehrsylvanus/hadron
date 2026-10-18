import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { ConfirmationDialog } from './index'

/**
 * Protects dirty object forms from the navigation paths users most often take:
 * shell, sidebar, breadcrumb and in-page links, plus tab close/refresh.
 *
 * React Router's data-router blocker is intentionally not used because Hadron
 * currently runs on BrowserRouter. Programmatic navigation remains owned by the
 * form (save/cancel), so a successful save is never blocked by stale state.
 */
export function UnsavedChangesGuard({ when }: { when: boolean }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [pendingPath, setPendingPath] = useState<string | null>(null)
  const [pendingHistoryDelta, setPendingHistoryDelta] = useState<number | null>(null)
  const currentHistoryIndex = useRef<number | null>(null)
  const restoringHistory = useRef(false)
  const proceedingThroughHistory = useRef(false)

  useEffect(() => {
    if (!when) return

    const browserHistory = window.history
    currentHistoryIndex.current = historyIndex(browserHistory?.state)

    const beforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }
    const interceptLink = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const target = event.target as Element | null
      const anchor = typeof target?.closest === 'function' ? target.closest<HTMLAnchorElement>('a[href]') : null
      if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) return

      const destination = new URL(anchor.href, window.location.href)
      if (destination.origin !== window.location.origin || !['http:', 'https:'].includes(destination.protocol)) return
      if (destination.pathname === window.location.pathname && destination.search === window.location.search) return

      event.preventDefault()
      setPendingPath(`${destination.pathname}${destination.search}${destination.hash}`)
    }
    const interceptHistoryNavigation = (event: PopStateEvent) => {
      if (!browserHistory || typeof browserHistory.go !== 'function') return
      const nextIndex = historyIndex(event.state)
      if (proceedingThroughHistory.current) {
        proceedingThroughHistory.current = false
        currentHistoryIndex.current = nextIndex
        return
      }
      if (restoringHistory.current) {
        restoringHistory.current = false
        currentHistoryIndex.current = nextIndex
        return
      }

      const currentIndex = currentHistoryIndex.current
      if (currentIndex === null || nextIndex === null || currentIndex === nextIndex) {
        currentHistoryIndex.current = nextIndex
        return
      }

      // A browser POP has already moved the history cursor. Stop BrowserRouter
      // from observing that temporary move, restore the dirty form entry, then
      // let the user decide in the same accessible dialog used for link exits.
      const delta = nextIndex - currentIndex
      event.preventDefault()
      event.stopImmediatePropagation()
      restoringHistory.current = true
      setPendingHistoryDelta(delta)
      browserHistory.go(-delta)
    }

    window.addEventListener('beforeunload', beforeUnload)
    if (browserHistory) window.addEventListener('popstate', interceptHistoryNavigation, true)
    document.addEventListener('click', interceptLink, true)
    return () => {
      window.removeEventListener('beforeunload', beforeUnload)
      if (browserHistory) window.removeEventListener('popstate', interceptHistoryNavigation, true)
      document.removeEventListener('click', interceptLink, true)
    }
  }, [when])

  function discardAndContinue() {
    const destination = pendingPath
    const historyDelta = pendingHistoryDelta
    setPendingPath(null)
    setPendingHistoryDelta(null)
    if (destination) navigate(destination)
    else if (historyDelta !== null) {
      proceedingThroughHistory.current = true
      window.history.go(historyDelta)
    }
  }

  return <ConfirmationDialog
    open={when && (pendingPath !== null || pendingHistoryDelta !== null)}
    title={t('common.discardTitle')}
    description={t('common.discardDescription')}
    confirmLabel={t('common.discard')}
    cancelLabel={t('common.cancel')}
    onConfirm={discardAndContinue}
    onCancel={() => { setPendingPath(null); setPendingHistoryDelta(null) }}
  />
}

function historyIndex(state: unknown): number | null {
  if (!state || typeof state !== 'object' || !('idx' in state)) return null
  const value = (state as { idx?: unknown }).idx
  return typeof value === 'number' && Number.isInteger(value) ? value : null
}
