import { textNeutral500 } from '../../lib/tokens'

interface ColumnHeaderProps {
  label: string
  count: number
}

export function ColumnHeader({ label, count }: ColumnHeaderProps) {
  return (
    <div className="flex items-center justify-between">
      <h2 className={`text-[11px] font-semibold uppercase tracking-wide ${textNeutral500}`}>{label}</h2>
      <span className={textNeutral500}>{count}</span>
    </div>
  )
}
