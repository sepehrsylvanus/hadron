export const analyticsDisplayName = (fa: string, en: string | undefined, language: string) =>
  language === 'en' && en?.trim() ? en : fa

type AnalyticsExportContext = {
  report: string
  evaluationDate: string
  language: string
  maximumRows: number
  search?: string
  dataCutoffUtc?: string
  runId?: string
  generationId?: string
  fromDate?: string
  toDate?: string
}

export function analyticsExportUrl(context: AnalyticsExportContext) {
  const parameters = new URLSearchParams({
    evaluationDate: context.evaluationDate,
    language: context.language,
    maximumRows: String(context.maximumRows),
  })
  for (const [key, value] of Object.entries({
    search: context.search,
    dataCutoffUtc: context.dataCutoffUtc,
    runId: context.runId,
    generationId: context.generationId,
    fromDate: context.fromDate,
    toDate: context.toDate,
  })) {
    if (value) parameters.set(key, value)
  }
  return `/api/v1/analytics/exports/${context.report}/xlsx?${parameters}`
}
