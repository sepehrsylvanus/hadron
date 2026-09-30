export function buildTransactionListQuery(transactionType: string, page: number, pageSize: number) {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (transactionType.trim()) query.set('transactionType', transactionType.trim())
  return query
}
