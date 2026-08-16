import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { useParams } from 'react-router-dom'
import { useIngestion } from '../hooks/useIngestion'
import { useBaseResumeCapture } from '../hooks/useBaseResumeCapture'
import { useBaseResumeReuse } from '../hooks/useBaseResumeReuse'
import { useIntakeFlow } from '../hooks/useIntakeFlow'
import { TailorButton } from '../components/TailorButton'
import { MarkdownPreview } from '../components/MarkdownPreview'
import { formatDate } from '../lib/formatting'
import { stepForStage } from '../lib/intakeSteps'
import { bgSurface, borderDivider, textAccent200, textNeutral400, textNeutral500 } from '../lib/tokens'

const STEPS: Array<{ step: 1 | 2 | 3; label: string }> = [
  { step: 1, label: '1 Provide' },
  { step: 2, label: '2 Review' },
  { step: 3, label: '3 Generate' },
]

const fileInputClasses =
  'text-xs text-slate-400 file:mr-3 file:py-1 file:px-3 file:rounded file:border file:border-slate-700 ' +
  'file:bg-slate-800 file:text-white file:text-xs focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 rounded'

// Reads a picked file's contents (and name) as text. Shared by the guided job-posting input and
// the generic document input below, so the read-to-text behavior isn't duplicated between them.
async function readFileAsText(e: ChangeEvent<HTMLInputElement>): Promise<{ text: string; name: string } | null> {
  const file = e.target.files?.[0]
  return file ? { text: await file.text(), name: file.name } : null
}

// The guided job-application flow (Sprint 6): paste a job posting + a base resume, parse the
// posting, then start tailoring, driven entirely by useIntakeFlow's stage machine. The original
// Epic-2 generic-document flow (useIngestion) is kept, unchanged, under a collapsed <details> below
// it - it is a real, separately-tested capability, not something Sprint 6 was asked to remove.
export function IngestDocument() {
  // The id lives in the route (App.tsx's /ingest/:sessionId route), not component state, so it
  // survives an unmount/remount and every save/parse/assemble call targets the same session
  // (PR #40 review finding #7). Non-null assertion is safe: this component only ever renders
  // behind that route, which cannot match without a :sessionId segment.
  const { sessionId } = useParams<{ sessionId: string }>()
  const ingestionSessionId = sessionId!

  const intake = useIntakeFlow(ingestionSessionId)
  const reuse = useBaseResumeReuse()
  const baseResumeCapture = useBaseResumeCapture()

  // T6.5: move focus to the current stage's primary heading on every stage transition, so a
  // keyboard/screen-reader user is never left focused on a control that just disappeared.
  const stageHeadingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    stageHeadingRef.current?.focus()
  }, [intake.stage])

  const onJobPostingFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const picked = await readFileAsText(e)
    if (picked) intake.setJobPostingText(picked.text)
  }

  const jobPostingEditable = intake.stage === 'provide' || intake.stage === 'parsing'
  const baseResumeEditable = intake.stage !== 'starting' && intake.stage !== 'building'

  // Generic document flow (Epic 2, kept verbatim behaviorally).
  const [genericText, setGenericText] = useState('')
  const [genericSourceName, setGenericSourceName] = useState('')
  const generic = useIngestion()

  const onGenericFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const picked = await readFileAsText(e)
    if (picked) {
      setGenericText(picked.text)
      setGenericSourceName(picked.name)
    }
  }

  const currentStep = stepForStage(intake.stage)
  const trimmedBaseResumeText = intake.baseResumeText.trim()

  return (
    <div className="max-w-2xl mx-auto p-6 text-white">
      <h1 ref={stageHeadingRef} tabIndex={-1} className="text-lg font-bold mb-3 outline-none focus-visible:ring-2 focus-visible:ring-blue-500 rounded">
        Start a job application
      </h1>

      <ol aria-label="Progress" className={`flex items-center gap-4 mb-6 text-xs font-medium ${textNeutral500}`}>
        {STEPS.map(({ step, label }) => (
          <li
            key={step}
            aria-current={step === currentStep ? 'step' : undefined}
            className={step === currentStep ? `${textAccent200} font-semibold` : undefined}
          >
            {label}
          </li>
        ))}
      </ol>

      {intake.error && (
        <div
          role="alert"
          className="mt-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2"
        >
          {intake.error}
        </div>
      )}

      <section className="mt-4">
        {jobPostingEditable ? (
          <>
            <label htmlFor="job-posting" className="block text-sm font-semibold mb-2">
              Job posting
            </label>
            <textarea
              id="job-posting"
              value={intake.jobPostingText}
              onChange={(e) => intake.setJobPostingText(e.target.value)}
              disabled={intake.stage === 'parsing'}
              aria-busy={intake.stage === 'parsing'}
              className="w-full h-48 p-3 rounded bg-slate-900 border border-slate-700 text-sm"
            />
            <div className="flex items-center gap-3 mt-3">
              <label htmlFor="job-posting-file" className="text-xs text-slate-400">
                Or upload a file
              </label>
              <input
                id="job-posting-file"
                type="file"
                onChange={onJobPostingFile}
                disabled={intake.stage === 'parsing'}
                className={fileInputClasses}
              />
              <button
                onClick={() => intake.parse()}
                disabled={!intake.jobPostingText || intake.stage !== 'provide'}
                className="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
              >
                {intake.stage === 'parsing' ? 'Parsing…' : 'Parse posting'}
              </button>
            </div>
          </>
        ) : (
          // U4.2: restyled parsed-result card. Title/company/description text nodes are kept
          // literal and unchanged (existing tests assert on them directly) - only the surrounding
          // markup and tokens change. No requirement chips: a real, deliberate scope cut (locked in
          // the epic doc) - TaskDraft has no structured requirements field to build them from.
          // PR #57 review: dropped the drafts[0].section paragraph entirely - Section is always ''
          // for this flow (ClaudeJobPostingParser/JobPostingParser, Sprint 3/PR #55, both set it to
          // string.Empty; Company carries the real value instead), so it could never render in
          // production and was dead code.
          <div className={`rounded-xl border ${borderDivider} ${bgSurface} p-4`}>
            <p className={`text-sm ${textNeutral400}`}>Job posting: {intake.drafts[0]?.title}</p>
            <p data-testid="parsed-company" className={`mt-1 text-xs ${textNeutral500}`}>
              {intake.drafts[0]?.company ?? '—'}
            </p>
            {intake.drafts[0]?.description && <p className={`mt-1 text-sm ${textNeutral400}`}>{intake.drafts[0].description}</p>}
          </div>
        )}
      </section>

      <section className="mt-6 pt-6 border-t border-slate-800">
        {baseResumeEditable ? (
          <>
            <label htmlFor="base-resume" className="block text-sm font-semibold mb-2">
              Base resume
            </label>
            {reuse.available && (
              <button
                type="button"
                onClick={() => intake.setBaseResumeText(reuse.content)}
                className="block mb-3 text-xs text-blue-400 hover:text-blue-300 underline focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 rounded"
              >
                Use previously saved base resume
                {reuse.updatedAt ? ` (updated ${formatDate(reuse.updatedAt)})` : ''}
              </button>
            )}
            <textarea
              id="base-resume"
              value={intake.baseResumeText}
              onChange={(e) => intake.setBaseResumeText(e.target.value)}
              placeholder="Paste your base resume"
              className="w-full h-48 p-3 rounded bg-slate-900 border border-slate-700 text-sm"
            />
            <button
              onClick={() => baseResumeCapture.save(ingestionSessionId, intake.baseResumeText)}
              disabled={baseResumeCapture.loading || !intake.baseResumeText}
              className="mt-3 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
            >
              {baseResumeCapture.loading ? 'Saving...' : 'Save base resume'}
            </button>

            {baseResumeCapture.error && (
              <div role="alert" className="mt-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
                {baseResumeCapture.error}
              </div>
            )}

            {baseResumeCapture.saved && (
              <p className="mt-3 text-sm text-emerald-400">Base resume saved.</p>
            )}

            {/* U4.3: additive click-to-expand preview, a sibling of the textarea above - not a
                replacement (the textarea/label pair stays exactly as-is, per the epic doc's own
                locked decision, and existing tests keep typing into it unchanged). Hidden entirely
                while there's nothing to preview (empty or whitespace-only base resume text): a
                native <details> with nothing behind it collapsed is a confusing affordance, and an
                empty MarkdownPreview isn't useful, so this renders nothing rather than a disabled
                state. */}
            {trimmedBaseResumeText !== '' && (
              <details className="mt-4">
                <summary className="cursor-pointer text-xs text-slate-400 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 rounded">
                  Preview base resume
                </summary>
                <div className="mt-3 rounded bg-slate-900/60 border border-slate-800 p-3">
                  <MarkdownPreview content={intake.baseResumeText} />
                </div>
              </details>
            )}
          </>
        ) : (
          <p className="text-sm text-slate-400">Base resume provided.</p>
        )}
      </section>

      {(intake.stage === 'review' || intake.stage === 'starting') && (
        <div className="mt-4">
          <TailorButton
            onClick={() => intake.startTailoring()}
            disabled={!intake.baseResumeText || intake.stage !== 'review'}
            busy={intake.stage === 'starting'}
          />
        </div>
      )}

      <details className="mt-8 pt-6 border-t border-slate-800">
        <summary className="cursor-pointer text-sm text-slate-400 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 rounded">
          Other: paste a generic document
        </summary>

        <div className="mt-4">
          <label htmlFor="generic-document" className="block text-sm font-semibold mb-2">
            Paste a document
          </label>
          <textarea
            id="generic-document"
            value={genericText}
            onChange={(e) => setGenericText(e.target.value)}
            placeholder="Paste a document"
            className="w-full h-48 p-3 rounded bg-slate-900 border border-slate-700 text-sm"
          />

          <div className="flex items-center gap-3 mt-3">
            <label htmlFor="generic-document-file" className="text-xs text-slate-400">
              Or upload a document file
            </label>
            <input
              id="generic-document-file"
              type="file"
              onChange={onGenericFile}
              className={fileInputClasses}
            />
            <button
              onClick={() => generic.submit(genericText)}
              disabled={generic.loading || !genericText}
              className="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
            >
              {generic.loading ? 'Parsing...' : 'Parse'}
            </button>
          </div>

          {generic.error && (
            <div role="alert" className="mt-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
              {generic.error}
            </div>
          )}

          {generic.drafts.length > 0 && (
            <>
              <ul className="mt-4 space-y-2">
                {generic.drafts.map((d, i) => (
                  <li key={i} className="border border-slate-800 rounded-lg p-3 bg-slate-900/60">
                    <div className="text-sm font-medium">{d.title}</div>
                    <div className="text-[11px] text-slate-500">{d.section}</div>
                    {d.description && <p className="text-xs text-slate-400 mt-1">{d.description}</p>}
                  </li>
                ))}
              </ul>
              <button
                onClick={() => generic.approve(genericSourceName)}
                disabled={generic.loading}
                className="mt-3 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
              >
                {generic.loading ? 'Adding...' : 'Approve and add to board'}
              </button>
            </>
          )}

          {generic.committedCount !== null && (
            <p className="mt-3 text-sm text-emerald-400">
              Added {generic.committedCount} task{generic.committedCount === 1 ? '' : 's'} to the board.
            </p>
          )}
        </div>
      </details>
    </div>
  )
}
