export type GuestMeetingCredentialKind = 'invite' | 'ticket'
export type GuestMeetingRequestScope = 'preview' | 'redeem' | 'status' | 'join'

export type GuestMeetingRequestToken = Readonly<{
  credentialKind: GuestMeetingCredentialKind
  credentialKey: string
  generation: number
  scope: GuestMeetingRequestScope
  sequence: number
}>

/**
 * Prevents an older guest request from mutating a newer invitation/ticket flow.
 * A sequence is tracked per operation so a slow poll cannot replace a newer poll,
 * while unrelated operations for the same credential may still complete normally.
 */
export class GuestMeetingRequestGate {
  private credential: { kind: GuestMeetingCredentialKind; key: string } | null = null
  private generation = 0
  private readonly scopeSequences = new Map<GuestMeetingRequestScope, number>()

  selectCredential(kind: GuestMeetingCredentialKind, key: string) {
    this.generation += 1
    this.credential = { kind, key }
    this.scopeSequences.clear()
    return this.generation
  }

  begin(scope: GuestMeetingRequestScope, kind: GuestMeetingCredentialKind, key: string) {
    if (!this.matches(kind, key)) return null
    const sequence = (this.scopeSequences.get(scope) ?? 0) + 1
    this.scopeSequences.set(scope, sequence)
    return {
      credentialKind: kind,
      credentialKey: key,
      generation: this.generation,
      scope,
      sequence,
    } satisfies GuestMeetingRequestToken
  }

  isCurrent(token: GuestMeetingRequestToken | null): token is GuestMeetingRequestToken {
    return token !== null
      && token.generation === this.generation
      && this.matches(token.credentialKind, token.credentialKey)
      && this.scopeSequences.get(token.scope) === token.sequence
  }

  clearIfCurrent(token: GuestMeetingRequestToken | null) {
    if (!this.isCurrent(token)) return false
    return this.clearCredentialIfCurrent(token.credentialKind, token.credentialKey)
  }

  clearCredentialIfCurrent(kind: GuestMeetingCredentialKind, key: string) {
    if (!this.matches(kind, key)) return false
    this.generation += 1
    this.credential = null
    this.scopeSequences.clear()
    return true
  }

  matches(kind: GuestMeetingCredentialKind, key: string) {
    return this.credential?.kind === kind && this.credential.key === key
  }
}

/** Removes guest credentials without discarding the current route or unrelated query state. */
export function scrubGuestMeetingCredentialFromUrl(value: string | URL) {
  const url = value instanceof URL ? new URL(value.toString()) : new URL(value, 'https://hadron.invalid')
  url.hash = ''
  url.searchParams.delete('invite')
  return `${url.pathname}${url.search}`
}
