import { describe, it, expect } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../test/server'
import { extractPdfText } from './files'

describe('extractPdfText', () => {
  it('posts the file as multipart form data and returns the extracted text', async () => {
    let receivedFile: FormDataEntryValue | null = null
    server.use(
      http.post('*/api/Files/extract-pdf-text', async ({ request }) => {
        const formData = await request.formData()
        receivedFile = formData.get('file')
        return HttpResponse.json('extracted resume text')
      }),
    )

    const file = new File(['pdf bytes'], 'resume.pdf', { type: 'application/pdf' })
    const text = await extractPdfText(file)

    expect(text).toBe('extracted resume text')
    // Only the entry's presence/content is asserted here, not its filename or exact class: a jsdom
    // File crossing into Node's native fetch (MSW's interception layer) comes back as a separate,
    // cross-realm File whose filename is dropped and whose prototype chain doesn't match jsdom's
    // global Blob/File - a known jsdom/undici interop gap, verified in isolation with a bare
    // fetch() and no app code involved. Real browsers have one consistent File/fetch realm, so this
    // never happens in production; client.test.ts's requestFormData suite already covers the
    // header-level contract (Authorization attached, Content-Type left for the browser to set).
    const received = receivedFile as unknown as { size: number; type: string }
    expect(received.size).toBe(9)
    expect(received.type).toBe('application/pdf')
  })
})
