export function supplierLinkPayload(itemId: string, supplierId: string) {
  return {
    itemId,
    supplierId,
    minimumOrderQuantity: null,
    shippingModeId: null,
    incotermId: null,
    supplierLeadTimeDays: null,
    forwarderToShipmentDays: null,
    shippingPeriodDays: null,
    destinationCustomsDays: null,
  }
}

export function applicableItemsUrl(
  api: string,
  parentItemId: string,
  showAll: boolean,
  search: string,
  page: number,
  pageSize: number,
) {
  const query = new URLSearchParams({
    parentItemId,
    showAll: String(showAll),
    page: String(page),
    pageSize: String(pageSize),
  })
  if (search.trim()) query.set('search', search.trim())
  return `${api}/bom-revisions/applicable-items?${query}`
}
