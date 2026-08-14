import type { ButtonHTMLAttributes } from 'react'
import { borderAccent, textAccent, textNeutral500, focusRingAccent } from '../../lib/tokens'

export type ButtonVariant = 'primary' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
}

const disabledClasses = 'disabled:opacity-50 disabled:cursor-not-allowed'

const variantClasses: Record<ButtonVariant, string> = {
  primary: `border ${borderAccent} ${textAccent} hover:bg-[#9184d9]/15 rounded-lg`,
  ghost: `${textNeutral500} hover:bg-white/5 rounded-lg`,
}

export function Button({ variant = 'primary', className = '', ...props }: ButtonProps) {
  const classes = [variantClasses[variant], focusRingAccent, disabledClasses, className].filter(Boolean).join(' ')

  return <button className={classes} {...props} />
}
