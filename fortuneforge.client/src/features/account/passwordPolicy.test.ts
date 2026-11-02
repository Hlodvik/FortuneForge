import { describe, expect, it } from 'vitest'
import { getPasswordChecks, isValidNewPassword } from './passwordPolicy'

describe('new password requirements', () => {
  it('requires the length, a capital letter, a number, and a symbol', () => {
    expect(getPasswordChecks('simple')).toEqual({ length: false, uppercase: false, number: false, symbol: false })
    expect(getPasswordChecks('LongEnough1')).toEqual({ length: true, uppercase: true, number: true, symbol: false })
    expect(isValidNewPassword('LongEnough1!')).toBe(true)
    expect(isValidNewPassword('longenough1!')).toBe(false)
    expect(isValidNewPassword('LongEnough!')).toBe(false)
    expect(isValidNewPassword('LongEnough1 ')).toBe(false)
    expect(isValidNewPassword(`A1!${'a'.repeat(126)}`)).toBe(false)
  })

  it('accepts punctuation and symbols without counting letters or control characters as symbols', () => {
    expect(isValidNewPassword('Passw0rd€')).toBe(true)
    expect(isValidNewPassword('Passw0rdé')).toBe(false)
    expect(isValidNewPassword('Passw0rd\u0001')).toBe(false)
  })
})
