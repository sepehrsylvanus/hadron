import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil, Plus, Search, ShieldCheck } from 'lucide-react'
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiRoot } from './apiClient'
import { useAuth } from './AuthContext'
import { formatBusinessDateTime } from './i18n/businessDate'
import { identityLookupAccess, isRoleAssignmentDisabled, roleAssignmentCoverage, shouldPersistAccessDetails, systemRoleChange, type SystemRoleChange } from './identityAccessSupport'
import {
  IdentityApiError,
  type IdentityUserDraft,
  type IdentityUserField,
  identityPasswordPattern,
  presentIdentityProblem,
  readIdentityApiError,
  validateIdentityUserDraft,
} from './identityProblem'
import { ConfirmationDialog, DataTable, EmptyState, FilterBar, FormField, LoadingState, MessageStrip, PageHeader, Pagination, Section, StatusBadge, TableToolbar } from './ui'

type Paged<T> = { items: T[]; totalCount: number; page: number; pageSize: number }
type Unit = { id: string; nameFa: string; nameEn?: string | null; isActive: boolean; version: number }
type UnitRow = Unit & { assignedUserCount: number; activeUserCount: number }
type Role = { id: string; name: string; descriptionFa?: string | null; descriptionEn?: string | null; isSystemRole: boolean; permissions: string[] }
type RoleRow = Omit<Role, 'permissions'> & { permissionCount: number; userCount: number }
type Permission = { code: string; area: string; nameFa: string; nameEn: string }
type User = { id: string; userName: string; firstName: string; lastName: string; fullName: string; emailAddress: string; cellphoneNumber: string; organizationalUnit: Unit; photoUrl: string; isActive: boolean; mustChangePassword: boolean; isSystemAdministrator: boolean; roleNames: string[]; lastSeenAtUtc?: string | null }
type EffectiveAccess = { code: string; area: string; nameFa: string; nameEn: string; isGranted: boolean; resolution: 'SYSTEM_ROLE' | 'DIRECT_DENY' | 'DIRECT_GRANT' | 'ROLE' | 'NONE'; sources: string[] }
type Detail = { user: User; roles: Role[]; permissionOverrides: { permissionCode: string; effect: 'GRANT' | 'DENY' }[]; dataScopes: { scopeType: string; scopeValue?: string | null }[]; effectivePermissions: string[]; effectiveAccess: EffectiveAccess[] }
type Session = { id: string; createdAtUtc: string; lastSeenAtUtc: string; revokedAtUtc?: string | null; userAgent?: string | null; isCurrent: boolean }
type Audit = { id: string; actorUserId?: string | null; targetUserId?: string | null; action: string; detail?: string | null; source: string; createdAtUtc: string }
type OperationalAudit = { id: string; entityType: string; entityId: string; action: string; occurredAtUtc: string; actorId?: string | null; correlationId?: string | null; reason?: string | null; source: string; hasPreviousValues: boolean; hasCurrentValues: boolean }
type OperationalAuditPage = { items: OperationalAudit[]; total: number; page: number; pageSize: number }
type WarehouseScopeOption = { id: string; code: string; nameFa: string; nameEn?: string | null; addressFa?: string | null; addressEn?: string | null; isActive: boolean }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiRoot}${path}`, init)
  if (!response.ok) throw await readIdentityApiError(response)
  if (response.status === 204) return undefined as T
  return response.json()
}

function useLocale() {
  const { i18n, t } = useTranslation()
  const en = i18n.resolvedLanguage === 'en'
  return { en, l: (fa: string, english: string) => en ? english : fa, translate: (key: string) => String(t(key)) }
}

function photoUrl(user: User) { return `${apiRoot}${user.photoUrl.replace('/api', '')}` }
function unitName(unit: Unit, en: boolean) { return en ? unit.nameEn?.trim() || unit.nameFa : unit.nameFa }
function unitOptionName(unit: Unit, en: boolean) {
  const primary = unitName(unit, en)
  const secondary = en ? unit.nameFa : unit.nameEn?.trim()
  return secondary && secondary !== primary ? `${primary} — ${secondary}` : primary
}
function pageSummary(page: number, pageSize: number, total: number, en: boolean) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  return en ? `Page ${page} of ${pages} — ${total} records` : `صفحه ${page} از ${pages} — ${total} رکورد`
}
function mergeById<T extends { id: string }>(current: T[], incoming: T[]) {
  return [...new Map([...current, ...incoming].map(value => [value.id, value])).values()]
}

type CreateUserDraft = Omit<IdentityUserDraft, 'photo'> & { photo: File | null }
const emptyCreateUserDraft: CreateUserDraft = {
  userName: '', temporaryPassword: '', firstName: '', lastName: '', emailAddress: '',
  cellphoneNumber: '', organizationalUnitId: '', photo: null,
}
const createUserFieldOrder: IdentityUserField[] = [
  'userName', 'temporaryPassword', 'firstName', 'lastName', 'emailAddress',
  'cellphoneNumber', 'organizationalUnitId', 'photo', 'roleIds',
]

export function IdentityUsersPage() {
  const { has } = useAuth(); const { en, l } = useLocale(); const [params, setParams] = useSearchParams()
  const canCreateUser = has('identity.user.manage')
  const { canViewRoleCatalog, canViewUnitCatalog } = identityLookupAccess(has)
  const pageSize = 25; const page = Math.max(1, Number(params.get('page') ?? 1)); const search = params.get('search') ?? ''
  const status = params.get('status') ?? 'all'; const unitId = canViewUnitCatalog ? params.get('unitId') ?? '' : ''; const roleId = canViewRoleCatalog ? params.get('roleId') ?? '' : ''; const password = params.get('password') ?? 'all'; const sort = params.get('sort') ?? 'name'
  const [data, setData] = useState<Paged<User>>({ items: [], totalCount: 0, page, pageSize }); const [units, setUnits] = useState<Unit[]>([]); const [roles, setRoles] = useState<Role[]>([]); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  const set = useCallback((name: string, value: string) => { const next = new URLSearchParams(params); if (!value || value === 'all') next.delete(name); else next.set(name, value); if (name !== 'page') next.set('page', '1'); setParams(next) }, [params, setParams])
  useEffect(() => {
    let active = true
    if (canViewUnitCatalog) void request<Unit[]>('/organization/units?includeInactive=true').then(value => { if (active) setUnits(value) }).catch(() => { if (active) setUnits([]) })
    if (canViewRoleCatalog) void request<Role[]>('/admin/roles').then(value => { if (active) setRoles(value) }).catch(() => { if (active) setRoles([]) })
    return () => { active = false }
  }, [canViewRoleCatalog, canViewUnitCatalog])
  useEffect(() => { let active = true; const query = new URLSearchParams({ includeInactive: 'true', page: String(page), pageSize: String(pageSize), sort }); if (search) query.set('search', search); if (status !== 'all') query.set('isActive', String(status === 'active')); if (unitId) query.set('organizationalUnitId', unitId); if (roleId) query.set('roleId', roleId); if (password !== 'all') query.set('mustChangePassword', String(password === 'temporary')); const timer = window.setTimeout(() => void request<Paged<User>>(`/admin/users?${query}`).then(value => { if (active) setData(value) }).catch(error => { if (active) setError(String(error)) }).finally(() => { if (active) setLoading(false) }), 180); return () => { active = false; window.clearTimeout(timer) } }, [page, password, roleId, search, sort, status, unitId])
  const filtered = Boolean(search || status !== 'all' || unitId || roleId || password !== 'all')
  return <main className="page admin-page"><PageHeader eyebrow="Identity & Access" title={l('کاربران سامانه', 'System users')} subtitle={l('جست‌وجو، وضعیت حساب، نقش و واحد سازمانی', 'Search, account status, roles, and organizational units')} actions={canCreateUser ? <Link className="button button--primary" to={`/administration/users/new?return=${encodeURIComponent(`?${params}`)}`}><Plus />{l('کاربر جدید', 'New user')}</Link> : undefined} />
    {error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <Section title={l('فهرست کاربران', 'User worklist')} description={l('نمای واحد برای مدیریت چرخه عمر و دسترسی کاربران', 'A single worklist for user lifecycle and access management')}>
      <FilterBar label={l('فیلتر کاربران', 'User filters')}><div className="admin-filter-grid"><label className="admin-search"><Search /><input value={search} onChange={e => set('search', e.target.value)} placeholder={l('نام، نام کاربری، ایمیل یا واحد', 'Name, username, email, or unit')} /></label><select value={status} onChange={e => set('status', e.target.value)} aria-label={l('وضعیت', 'Status')}><option value="all">{l('همه وضعیت‌ها', 'All statuses')}</option><option value="active">{l('فعال', 'Active')}</option><option value="inactive">{l('غیرفعال', 'Inactive')}</option></select>{canViewUnitCatalog && <select value={unitId} onChange={e => set('unitId', e.target.value)} aria-label={l('واحد سازمانی', 'Organizational unit')}><option value="">{l('همه واحدها', 'All units')}</option>{units.map(unit => <option key={unit.id} value={unit.id}>{unitOptionName(unit, en)}</option>)}</select>}{canViewRoleCatalog && <select value={roleId} onChange={e => set('roleId', e.target.value)} aria-label={l('نقش', 'Role')}><option value="">{l('همه نقش‌ها', 'All roles')}</option>{roles.map(role => <option key={role.id} value={role.id}>{role.name}</option>)}</select>}<select value={password} onChange={e => set('password', e.target.value)} aria-label={l('وضعیت رمز', 'Password state')}><option value="all">{l('همه رمزها', 'All password states')}</option><option value="temporary">{l('تغییر رمز الزامی', 'Password change required')}</option><option value="settled">{l('رمز تثبیت‌شده', 'Password established')}</option></select><select value={sort} onChange={e => set('sort', e.target.value)} aria-label={l('مرتب‌سازی', 'Sort')}><option value="name">{l('نام', 'Name')}</option><option value="username">{l('نام کاربری', 'Username')}</option><option value="unit">{l('واحد', 'Unit')}</option><option value="status">{l('وضعیت', 'Status')}</option></select>{filtered && <button className="button" onClick={() => setParams({ page: '1' })}>{l('پاک‌کردن فیلتر', 'Clear filters')}</button>}</div></FilterBar>
      <TableToolbar title={l('کاربران', 'Users')} count={l(`${data.totalCount} کاربر`, `${data.totalCount} users`)} />
      <DataTable headers={[l('کاربر', 'User'), l('واحد', 'Unit'), l('نقش‌ها', 'Roles'), l('حساب', 'Account'), l('رمز', 'Password'), l('آخرین فعالیت', 'Last activity')]} loading={loading} empty={!loading && data.items.length === 0} filtered={filtered} loadingLabel={l('در حال بارگذاری کاربران…', 'Loading users…')} emptyTitle={l('کاربری وجود ندارد', 'No users')} noResultTitle={l('نتیجه‌ای پیدا نشد', 'No matching users')} emptyDescription="" noResultDescription={l('فیلترها را تغییر دهید.', 'Adjust the filters.')}>
        {data.items.map(user => <tr key={user.id}><td><Link className="admin-user-link" to={`/administration/users/${user.id}?return=${encodeURIComponent(`?${params}`)}`}><img src={photoUrl(user)} alt="" /><span><strong>{user.fullName}</strong><small dir="ltr">{user.userName}</small></span>{user.isSystemAdministrator && <ShieldCheck aria-label={l('مدیر سامانه', 'System administrator')} />}</Link></td><td>{unitName(user.organizationalUnit, en)}</td><td><div className="admin-chips">{user.roleNames.length ? user.roleNames.map(role => <span key={role}>{role}</span>) : <span>—</span>}</div></td><td><StatusBadge tone={user.isActive ? 'positive' : 'neutral'} text={user.isActive ? l('فعال', 'Active') : l('غیرفعال', 'Inactive')} /></td><td><StatusBadge tone={user.mustChangePassword ? 'warning' : 'positive'} text={user.mustChangePassword ? l('موقت', 'Temporary') : l('تثبیت‌شده', 'Established')} /></td><td>{user.lastSeenAtUtc ? new Date(user.lastSeenAtUtc).toLocaleString(en ? 'en' : 'fa-IR') : '—'}</td></tr>)}
      </DataTable><Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={value => set('page', String(value))} previousLabel={l('قبلی', 'Previous')} nextLabel={l('بعدی', 'Next')} summary={pageSummary(page, pageSize, data.totalCount, en)} />
    </Section>
  </main>
}

type UserMode = 'create' | 'view' | 'edit'
export function IdentityUserPage({ mode }: { mode: UserMode }) {
  const { id } = useParams(); const navigate = useNavigate(); const [params] = useSearchParams(); const { has, user: currentUser } = useAuth(); const { en, l, translate } = useLocale(); const returnQuery = params.get('return') ?? ''
  const { canViewPermissionCatalog, canViewRoleCatalog, canViewUnitCatalog } = identityLookupAccess(has)
  const [detail, setDetail] = useState<Detail | null>(null); const [roles, setRoles] = useState<Role[]>([]); const [units, setUnits] = useState<Unit[]>([]); const [permissions, setPermissions] = useState<Permission[]>([]); const [sessions, setSessions] = useState<Session[]>([]); const [audits, setAudits] = useState<Audit[]>([]); const [loading, setLoading] = useState(mode !== 'create'); const [error, setError] = useState(''); const [step, setStep] = useState(1); const [confirmState, setConfirmState] = useState(false); const [accessConfirm, setAccessConfirm] = useState<SystemRoleChange>(null); const [accessSaving, setAccessSaving] = useState(false); const [accessSearch, setAccessSearch] = useState('')
  const [roleIds, setRoleIds] = useState<string[]>([]); const [overrides, setOverrides] = useState<Map<string, 'GRANT' | 'DENY'>>(new Map()); const [profile, setProfile] = useState({ firstName: '', lastName: '', emailAddress: '', cellphoneNumber: '', organizationalUnitId: '' })
  const [createDraft, setCreateDraft] = useState<CreateUserDraft>(emptyCreateUserDraft); const [createFieldErrors, setCreateFieldErrors] = useState<Partial<Record<IdentityUserField, string>>>({}); const [creating, setCreating] = useState(false)
  const [scopeMode, setScopeMode] = useState<'ALL' | 'WAREHOUSE'>('ALL'); const [warehouseScopeIds, setWarehouseScopeIds] = useState<string[]>([]); const [warehouseOptions, setWarehouseOptions] = useState<WarehouseScopeOption[]>([]); const [selectedWarehouseOptions, setSelectedWarehouseOptions] = useState<WarehouseScopeOption[]>([]); const [scopeSearch, setScopeSearch] = useState(''); const [scopePage, setScopePage] = useState(1); const [scopeTotal, setScopeTotal] = useState(0)
  const load = useCallback(async () => { if (!id) return; setLoading(true); try { const d = await request<Detail>(`/admin/users/${id}`); setDetail(d); setRoleIds(d.roles.map(role => role.id)); setOverrides(new Map(d.permissionOverrides.map(item => [item.permissionCode, item.effect]))); const warehouseIds = d.dataScopes.filter(scope => scope.scopeType === 'WAREHOUSE' && scope.scopeValue).map(scope => scope.scopeValue!); setScopeMode(warehouseIds.length ? 'WAREHOUSE' : 'ALL'); setWarehouseScopeIds(warehouseIds); setProfile({ firstName: d.user.firstName, lastName: d.user.lastName, emailAddress: d.user.emailAddress, cellphoneNumber: d.user.cellphoneNumber, organizationalUnitId: d.user.organizationalUnit.id }); const side: Promise<unknown>[] = [request<Session[]>(`/admin/users/${id}/sessions`).then(setSessions)]; if (has('identity.security_audit.view')) side.push(request<Paged<Audit>>(`/admin/security-audit?targetUserId=${id}&page=1&pageSize=25`).then(value => setAudits(value.items))); if (warehouseIds.length) side.push(Promise.all(warehouseIds.map(warehouseId => request<WarehouseScopeOption>(`/inventory/warehouses/${warehouseId}`))).then(setSelectedWarehouseOptions)); else setSelectedWarehouseOptions([]); await Promise.all(side) } catch (reason) { setError(String(reason)) } finally { setLoading(false) } }, [has, id])
  useEffect(() => {
    let active = true
    if (canViewRoleCatalog) void request<Role[]>('/admin/roles').then(value => { if (active) setRoles(value) }).catch(() => { if (active) setRoles([]) })
    if (canViewUnitCatalog) void request<Unit[]>('/organization/units?includeInactive=true').then(value => { if (active) setUnits(value) }).catch(() => { if (active) setUnits([]) })
    if (canViewPermissionCatalog) void request<Permission[]>('/admin/permissions').then(value => { if (active) setPermissions(value) }).catch(() => { if (active) setPermissions([]) })
    return () => { active = false }
  }, [canViewPermissionCatalog, canViewRoleCatalog, canViewUnitCatalog])
  useEffect(() => { if (mode !== 'create') void Promise.resolve().then(load) }, [load, mode])
  useEffect(() => { if (mode === 'create' || !has('identity.permission.assign')) return; let active = true; const timer = window.setTimeout(() => { const query = new URLSearchParams({ search: scopeSearch, includeInactive: 'true', page: String(scopePage), pageSize: '10' }); void request<Paged<WarehouseScopeOption>>(`/inventory/warehouses?${query}`).then(value => { if (!active) return; setWarehouseOptions(value.items); setScopeTotal(value.totalCount) }).catch(reason => { if (active) setError(String(reason)) }) }, 150); return () => { active = false; window.clearTimeout(timer) } }, [has, mode, scopePage, scopeSearch])
  if (mode === 'create' && !has('identity.user.manage')) return <Navigate to="/administration/users" replace />
  if (mode === 'edit' && !has('identity.user.manage')) return <Navigate to={`/administration/users/${id}`} replace />
  if (loading) return <main className="page"><LoadingState label={l('در حال بارگذاری کاربر…', 'Loading user…')} /></main>
  if (mode !== 'create' && !detail) return <main className="page"><MessageStrip tone="critical">{error || l('کاربر پیدا نشد.', 'User not found.')}</MessageStrip></main>

  function updateCreateField<K extends keyof CreateUserDraft>(field: K, value: CreateUserDraft[K]) {
    setCreateDraft(current => ({ ...current, [field]: value }))
    setCreateFieldErrors(current => {
      if (!current[field as IdentityUserField]) return current
      const next = { ...current }
      delete next[field as IdentityUserField]
      return next
    })
  }
  function validateCreateFields() {
    const validation = validateIdentityUserDraft(createDraft, translate)
    setCreateFieldErrors(validation)
    if (Object.keys(validation).length === 0) return true
    setError(translate('identity.correctHighlightedFields'))
    setStep(1)
    return false
  }
  function goToCreateStep(target: number) {
    if (target > 1 && !validateCreateFields()) return
    setError('')
    setStep(target)
  }
  async function createUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!validateCreateFields()) return
    const form = new FormData()
    form.append('userName', createDraft.userName.trim())
    form.append('temporaryPassword', createDraft.temporaryPassword)
    form.append('firstName', createDraft.firstName.trim())
    form.append('lastName', createDraft.lastName.trim())
    form.append('emailAddress', createDraft.emailAddress.trim())
    form.append('cellphoneNumber', createDraft.cellphoneNumber.trim())
    form.append('organizationalUnitId', createDraft.organizationalUnitId)
    if (createDraft.photo) form.append('photo', createDraft.photo)
    form.append('isActive', 'true')
    roleIds.forEach(roleId => form.append('roleIds', roleId))
    setCreating(true)
    setError('')
    setCreateFieldErrors({})
    try {
      const created = await request<Detail>('/admin/users', { method: 'POST', body: form })
      navigate(`/administration/users/${created.user.id}`, { replace: true })
    } catch (reason) {
      if (reason instanceof IdentityApiError) {
        const presentation = presentIdentityProblem(reason.problem, translate('identity.userCreateFailed'), translate)
        setError(presentation.summary)
        setCreateFieldErrors(presentation.fieldErrors)
        const firstField = createUserFieldOrder.find(field => presentation.fieldErrors[field])
        if (firstField) {
          setStep(firstField === 'roleIds' ? 2 : 1)
          window.setTimeout(() => document.querySelector<HTMLElement>(`[name="${firstField}"]`)?.focus(), 0)
        }
      } else {
        setError(reason instanceof Error ? reason.message : translate('identity.userCreateFailed'))
      }
    } finally {
      setCreating(false)
    }
  }
  async function saveProfile(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!id) return; try { await request(`/admin/users/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(profile) }); await load(); navigate(`/administration/users/${id}`, { replace: true }) } catch (reason) { setError(String(reason)) } }
  function requestAccessSave() {
    if (!detail) return
    const change = systemRoleChange(detail.user.isSystemAdministrator, roles, roleIds)
    if (change) {
      setAccessConfirm(change)
      return
    }
    void saveAccess()
  }
  async function saveAccess() {
    if (!id) return
    const persistDetails = shouldPersistAccessDetails(roles, roleIds)
    try {
      if (persistDetails && scopeMode === 'WAREHOUSE' && warehouseScopeIds.length === 0) throw new Error(l('حداقل یک انبار برای محدوده انتخابی لازم است.', 'Select at least one warehouse for warehouse scope.'))
      setAccessSaving(true)
      setError('')
      await request(`/admin/users/${id}/roles`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ roleIds }) })
      if (persistDetails) {
        await request(`/admin/users/${id}/permission-overrides`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ overrides: [...overrides.entries()].map(([permissionCode, effect]) => ({ permissionCode, effect })) }) })
        const scopes = scopeMode === 'ALL' ? [{ scopeType: 'ALL', scopeValue: null }] : warehouseScopeIds.map(scopeValue => ({ scopeType: 'WAREHOUSE', scopeValue }))
        await request(`/admin/users/${id}/data-scopes`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ scopes }) })
      }
      setAccessConfirm(null)
      await load()
    } catch (reason) {
      setError(String(reason))
    } finally {
      setAccessSaving(false)
    }
  }
  async function changeState() { if (!detail || !id) return; try { await request(`/admin/users/${id}/${detail.user.isActive ? 'deactivate' : 'activate'}`, { method: 'POST' }); setConfirmState(false); await load() } catch (reason) { setError(String(reason)) } }
  async function revoke(sessionId: string) { if (!id) return; try { await request(`/admin/users/${id}/sessions/${sessionId}/revoke`, { method: 'POST' }); setSessions(await request(`/admin/users/${id}/sessions`)) } catch (reason) { setError(String(reason)) } }
  const access = detail?.effectiveAccess.filter(item => !accessSearch || `${item.code} ${item.nameFa} ${item.nameEn} ${item.sources.join(' ')}`.toLocaleLowerCase().includes(accessSearch.toLocaleLowerCase())) ?? []
  if (mode === 'create') return <main className="page admin-page"><PageHeader eyebrow="Identity & Access" title={l('ایجاد کاربر', 'Create user')} subtitle={l('حساب، پروفایل و دسترسی اولیه در یک فرایند کنترل‌شده', 'Account, profile, and initial access in one controlled flow')} backTo={`/administration/users${returnQuery}`} backLabel={l('بازگشت به کاربران', 'Back to users')} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}<div className="admin-wizard-steps" aria-label={l('مراحل ایجاد کاربر', 'User creation steps')}><button type="button" className={step === 1 ? 'active' : ''} aria-current={step === 1 ? 'step' : undefined} onClick={() => goToCreateStep(1)}>1. {l('حساب و پروفایل', 'Account & profile')}</button><button type="button" className={step === 2 ? 'active' : ''} aria-current={step === 2 ? 'step' : undefined} onClick={() => goToCreateStep(2)}>2. {l('نقش‌ها', 'Roles')}</button><button type="button" className={step === 3 ? 'active' : ''} aria-current={step === 3 ? 'step' : undefined} onClick={() => goToCreateStep(3)}>3. {l('بازبینی', 'Review')}</button></div><form id="create-user-form" className="admin-object-form" onSubmit={createUser} noValidate><Section title={step === 1 ? l('مشخصات حساب و پرسنل', 'Account and personnel profile') : step === 2 ? l('دسترسی اولیه', 'Initial access') : l('بازبینی و ایجاد', 'Review and create')}>
    <div hidden={step !== 1} className="admin-wizard-panel admin-form-grid"><FormField label={l('نام کاربری', 'Username')} error={createFieldErrors.userName} required><input name="userName" dir="ltr" value={createDraft.userName} onChange={event => updateCreateField('userName', event.target.value)} required /></FormField><FormField label={l('رمز موقت', 'Temporary password')} hint={l('حداقل ۱۰ نویسه شامل حروف بزرگ/کوچک، عدد و نماد', 'At least 10 characters with upper/lowercase, number, and symbol')} error={createFieldErrors.temporaryPassword} required><input name="temporaryPassword" type="password" dir="ltr" value={createDraft.temporaryPassword} pattern={identityPasswordPattern} onChange={event => updateCreateField('temporaryPassword', event.target.value)} required /></FormField><FormField label={l('نام', 'First name')} error={createFieldErrors.firstName} required><input name="firstName" value={createDraft.firstName} onChange={event => updateCreateField('firstName', event.target.value)} required /></FormField><FormField label={l('نام خانوادگی', 'Last name')} error={createFieldErrors.lastName} required><input name="lastName" value={createDraft.lastName} onChange={event => updateCreateField('lastName', event.target.value)} required /></FormField><FormField label={l('ایمیل سازمانی', 'Business email')} error={createFieldErrors.emailAddress} required><input name="emailAddress" type="email" dir="ltr" value={createDraft.emailAddress} onChange={event => updateCreateField('emailAddress', event.target.value)} required /></FormField><FormField label={l('شماره موبایل', 'Cellphone')} error={createFieldErrors.cellphoneNumber} required><input name="cellphoneNumber" dir="ltr" value={createDraft.cellphoneNumber} onChange={event => updateCreateField('cellphoneNumber', event.target.value)} required /></FormField><FormField label={l('واحد سازمانی', 'Organizational unit')} error={createFieldErrors.organizationalUnitId} required><select name="organizationalUnitId" required value={createDraft.organizationalUnitId} onChange={event => updateCreateField('organizationalUnitId', event.target.value)}><option value="" disabled>{l('انتخاب کنید', 'Select')}</option>{units.filter(x => x.isActive).map(unit => <option value={unit.id} key={unit.id}>{unitOptionName(unit, en)}</option>)}</select></FormField><FormField label={l('تصویر پروفایل', 'Profile image')} hint={createDraft.photo ? l(`فایل انتخاب‌شده: ${createDraft.photo.name}`, `Selected file: ${createDraft.photo.name}`) : l('PNG، JPG یا WebP معتبر، حداکثر ۲ مگابایت', 'Valid PNG, JPG, or WebP, up to 2 MB')} error={createFieldErrors.photo} required><input name="photo" type="file" accept="image/png,image/jpeg,image/webp" onChange={event => updateCreateField('photo', event.target.files?.[0] ?? null)} required /></FormField></div>
    <div hidden={step !== 2} className="admin-wizard-panel"><RolePicker roles={roles} roleIds={roleIds} permissionCodes={permissions.map(permission => permission.code)} setRoleIds={ids => { setRoleIds(ids); setCreateFieldErrors(current => { const next = { ...current }; delete next.roleIds; return next }) }} disabled={!has('identity.permission.assign')} canAssignSystemRole={currentUser.isSystemAdministrator} l={l} />{createFieldErrors.roleIds && <p className="ui-field-error" role="alert">{createFieldErrors.roleIds}</p>}</div>
    <div hidden={step !== 3} className="admin-wizard-panel admin-review"><ShieldCheck /><h3>{l('کاربر فعال ایجاد می‌شود', 'An active user will be created')}</h3><p>{l(`${roleIds.length} نقش انتخاب شده است. کاربر در اولین ورود باید رمز موقت را تغییر دهد.`, `${roleIds.length} roles selected. The user must change the temporary password at first sign-in.`)}</p></div>
    <div className="admin-wizard-actions">{step > 1 && <button type="button" className="button" disabled={creating} onClick={() => goToCreateStep(step - 1)}>{l('مرحله قبل', 'Previous')}</button>}{step < 3 ? <button key="create-user-next" type="button" className="button button--primary" onClick={() => goToCreateStep(step + 1)}>{l('مرحله بعد', 'Next')}</button> : <button key="create-user-submit" type="submit" className="button button--primary" disabled={creating}>{creating ? translate('identity.creatingUser') : l('ایجاد کاربر', 'Create user')}</button>}</div>
  </Section></form></main>

  const user = detail!.user
  const profileUnits = units.some(unit => unit.id === user.organizationalUnit.id) ? units : [user.organizationalUnit, ...units]
  const selectedRoleCoverage = roleAssignmentCoverage(roles, roleIds, permissions.map(permission => permission.code))
  const systemRoleSelected = selectedRoleCoverage.systemRoleSelected
  const visibleWarehouseOptions = mergeById(selectedWarehouseOptions, warehouseOptions)
    .filter(option => warehouseScopeIds.includes(option.id) || warehouseOptions.some(current => current.id === option.id))
  return <main className="page admin-page"><PageHeader eyebrow={user.userName} title={user.fullName} subtitle={`${unitName(user.organizationalUnit, en)} · ${user.emailAddress}`} backTo={`/administration/users${returnQuery}`} backLabel={l('بازگشت به کاربران', 'Back to users')} status={<StatusBadge tone={user.isActive ? 'positive' : 'neutral'} text={user.isActive ? l('فعال', 'Active') : l('غیرفعال', 'Inactive')} />} actions={<>{mode === 'view' && has('identity.user.manage') && <Link className="button button--primary" to={`/administration/users/${id}/edit?return=${encodeURIComponent(returnQuery)}`}><Pencil />{l('ویرایش پروفایل', 'Edit profile')}</Link>}{has('identity.user.manage') && <button className={user.isActive ? 'button button--danger' : 'button button--primary'} onClick={() => setConfirmState(true)}>{user.isActive ? l('غیرفعال‌سازی', 'Deactivate') : l('فعال‌سازی', 'Activate')}</button>}</>} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}
    <div className="admin-object-layout"><section className="admin-profile-card"><img src={photoUrl(user)} alt={user.fullName} /><strong>{user.fullName}</strong><span dir="ltr">@{user.userName}</span>{user.isSystemAdministrator && <StatusBadge tone="information" text={l('مدیر سامانه', 'System administrator')} />}</section><div className="admin-object-main">
      <Section title={l('پروفایل و حساب', 'Profile and account')} description={l('هویت سازمانی و وضعیت ورود', 'Organizational identity and sign-in state')}>{mode === 'edit' ? <form className="admin-form-grid" onSubmit={saveProfile}><FormField label={l('نام', 'First name')} required><input value={profile.firstName} onChange={e => setProfile(value => ({ ...value, firstName: e.target.value }))} required /></FormField><FormField label={l('نام خانوادگی', 'Last name')} required><input value={profile.lastName} onChange={e => setProfile(value => ({ ...value, lastName: e.target.value }))} required /></FormField><FormField label={l('ایمیل', 'Email')} required><input type="email" dir="ltr" value={profile.emailAddress} onChange={e => setProfile(value => ({ ...value, emailAddress: e.target.value }))} required /></FormField><FormField label={l('موبایل', 'Cellphone')} required><input dir="ltr" value={profile.cellphoneNumber} onChange={e => setProfile(value => ({ ...value, cellphoneNumber: e.target.value }))} required /></FormField><FormField label={l('واحد سازمانی', 'Organizational unit')} required><select value={profile.organizationalUnitId} disabled={!canViewUnitCatalog} onChange={e => setProfile(value => ({ ...value, organizationalUnitId: e.target.value }))}>{profileUnits.map(unit => <option key={unit.id} value={unit.id} disabled={!unit.isActive && unit.id !== profile.organizationalUnitId}>{unitOptionName(unit, en)}</option>)}</select></FormField><div className="admin-form-actions"><button className="button button--primary">{l('ذخیره', 'Save')}</button><Link className="button" to={`/administration/users/${id}?return=${encodeURIComponent(returnQuery)}`}>{l('انصراف', 'Cancel')}</Link></div></form> : <dl className="admin-definition-grid"><div><dt>{l('نام کاربری', 'Username')}</dt><dd dir="ltr">{user.userName}</dd></div><div><dt>{l('ایمیل', 'Email')}</dt><dd dir="ltr">{user.emailAddress}</dd></div><div><dt>{l('موبایل', 'Cellphone')}</dt><dd dir="ltr">{user.cellphoneNumber}</dd></div><div><dt>{l('واحد سازمانی', 'Organizational unit')}</dt><dd>{unitName(user.organizationalUnit, en)}</dd></div><div><dt>{l('رمز ورود', 'Password')}</dt><dd><StatusBadge tone={user.mustChangePassword ? 'warning' : 'positive'} text={user.mustChangePassword ? l('تغییر الزامی', 'Change required') : l('تثبیت‌شده', 'Established')} /></dd></div><div><dt>{l('آخرین فعالیت', 'Last activity')}</dt><dd>{user.lastSeenAtUtc ? new Date(user.lastSeenAtUtc).toLocaleString(en ? 'en' : 'fa-IR') : '—'}</dd></div></dl>}</Section>
      <Section title={l('نقش‌ها و دسترسی مستقیم', 'Roles and direct access')} description={l('Deny مستقیم بر Grant و نقش اولویت دارد.', 'Direct Deny takes precedence over grants and roles.')} actions={has('identity.permission.assign') ? <button className="button button--primary" disabled={accessSaving} onClick={requestAccessSave}>{accessSaving ? l('در حال ذخیره…', 'Saving…') : l('ذخیره دسترسی', 'Save access')}</button> : undefined}><RolePicker roles={roles} roleIds={roleIds} permissionCodes={permissions.map(permission => permission.code)} setRoleIds={setRoleIds} disabled={!has('identity.permission.assign')} canAssignSystemRole={currentUser.isSystemAdministrator} l={l} />{!systemRoleSelected && has('identity.permission.assign') && <details className="admin-overrides"><summary>{l('استثناهای مستقیم', 'Direct overrides')}</summary><div className="admin-permission-list">{permissions.map(permission => <label key={permission.code}><span><strong>{en ? permission.nameEn : permission.nameFa}</strong><small>{permission.code}</small></span><select value={overrides.get(permission.code) ?? ''} onChange={e => setOverrides(current => { const next = new Map(current); const value = e.target.value as '' | 'GRANT' | 'DENY'; if (value) next.set(permission.code, value); else next.delete(permission.code); return next })}><option value="">{l('ارث‌بری', 'Inherit')}</option><option value="GRANT">{l('اجازه مستقیم', 'Direct grant')}</option><option value="DENY">{l('منع مستقیم', 'Direct deny')}</option></select></label>)}</div></details>}</Section>
      <Section title={l('محدوده داده انبار', 'Warehouse data scope')} description={l('مجوز عملیات مشخص می‌کند کاربر چه کاری انجام دهد؛ این محدوده مشخص می‌کند در کدام انبارها.', 'Permissions define what the user may do; this scope defines in which warehouses.')}><div className="admin-scope-mode"><label><input type="radio" checked={systemRoleSelected || scopeMode === 'ALL'} disabled={systemRoleSelected || !has('identity.permission.assign')} onChange={() => setScopeMode('ALL')} />{l('همه انبارها', 'All warehouses')}</label><label><input type="radio" checked={!systemRoleSelected && scopeMode === 'WAREHOUSE'} disabled={systemRoleSelected || !has('identity.permission.assign')} onChange={() => setScopeMode('WAREHOUSE')} />{l('انبارهای انتخاب‌شده', 'Selected warehouses')}</label></div>{systemRoleSelected && <MessageStrip tone="information">{l('مدیر سامانه همیشه محدوده کامل دارد و قابل محدودسازی نیست.', 'A system administrator always has full scope and cannot be narrowed.')}</MessageStrip>}{!systemRoleSelected && scopeMode === 'WAREHOUSE' && <><FilterBar label={l('جست‌وجوی انبار', 'Warehouse search')}><label className="admin-search"><Search /><input value={scopeSearch} onChange={event => { setScopeSearch(event.target.value); setScopePage(1) }} placeholder={l('کد رسمی یا نام انبار', 'Official code or warehouse name')} /></label></FilterBar><div className="admin-warehouse-scopes">{visibleWarehouseOptions.map(option => <label key={option.id}><input type="checkbox" disabled={!has('identity.permission.assign')} checked={warehouseScopeIds.includes(option.id)} onChange={event => { setWarehouseScopeIds(current => event.target.checked ? [...new Set([...current, option.id])] : current.filter(value => value !== option.id)); if (event.target.checked) setSelectedWarehouseOptions(current => mergeById(current, [option])); else setSelectedWarehouseOptions(current => current.filter(value => value.id !== option.id)) }} /><span><strong>{option.code} — {en ? option.nameEn?.trim() || option.nameFa : option.nameFa}</strong><small>{en ? option.addressEn || option.addressFa : option.addressFa}{!option.isActive ? ` · ${l('غیرفعال', 'Inactive')}` : ''}</small></span></label>)}</div><Pagination page={scopePage} pageSize={10} totalCount={scopeTotal} onPageChange={setScopePage} previousLabel={l('قبلی', 'Previous')} nextLabel={l('بعدی', 'Next')} summary={pageSummary(scopePage, 10, scopeTotal, en)} /></>}</Section>
      <Section title={l('دسترسی مؤثر', 'Effective access')} description={l('نتیجه واقعی تصمیم مجوز همراه با منشأ', 'The effective permission decision with provenance')}><label className="admin-search"><Search /><input value={accessSearch} onChange={e => setAccessSearch(e.target.value)} placeholder={l('جست‌وجوی دسترسی', 'Search access')} /></label><DataTable headers={[l('دسترسی', 'Permission'), l('حوزه', 'Area'), l('نتیجه', 'Decision'), l('منشأ', 'Source')]} loading={false} empty={access.length === 0} loadingLabel="" emptyTitle={l('دسترسی یافت نشد', 'No access found')} noResultTitle={l('دسترسی یافت نشد', 'No access found')}><>{access.map(item => <tr key={item.code}><td><strong>{en ? item.nameEn : item.nameFa}</strong><small className="admin-code">{item.code}</small></td><td>{item.area}</td><td><StatusBadge tone={item.isGranted ? 'positive' : item.resolution === 'DIRECT_DENY' ? 'critical' : 'neutral'} text={item.isGranted ? l('مجاز', 'Allowed') : l('غیرمجاز', 'Denied')} /></td><td>{item.resolution === 'SYSTEM_ROLE' ? l('مدیر سامانه', 'System role') : item.resolution === 'DIRECT_DENY' ? l('منع مستقیم', 'Direct deny') : item.resolution === 'DIRECT_GRANT' ? l('اجازه مستقیم', 'Direct grant') : item.resolution === 'ROLE' ? `${l('نقش', 'Role')}: ${item.sources.join(', ')}` : l('بدون تخصیص', 'Unassigned')}</td></tr>)}</></DataTable></Section>
      <Section title={l('نشست‌ها و تاریخچه امنیتی', 'Sessions and security history')}><div className="admin-split"><div><h3>{l('نشست‌های ورود', 'Sign-in sessions')}</h3>{sessions.length === 0 ? <EmptyState compact title={l('نشستی ثبت نشده', 'No sessions')} /> : <div className="admin-event-list">{sessions.map(session => <article key={session.id}><div><strong>{session.isCurrent ? l('نشست فعلی', 'Current session') : l('نشست', 'Session')}</strong><small>{new Date(session.lastSeenAtUtc).toLocaleString(en ? 'en' : 'fa-IR')}</small><small>{session.userAgent || '—'}</small></div><StatusBadge tone={session.revokedAtUtc ? 'neutral' : 'positive'} text={session.revokedAtUtc ? l('باطل‌شده', 'Revoked') : l('فعال', 'Active')} />{!session.revokedAtUtc && has('identity.session.revoke') && <button className="button" onClick={() => void revoke(session.id)}>{l('ابطال', 'Revoke')}</button>}</article>)}</div>}</div><div><h3>{l('رویدادهای امنیتی', 'Security events')}</h3>{audits.length === 0 ? <EmptyState compact title={l('رویدادی ثبت نشده', 'No events')} /> : <div className="admin-event-list">{audits.map(audit => <article key={audit.id}><div><strong className="admin-code">{audit.action}</strong><small>{new Date(audit.createdAtUtc).toLocaleString(en ? 'en' : 'fa-IR')}</small></div><span>{audit.source}</span></article>)}</div>}</div></div></Section>
    </div></div><ConfirmationDialog open={confirmState} title={user.isActive ? l('غیرفعال‌سازی کاربر', 'Deactivate user') : l('فعال‌سازی کاربر', 'Activate user')} description={user.isActive ? l('نشست‌های فعال کاربر نیز باطل می‌شوند.', 'Active sessions will also be revoked.') : l('کاربر دوباره امکان ورود خواهد داشت.', 'The user will be able to sign in again.')} confirmLabel={l('تأیید', 'Confirm')} cancelLabel={l('انصراف', 'Cancel')} onConfirm={() => void changeState()} onCancel={() => setConfirmState(false)} />
    <ConfirmationDialog open={accessConfirm !== null} title={accessConfirm === 'PROMOTION' ? l('ارتقا به مدیر سامانه', 'Promote to System Administrator') : l('حذف دسترسی مدیر سامانه', 'Remove System Administrator access')} description={accessConfirm === 'PROMOTION' ? l('این کار همه مجوزهای فعلی و آینده و محدوده کامل داده را به کاربر می‌دهد. استثناهای مستقیم و محدوده انبار برای مدیر سامانه اعمال نمی‌شوند.', 'This grants every current and future permission with full data scope. Direct overrides and warehouse scope do not apply to a System Administrator.') : l('دسترسی کامل و پویای کاربر حذف می‌شود و فقط نقش‌ها، استثناهای مستقیم و محدوده داده انتخاب‌شده باقی می‌ماند.', 'Full dynamic access will be removed; only the selected roles, direct overrides, and data scope will remain.')} confirmLabel={accessConfirm === 'PROMOTION' ? l('ارتقا و ذخیره', 'Promote and save') : l('حذف دسترسی و ذخیره', 'Remove access and save')} cancelLabel={l('انصراف', 'Cancel')} tone={accessConfirm === 'PROMOTION' ? 'normal' : 'critical'} confirmDisabled={accessSaving} onConfirm={() => void saveAccess()} onCancel={() => setAccessConfirm(null)} />
  </main>
}

function RolePicker({ roles, roleIds, permissionCodes, setRoleIds, disabled = false, canAssignSystemRole = false, l }: { roles: Role[]; roleIds: string[]; permissionCodes: string[]; setRoleIds: (ids: string[]) => void; disabled?: boolean; canAssignSystemRole?: boolean; l: (fa: string, en: string) => string }) {
  const coverage = roleAssignmentCoverage(roles, roleIds, permissionCodes)
  return <div className="admin-role-picker">
    <div className="admin-role-coverage" role="status" aria-live="polite"><span>{l('پوشش نقش‌های انتخابی از فهرست جاری مجوزها', 'Selected-role coverage of the current permission catalog')}</span><strong dir="ltr">{coverage.selectedPermissionCount} / {coverage.catalogPermissionCount}</strong></div>
    {coverage.systemRoleSelected && <MessageStrip tone="warning">{l('نقش مدیر سامانه همه منوها، عملیات و مجوزهای فعلی و آینده را اعطا می‌کند.', 'The SystemAdministrator role grants every current and future menu, operation, and permission.')}</MessageStrip>}
    {!coverage.systemRoleSelected && coverage.allOrdinaryRolesSelected && <MessageStrip tone="information">{l('انتخاب همه نقش‌های عادی لزوماً به معنای دسترسی کامل نیست؛ پوشش واقعی را در جدول «دسترسی مؤثر» بررسی کنید.', 'Selecting every ordinary role does not necessarily grant complete access; verify the actual coverage in Effective access.')}</MessageStrip>}
    {roles.map(role => <label key={role.id}><input type="checkbox" disabled={isRoleAssignmentDisabled(role, disabled, canAssignSystemRole)} checked={roleIds.includes(role.id)} onChange={e => setRoleIds(e.target.checked ? [...new Set([...roleIds, role.id])] : roleIds.filter(id => id !== role.id))} /><span><strong>{role.name}{role.isSystemRole && <ShieldCheck />}</strong><small>{role.isSystemRole ? l('همه دسترسی‌های فعلی و آینده', 'All current and future permissions') : role.descriptionFa || role.descriptionEn || l('بدون توضیح', 'No description')}</small></span><StatusBadge tone="information" text={role.isSystemRole ? l('کامل و پویا', 'Full and dynamic') : l(`${role.permissions.length} دسترسی`, `${role.permissions.length} permissions`)} /></label>)}
  </div>
}

export function IdentityRolesPage() {
  const { has } = useAuth(); const { en, l } = useLocale(); const [params, setParams] = useSearchParams(); const page = Math.max(1, Number(params.get('page') ?? 1)); const search = params.get('search') ?? ''; const pageSize = 25
  const canDefineRole = has('identity.role.manage')
  const [data, setData] = useState<Paged<RoleRow>>({ items: [], totalCount: 0, page, pageSize }); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  useEffect(() => { let active = true; const timer = window.setTimeout(() => void request<Paged<RoleRow>>(`/admin/roles/worklist?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`).then(value => { if (active) setData(value) }).catch(reason => { if (active) setError(String(reason)) }).finally(() => { if (active) setLoading(false) }), 150); return () => { active = false; window.clearTimeout(timer) } }, [page, search])
  function set(name: string, value: string) { const next = new URLSearchParams(params); if (value) next.set(name, value); else next.delete(name); if (name !== 'page') next.set('page', '1'); setParams(next) }
  return <main className="page admin-page"><PageHeader eyebrow="RBAC" title={l('نقش‌ها و مجوزها', 'Roles and permissions')} subtitle={l('نقش‌های قابل‌استفاده مجدد و دامنه اثر آن‌ها', 'Reusable roles and their permission impact')} actions={canDefineRole ? <Link className="button button--primary" to="/administration/roles/new"><Plus />{l('نقش جدید', 'New role')}</Link> : undefined} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}<Section title={l('فهرست نقش‌ها', 'Role worklist')}><FilterBar label={l('فیلتر نقش‌ها', 'Role filters')}><label className="admin-search"><Search /><input value={search} onChange={e => set('search', e.target.value)} placeholder={l('نام یا توضیح نقش', 'Role name or description')} /></label></FilterBar><DataTable headers={[l('نقش', 'Role'), l('نوع', 'Type'), l('دسترسی‌ها', 'Permissions'), l('کاربران', 'Users')]} loading={loading} empty={!loading && data.items.length === 0} filtered={Boolean(search)} loadingLabel={l('در حال بارگذاری…', 'Loading…')} emptyTitle={l('نقشی وجود ندارد', 'No roles')} noResultTitle={l('نتیجه‌ای پیدا نشد', 'No matching roles')}><>{data.items.map(role => <tr key={role.id}><td><Link className="admin-primary-link" to={`/administration/roles/${role.id}`}><strong>{role.name}</strong><small>{en ? role.descriptionEn || role.descriptionFa : role.descriptionFa || role.descriptionEn}</small></Link></td><td><StatusBadge tone={role.isSystemRole ? 'information' : 'neutral'} text={role.isSystemRole ? l('سیستمی', 'System') : l('سفارشی', 'Custom')} /></td><td>{role.isSystemRole ? l('همه مجوزهای فعلی و آینده', 'All current and future permissions') : role.permissionCount}</td><td>{role.userCount}</td></tr>)}</></DataTable><Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={value => set('page', String(value))} previousLabel={l('قبلی', 'Previous')} nextLabel={l('بعدی', 'Next')} summary={pageSummary(page, pageSize, data.totalCount, en)} /></Section></main>
}

export function IdentityRolePage({ mode }: { mode: 'create' | 'view' | 'edit' }) {
  const { id } = useParams(); const navigate = useNavigate(); const { has } = useAuth(); const { en, l } = useLocale(); const [roles, setRoles] = useState<Role[]>([]); const [permissions, setPermissions] = useState<Permission[]>([]); const [selected, setSelected] = useState<string[]>([]); const [form, setForm] = useState({ name: '', descriptionFa: '', descriptionEn: '' }); const [search, setSearch] = useState(''); const [area, setArea] = useState(''); const [step, setStep] = useState(1); const [error, setError] = useState(''); const [loading, setLoading] = useState(mode !== 'create')
  const canManageRole = has('identity.role.manage')
  const canAssignPermissions = has('identity.permission.assign')
  const canCreateRole = canManageRole
  useEffect(() => { void Promise.all([request<Role[]>('/admin/roles'), request<Permission[]>('/admin/permissions')]).then(([r, p]) => { setRoles(r); setPermissions(p); if (id) { const role = r.find(value => value.id === id); if (role) { setForm({ name: role.name, descriptionFa: role.descriptionFa ?? '', descriptionEn: role.descriptionEn ?? '' }); setSelected(role.permissions) } } }).catch(reason => setError(String(reason))).finally(() => setLoading(false)) }, [id])
  const role = roles.find(value => value.id === id); const editing = mode === 'create' || mode === 'edit'; const permissionEditing = editing && canAssignPermissions && !role?.isSystemRole; const areas = [...new Set(permissions.map(value => value.area))]; const shown = permissions.filter(permission => (!area || permission.area === area) && (!search || `${permission.code} ${permission.nameFa} ${permission.nameEn}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()))); const changedCount = role ? new Set([...role.permissions.filter(code => !selected.includes(code)), ...selected.filter(code => !role.permissions.includes(code))]).size : selected.length
  if (mode === 'create' && !canCreateRole) return <Navigate to="/administration/roles" replace />
  if (mode === 'edit' && (!canManageRole || role?.isSystemRole)) return <Navigate to={`/administration/roles/${id}`} replace />
  async function save(event: FormEvent) { event.preventDefault(); try { if (mode === 'create') { const body = JSON.stringify({ ...form, permissionCodes: selected }); const created = await request<Role>('/admin/roles', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body }); navigate(`/administration/roles/${created.id}`, { replace: true }) } else { const body = JSON.stringify(canAssignPermissions ? { ...form, permissionCodes: selected } : form); const endpoint = canAssignPermissions ? `/admin/roles/${id}/definition` : `/admin/roles/${id}`; const updated = await request<Role>(endpoint, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body }); navigate(`/administration/roles/${updated.id}`, { replace: true }) } } catch (reason) { setError(String(reason)) } }
  if (loading) return <main className="page"><LoadingState label={l('در حال بارگذاری نقش…', 'Loading role…')} /></main>
  if (mode !== 'create' && !role) return <main className="page"><MessageStrip tone="critical">{error || l('نقش پیدا نشد.', 'Role not found.')}</MessageStrip></main>
  const title = mode === 'create' ? l('ایجاد نقش', 'Create role') : role!.name
  return <main className="page admin-page"><PageHeader eyebrow="RBAC" title={title} subtitle={mode === 'create' ? l('تعریف نقش و دسترسی‌ها در یک عملیات', 'Define role and permissions atomically') : (en ? role!.descriptionEn || role!.descriptionFa : role!.descriptionFa || role!.descriptionEn) || l('بدون توضیح', 'No description')} backTo="/administration/roles" backLabel={l('بازگشت به نقش‌ها', 'Back to roles')} status={role?.isSystemRole ? <StatusBadge tone="information" text={l('نقش سیستمی', 'System role')} /> : undefined} actions={mode === 'view' && !role?.isSystemRole && canManageRole ? <Link className="button button--primary" to={`/administration/roles/${id}/edit`}><Pencil />{l('ویرایش نقش', 'Edit role')}</Link> : undefined} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}{mode === 'create' && <div className="admin-wizard-steps"><button type="button" className={step === 1 ? 'active' : ''} onClick={() => setStep(1)}>1. {l('مشخصات', 'Details')}</button><button type="button" className={step === 2 ? 'active' : ''} onClick={() => setStep(2)}>2. {l('مجوزها', 'Permissions')}</button><button type="button" className={step === 3 ? 'active' : ''} onClick={() => setStep(3)}>3. {l('اثر و بازبینی', 'Impact & review')}</button></div>}<form onSubmit={save} className="admin-object-form">
    {(!editing || step === 1 || mode === 'edit') && <Section title={l('تعریف نقش', 'Role definition')}><div className="admin-form-grid"><FormField label={l('نام فنی نقش', 'Technical role name')} required><input dir="ltr" value={form.name} disabled={!editing} onChange={e => setForm(value => ({ ...value, name: e.target.value }))} required /></FormField><FormField label={l('توضیح فارسی', 'Persian description')}><input value={form.descriptionFa} disabled={!editing} onChange={e => setForm(value => ({ ...value, descriptionFa: e.target.value }))} /></FormField><FormField label={l('توضیح انگلیسی', 'English description')}><input dir="ltr" value={form.descriptionEn} disabled={!editing} onChange={e => setForm(value => ({ ...value, descriptionEn: e.target.value }))} /></FormField></div></Section>}
    {(!editing || step === 2 || mode === 'edit') && <Section title={l('ماتریس دسترسی', 'Permission matrix')} description={canAssignPermissions ? l('مجوزها براساس حوزه کسب‌وکار گروه‌بندی شده‌اند.', 'Permissions are grouped by business area.') : l('برای تغییر ماتریس، مجوز تخصیص دسترسی لازم است؛ شما فقط مشخصات نقش را ویرایش می‌کنید.', 'Permission assignment is required to change the matrix; you can edit role metadata only.')}><FilterBar label={l('فیلتر مجوز', 'Permission filters')}><label className="admin-search"><Search /><input value={search} onChange={e => setSearch(e.target.value)} placeholder={l('کد یا عنوان مجوز', 'Permission code or name')} /></label><select value={area} onChange={e => setArea(e.target.value)}><option value="">{l('همه حوزه‌ها', 'All areas')}</option>{areas.map(value => <option key={value}>{value}</option>)}</select></FilterBar><div className="admin-permission-groups">{areas.filter(value => !area || value === area).map(group => { const groupPermissions = shown.filter(value => value.area === group); if (!groupPermissions.length) return null; const allSelected = groupPermissions.every(value => selected.includes(value.code)); return <section key={group}><header><h3>{group}</h3>{permissionEditing && <button type="button" className="button button--quiet" onClick={() => setSelected(current => allSelected ? current.filter(code => !groupPermissions.some(permission => permission.code === code)) : [...new Set([...current, ...groupPermissions.map(permission => permission.code)])])}>{allSelected ? l('لغو همه', 'Clear all') : l('انتخاب همه', 'Select all')}</button>}</header>{groupPermissions.map(permission => <label key={permission.code}><input type="checkbox" disabled={!permissionEditing} checked={role?.isSystemRole || selected.includes(permission.code)} onChange={e => setSelected(current => e.target.checked ? [...new Set([...current, permission.code])] : current.filter(code => code !== permission.code))} /><span><strong>{en ? permission.nameEn : permission.nameFa}</strong><small>{permission.code}</small></span></label>)}</section> })}</div></Section>}
    {editing && (step === 3 || mode === 'edit') && <Section title={l('اثر تغییر', 'Change impact')}><div className="admin-review"><ShieldCheck /><h3>{mode === 'create' ? l(`${selected.length} مجوز تخصیص می‌یابد`, `${selected.length} permissions will be assigned`) : canAssignPermissions ? l(`${changedCount} تغییر دسترسی`, `${changedCount} permission changes`) : l('فقط مشخصات نقش ذخیره می‌شود', 'Only role metadata will be saved')}</h3><p>{canAssignPermissions ? l('تغییر نقش بلافاصله روی کاربران تخصیص‌یافته اعمال و AccessVersion آن‌ها افزایش می‌یابد.', 'Role changes take effect immediately for assigned users and advance their AccessVersion.') : l('نام و توضیحات به‌روزرسانی می‌شوند و ماتریس مجوز بدون تغییر می‌ماند.', 'Name and descriptions will be updated while the permission matrix remains unchanged.')}</p></div></Section>}
    {editing && <div className="admin-wizard-actions">{mode === 'create' && step > 1 && <button type="button" className="button" onClick={() => setStep(value => value - 1)}>{l('مرحله قبل', 'Previous')}</button>}{mode === 'create' && step < 3 ? <button key="create-role-next" type="button" className="button button--primary" onClick={() => setStep(value => value + 1)}>{l('مرحله بعد', 'Next')}</button> : <button key="create-role-submit" type="submit" className="button button--primary">{l('ذخیره نقش', 'Save role')}</button>}<Link className="button" to={mode === 'create' ? '/administration/roles' : `/administration/roles/${id}`}>{l('انصراف', 'Cancel')}</Link></div>}
  </form></main>
}

export function OrganizationalUnitsPage() {
  const { has } = useAuth(); const { en, l } = useLocale(); const [params, setParams] = useSearchParams(); const search = params.get('search') ?? ''; const page = Math.max(1, Number(params.get('page') ?? 1)); const pageSize = 25
  const [data, setData] = useState<Paged<UnitRow>>({ items: [], totalCount: 0, page, pageSize }); const [selected, setSelected] = useState<UnitRow | null>(null); const [editor, setEditor] = useState(false); const [error, setError] = useState(''); const [loading, setLoading] = useState(true); const [confirm, setConfirm] = useState<UnitRow | null>(null)
  const load = useCallback(async () => { setLoading(true); try { setData(await request(`/organization/units/worklist?includeInactive=true&search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`)) } catch (reason) { setError(String(reason)) } finally { setLoading(false) } }, [page, search])
  useEffect(() => { const timer = window.setTimeout(() => void load(), 150); return () => window.clearTimeout(timer) }, [load])
  function set(name: string, value: string) { const next = new URLSearchParams(params); if (value) next.set(name, value); else next.delete(name); if (name !== 'page') next.set('page', '1'); setParams(next) }
  async function save(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const form = new FormData(event.currentTarget); const body = JSON.stringify({ nameFa: form.get('nameFa'), nameEn: form.get('nameEn') || null, ...(selected ? { expectedVersion: selected.version } : {}) }); try { await request(selected ? `/organization/units/${selected.id}` : '/organization/units', { method: selected ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body }); setEditor(false); setSelected(null); await load() } catch (reason) { setError(String(reason)) } }
  async function state() { if (!confirm) return; try { await request(`/organization/units/${confirm.id}/${confirm.isActive ? 'deactivate' : 'activate'}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedVersion: confirm.version }) }); setConfirm(null); await load() } catch (reason) { setError(String(reason)) } }
  return <main className="page admin-page"><PageHeader eyebrow="Organization" title={l('واحدهای سازمانی', 'Organizational units')} subtitle={l('مرجع سبک و تخت برای انتساب پرسنل؛ بدون ساختار HR', 'A lightweight flat personnel-assignment master, not an HR hierarchy')} actions={has('organization.unit.manage') ? <button className="button button--primary" onClick={() => { setSelected(null); setEditor(true) }}><Plus />{l('واحد جدید', 'New unit')}</button> : undefined} />{error && <MessageStrip tone="critical">{error}</MessageStrip>}<Section title={l('فهرست واحدها', 'Unit worklist')}><FilterBar label={l('جست‌وجوی واحد', 'Unit search')}><label className="admin-search"><Search /><input value={search} onChange={e => set('search', e.target.value)} placeholder={l('نام فارسی یا انگلیسی', 'Persian or English name')} /></label></FilterBar><DataTable headers={[l('واحد', 'Unit'), l('وضعیت', 'Status'), l('کاربران تخصیص‌یافته', 'Assigned users'), l('کاربران فعال', 'Active users'), l('عملیات', 'Actions')]} loading={loading} empty={!loading && data.items.length === 0} filtered={Boolean(search)} loadingLabel={l('در حال بارگذاری…', 'Loading…')} emptyTitle={l('واحدی وجود ندارد', 'No units')} noResultTitle={l('نتیجه‌ای پیدا نشد', 'No matching units')}><>{data.items.map(unit => <tr key={unit.id}><td><strong>{unitName(unit, en)}</strong><small className="admin-code">{en ? unit.nameFa : unit.nameEn || '—'}</small></td><td><StatusBadge tone={unit.isActive ? 'positive' : 'neutral'} text={unit.isActive ? l('فعال', 'Active') : l('غیرفعال', 'Inactive')} /></td><td>{unit.assignedUserCount}</td><td>{unit.activeUserCount}</td><td><div className="admin-row-actions">{has('organization.unit.manage') && <button className="button button--quiet" onClick={() => { setSelected(unit); setEditor(true) }}><Pencil />{l('ویرایش', 'Edit')}</button>}{has('organization.unit.manage') && <button className="button button--quiet" onClick={() => setConfirm(unit)}>{unit.isActive ? l('غیرفعال', 'Deactivate') : l('فعال', 'Activate')}</button>}</div></td></tr>)}</></DataTable><Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} onPageChange={value => set('page', String(value))} previousLabel={l('قبلی', 'Previous')} nextLabel={l('بعدی', 'Next')} summary={pageSummary(page, pageSize, data.totalCount, en)} /></Section>
    {editor && <div className="ui-dialog-backdrop" role="presentation"><section className="ui-confirmation admin-unit-dialog" role="dialog" aria-modal="true"><h2>{selected ? l('ویرایش واحد', 'Edit unit') : l('ایجاد واحد', 'Create unit')}</h2><form onSubmit={save} className="admin-form-grid"><FormField label={l('نام فارسی', 'Persian name')} required><input name="nameFa" defaultValue={selected?.nameFa} required /></FormField><FormField label={l('نام انگلیسی', 'English name')}><input name="nameEn" dir="ltr" defaultValue={selected?.nameEn ?? ''} /></FormField><div className="admin-form-actions"><button className="button button--primary">{l('ذخیره', 'Save')}</button><button className="button" type="button" onClick={() => setEditor(false)}>{l('انصراف', 'Cancel')}</button></div></form></section></div>}
    <ConfirmationDialog open={Boolean(confirm)} title={confirm?.isActive ? l('غیرفعال‌سازی واحد', 'Deactivate unit') : l('فعال‌سازی واحد', 'Activate unit')} description={confirm?.isActive && confirm.activeUserCount > 0 ? l('تا زمانی که کاربر فعال در این واحد وجود دارد، عملیات توسط سرور رد می‌شود.', 'The server will reject this while active users remain assigned.') : undefined} confirmLabel={l('تأیید', 'Confirm')} cancelLabel={l('انصراف', 'Cancel')} onConfirm={() => void state()} onCancel={() => setConfirm(null)} />
  </main>
}

export function IdentitySecurityAuditPage() {
  const { en, l } = useLocale()
  const [params, setParams] = useSearchParams()
  const auditKind = params.get('audit') === 'operations' ? 'operations' : 'security'
  const search = params.get('search') ?? ''
  const page = Math.max(1, Number(params.get('page') ?? 1))
  const pageSize = 25
  const [securityData, setSecurityData] = useState<Paged<Audit>>({ items: [], totalCount: 0, page, pageSize })
  const [operationalData, setOperationalData] = useState<OperationalAuditPage>({ items: [], total: 0, page, pageSize })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const endpoint = auditKind === 'operations'
      ? `/admin/operational-audit?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`
      : `/admin/security-audit?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`
    const timer = window.setTimeout(() => {
      if (!active) return
      setLoading(true)
      setError('')
      const load = auditKind === 'operations'
        ? request<OperationalAuditPage>(endpoint).then(value => { if (active) setOperationalData(value) })
        : request<Paged<Audit>>(endpoint).then(value => { if (active) setSecurityData(value) })
      void load.catch(reason => { if (active) setError(String(reason)) })
        .finally(() => { if (active) setLoading(false) })
    }, 150)
    return () => { active = false; window.clearTimeout(timer) }
  }, [auditKind, page, search])

  function set(name: string, value: string) {
    const next = new URLSearchParams(params)
    if (value) next.set(name, value); else next.delete(name)
    if (name !== 'page') next.set('page', '1')
    setParams(next)
  }
  function selectAuditKind(value: 'security' | 'operations') {
    const next = new URLSearchParams(params)
    if (value === 'operations') next.set('audit', 'operations'); else next.delete('audit')
    next.delete('search')
    next.set('page', '1')
    setParams(next)
  }

  const total = auditKind === 'operations' ? operationalData.total : securityData.totalCount
  return <main className="page admin-page">
    <PageHeader eyebrow="Audit" title={l('مرکز ممیزی سامانه', 'System audit center')}
      subtitle={l('تاریخچه تغییرناپذیر امنیت، دسترسی و عملیات کسب‌وکار', 'Immutable security, access, and business-operation history')} />
    <nav className="identity-tabs" aria-label={l('نوع ممیزی', 'Audit type')}>
      <button type="button" className={auditKind === 'security' ? 'active' : ''} aria-current={auditKind === 'security' ? 'page' : undefined} onClick={() => selectAuditKind('security')}>{l('امنیت و دسترسی', 'Security & access')}</button>
      <button type="button" className={auditKind === 'operations' ? 'active' : ''} aria-current={auditKind === 'operations' ? 'page' : undefined} onClick={() => selectAuditKind('operations')}>{l('عملیات کسب‌وکار', 'Business operations')}</button>
    </nav>
    <Section title={auditKind === 'operations' ? l('رویدادهای عملیاتی', 'Operational events') : l('رویدادهای امنیتی', 'Security events')}
      description={auditKind === 'operations'
        ? l('نمای فقط‌خواندنیِ عامل، موجودیت، عملیات و شناسه رهگیری؛ مقادیر حساس نمایش داده نمی‌شوند.', 'Read-only actor, entity, action, and trace context; sensitive values are not exposed.')
        : l('نمای فقط‌خواندنی رویدادهای هویت، نشست و تغییر دسترسی', 'Read-only identity, session, and access-change events')}>
      <FilterBar label={l('جست‌وجوی رویداد', 'Event search')}><label className="admin-search"><Search /><input value={search} onChange={event => set('search', event.target.value)} placeholder={auditKind === 'operations' ? l('نوع موجودیت، عملیات، عامل یا شناسه رهگیری', 'Entity, action, actor, or trace ID') : l('عملیات، منبع یا جزئیات', 'Action, source, or detail')} /></label></FilterBar>
      {error && <MessageStrip tone="critical">{error}</MessageStrip>}
      {auditKind === 'security' ? <DataTable
        headers={[l('زمان', 'Time'), l('عملیات', 'Action'), l('منبع', 'Source'), l('شناسه عامل', 'Actor ID'), l('شناسه هدف', 'Target ID')]}
        loading={loading} empty={!loading && securityData.items.length === 0} filtered={Boolean(search)}
        loadingLabel={l('در حال بارگذاری…', 'Loading…')} emptyTitle={l('رویدادی وجود ندارد', 'No events')} noResultTitle={l('نتیجه‌ای پیدا نشد', 'No matching events')}>
        <>{securityData.items.map(audit => <tr key={audit.id}><td>{formatBusinessDateTime(audit.createdAtUtc, en ? 'en' : 'fa-IR')}</td><td><strong className="admin-code">{audit.action}</strong>{audit.detail && <details><summary>{l('جزئیات', 'Details')}</summary><code>{audit.detail}</code></details>}</td><td>{audit.source}</td><td className="admin-code">{audit.actorUserId || '—'}</td><td className="admin-code">{audit.targetUserId || '—'}</td></tr>)}</>
      </DataTable> : <DataTable
        headers={[l('زمان', 'Time'), l('موجودیت', 'Entity'), l('عملیات', 'Action'), l('عامل / منبع', 'Actor / source'), l('شناسه رهگیری', 'Trace ID')]}
        loading={loading} empty={!loading && operationalData.items.length === 0} filtered={Boolean(search)}
        loadingLabel={l('در حال بارگذاری…', 'Loading…')} emptyTitle={l('رویداد عملیاتی وجود ندارد', 'No operational events')} noResultTitle={l('نتیجه‌ای پیدا نشد', 'No matching events')}>
        <>{operationalData.items.map(audit => <tr key={audit.id}>
          <td>{formatBusinessDateTime(audit.occurredAtUtc, en ? 'en' : 'fa-IR')}</td>
          <td><strong>{audit.entityType}</strong><small className="admin-code">{audit.entityId}</small></td>
          <td><strong className="admin-code">{audit.action}</strong>{audit.reason && <small>{audit.reason}</small>}</td>
          <td><span className="admin-code">{audit.actorId || '—'}</span><small>{audit.source}</small></td>
          <td><span className="admin-code">{audit.correlationId || '—'}</span><small>{audit.hasPreviousValues || audit.hasCurrentValues ? l('تغییر ثبت شده است', 'Change captured') : l('رویداد بدون تغییر فیلد', 'Event without field change')}</small></td>
        </tr>)}</>
      </DataTable>}
      <Pagination page={page} pageSize={pageSize} totalCount={total} onPageChange={value => set('page', String(value))}
        previousLabel={l('قبلی', 'Previous')} nextLabel={l('بعدی', 'Next')} summary={pageSummary(page, pageSize, total, en)} />
    </Section>
  </main>
}
