import type { CSSProperties } from 'react'
import { SparkleIcon, StarFourIcon } from '@phosphor-icons/react'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'
import { bgSurface, borderAccent, focusRingAccent, textAccent200, textAccent300 } from '../lib/tokens'

// Epic 3.1 Sprint 4 (U4.4) - exact dx/dy/icon/size/color-token values copied verbatim from the
// design handoff's markup (see epic doc's "Implementation decisions" table under Sprint 4), not
// re-derived. Spark #5 substitutes textAccent200 for the design's bare accent-100, which has no
// equivalent in this codebase's locked token set - a recorded, deliberate downgrade.
type SparkIcon = typeof SparkleIcon

interface SparkSpec {
  dx: string
  dy: string
  icon: SparkIcon
  size: number
  colorToken: string
}

const SPARKS: SparkSpec[] = [
  { dx: '-54px', dy: '-42px', icon: SparkleIcon, size: 13, colorToken: textAccent300 },
  { dx: '50px', dy: '-46px', icon: SparkleIcon, size: 11, colorToken: textAccent200 },
  { dx: '60px', dy: '22px', icon: StarFourIcon, size: 12, colorToken: textAccent300 },
  { dx: '-60px', dy: '18px', icon: SparkleIcon, size: 10, colorToken: textAccent200 },
  { dx: '-22px', dy: '-62px', icon: StarFourIcon, size: 10, colorToken: textAccent200 },
  { dx: '28px', dy: '58px', icon: SparkleIcon, size: 12, colorToken: textAccent300 },
  { dx: '-32px', dy: '54px', icon: SparkleIcon, size: 11, colorToken: textAccent200 },
  { dx: '14px', dy: '-60px', icon: StarFourIcon, size: 9, colorToken: textAccent300 },
]

// Purely presentational - no internal state, no hook access to intake/application state (mirrors
// TaskCardView taking fully-resolved props). The caller (IngestDocument) owns eligibility and
// passes busy={intake.stage === 'starting'}.
export function TailorButton({
  onClick,
  disabled,
  busy,
}: {
  onClick: () => void
  disabled: boolean
  busy: boolean
}) {
  const prefersReducedMotion = usePrefersReducedMotion()
  const animate = busy && !prefersReducedMotion

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`relative w-[184px] h-[184px] flex flex-col items-center justify-center gap-2 rounded-2xl ${bgSurface} border ${borderAccent} ${focusRingAccent} disabled:opacity-50 disabled:cursor-not-allowed`}
    >
      {animate && (
        <div
          aria-hidden="true"
          className="absolute inset-0 m-auto w-16 h-16 rounded-full bg-[#9184d9]/40 animate-[goGlow_0.7s_ease-out]"
        />
      )}

      {animate &&
        SPARKS.map((spark, index) => {
          const Icon = spark.icon
          return (
            <span
              key={index}
              data-testid="tailor-spark"
              aria-hidden="true"
              className="absolute left-1/2 top-1/2 animate-[sparkFly_0.8s_ease-out_forwards]"
              style={{ '--dx': spark.dx, '--dy': spark.dy } as CSSProperties}
            >
              <Icon aria-hidden="true" size={spark.size} className={spark.colorToken} />
            </span>
          )
        })}

      <SparkleIcon aria-hidden="true" size={28} className={animate ? 'animate-spin' : ''} />
      <span className="text-sm font-medium">{busy ? 'Tailoring…' : 'Start tailoring'}</span>
    </button>
  )
}
