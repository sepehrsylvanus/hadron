import type { SetupValues } from './TradingProjectSetupView'

export const emptySetup: SetupValues = { titleFa: '', startDate: '', customerId: '', businessCardId: '', responsibleUserId: '', notes: '' }

/** Explicit allowlist: no status, priority, generated number or facts from later steps. */
export function projectSetupPayload(form: SetupValues, startDate: string) {
  return {
    titleFa: form.titleFa.trim(), startDate,
    customerId: form.customerId || null, businessCardId: form.businessCardId || null,
    responsibleUserId: form.responsibleUserId, notes: form.notes.trim() || null,
  }
}
