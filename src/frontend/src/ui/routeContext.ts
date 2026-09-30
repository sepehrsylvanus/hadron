import { useCallback } from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'

export function useListQuery(defaultPageSize = 25) {
  const [params, setParams] = useSearchParams()
  const page = positive(params.get('page'), 1)
  const pageSize = positive(params.get('pageSize'), defaultPageSize)
  const value = (key: string) => params.get(key) ?? ''
  const flag = (key: string) => params.get(key) === 'true'
  const update = useCallback((changes: Record<string, string | number | boolean | null | undefined>, resetPage = false) => {
    setParams(current => {
      const next = new URLSearchParams(current)
      for (const [key, raw] of Object.entries(changes)) {
        if (raw == null || raw === '' || raw === false) next.delete(key)
        else next.set(key, String(raw))
      }
      if (resetPage) next.delete('page')
      return next
    })
  }, [setParams])
  return { params, page, pageSize, value, flag, update }
}

export function useListReturn(defaultPath: string, additionalPaths: string[] = []) {
  const location = useLocation()
  const from = new URLSearchParams(location.search).get('from')
  if (!from) return defaultPath
  try {
    const decoded = decodeURIComponent(from)
    const allowed = [defaultPath, ...additionalPaths]
    return allowed.some(path => decoded === path || decoded.startsWith(`${path}?`)) ? decoded : defaultPath
  } catch { return defaultPath }
}

export function currentListContext(pathname: string, search: string) {
  return encodeURIComponent(`${pathname}${search}`)
}

function positive(value: string | null, fallback: number) {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback
}
