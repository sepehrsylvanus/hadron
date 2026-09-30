export const sourcingReadinessReasonKeys: Record<string, string> = {
  ITEM_INACTIVE: 'sourcing.reasonItemInactive',
  SUPPLIER_INACTIVE: 'sourcing.reasonSupplierInactive',
  SUPPLY_METHOD_INCOMPATIBLE: 'sourcing.reasonSupplyMethodIncompatible',
  BASE_UNIT_CHANGED: 'sourcing.reasonBaseUnitChanged',
  SHIPPING_MODE_INACTIVE: 'sourcing.reasonShippingModeInactive',
  INCOTERM_INACTIVE: 'sourcing.reasonIncotermInactive',
  MOQ_MISSING: 'sourcing.reasonMoqMissing',
  SHIPPING_MODE_MISSING: 'sourcing.reasonShippingModeMissing',
  SUPPLIER_LEAD_TIME_MISSING: 'sourcing.reasonSupplierLeadMissing',
  FORWARDER_LEAD_TIME_MISSING: 'sourcing.reasonForwarderLeadMissing',
  SHIPPING_PERIOD_MISSING: 'sourcing.reasonShippingPeriodMissing',
  DESTINATION_CUSTOMS_MISSING: 'sourcing.reasonDestinationLeadMissing',
}

export function readinessReasonKey(code: string) {
  return sourcingReadinessReasonKeys[code] ?? 'sourcing.reasonUnknown'
}

export function toggleExpandedItem(current: ReadonlySet<string>, itemId: string) {
  const next = new Set(current)
  if (next.has(itemId)) next.delete(itemId)
  else next.add(itemId)
  return next
}

export function selectedOptionAfterReload<T extends { id: string }>(currentId: string | undefined,
  groups: ReadonlyArray<{ options: ReadonlyArray<T> }>) {
  const options = groups.flatMap(group => group.options)
  return options.find(option => option.id === currentId) ?? options[0] ?? null
}
