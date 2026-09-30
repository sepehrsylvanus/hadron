import assert from 'node:assert/strict'
import test from 'node:test'
import {
  formatIdentityProblem,
  identityPasswordPattern,
  presentIdentityProblem,
  readIdentityApiError,
  validateIdentityUserDraft,
} from './identityProblem.ts'

test('bootstrap validation exposes specific password errors instead of the generic problem title', () => {
  const translations: Record<string, string> = {
    'identity.passwordTooShort': 'حداقل ۱۰ کاراکتر لازم است.',
    'identity.passwordRequiresUpper': 'حداقل یک حرف بزرگ لازم است.',
  }
  const message = formatIdentityProblem({
    title: 'One or more validation errors occurred.',
    errors: { PasswordTooShort: ['server text'], PasswordRequiresUpper: ['server text'] },
  }, 'خطا', key => translations[key] ?? key)

  assert.equal(message, 'حداقل ۱۰ کاراکتر لازم است. حداقل یک حرف بزرگ لازم است.')
  assert.doesNotMatch(message, /One or more validation errors/)
})

test('bootstrap validation retains useful server field errors and has a safe fallback', () => {
  assert.equal(formatIdentityProblem({ errors: { firstName: ['این فیلد الزامی است.'] } }, 'خطا', key => key), 'این فیلد الزامی است.')
  assert.equal(formatIdentityProblem({}, 'خطا', key => key), 'خطا')
})

test('temporary password client rule matches the backend security policy', () => {
  const password = new RegExp(`^(?:${identityPasswordPattern})$`)
  assert.equal(password.test('654321'), false)
  assert.equal(password.test('TempPass!234'), true)
})

test('user-create validation maps RFC 7807 errors to their form fields and suppresses the generic title', () => {
  const translations: Record<string, string> = {
    'identity.correctHighlightedFields': 'موارد مشخص‌شده را اصلاح کنید.',
    'identity.fieldRequired': 'این فیلد الزامی است.',
    'identity.duplicateEmail': 'این ایمیل قبلاً ثبت شده است.',
    'identity.passwordRequiresDigit': 'رمز باید عدد داشته باشد.',
  }
  const presentation = presentIdentityProblem({
    title: 'One or more validation errors occurred.',
    status: 400,
    errors: {
      '$.firstName': ['This field is required.'],
      DuplicateEmail: ['server detail'],
      PasswordRequiresDigit: ['server detail'],
    },
  }, 'ایجاد کاربر انجام نشد.', key => translations[key] ?? key)

  assert.equal(presentation.summary, 'موارد مشخص‌شده را اصلاح کنید.')
  assert.deepEqual(presentation.fieldErrors, {
    firstName: 'این فیلد الزامی است.',
    emailAddress: 'این ایمیل قبلاً ثبت شده است.',
    temporaryPassword: 'رمز باید عدد داشته باشد.',
  })
  assert.doesNotMatch(presentation.summary, /validation errors/i)
})

test('user-create problem codes map photo and organizational-unit failures to actionable fields', () => {
  const translated: Record<string, string> = {
    'identity.correctHighlightedFields': 'اصلاح کنید.',
    'identity.photoTypeInvalid': 'فایل عکس باید JPEG، PNG یا WebP معتبر باشد.',
    'identity.organizationalUnitInactive': 'یک واحد سازمانی فعال انتخاب کنید.',
  }

  assert.deepEqual(
    presentIdentityProblem({ code: 'PHOTO_TYPE_INVALID', title: 'server title' }, 'خطا', key => translated[key] ?? key).fieldErrors,
    { photo: 'فایل عکس باید JPEG، PNG یا WebP معتبر باشد.' },
  )
  assert.deepEqual(
    presentIdentityProblem({ code: 'ORGANIZATIONAL_UNIT_INACTIVE' }, 'خطا', key => translated[key] ?? key).fieldErrors,
    { organizationalUnitId: 'یک واحد سازمانی فعال انتخاب کنید.' },
  )
})

test('RFC 7807 reader preserves structured errors and safely handles plain-text failures', async () => {
  const structured = await readIdentityApiError(new Response(JSON.stringify({
    title: 'One or more validation errors occurred.',
    errors: { userName: ['این فیلد الزامی است.'] },
    traceId: 'trace-42',
  }), { status: 400, headers: { 'content-type': 'application/problem+json' } }))
  assert.equal(structured.status, 400)
  assert.deepEqual(structured.problem.errors, { userName: ['این فیلد الزامی است.'] })
  assert.equal(structured.problem.traceId, 'trace-42')

  const plain = await readIdentityApiError(new Response('Gateway unavailable', { status: 502 }))
  assert.equal(plain.message, 'Gateway unavailable')
})

test('create-user draft validation survives wizard navigation and matches server-required fields', () => {
  const translate = (key: string) => ({
    'identity.fieldRequired': 'الزامی است.',
    'identity.passwordTooShort': 'کوتاه است.',
    'identity.passwordRequiresLower': 'حرف کوچک لازم است.',
    'identity.passwordRequiresUpper': 'حرف بزرگ لازم است.',
    'identity.passwordRequiresDigit': 'عدد لازم است.',
    'identity.passwordRequiresSymbol': 'نماد لازم است.',
    'identity.invalidEmail': 'ایمیل نامعتبر است.',
    'identity.photoRequired': 'عکس الزامی است.',
    'identity.photoSizeInvalid': 'حجم عکس نامعتبر است.',
    'identity.photoTypeInvalid': 'نوع عکس نامعتبر است.',
  })[key] ?? key

  const valid = validateIdentityUserDraft({
    userName: 'colleague.test', temporaryPassword: 'TempPass!234', firstName: 'همکار', lastName: 'تست',
    emailAddress: 'colleague@example.com', cellphoneNumber: '09120000000', organizationalUnitId: 'unit-1',
    photo: { size: 150_000, type: 'image/jpeg' },
  }, translate)
  assert.deepEqual(valid, {})

  const invalid = validateIdentityUserDraft({
    userName: '', temporaryPassword: '654321', firstName: '', lastName: '', emailAddress: 'bad',
    cellphoneNumber: '', organizationalUnitId: '', photo: null,
  }, translate)
  assert.equal(invalid.userName, 'الزامی است.')
  assert.match(invalid.temporaryPassword ?? '', /کوتاه است.*حرف کوچک لازم است.*حرف بزرگ لازم است.*نماد لازم است/)
  assert.equal(invalid.emailAddress, 'ایمیل نامعتبر است.')
  assert.equal(invalid.photo, 'عکس الزامی است.')
})
