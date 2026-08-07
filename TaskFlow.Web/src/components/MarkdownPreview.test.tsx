import { render } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { MarkdownPreview } from './MarkdownPreview'

describe('MarkdownPreview', () => {
  it('renders plain markdown as formatted content', () => {
    const { getByText } = render(
      <MarkdownPreview content={'# Hello\n\nSome **bold** text.'} />
    )
    expect(getByText('Hello')).toBeInTheDocument()
    expect(getByText('bold')).toBeInTheDocument()
  })

  it('strips a raw <script> tag out of agent-produced markdown', () => {
    const { container } = render(
      <MarkdownPreview content={"# Title\n\n<script>alert('xss')</script>\n\nSafe text."} />
    )
    expect(container.querySelector('script')).toBeNull()
    expect(container.innerHTML).not.toContain('<script>')
  })

  it('strips an onerror handler off an injected image tag', () => {
    const { container } = render(
      <MarkdownPreview content={'<img src="x" onerror="alert(1)">'} />
    )
    expect(container.querySelector('[onerror]')).toBeNull()
  })
})
