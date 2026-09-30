export type ReferenceDataType = { id: string; code: string; nameFa: string; nameEn: string }

export function usesGeneratedReferenceCode(typeCode: string) {
  return typeCode !== 'CURRENCY' && typeCode !== 'UNIT'
}

type RetryDelay = (signal: AbortSignal) => Promise<void>

const defaultRetryDelay: RetryDelay = signal => new Promise(resolve => {
  const timer = globalThis.setTimeout(resolve, 1_000)
  signal.addEventListener('abort', () => {
    globalThis.clearTimeout(timer)
    resolve()
  }, { once: true })
})

export async function fetchReferenceTypesUntilAvailable(
  url: string,
  signal: AbortSignal,
  onFailure: (message: string) => void,
  fetcher: typeof fetch = fetch,
  retryDelay: RetryDelay = defaultRetryDelay,
): Promise<ReferenceDataType[]> {
  while (!signal.aborted) {
    try {
      const response = await fetcher(url, { signal })
      if (!response.ok) throw new Error(await referenceDataProblem(response))
      return await response.json() as ReferenceDataType[]
    } catch (caught) {
      if (signal.aborted) break
      onFailure(caught instanceof Error ? caught.message : 'Failed to fetch')
      await retryDelay(signal)
    }
  }

  throw new DOMException('Reference Data request was cancelled.', 'AbortError')
}

export async function referenceDataProblem(response: Response) {
  const body = await response.json().catch(() => null) as {
    detail?: string
    code?: string
    errors?: Record<string, string[]>
  } | null
  return [body?.code, body?.detail, ...Object.values(body?.errors ?? {}).flat()]
    .filter(Boolean).join(' — ') || `HTTP ${response.status}`
}
