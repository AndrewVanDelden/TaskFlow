import ReactMarkdown from 'react-markdown'
import rehypeSanitize from 'rehype-sanitize'

// The single shared renderer for agent-produced markdown. Every future consumer of AI-generated
// content (resume drafts, cover letters, etc.) must render through this component rather than
// rolling its own markdown handling. It never touches dangerouslySetInnerHTML: react-markdown
// renders real React elements via the unified/remark/rehype pipeline, and rehype-sanitize's
// default schema strips <script> tags, inline event handlers (onerror, onclick, ...), and
// javascript: URLs before anything reaches the DOM.
export function MarkdownPreview({ content }: { content: string }) {
  return (
    <div className="prose prose-invert prose-sm max-w-none">
      <ReactMarkdown rehypePlugins={[rehypeSanitize]}>{content}</ReactMarkdown>
    </div>
  )
}
