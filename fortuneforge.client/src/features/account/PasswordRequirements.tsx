import { getPasswordChecks, passwordRequirements } from './passwordPolicy'
import './passwordPolicy.css'

export function PasswordRequirements({ password, id }: { password: string; id: string }) {
  const checks = getPasswordChecks(password)

  return (
    <ul className="password-requirements" id={id} aria-label="Password requirements">
      {passwordRequirements.map(({ key, label }) => (
        <li className={checks[key] ? 'is-met' : ''} key={key} aria-label={`${checks[key] ? 'Met' : 'Not met'}: ${label}`}>
          <span aria-hidden="true">{checks[key] ? '✓' : '○'}</span>{label}
        </li>
      ))}
    </ul>
  )
}
