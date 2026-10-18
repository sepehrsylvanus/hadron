import { FormEvent, ReactNode, createContext, useCallback, useContext, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import officialHadronLogo from './assets/brand/hadron-logo-red.svg'
import hadronCompanionThinking from './assets/brand/character/hadron-companion-thinking.webp'
import hadronCompanionWelcome from './assets/brand/character/hadron-companion-welcome.webp'
import { apiRoot } from './apiClient'
import { loadInitialAuthentication } from './authSessionSupport'
import { normalizeLanguage, type SupportedLanguage } from './i18n'
import { formatIdentityProblem, identityPasswordPattern } from './identityProblem'

export type CurrentUser = {
  id: string; userName: string; fullName: string; emailAddress: string; cellphoneNumber: string
  organizationalUnit: { id: string; nameFa: string; nameEn?: string | null }
  photoUrl: string; mustChangePassword: boolean; isSystemAdministrator: boolean; dataScopes: { scopeType: string; scopeValue?: string | null }[]; permissions: string[]
}

type AuthValue = {
  user: CurrentUser
  has: (permission: string) => boolean
  refresh: () => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthValue | null>(null)

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('AuthContext is not available')
  return value
}

// eslint-disable-next-line react-refresh/only-export-components
export function useOptionalAuth() { return useContext(AuthContext) }

export function AuthProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)
  const [initialized, setInitialized] = useState(true)
  const [serviceUnavailable, setServiceUnavailable] = useState(false)
  const [bootstrapAttempt, setBootstrapAttempt] = useState(0)
  const [error, setError] = useState('')
  const refresh = useCallback(async () => {
    const response = await fetch(`${apiRoot}/auth/me`)
    if (response.ok) setUser(await response.json())
    else if (response.status === 401) setUser(null)
  }, [])
  useEffect(() => {
    let active = true
    void loadInitialAuthentication<CurrentUser>(apiRoot).then(result => {
      if (!active) return
      if (result.kind === 'unavailable') {
        setServiceUnavailable(true)
        return
      }
      setInitialized(result.kind !== 'setup-required')
      setUser(result.kind === 'authenticated' ? result.user : null)
    }).finally(() => { if (active) setLoading(false) })
    const expired = () => setUser(null); window.addEventListener('hadron:authentication-required', expired)
    return () => { active = false; window.removeEventListener('hadron:authentication-required', expired) }
  }, [bootstrapAttempt])

  if (loading) return <AuthFrame mood="thinking"><div className="identity-state" role="status" aria-live="polite">{t('common.loading')}</div></AuthFrame>
  if (serviceUnavailable) return <AuthFrame mood="thinking"><div className="identity-state" role="alert"><h1>{t('identity.serviceUnavailable')}</h1><p>{t('identity.apiUnavailable')}</p><button className="primary" onClick={() => { setLoading(true); setServiceUnavailable(false); setError(''); setBootstrapAttempt(value => value + 1) }}>{t('identity.retryConnection')}</button></div></AuthFrame>
  if (!initialized) return <BootstrapForm onReady={() => { setInitialized(true); setError('') }} error={error} setError={setError} />
  if (!user) return <LoginForm onLogin={setUser} error={error} setError={setError} />
  if (user.mustChangePassword) return <PasswordChange onDone={() => setUser(null)} />

  const value: AuthValue = { user, has: permission => user.isSystemAdministrator || user.permissions.includes(permission), refresh,
    logout: async () => { await fetch(`${apiRoot}/auth/logout`, { method: 'POST' }); setUser(null) } }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

function AuthFrame({ children, mood = 'welcome' }: { children: ReactNode; mood?: 'welcome' | 'thinking' }) {
  const { i18n, t } = useTranslation()
  const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
  const companion = mood === 'thinking' ? hadronCompanionThinking : hadronCompanionWelcome
  return <main className="identity-entry"><section className="identity-card"><div className="identity-entry-tools"><label className="language-switcher"><span>{t('language.label')}</span><select value={language} aria-label={t('language.selectAria')} onChange={event => void i18n.changeLanguage(event.target.value as SupportedLanguage)}><option value="fa-IR">{t('language.persian')}</option><option value="en">{t('language.english')}</option></select></label></div><aside className="identity-brand-stage" aria-hidden="true"><img className="identity-brand-logo" src={officialHadronLogo} alt="" /><img className="identity-companion" src={companion} alt="" width="320" height="400" fetchPriority={mood === 'welcome' ? 'high' : 'auto'} /></aside><div className="identity-entry-content">{children}</div></section></main>
}

function LoginForm({ onLogin, error, setError }: { onLogin: (user: CurrentUser) => void; error: string; setError: (value: string) => void }) {
  const { t } = useTranslation(); const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(''); const form = new FormData(event.currentTarget)
    try {
      const response = await fetch(`${apiRoot}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: form.get('userName'), password: form.get('password') }) })
      if (response.ok) onLogin(await response.json()); else { const body = await response.json().catch(() => ({})); setError(body.title ?? t('identity.loginFailed')) }
    } catch { setError(t('identity.apiUnavailable')) } finally { setBusy(false) }
  }
  return <AuthFrame><p className="eyebrow">{t('identity.secureAccess')}</p><h1>{t('identity.login')}</h1><p>{t('identity.loginHint')}</p>{error && <div className="message error">{error}</div>}<form className="identity-form" onSubmit={submit}><label>{t('identity.userName')}<input name="userName" autoComplete="username" required autoFocus /></label><label>{t('identity.password')}<input name="password" type="password" autoComplete="current-password" required /></label><button className="primary" disabled={busy}>{busy ? t('common.loading') : t('identity.login')}</button></form></AuthFrame>
}

function BootstrapForm({ onReady, error, setError }: { onReady: () => void; error: string; setError: (value: string) => void }) {
  const { t } = useTranslation(); const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget); const token = String(data.get('bootstrapToken')); data.delete('bootstrapToken')
    try {
      const response = await fetch(`${apiRoot}/auth/bootstrap`, { method: 'POST', headers: { 'X-Hadron-Bootstrap-Token': token }, body: data })
      if (response.ok) onReady(); else { const body = await response.json().catch(() => ({})); setError(formatIdentityProblem(body, t('common.error'), key => t(key))) }
    } catch { setError(t('identity.apiUnavailable')) } finally { setBusy(false) }
  }
  return <AuthFrame><p className="eyebrow">{t('identity.initialSetup')}</p><h1>{t('identity.createFirstAdmin')}</h1><p>{t('identity.bootstrapHint')}</p>{error && <div className="message error">{error}</div>}<form className="identity-form two-column" onSubmit={submit} encType="multipart/form-data"><label>{t('identity.bootstrapToken')}<input name="bootstrapToken" type="password" required /></label><label>{t('identity.userName')}<input name="userName" required /></label><label>{t('identity.firstName')}<input name="firstName" required /></label><label>{t('identity.lastName')}<input name="lastName" required /></label><label>{t('identity.email')}<input name="emailAddress" type="email" required /></label><label>{t('identity.cellphone')}<input name="cellphoneNumber" required /></label><label>{t('identity.orgUnitFa')}<input name="organizationalUnitNameFa" required /></label><label>{t('identity.orgUnitEn')}<input name="organizationalUnitNameEn" /></label><label>{t('identity.temporaryPassword')}<input name="temporaryPassword" type="password" minLength={10} pattern={identityPasswordPattern} aria-describedby="bootstrap-password-rules" required /><small id="bootstrap-password-rules">{t('identity.passwordRules')}</small></label><label>{t('identity.personnelPhoto')}<input name="photo" type="file" accept="image/jpeg,image/png,image/webp" required /></label><button className="primary" disabled={busy}>{busy ? t('common.loading') : t('identity.initialize')}</button></form></AuthFrame>
}

function PasswordChange({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation(); const [error, setError] = useState(''); const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setBusy(true); const data = new FormData(event.currentTarget); const password = String(data.get('newPassword')); if (password !== data.get('confirmPassword')) { setError(t('identity.passwordMismatch')); setBusy(false); return }
    try { const response = await fetch(`${apiRoot}/auth/change-password`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ currentPassword: data.get('currentPassword'), newPassword: password }) }); if (response.ok) onDone(); else { const body = await response.json().catch(() => ({})); setError(body.title ?? Object.values(body.errors ?? {}).flat().join(' ') ?? t('common.error')) } }
    catch { setError(t('identity.apiUnavailable')) } finally { setBusy(false) } }
  return <AuthFrame><h1>{t('identity.changePasswordRequired')}</h1><p>{t('identity.changePasswordHint')}</p>{error && <div className="message error">{error}</div>}<form className="identity-form" onSubmit={submit}><label>{t('identity.currentPassword')}<input name="currentPassword" type="password" required /></label><label>{t('identity.newPassword')}<input name="newPassword" type="password" required /></label><label>{t('identity.confirmPassword')}<input name="confirmPassword" type="password" required /></label><button className="primary" disabled={busy}>{t('identity.changePassword')}</button></form></AuthFrame>
}
