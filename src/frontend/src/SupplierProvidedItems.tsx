import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Boxes, GitBranch } from 'lucide-react'
import { formatOperationalNumber } from './i18n/numberFormatting'
import { localizedName } from './i18n/localizedName'
import { structureItemRoute, structureTypeKey } from './productStructureSupport'
import { tehranTodayIso } from './cashFlowUi'
import { EmptyState, LoadingState, MessageStrip, Section, StatusBadge } from './ui'

type UnitRef = { id: string; code: string; nameFa: string; nameEn: string; symbol?: string | null; isActive: boolean }
type SuppliedItem = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; supplyMethod: string; isActive: boolean; baseUnit: UnitRef }
type SupplierItemGroup = { item: SuppliedItem }
type SupplierItemsPage = { items: SupplierItemGroup[]; totalItemCount: number }
type WhereUsedItem = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean; currentBaseUnit: UnitRef }
type UsageRelationship = { usageQty: number; childBaseUnitSnapshot: UnitRef }
type WhereUsedParent = { parentItem: WhereUsedItem; revision: { revisionCode: string }; relationship: UsageRelationship }
type WhereUsedResponse = { directParents: WhereUsedParent[] }
type UsageTreeNode = { item: SuppliedItem | WhereUsedItem; revisionCode?: string; relationship?: UsageRelationship; children: UsageTreeNode[] }
type UsageStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error'

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')
const pageSize = 100

export default function SupplierProvidedItems({ supplierId, canViewBom }: { supplierId: string; canViewBom: boolean }) {
  const { t } = useTranslation()
  const [items, setItems] = useState<SuppliedItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    async function load() {
      setLoading(true)
      setError('')
      try {
        const suppliedItems: SuppliedItem[] = []
        const evaluationDate = tehranTodayIso()
        let page = 1
        let pageCount = 1
        do {
          const query = new URLSearchParams({
            evaluationDate, supplierId, includeInactive: 'false',
            page: String(page), pageSize: String(pageSize),
          })
          const response = await fetch(`${api}/sourcing-options/tree?${query}`)
          if (!response.ok) throw new Error(await problem(response))
          const body = await response.json() as SupplierItemsPage
          suppliedItems.push(...body.items.map(group => group.item))
          pageCount = Math.max(1, Math.ceil(body.totalItemCount / pageSize))
          page++
        } while (page <= pageCount)
        if (active) setItems(suppliedItems)
      } catch (caught) {
        if (active) setError(caught instanceof Error ? caught.message : t('common.error'))
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [supplierId, t])

  const description = [t('suppliers.providedItemsHint'), ...(canViewBom ? [t('suppliers.providedItemsHoverHint')] : [])].join(' ')
  return <Section title={t('suppliers.providedItems')} description={description}>
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    {loading ? <LoadingState label={t('common.loading')} compact /> : items.length === 0
      ? <EmptyState title={t('suppliers.noProvidedItems')} compact />
      : <ul className="supplier-provided-items">{items.map(item => <SupplierProvidedItem key={item.id} item={item} canViewBom={canViewBom} />)}</ul>}
  </Section>
}

function SupplierProvidedItem({ item, canViewBom }: { item: SuppliedItem; canViewBom: boolean }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? i18n.language
  const [tree, setTree] = useState<UsageTreeNode | null>(null)
  const [status, setStatus] = useState<UsageStatus>('idle')
  const [open, setOpen] = useState(false)
  const [error, setError] = useState('')
  const popoverId = `supplier-usage-${item.id}`
  const statusRef = useRef<UsageStatus>('idle')
  const pointerOrFocusInside = useRef(false)
  const mounted = useRef(true)

  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])

  function showUsage() {
    if (!canViewBom || statusRef.current === 'empty') return
    pointerOrFocusInside.current = true
    setOpen(true)
    if (statusRef.current !== 'idle') return
    statusRef.current = 'loading'
    setStatus('loading')
    void loadUsageTree(item).then(value => {
      if (!mounted.current) return
      statusRef.current = value ? 'ready' : 'empty'
      setStatus(statusRef.current)
      setTree(value)
      if (!value) {
        pointerOrFocusInside.current = false
        setOpen(false)
      } else if (pointerOrFocusInside.current) setOpen(true)
    }).catch(caught => {
      if (!mounted.current) return
      statusRef.current = 'error'
      setStatus('error')
      setError(caught instanceof Error ? caught.message : t('common.error'))
      if (pointerOrFocusInside.current) setOpen(true)
    })
  }

  function hideUsage() {
    pointerOrFocusInside.current = false
    setOpen(false)
  }

  return <li className="supplier-provided-item" onMouseEnter={showUsage} onMouseLeave={event => {
    const focused = typeof document !== 'undefined' && event.currentTarget.contains(document.activeElement)
    if (!focused) hideUsage()
  }} onFocusCapture={showUsage} onBlurCapture={event => {
    const next = event.relatedTarget
    if (!(next instanceof Node) || !event.currentTarget.contains(next)) hideUsage()
  }}>
    <button type="button" className={`supplier-provided-item__trigger${canViewBom ? ' supplier-provided-item__trigger--interactive' : ''}`}
      aria-expanded={canViewBom ? open : undefined} aria-controls={canViewBom && open ? popoverId : undefined} onClick={showUsage}>
      <span className="supplier-provided-item__title"><strong>{localizedName(item, language)}</strong><bdi className="ui-technical">{item.code}</bdi></span>
      <span className="supplier-provided-item__metadata">{t(structureTypeKey(item.itemType))} · {item.baseUnit.symbol ?? item.baseUnit.code}</span>
      {!item.isActive && <StatusBadge text={t('common.inactive')} tone="warning" />}
    </button>
    {canViewBom && open && <div id={popoverId} className="supplier-item-usage-popover" role="region" aria-label={t('productStructure.whereUsed')}>
      <h3>{t('productStructure.whereUsed')}</h3>
      {status === 'loading' && <LoadingState label={t('common.loading')} compact />}
      {status === 'error' && <MessageStrip tone="critical">{error}</MessageStrip>}
      {status === 'ready' && tree && <ul className="supplier-usage-tree"><UsageTreeBranch node={tree} language={language} t={t} /></ul>}
    </div>}
  </li>
}

function UsageTreeBranch({ node, language, t }: { node: UsageTreeNode; language: string; t: ReturnType<typeof useTranslation>['t'] }) {
  const route = structureItemRoute(node.item.itemType, node.item.id)
  const name = localizedName(node.item, language)
  const unit = node.relationship?.childBaseUnitSnapshot
  return <li className="supplier-usage-tree__branch">
    <div className="supplier-usage-tree__node">
      <span className="supplier-usage-tree__icon" aria-hidden="true">{node.children.length ? <GitBranch /> : <Boxes />}</span>
      <div className="supplier-usage-tree__identity">
        <strong>{route ? <Link to={route}>{name}</Link> : name}</strong>
        <span><bdi className="ui-technical">{node.item.code}</bdi> · {t(structureTypeKey(node.item.itemType))}</span>
        {node.revisionCode && <small><bdi>{node.revisionCode}</bdi></small>}
        {node.relationship && <small>{t('productStructure.usage')}: {formatOperationalNumber(node.relationship.usageQty, language)} {unit?.symbol ?? unit?.code}</small>}
      </div>
    </div>
    {node.children.length > 0 && <ul className="supplier-usage-tree__children">{node.children.map((child, index) =>
      <UsageTreeBranch key={`${node.item.id}-${child.item.id}-${index}`} node={child} language={language} t={t} />)}</ul>}
  </li>
}

async function loadUsageTree(root: SuppliedItem): Promise<UsageTreeNode | null> {
  const cache = new Map<string, Promise<WhereUsedResponse>>()
  const whereUsed = (itemId: string) => {
    const existing = cache.get(itemId)
    if (existing) return existing
    const query = new URLSearchParams({ itemId })
    const request = fetch(`${api}/bom-revisions/where-used?${query}`).then(async response => {
      if (!response.ok) throw new Error(await problem(response))
      return await response.json() as WhereUsedResponse
    })
    cache.set(itemId, request)
    return request
  }

  const rootResponse = await whereUsed(root.id)
  if (!rootResponse.directParents.length) return null

  async function expand(item: SuppliedItem | WhereUsedItem, via: WhereUsedParent | undefined, ancestors: Set<string>): Promise<UsageTreeNode> {
    const path = new Set(ancestors)
    path.add(item.id)
    const parentRows = item.itemType === 'PRODUCT' ? [] : (await whereUsed(item.id)).directParents
    const children = await Promise.all(parentRows.map(parent => path.has(parent.parentItem.id)
      ? Promise.resolve({ item: parent.parentItem, revisionCode: parent.revision.revisionCode, relationship: parent.relationship, children: [] })
      : expand(parent.parentItem, parent, path)))
    return { item, revisionCode: via?.revision.revisionCode, relationship: via?.relationship, children }
  }

  return expand(root, undefined, new Set())
}

async function problem(response: Response) {
  const body = await response.json().catch(() => null) as { detail?: string; code?: string } | null
  return [body?.code, body?.detail].filter(Boolean).join(' — ') || `HTTP ${response.status}`
}
