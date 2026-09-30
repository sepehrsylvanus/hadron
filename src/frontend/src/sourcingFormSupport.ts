export type SourcingEditor = {
  itemId: string
  supplierId: string
  routeScope: 'DOMESTIC' | 'IMPORT'
  minimumOrderQuantity: string
  shippingModeId: string
  incotermId: string
  supplierLeadTimeDays: string
  forwarderToShipmentDays: string
  shippingPeriodDays: string
  destinationCustomsDays: string
  version?: number
}

export type SourcingLeadTimeField =
  | 'supplierLeadTimeDays'
  | 'forwarderToShipmentDays'
  | 'shippingPeriodDays'
  | 'destinationCustomsDays'

export function sourcingLeadTimeFields(
  routeScope: SourcingEditor['routeScope'],
): readonly SourcingLeadTimeField[] {
  return routeScope === 'DOMESTIC'
    ? ['supplierLeadTimeDays', 'shippingPeriodDays']
    : ['supplierLeadTimeDays', 'forwarderToShipmentDays', 'shippingPeriodDays', 'destinationCustomsDays']
}

export function updateSourcingEditor(current: SourcingEditor, patch: Partial<SourcingEditor>): SourcingEditor {
  return { ...current, ...patch }
}

export function changeSourcingRouteScope(
  current: SourcingEditor,
  routeScope: SourcingEditor['routeScope'],
): SourcingEditor {
  return routeScope === 'DOMESTIC'
    ? { ...current, routeScope, incotermId: '', forwarderToShipmentDays: '', destinationCustomsDays: '' }
    : { ...current, routeScope }
}

export function isExternalSourcingEligible(supplyMethod: string): boolean {
  return supplyMethod === 'BUY' || supplyMethod === 'OUTSOURCE' || supplyMethod === 'HYBRID'
}
