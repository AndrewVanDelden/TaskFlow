import { useState } from 'react'
import type { TaskDraft } from '../types'
import { parseJobPosting, saveResumeContext, assembleApplication } from '../api/jobApplications'

// T6.2's stage model: one discriminated field, not several booleans. Owned entirely here, with no
// dependency on Slice D's live-progress data - 'building' is this hook's own terminal state.
export type IntakeStage = 'provide' | 'parsing' | 'review' | 'starting' | 'building'

export function useIntakeFlow(sessionId: string) {
  const [stage, setStage] = useState<IntakeStage>('provide')
  const [jobPostingText, setJobPostingText] = useState('')
  const [baseResumeText, setBaseResumeText] = useState('')
  const [drafts, setDrafts] = useState<TaskDraft[]>([])
  const [error, setError] = useState<string | null>(null)
  const [applicationId, setApplicationId] = useState<number | null>(null)
  const [resumeTaskId, setResumeTaskId] = useState<number | null>(null)
  const [coverLetterTaskId, setCoverLetterTaskId] = useState<number | null>(null)

  const parse = async () => {
    setStage('parsing')
    setError(null)
    try {
      const result = await parseJobPosting(jobPostingText)
      // An empty list is a legitimate, successful HTTP response (no Anthropic key configured,
      // and/or the posting has no heading the free parser recognizes) - not a server error, but
      // still nothing usable to review or assemble. Treated the same as a parse failure so the
      // user gets a clear message instead of silently reaching 'review' with nothing in it.
      if (result.length === 0) {
        throw new Error('Could not find a job title in that posting. Try adding a heading or more detail.')
      }
      setDrafts(result)
      setStage('review')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to parse the job posting.')
      setStage('provide')
    }
  }

  const startTailoring = async () => {
    setStage('starting')
    setError(null)
    try {
      const posting = drafts[0]
      await saveResumeContext(sessionId, baseResumeText)
      const application = await assembleApplication(sessionId, {
        title: posting.title,
        description: posting.description,
        section: posting.section,
        company: posting.company ?? null,
      })
      const resumeTask = application.tasks.find((t) => t.kind === 'ResumeTailoring')
      const coverLetterTask = application.tasks.find((t) => t.kind === 'CoverLetterTailoring')
      setApplicationId(application.id)
      setResumeTaskId(resumeTask?.id ?? null)
      setCoverLetterTaskId(coverLetterTask?.id ?? null)
      setStage('building')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start tailoring.')
      setStage('review')
    }
  }

  return {
    stage, jobPostingText, setJobPostingText, baseResumeText, setBaseResumeText,
    drafts, error, applicationId, resumeTaskId, coverLetterTaskId, parse, startTailoring,
  }
}
