import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { KanbanColumn } from './KanbanColumn'
import type { TaskItem, TaskKind } from '../types'
import { axe } from '../test/axe'

const task: TaskItem = {
  id: 1, title: 'Ship it', description: null, status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
  kind: 'Generic', applicationId: null, tailoredContent: null,
}

// Builds an Epic 3 sibling task (Sprint 6, T6.4 grouping tests), mirroring board.test.ts's own
// epicTask helper.
const epicTask = (id: number, title: string, applicationId: number | null, kind: TaskKind): TaskItem => ({
  id, title, description: null, status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
  kind, applicationId, tailoredContent: null,
})

// KanbanColumn uses useDroppable and renders TaskCards (useSortable), so wrap in DndContext.
describe('KanbanColumn', () => {
  it('shows its label and renders the tasks it is given', () => {
    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[task]} />
      </DndContext>,
    )
    expect(screen.getByText('To Do')).toBeInTheDocument()
    expect(screen.getByText('Ship it')).toBeInTheDocument()
  })

  it('shows the empty state when there are no tasks', () => {
    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[]} />
      </DndContext>,
    )
    expect(screen.getByText('No tasks')).toBeInTheDocument()
  })

  // Sprint 6, T6.4: same-applicationId siblings visible in one column get a shared visual wrapper.
  it('wraps two same-application sibling tasks in a shared visual group', () => {
    const resumeTask = epicTask(1, 'Tailor resume', 10, 'ResumeTailoring')
    const coverLetterTask = epicTask(2, 'Tailor cover letter', 10, 'CoverLetterTailoring')

    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[resumeTask, coverLetterTask]} />
      </DndContext>,
    )

    const group = screen.getByTestId('sibling-group-10')
    expect(within(group).getByText('Tailor resume')).toBeInTheDocument()
    expect(within(group).getByText('Tailor cover letter')).toBeInTheDocument()
  })

  // My own review finding (PR #49): the wrapper had no ARIA group semantics, so a screen-reader
  // user encountering the two cards had no indication they belong to the same job application -
  // only a visual border, which conveys nothing to assistive tech.
  it('exposes a grouped sibling pair to assistive tech as a labeled group', () => {
    const resumeTask = epicTask(1, 'Tailor resume', 10, 'ResumeTailoring')
    const coverLetterTask = epicTask(2, 'Tailor cover letter', 10, 'CoverLetterTailoring')

    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[resumeTask, coverLetterTask]} />
      </DndContext>,
    )

    const group = screen.getByRole('group')
    expect(group).toHaveAccessibleName(/job application/i)
    expect(within(group).getByText('Tailor resume')).toBeInTheDocument()
    expect(within(group).getByText('Tailor cover letter')).toBeInTheDocument()
  })

  it('does not group two tasks from different applications', () => {
    const taskA = epicTask(1, 'Tailor resume A', 10, 'ResumeTailoring')
    const taskB = epicTask(2, 'Tailor resume B', 20, 'ResumeTailoring')

    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[taskA, taskB]} />
      </DndContext>,
    )

    expect(screen.getByText('Tailor resume A')).toBeInTheDocument()
    expect(screen.getByText('Tailor resume B')).toBeInTheDocument()
    expect(screen.queryByTestId('sibling-group-10')).toBeNull()
    expect(screen.queryByTestId('sibling-group-20')).toBeNull()
  })

  it('still allows dragging a grouped card', () => {
    const resumeTask = epicTask(1, 'Tailor resume', 10, 'ResumeTailoring')
    const coverLetterTask = epicTask(2, 'Tailor cover letter', 10, 'CoverLetterTailoring')

    render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[resumeTask, coverLetterTask]} />
      </DndContext>,
    )

    // Matches this codebase's existing drag-coverage convention (see TaskCard.test.tsx /
    // KanbanBoard.test.tsx): confirm useSortable's attributes (role="button", tabIndex) are still
    // present on each individually-wrapped card, rather than simulating a real pointer drag.
    const resumeCard = screen.getByText('Tailor resume').closest('[role="button"]')
    const coverLetterCard = screen.getByText('Tailor cover letter').closest('[role="button"]')
    expect(resumeCard).toHaveAttribute('tabindex', '0')
    expect(coverLetterCard).toHaveAttribute('tabindex', '0')
  })

  it('has no accessibility violations', async () => {
    const { container } = render(
      <DndContext>
        <KanbanColumn status="Todo" label="To Do" tasks={[task]} />
      </DndContext>,
    )

    expect(await axe(container)).toHaveNoViolations()
  })

  describe('Clear Done', () => {
    const doneTask: TaskItem = { ...task, id: 5, status: 'Done' }

    afterEach(() => {
      vi.restoreAllMocks()
    })

    it('shows a Clear Done button only in the Done column', () => {
      render(
        <DndContext>
          <KanbanColumn status="Todo" label="To Do" tasks={[task]} onArchiveDone={vi.fn()} />
        </DndContext>,
      )
      expect(screen.queryByRole('button', { name: 'Clear Done' })).toBeNull()
    })

    it('shows a Clear Done button in the Done column when onArchiveDone is supplied', () => {
      render(
        <DndContext>
          <KanbanColumn status="Done" label="Done" tasks={[doneTask]} onArchiveDone={vi.fn()} />
        </DndContext>,
      )
      expect(screen.getByRole('button', { name: 'Clear Done' })).toBeInTheDocument()
    })

    it('disables Clear Done when the Done column has zero tasks', () => {
      render(
        <DndContext>
          <KanbanColumn status="Done" label="Done" tasks={[]} onArchiveDone={vi.fn()} />
        </DndContext>,
      )
      expect(screen.getByRole('button', { name: 'Clear Done' })).toBeDisabled()
    })

    it('calls onArchiveDone after the user confirms', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true)
      const onArchiveDone = vi.fn()
      render(
        <DndContext>
          <KanbanColumn status="Done" label="Done" tasks={[doneTask]} onArchiveDone={onArchiveDone} />
        </DndContext>,
      )

      await userEvent.click(screen.getByRole('button', { name: 'Clear Done' }))

      expect(window.confirm).toHaveBeenCalled()
      expect(onArchiveDone).toHaveBeenCalledOnce()
    })

    it('does not call onArchiveDone when the user cancels the confirmation', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(false)
      const onArchiveDone = vi.fn()
      render(
        <DndContext>
          <KanbanColumn status="Done" label="Done" tasks={[doneTask]} onArchiveDone={onArchiveDone} />
        </DndContext>,
      )

      await userEvent.click(screen.getByRole('button', { name: 'Clear Done' }))

      expect(onArchiveDone).not.toHaveBeenCalled()
    })
  })

  it('threads onArchive through to a Done card so clicking Archive calls it with the task id', async () => {
    const doneTask: TaskItem = { ...task, id: 5, status: 'Done' }
    const onArchive = vi.fn()

    render(
      <DndContext>
        <KanbanColumn status="Done" label="Done" tasks={[doneTask]} onArchive={onArchive} />
      </DndContext>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(onArchive).toHaveBeenCalledWith(5)
  })
})
