export function requiresBaseUnitChangeConfirmation(currentUnitId: string | null | undefined,
  requestedUnitId: string | null | undefined) {
  return Boolean(currentUnitId && requestedUnitId && currentUnitId !== requestedUnitId)
}
