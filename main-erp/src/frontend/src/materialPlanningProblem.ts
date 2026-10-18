export type MaterialPlanningProblem = {
  title?: unknown
  detail?: unknown
  code?: unknown
  traceId?: unknown
}

export class MaterialPlanningApiError extends Error {
  readonly status: number
  readonly endpoint: string
  readonly traceId?: string
  readonly code?: string

  constructor(
    status: number,
    endpoint: string,
    traceId?: string,
    code?: string,
  ) {
    super(`HTTP ${status}`)
    this.status = status
    this.endpoint = endpoint
    this.traceId = traceId
    this.code = code
  }
}

export async function createMaterialPlanningApiError(response: Response, endpoint: string) {
  const body = await response.clone().json().catch(() => null) as MaterialPlanningProblem | null
  return new MaterialPlanningApiError(
    response.status,
    endpoint,
    typeof body?.traceId === 'string' ? body.traceId : undefined,
    typeof body?.code === 'string' ? body.code : undefined,
  )
}

export function formatMaterialPlanningProblem(
  error: unknown,
  translate: (key: string, options?: Record<string, unknown>) => string,
) {
  if (error instanceof MaterialPlanningApiError) {
    if (error.status === 401) return translate('materialPlanning.errors.authentication')
    if (error.status === 403) return translate('materialPlanning.errors.permission')
    if (error.status >= 500) return translate('materialPlanning.errors.server', {
      endpoint: translate(`materialPlanning.errors.endpoints.${endpointName(error.endpoint)}`),
      traceId: error.traceId ?? '—',
    })
    return translate('materialPlanning.errors.request', {
      endpoint: translate(`materialPlanning.errors.endpoints.${endpointName(error.endpoint)}`),
      status: error.status,
    })
  }
  if (error instanceof TypeError) return translate('materialPlanning.errors.network')
  return translate('common.error')
}

function endpointName(endpoint: string) {
  const path = endpoint.split('?')[0]
  if (path.endsWith('/products')) return 'products'
  if (path.includes('/products/') && path.endsWith('/materials')) return 'productMaterials'
  if (path.endsWith('/summary')) return 'summary'
  if (path.endsWith('/exceptions')) return 'exceptions'
  if (path.endsWith('/facets')) return 'facets'
  if (path.endsWith('/timeline')) return 'timeline'
  if (path.endsWith('/trace')) return 'trace'
  if (path.endsWith('/refresh-status')) return 'refresh'
  if (path.endsWith('/runs')) return 'runs'
  return 'planning'
}
