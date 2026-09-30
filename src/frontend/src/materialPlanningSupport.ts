export const materialPlanningRunPreferenceKey = 'hadron.erp.mrp.material-planning.run-id'

export type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

export function readRememberedRunId(storage: StorageLike | null | undefined) {
  const value = storage?.getItem(materialPlanningRunPreferenceKey)?.trim()
  return value || null
}

export function rememberRunId(storage: StorageLike | null | undefined, runId: string) {
  if (!storage) return
  if (runId.trim()) storage.setItem(materialPlanningRunPreferenceKey, runId.trim())
  else storage.removeItem(materialPlanningRunPreferenceKey)
}

export function chooseInitialRunId(urlRunId: string, rememberedRunId: string | null, latestRunId?: string) {
  if (urlRunId.trim()) return { runId: urlRunId.trim(), source: 'URL' as const }
  if (rememberedRunId) return { runId: rememberedRunId, source: 'REMEMBERED' as const }
  return { runId: latestRunId ?? '', source: latestRunId ? 'LATEST' as const : 'NONE' as const }
}

export function sourceNavigation(sourceModule: string, sourceReference?: string | null) {
  const reference = sourceReference?.trim()
  if (!reference) return null
  const query = encodeURIComponent(reference)
  if (sourceModule === 'BOM') return `/mrp/engineering/bom?search=${query}`
  if (sourceModule === 'PURCHASE_SUPPLY') return `/mrp/supply/purchase-orders?search=${query}`
  if (sourceModule === 'INVENTORY') return `/inventory/warehouses?search=${query}`
  return null
}

export const materialPlanningRequiredSummaryFields = [
  'officialItemCode', 'itemName', 'itemType', 'category', 'supplyMethod', 'unit',
  'startingOnHand', 'totalFirmIncoming', 'totalNetRequirement', 'stockoutDate',
  'safetyBreachDate', 'stockCoverage', 'firmCoverage', 'overdueFirmQty',
  'missingEtaFirmQty', 'riskBand', 'planningCompleteness', 'exceptionCount',
] as const

export const materialPlanningRequiredRunHeaderFields = [
  'runNumber', 'asOfDate', 'horizonEndDate', 'engineVersion', 'runStatus',
  'planningCompleteness', 'completedAtUtc', 'latestHistoricalIndicator',
] as const

export const materialPlanningRequiredSupplyTraceFields = [
  'purchaseOrderNumber', 'purchaseOrderLineId', 'deliveryScheduleId', 'supplierSnapshot',
  'openQty', 'eta', 'etaStatus', 'appliedState',
] as const
