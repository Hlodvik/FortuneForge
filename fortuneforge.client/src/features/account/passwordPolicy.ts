export const passwordRequirements = [
  { key: 'length', label: '8 to 128 characters' },
  { key: 'uppercase', label: 'One capital letter' },
  { key: 'number', label: 'One number' },
  { key: 'symbol', label: 'One symbol' },
] as const

export function getPasswordChecks(password: string) {
  return {
    length: password.length >= 8 && password.length <= 128,
    uppercase: /[A-Z]/.test(password),
    number: /[0-9]/.test(password),
    symbol: /[\p{P}\p{S}]/u.test(password),
  }
}

export function isValidNewPassword(password: string) {
  return Object.values(getPasswordChecks(password)).every(Boolean)
}
