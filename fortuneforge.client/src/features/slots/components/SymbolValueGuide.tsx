import type { ComponentProps } from 'react'
import { SymbolValueGuide as SharedSymbolValueGuide } from '@fortuneforge/slot-client-core/components/SymbolValueGuide'

export function SymbolValueGuide(props: ComponentProps<typeof SharedSymbolValueGuide>) {
  const triggerContent = props.triggerContent ?? (props.theme === 'midnight-vault'
    ? <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" width="24" height="24"
        fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
        <circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7h.01" />
      </svg>
    : undefined)
  return <SharedSymbolValueGuide {...props} triggerContent={triggerContent} />
}
