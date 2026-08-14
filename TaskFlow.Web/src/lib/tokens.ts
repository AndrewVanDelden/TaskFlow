// Nocturne design tokens (Epic 3.1). Tailwind v4 has no theme/config file here (project
// constraint: "Tailwind v4 utilities only, no custom theme/config"), so these are plain string
// constants — the single source every component imports instead of hand-typing hex values,
// mirroring the existing lib/styles.ts pattern for status colors. Values must stay literal
// strings (not built via template-literal interpolation): Tailwind's class scanner matches
// literal text in source files, so a dynamically-built class name would never get its CSS
// generated.

export const bgPage = 'bg-[#161826]'
export const bgSurface = 'bg-[#232532]'
export const borderDivider = 'border-[rgba(233,233,237,0.16)]'
export const borderAccent = 'border-[#9184d9]'
export const textPrimary = 'text-[#e9e9ed]'
export const textAccent = 'text-[#9184d9]'
export const textAccent200 = 'text-[#e7e5fe]'
export const textAccent300 = 'text-[#d2cefd]'
export const bgAccent400 = 'bg-[#b5abfc]'
export const bgAccent500 = 'bg-[#968ae0]'
export const bgAccent700 = 'bg-[#5d5294]'
export const bgAccent800 = 'bg-[#423a6a]'
export const textNeutral300 = 'text-[#cfd3e5]'
export const textNeutral400 = 'text-[#b2b6ca]'
export const textNeutral500 = 'text-[#9397ab]'
export const placeholderNeutral500 = 'placeholder-[#9397ab]'
export const textNeutral600 = 'text-[#75798c]'

// Global rule (design system): every interactive element gets this accent focus-visible ring,
// never the browser default blue — shared here so it can't drift between consumers (Button,
// form fields, etc.) the way Login's inputs once did with a stale focus:ring-blue-500.
export const focusRingAccent =
  'outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#9184d9]'

type ComputedColorProperty = 'backgroundColor' | 'color'

interface DesignToken {
  name: string
  className: string
  property: ComputedColorProperty
  expected: string
}

// Divider/border is intentionally excluded here — see tokens.test.tsx for why.
export const designTokens: DesignToken[] = [
  { name: 'page background', className: bgPage, property: 'backgroundColor', expected: 'rgb(22, 24, 38)' },
  { name: 'surface (card) background', className: bgSurface, property: 'backgroundColor', expected: 'rgb(35, 37, 50)' },
  { name: 'text', className: textPrimary, property: 'color', expected: 'rgb(233, 233, 237)' },
  { name: 'accent', className: textAccent, property: 'color', expected: 'rgb(145, 132, 217)' },
  { name: 'accent-200', className: textAccent200, property: 'color', expected: 'rgb(231, 229, 254)' },
  { name: 'accent-300', className: textAccent300, property: 'color', expected: 'rgb(210, 206, 253)' },
  { name: 'accent-400', className: bgAccent400, property: 'backgroundColor', expected: 'rgb(181, 171, 252)' },
  { name: 'accent-500', className: bgAccent500, property: 'backgroundColor', expected: 'rgb(150, 138, 224)' },
  { name: 'accent-700', className: bgAccent700, property: 'backgroundColor', expected: 'rgb(93, 82, 148)' },
  { name: 'accent-800', className: bgAccent800, property: 'backgroundColor', expected: 'rgb(66, 58, 106)' },
  { name: 'neutral-300', className: textNeutral300, property: 'color', expected: 'rgb(207, 211, 229)' },
  { name: 'neutral-400', className: textNeutral400, property: 'color', expected: 'rgb(178, 182, 202)' },
  { name: 'neutral-500', className: textNeutral500, property: 'color', expected: 'rgb(147, 151, 171)' },
  { name: 'neutral-600', className: textNeutral600, property: 'color', expected: 'rgb(117, 121, 140)' },
]
