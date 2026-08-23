import {
  AlertCircle,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  CircleDashed,
  Info,
  SearchX,
  TriangleAlert,
} from 'lucide-react'
import {
  FormEvent,
  ReactNode,
  useEffect,
  useId,
  useRef,
} from 'react'
import { Link } from 'react-router-dom'

export type StatusTone = 'positive' | 'critical' | 'warning' | 'information' | 'neutral'

export function PageHeader({
  title,
  subtitle,
  eyebrow,
  status,
  actions,
  backTo,
  backLabel,
}: {
  title: string
  subtitle?: string
  eyebrow?: string
  status?: ReactNode
  actions?: ReactNode
  backTo?: string
  backLabel?: string
}) {
  return <header className="ui-page-header">
    <div className="ui-page-header__identity">
      {backTo && <Link className="ui-back-link" to={backTo}>{backLabel}</Link>}
      {eyebrow && <p className="ui-eyebrow">{eyebrow}</p>}
      <div className="ui-page-header__title"><h1>{title}</h1>{status}</div>
      {subtitle && <p>{subtitle}</p>}
    </div>
    {actions && <div className="ui-page-header__actions">{actions}</div>}
  </header>
}

export function FilterBar({ children, label }: { children: ReactNode; label: string }) {
  return <section className="ui-filter-bar" aria-label={label}>{children}</section>
}

export function TableToolbar({
  title,
  description,
  count,
  actions,
}: {
  title: string
  description?: string
  count?: ReactNode
  actions?: ReactNode
}) {
  return <div className="ui-table-toolbar">
    <div><h2>{title}</h2>{description && <p>{description}</p>}</div>
    <div className="ui-table-toolbar__actions">{count != null && <span className="ui-count">{count}</span>}{actions}</div>
  </div>
}

export function DataTable({
  headers,
  children,
  loading,
  empty,
  filtered,
  loadingLabel,
  emptyTitle,
  emptyDescription,
  noResultTitle,
  noResultDescription,
}: {
  headers: ReactNode[]
  children: ReactNode
  loading?: boolean
  empty?: boolean
  filtered?: boolean
  loadingLabel: string
  emptyTitle: string
  emptyDescription?: string
  noResultTitle: string
  noResultDescription?: string
}) {
  return <div className="ui-table-wrap">
    <table className="ui-data-table">
      <thead><tr>{headers.map((header, index) => <th key={index}>{header}</th>)}</tr></thead>
      <tbody>
        {loading
          ? <tr><td colSpan={headers.length}><LoadingState label={loadingLabel} compact /></td></tr>
          : empty
            ? <tr><td colSpan={headers.length}><EmptyState
              title={filtered ? noResultTitle : emptyTitle}
              description={filtered ? noResultDescription : emptyDescription}
              filtered={filtered}
              compact
            /></td></tr>
            : children}
      </tbody>
    </table>
  </div>
}

export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
  previousLabel,
  nextLabel,
  summary,
}: {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  previousLabel: string
  nextLabel: string
  summary: string
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize))
  return <nav className="ui-pagination" aria-label={summary}>
    <button type="button" className="button button--quiet" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
      <ChevronRight aria-hidden="true" />{previousLabel}
    </button>
    <span>{summary}</span>
    <button type="button" className="button button--quiet" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>
      {nextLabel}<ChevronLeft aria-hidden="true" />
    </button>
  </nav>
}

export function StatusBadge({ text, tone = 'neutral' }: { text: string; tone?: StatusTone }) {
  const Icon = tone === 'positive' ? CheckCircle2 : tone === 'critical' ? AlertCircle : tone === 'warning' ? TriangleAlert : tone === 'information' ? Info : CircleDashed
  return <span className={`ui-status ui-status--${tone}`}><Icon aria-hidden="true" />{text}</span>
}

export function FormField({
  label,
  children,
  hint,
  error,
  required,
  className = '',
}: {
  label: string
  children: ReactNode
  hint?: string
  error?: string
  required?: boolean
  className?: string
}) {
  const hintId = useId()
  return <label className={`ui-form-field ${className}`}>
    <span>{label}{required && <span className="ui-required" aria-hidden="true">*</span>}</span>
    {children}
    {error ? <small id={hintId} className="ui-field-error">{error}</small> : hint && <small id={hintId}>{hint}</small>}
  </label>
}

export function Section({
  title,
  description,
  actions,
  children,
}: {
  title: string
  description?: string
  actions?: ReactNode
  children: ReactNode
}) {
  return <section className="ui-section">
    <div className="ui-section__header"><div><h2>{title}</h2>{description && <p>{description}</p>}</div>{actions}</div>
    {children}
  </section>
}

export function MessageStrip({ tone = 'information', children }: { tone?: StatusTone; children: ReactNode }) {
  const Icon = tone === 'positive' ? CheckCircle2 : tone === 'critical' ? AlertCircle : tone === 'warning' ? TriangleAlert : Info
  return <div className={`ui-message ui-message--${tone}`} role={tone === 'critical' ? 'alert' : 'status'}><Icon aria-hidden="true" /><div>{children}</div></div>
}

export function LoadingState({ label, compact = false }: { label: string; compact?: boolean }) {
  return <div className={`ui-loading${compact ? ' ui-state--compact' : ''}`} role="status"><span className="ui-spinner" aria-hidden="true" />{label}</div>
}

export function EmptyState({
  title,
  description,
  action,
  filtered = false,
  compact = false,
}: {
  title: string
  description?: string
  action?: ReactNode
  filtered?: boolean
  compact?: boolean
}) {
  const Icon = filtered ? SearchX : CircleDashed
  return <div className={`ui-empty${compact ? ' ui-state--compact' : ''}`}><Icon aria-hidden="true" /><strong>{title}</strong>{description && <p>{description}</p>}{action}</div>
}

export function ConfirmationDialog({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  tone = 'critical',
  children,
  onConfirm,
  onCancel,
}: {
  open: boolean
  title: string
  description?: string
  confirmLabel: string
  cancelLabel: string
  tone?: 'critical' | 'normal'
  children?: ReactNode
  onConfirm: () => void
  onCancel: () => void
}) {
  const titleId = useId()
  const confirmRef = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    if (!open) return
    const previous = document.activeElement as HTMLElement | null
    confirmRef.current?.focus()
    const key = (event: KeyboardEvent) => { if (event.key === 'Escape') onCancel() }
    document.addEventListener('keydown', key)
    return () => { document.removeEventListener('keydown', key); previous?.focus() }
  }, [open, onCancel])
  if (!open) return null
  return <div className="ui-dialog-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onCancel() }}>
    <section className="ui-confirmation" role="alertdialog" aria-modal="true" aria-labelledby={titleId}>
      <h2 id={titleId}>{title}</h2>{description && <p>{description}</p>}{children}
      <div className="ui-dialog-actions">
        <button ref={confirmRef} type="button" className={`button ${tone === 'critical' ? 'button--danger' : 'button--primary'}`} onClick={onConfirm}>{confirmLabel}</button>
        <button type="button" className="button" onClick={onCancel}>{cancelLabel}</button>
      </div>
    </section>
  </div>
}

export function DisplayEditActions({
  editing,
  dirty,
  editLabel,
  saveLabel,
  cancelLabel,
  onEdit,
  onCancel,
  formId,
  saving,
}: {
  editing: boolean
  dirty?: boolean
  editLabel: string
  saveLabel: string
  cancelLabel: string
  onEdit: () => void
  onCancel: () => void
  formId?: string
  saving?: boolean
}) {
  return editing
    ? <><button className="button button--primary" type="submit" form={formId} disabled={saving}>{saveLabel}</button><button className="button" type="button" onClick={onCancel}>{cancelLabel}{dirty ? ' •' : ''}</button></>
    : <button className="button button--primary" type="button" onClick={onEdit}>{editLabel}</button>
}

export function ObjectForm({ id, onSubmit, children }: { id: string; onSubmit: (event: FormEvent) => void; children: ReactNode }) {
  return <form id={id} className="ui-object-form" onSubmit={onSubmit}>{children}</form>
}

export function DefinitionGrid({ children }: { children: ReactNode }) {
  return <dl className="ui-definition-grid">{children}</dl>
}

export function Definition({ label, children, technical = false }: { label: string; children: ReactNode; technical?: boolean }) {
  return <div><dt>{label}</dt><dd dir={technical ? 'ltr' : undefined}>{children}</dd></div>
}
