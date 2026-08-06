import { useState, type ChangeEvent } from 'react'
import { useIngestion } from '../hooks/useIngestion'

// Source-agnostic input: paste into the textarea or pick a file (read to text here). Both feed
// the same content to the parser. Approve commits the previewed drafts to the board.
export function IngestDocument() {
  const [text, setText] = useState('')
  const [sourceName, setSourceName] = useState('')
  const { drafts, loading, error, committedCount, submit, approve } = useIngestion()

  const onFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      setText(await file.text())
      setSourceName(file.name)
    }
  }

  return (
    <div className="max-w-2xl mx-auto p-6 text-white">
      <h1 className="text-lg font-bold mb-3">Ingest a document</h1>

      <textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="Paste a document"
        className="w-full h-48 p-3 rounded bg-slate-900 border border-slate-700 text-sm"
      />

      <div className="flex items-center gap-3 mt-3">
        <input type="file" onChange={onFile} className="text-xs text-slate-400" />
        <button
          onClick={() => submit(text)}
          disabled={loading || !text}
          className="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
        >
          {loading ? 'Parsing...' : 'Parse'}
        </button>
      </div>

      {error && (
        <div className="mt-3 text-sm text-red-400 bg-red-950 border border-red-900 rounded px-3 py-2">
          {error}
        </div>
      )}

      {drafts.length > 0 && (
        <>
          <ul className="mt-4 space-y-2">
            {drafts.map((d, i) => (
              <li key={i} className="border border-slate-800 rounded-lg p-3 bg-slate-900/60">
                <div className="text-sm font-medium">{d.title}</div>
                <div className="text-[11px] text-slate-500">{d.section}</div>
                {d.description && <p className="text-xs text-slate-400 mt-1">{d.description}</p>}
              </li>
            ))}
          </ul>
          <button
            onClick={() => approve(sourceName)}
            disabled={loading}
            className="mt-3 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white text-sm font-semibold px-4 py-2 rounded"
          >
            {loading ? 'Adding...' : 'Approve and add to board'}
          </button>
        </>
      )}

      {committedCount !== null && (
        <p className="mt-3 text-sm text-emerald-400">
          Added {committedCount} task{committedCount === 1 ? '' : 's'} to the board.
        </p>
      )}
    </div>
  )
}
