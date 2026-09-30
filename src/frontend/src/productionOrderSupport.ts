export function productionReportStandard(
  requiredQuantity: number,
  plannedQuantity: number,
  goodQuantity: number,
  rejectedQuantity: number,
) {
  if (plannedQuantity <= 0) return 0
  const output = Math.max(goodQuantity, 0) + Math.max(rejectedQuantity, 0)
  return Math.round((requiredQuantity * output / plannedQuantity) * 1_000_000) / 1_000_000
}

export function productionReportActual(
  explicitValue: string | undefined,
  standardQuantity: number,
) {
  return explicitValue == null || explicitValue.trim() === ''
    ? standardQuantity
    : Number(explicitValue)
}
