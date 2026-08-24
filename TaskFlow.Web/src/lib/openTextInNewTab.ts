// Opens plain text in a new tab, for content with no backend export/PDF pipeline of its own (e.g.
// the candidate's own base resume - user-pasted text, not an AI-tailored artifact). The content is
// already in memory at the point this is called, so window.open() runs synchronously in the click
// handler with no awaited work first - unlike useExportDownload.ts's PDF/Markdown preview, there is
// no popup-blocker risk here to design around.
export function openTextInNewTab(content: string, title: string): boolean {
  const win = window.open('', '_blank')
  if (!win) return false

  win.document.write(
    `<!doctype html><title>${escapeHtml(title)}</title>` +
    '<style>body{background:#161826;color:#e9e9ed;font:14px/1.6 ui-monospace,Consolas,monospace;' +
    'white-space:pre-wrap;padding:24px;max-width:800px;margin:0 auto}</style>' +
    `<body>${escapeHtml(content)}</body>`,
  )
  win.document.close()
  return true
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}
