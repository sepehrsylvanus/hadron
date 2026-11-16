export const supplyMethods = ['BUY', 'MAKE', 'OUTSOURCE', 'HYBRID'] as const
export type SupplyMethod = (typeof supplyMethods)[number]

export function requiresBaseUnitChangeConfirmation(currentUnitId: string | null | undefined,
  requestedUnitId: string | null | undefined) {
  return Boolean(currentUnitId && requestedUnitId && currentUnitId !== requestedUnitId)
}
