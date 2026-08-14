import type { ButtonHTMLAttributes } from 'react'
import { borderAccent, textAccent, textNeutral500 } from '../../lib/tokens'

export type ButtonVariant = 'primary' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
}

// Accent focus-visible ring, never the browser default — outline-none suppresses the default
// unconditionally, and the focus-visible utilities reinstate a visible accent ring only when the
// browser's own focus-visible heuristic would show one.
const focusRingClasses =
  'outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#9184d9]'

const variantClasses: Record<ButtonVariant, string> = {
  primary: `border ${borderAccent} ${textAccent} hover:bg-[#9184d9]/15 rounded-lg`,
  ghost: `${textNeutral500} hover:bg-white/5 rounded-lg`,
}

export function Button({ variant = 'primary', className = '', ...props }: ButtonProps) {
  const classes = [variantClasses[variant], focusRingClasses, className].filter(Boolean).join(' ')

  return <button className={classes} {...props} />
}
