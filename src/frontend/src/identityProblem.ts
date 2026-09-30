export type IdentityProblem = {
  type?: unknown
  title?: unknown
  status?: unknown
  detail?: unknown
  instance?: unknown
  code?: unknown
  traceId?: unknown
  errors?: unknown
}

export type IdentityUserField =
  | 'userName'
  | 'temporaryPassword'
  | 'firstName'
  | 'lastName'
  | 'emailAddress'
  | 'cellphoneNumber'
  | 'organizationalUnitId'
  | 'photo'
  | 'roleIds'

export type IdentityProblemPresentation = {
  summary: string
  fieldErrors: Partial<Record<IdentityUserField, string>>
  code?: string
  traceId?: string
}

export type IdentityUserDraft = {
  userName: string
  temporaryPassword: string
  firstName: string
  lastName: string
  emailAddress: string
  cellphoneNumber: string
  organizationalUnitId: string
  photo: { size: number; type: string } | null
}

type ErrorDefinition = { translationKey: string; field?: IdentityUserField }

export const identityPasswordPattern = String.raw`(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{10,}`

const localizedErrors: Record<string, ErrorDefinition> = {
  PasswordTooShort: { translationKey: 'identity.passwordTooShort', field: 'temporaryPassword' },
  PasswordRequiresNonAlphanumeric: { translationKey: 'identity.passwordRequiresSymbol', field: 'temporaryPassword' },
  PasswordRequiresLower: { translationKey: 'identity.passwordRequiresLower', field: 'temporaryPassword' },
  PasswordRequiresUpper: { translationKey: 'identity.passwordRequiresUpper', field: 'temporaryPassword' },
  PasswordRequiresDigit: { translationKey: 'identity.passwordRequiresDigit', field: 'temporaryPassword' },
  DuplicateUserName: { translationKey: 'identity.duplicateUserName', field: 'userName' },
  DuplicateEmail: { translationKey: 'identity.duplicateEmail', field: 'emailAddress' },
  InvalidUserName: { translationKey: 'identity.invalidUserName', field: 'userName' },
  InvalidEmail: { translationKey: 'identity.invalidEmail', field: 'emailAddress' },
  PHOTO_REQUIRED: { translationKey: 'identity.photoRequired', field: 'photo' },
  PROFILE_PHOTO_REQUIRED: { translationKey: 'identity.photoRequired', field: 'photo' },
  PHOTO_SIZE_INVALID: { translationKey: 'identity.photoSizeInvalid', field: 'photo' },
  PHOTO_TYPE_INVALID: { translationKey: 'identity.photoTypeInvalid', field: 'photo' },
  PHOTO_CONTENT_TYPE_MISMATCH: { translationKey: 'identity.photoContentMismatch', field: 'photo' },
  ORGANIZATIONAL_UNIT_INACTIVE: { translationKey: 'identity.organizationalUnitInactive', field: 'organizationalUnitId' },
}

const fieldAliases: Record<string, IdentityUserField> = {
  username: 'userName',
  temporarypassword: 'temporaryPassword',
  password: 'temporaryPassword',
  firstname: 'firstName',
  lastname: 'lastName',
  email: 'emailAddress',
  emailaddress: 'emailAddress',
  cellphone: 'cellphoneNumber',
  cellphonenumber: 'cellphoneNumber',
  organizationalunit: 'organizationalUnitId',
  organizationalunitid: 'organizationalUnitId',
  photo: 'photo',
  profilephoto: 'photo',
  roleid: 'roleIds',
  roleids: 'roleIds',
}

const genericValidationTitles = new Set([
  'one or more validation errors occurred.',
  'one or more validation errors occurred',
  'validation failed.',
  'validation failed',
])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function asText(value: unknown) {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined
}

function errorValues(value: unknown): string[] {
  if (Array.isArray(value)) return value.flatMap(errorValues)
  const message = asText(value)
  return message ? [message] : []
}

function unique(messages: string[]) {
  return [...new Set(messages.filter(Boolean))]
}

function identityField(key: string): IdentityUserField | undefined {
  const leaf = key.replace(/^\$\.?/, '').split(/[.[\]]/).filter(Boolean).at(-1)
  return leaf ? fieldAliases[leaf.replace(/[^a-z0-9]/gi, '').toLocaleLowerCase()] : undefined
}

function localizeServerMessage(message: string, translate: (key: string) => string) {
  const normalized = message.trim().toLocaleLowerCase()
  if (normalized === 'this field is required.' || normalized === 'the field is required.' || normalized === 'این فیلد الزامی است.') {
    const localized = translate('identity.fieldRequired')
    return localized === 'identity.fieldRequired' ? message.trim() : localized
  }
  return message.trim()
}

function rawProblemMessage(body: IdentityProblem, fallback: string) {
  const errors = isRecord(body.errors) ? Object.values(body.errors).flatMap(errorValues) : []
  return unique([...errors, asText(body.detail) ?? '', asText(body.title) ?? '']).join(' ') || fallback
}

export class IdentityApiError extends Error {
  readonly status: number
  readonly problem: IdentityProblem

  constructor(status: number, problem: IdentityProblem) {
    super(rawProblemMessage(problem, `HTTP ${status}`))
    this.name = 'IdentityApiError'
    this.status = status
    this.problem = problem
  }

  override toString() { return this.message }
}

/** Reads RFC 7807 / ValidationProblemDetails without assuming every failed response is valid JSON. */
export async function readIdentityApiError(response: Response) {
  const raw = await response.text().catch(() => '')
  let problem: IdentityProblem = {}
  if (raw.trim()) {
    try {
      const parsed: unknown = JSON.parse(raw)
      problem = isRecord(parsed) ? parsed : { title: raw.trim() }
    } catch {
      problem = { title: raw.trim() }
    }
  }
  return new IdentityApiError(response.status, problem)
}

export function presentIdentityProblem(
  body: IdentityProblem,
  fallback: string,
  translate: (key: string) => string,
): IdentityProblemPresentation {
  const buckets = new Map<IdentityUserField, string[]>()
  const unbound: string[] = []
  const add = (field: IdentityUserField | undefined, message: string) => {
    if (!field) { unbound.push(message); return }
    buckets.set(field, [...(buckets.get(field) ?? []), message])
  }

  if (isRecord(body.errors)) {
    Object.entries(body.errors).forEach(([key, values]) => {
      const definition = localizedErrors[key]
      if (definition) {
        add(definition.field, translate(definition.translationKey))
        return
      }
      const field = identityField(key)
      errorValues(values).forEach(message => add(field, localizeServerMessage(message, translate)))
    })
  }

  const code = asText(body.code)
  const codeDefinition = code ? localizedErrors[code] : undefined
  if (codeDefinition) add(codeDefinition.field, translate(codeDefinition.translationKey))

  const fieldErrors = Object.fromEntries(
    [...buckets.entries()].map(([field, messages]) => [field, unique(messages).join(' ')]),
  ) as Partial<Record<IdentityUserField, string>>

  let summary: string
  if (Object.keys(fieldErrors).length > 0) {
    summary = translate('identity.correctHighlightedFields')
  } else if (unbound.length > 0) {
    summary = unique(unbound).join(' ')
  } else if (asText(body.detail)) {
    summary = asText(body.detail)!
  } else {
    const title = asText(body.title)
    summary = title && !genericValidationTitles.has(title.toLocaleLowerCase()) ? title : fallback
  }

  return { summary, fieldErrors, code, traceId: asText(body.traceId) }
}

export function formatIdentityProblem(
  body: IdentityProblem,
  fallback: string,
  translate: (key: string) => string,
) {
  const presentation = presentIdentityProblem(body, fallback, translate)
  const fieldMessages = unique(Object.values(presentation.fieldErrors))
  return fieldMessages.length > 0 ? fieldMessages.join(' ') : presentation.summary
}

export function validateIdentityUserDraft(
  draft: IdentityUserDraft,
  translate: (key: string) => string,
): Partial<Record<IdentityUserField, string>> {
  const errors: Partial<Record<IdentityUserField, string>> = {}
  const required = [
    'userName', 'temporaryPassword', 'firstName', 'lastName', 'emailAddress',
    'cellphoneNumber', 'organizationalUnitId',
  ] as const satisfies readonly (keyof IdentityUserDraft)[]
  required.forEach(field => {
    const value = draft[field]
    if (typeof value === 'string' && !value.trim()) errors[field as IdentityUserField] = translate('identity.fieldRequired')
  })

  if (draft.temporaryPassword) {
    const passwordMessages: string[] = []
    if (draft.temporaryPassword.length < 10) passwordMessages.push(translate('identity.passwordTooShort'))
    if (!/[a-z]/.test(draft.temporaryPassword)) passwordMessages.push(translate('identity.passwordRequiresLower'))
    if (!/[A-Z]/.test(draft.temporaryPassword)) passwordMessages.push(translate('identity.passwordRequiresUpper'))
    if (!/\d/.test(draft.temporaryPassword)) passwordMessages.push(translate('identity.passwordRequiresDigit'))
    if (!/[^A-Za-z0-9]/.test(draft.temporaryPassword)) passwordMessages.push(translate('identity.passwordRequiresSymbol'))
    if (passwordMessages.length) errors.temporaryPassword = unique(passwordMessages).join(' ')
  }

  if (draft.emailAddress && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(draft.emailAddress)) {
    errors.emailAddress = translate('identity.invalidEmail')
  }

  if (!draft.photo) {
    errors.photo = translate('identity.photoRequired')
  } else if (draft.photo.size < 1 || draft.photo.size > 2 * 1024 * 1024) {
    errors.photo = translate('identity.photoSizeInvalid')
  } else if (draft.photo.type && !['image/jpeg', 'image/png', 'image/webp'].includes(draft.photo.type.toLocaleLowerCase())) {
    errors.photo = translate('identity.photoTypeInvalid')
  }

  return errors
}
