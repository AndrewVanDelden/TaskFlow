// Trusted, hand-authored Typst template for a tailored cover-letter PDF export (T5.1c).
//
// Same composition contract as resume.typ (see that file's header comment for the full reasoning):
// ExportService (T5.1d, not yet built) concatenates this file's text, verbatim, with the escaped
// Typst markup produced by TailoredContentTypstRenderer. No #import, no file reads.
//
// A cover letter is prose-heavy rather than list-heavy, so this template favors a single-column,
// left-aligned, slightly more spacious layout over the resume's denser, justified one.

#let document(body) = {
  set page(
    paper: "us-letter",
    margin: (x: 1in, y: 1in),
  )
  set text(
    font: ("Linux Libertine", "New Computer Modern"),
    size: 11pt,
    lang: "en",
  )
  set par(justify: false, leading: 0.75em, first-line-indent: 0em)

  show heading.where(level: 1): it => {
    set text(size: 14pt, weight: "bold")
    block(above: 0em, below: 0.8em, it.body)
  }
  show heading.where(level: 2): it => {
    set text(size: 11pt, weight: "bold")
    block(above: 0.8em, below: 0.4em, it.body)
  }

  body
}

#show: document
