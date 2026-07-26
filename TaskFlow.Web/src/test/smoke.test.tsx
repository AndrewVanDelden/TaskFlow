import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'

describe('harness', () => {
  it('renders and asserts', () => {
    render(<h1>TaskFlow</h1>)
    expect(screen.getByText('TaskFlow')).toBeInTheDocument()
  })
})
