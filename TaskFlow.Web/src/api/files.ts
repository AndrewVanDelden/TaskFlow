import { requestFormData } from './client'

// PDF is a binary format - decoding it as text client-side (File.text()) produces garbage. This
// posts the raw file to the server so PdfTextExtractor (PdfPig) can pull out the real text.
export function extractPdfText(file: File): Promise<string> {
  const formData = new FormData()
  formData.append('file', file, file.name)
  return requestFormData<string>('/api/Files/extract-pdf-text', formData)
}
