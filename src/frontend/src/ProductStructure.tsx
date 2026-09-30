import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Boxes, GitBranch } from 'lucide-react'
import { EmptyState, LoadingState, MessageStrip, Section, StatusBadge } from './ui'
import { localizedStructureName, structureItemRoute, structureTypeKey } from './productStructureSupport'

type UnitRef = { id: string; code: string; nameFa: string; nameEn: string; symbol?: string | null; isActive: boolean }
type ItemRef = { id: string; code: string; nameFa: string; nameEn?: string | null; itemType: string; isActive: boolean; supplyMethod?: string | null; netWeightKg?: number | null; currentBaseUnit: UnitRef }
type RevisionRef = { id: string; revisionCode: string; baseOutputQty: number }
type Relationship = { usageQty: number; scrapOverridePercent?: number | null; childBaseUnitSnapshot: UnitRef }
type TreeNode = { item: ItemRef; revision?: RevisionRef | null; relationshipFromParent?: Relationship | null; children: TreeNode[] }
type TreeResponse = { root: TreeNode }
type WhereUsedParent = { parentItem: ItemRef; revision: RevisionRef; relationship: Relationship }
type WhereUsedResponse = { item: ItemRef; directParents: WhereUsedParent[]; finalProducts: ItemRef[] }

const api = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '')

export default function ProductStructure({ itemId, itemType }: { itemId: string; itemType: string }) {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? 'fa-IR'
  const [tree, setTree] = useState<TreeResponse | null>(null)
  const [whereUsed, setWhereUsed] = useState<WhereUsedResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const requests: Promise<Response>[] = [fetch(`${api}/bom-revisions/product-tree?rootItemId=${itemId}`)]
    if (itemType !== 'PRODUCT') requests.push(fetch(`${api}/bom-revisions/where-used?itemId=${itemId}`))
    void Promise.all(requests).then(async responses => {
      const failed = responses.find(response => !response.ok)
      if (failed) throw new Error(await problem(failed))
      const values = await Promise.all(responses.map(response => response.json()))
      if (!active) return
      setTree(values[0] as TreeResponse)
      setWhereUsed(itemType === 'PRODUCT' ? null : values[1] as WhereUsedResponse)
    }).catch(caught => {
      if (active) setError(caught instanceof Error ? caught.message : t('common.error'))
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [itemId, itemType, t])

  if (loading) return <Section title={t('productStructure.title')}><LoadingState label={t('common.loading')} compact /></Section>
  if (error) return <Section title={t('productStructure.title')}><MessageStrip tone="critical">{error}</MessageStrip></Section>

  return <>
    <Section title={t('productStructure.title')} description={tree ? t('productStructure.currentStructureHint') : undefined}>
      {tree?.root.revision
        ? <div className="product-tree" role="tree"><TreeBranch node={tree.root} depth={0} language={language} t={t} /></div>
        : <EmptyState title={t('productStructure.noApprovedBom')} description={t('productStructure.noApprovedBomHint')} compact />}
    </Section>
    {itemType !== 'PRODUCT' && <Section title={t('productStructure.whereUsed')} description={t('productStructure.whereUsedHint')}>
      {!whereUsed?.directParents.length && !whereUsed?.finalProducts.length
        ? <EmptyState title={t('productStructure.notUsed')} compact />
        : <div className="where-used-grid">
          <WhereUsedList title={t('productStructure.directParents')} items={whereUsed?.directParents.map(x => x.parentItem) ?? []} language={language} t={t} />
          <WhereUsedList title={t('productStructure.finalProducts')} items={whereUsed?.finalProducts ?? []} language={language} t={t} />
        </div>}
    </Section>}
  </>
}

function TreeBranch({ node, depth, language, t }: { node: TreeNode; depth: number; language: string; t: ReturnType<typeof useTranslation>['t'] }) {
  const route = structureItemRoute(node.item.itemType, node.item.id)
  const name = localizedStructureName(node.item, language)
  const unitName = localizedStructureName(node.item.currentBaseUnit, language)
  return <div className="product-tree__branch" role="treeitem" aria-level={depth + 1}>
    <div className="product-tree__node">
      <span className="product-tree__icon" aria-hidden="true">{node.children.length ? <GitBranch /> : <Boxes />}</span>
      <div className="product-tree__identity">
        <div>{route ? <Link to={route}>{name}</Link> : <strong>{name}</strong>} <bdi className="ui-technical">{node.item.code}</bdi></div>
        <small>{t(structureTypeKey(node.item.itemType))} · {unitName} ({node.item.currentBaseUnit.code}){node.item.supplyMethod ? ` · ${t(`materials.${node.item.supplyMethod}`)}` : ''}</small>
      </div>
      {!node.item.isActive && <StatusBadge text={t('common.inactive')} tone="warning" />}
      {node.relationshipFromParent && <div className="product-tree__quantity"><span>{t('productStructure.usage')}</span><bdi>{formatNumber(node.relationshipFromParent.usageQty)} {node.relationshipFromParent.childBaseUnitSnapshot.symbol || node.relationshipFromParent.childBaseUnitSnapshot.code}</bdi></div>}
      {node.item.netWeightKg != null && <div className="product-tree__weight"><span>{t('productStructure.unitWeight')}</span><bdi>{formatNumber(node.item.netWeightKg)} kg</bdi></div>}
    </div>
    {node.revision && <div className="product-tree__revision"><bdi>{node.revision.revisionCode}</bdi><span>{t('productStructure.baseOutput')}: {formatNumber(node.revision.baseOutputQty)} {node.item.currentBaseUnit.symbol || node.item.currentBaseUnit.code}</span></div>}
    {node.children.length > 0 && <div className="product-tree__children" role="group">{node.children.map(child => <TreeBranch key={`${node.item.id}-${child.item.id}`} node={child} depth={depth + 1} language={language} t={t} />)}</div>}
  </div>
}

function WhereUsedList({ title, items, language, t }: { title: string; items: ItemRef[]; language: string; t: ReturnType<typeof useTranslation>['t'] }) {
  return <div className="where-used-list"><h3>{title}</h3>{items.length === 0 ? <p className="ui-muted">{t('common.noData')}</p> : <ul>{items.map(item => {
    const route = structureItemRoute(item.itemType, item.id)
    return <li key={item.id}>{route ? <Link to={route}>{localizedStructureName(item, language)}</Link> : localizedStructureName(item, language)}<bdi className="ui-technical">{item.code}</bdi>{!item.isActive && <StatusBadge text={t('common.inactive')} tone="warning" />}</li>
  })}</ul>}</div>
}

function formatNumber(value: number) { return new Intl.NumberFormat('en-US', { maximumFractionDigits: 6 }).format(value) }
async function problem(response: Response) { const body = await response.json().catch(() => null) as { detail?: string; code?: string } | null; return [body?.code, body?.detail].filter(Boolean).join(' — ') || `HTTP ${response.status}` }
