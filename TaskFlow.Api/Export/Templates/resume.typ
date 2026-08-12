// Trusted, hand-authored Typst template for a tailored resume PDF export (T5.1c).
//
// ExportService (T5.1d, not yet built) will concatenate this file's text, verbatim, with the
// escaped Typst markup produced by TailoredContentTypstRenderer to form the full stdin payload
// sent to the `typst` compiler ("typst compile - -"). Everything below is page/typography setup
// only -- no #import, no file reads, nothing that depends on --root -- so it stays safe to compose
// with untrusted-derived content by plain string concatenation, per this sprint's decision to
// avoid #import entirely and keep the sandboxed --root directory empty and never read from.
//
// `document` establishes page margins, body typography, and heading style. `#show: document`
// applies it to everything that follows in the composed file -- i.e. the rendered resume content
// appended after this template becomes `document`'s `body` argument implicitly.

#let document(body) = {
  set page(
    paper: "us-letter",
    margin: (x: 0.75in, y: 0.75in),
  )
  set text(
    font: ("Linux Libertine", "New Computer Modern"),
    size: 10.5pt,
    lang: "en",
  )
  set par(justify: true, leading: 0.6em)
  set list(indent: 1em, marker: [•])
  set enum(indent: 1em)

  show heading.where(level: 1): it => {
    set text(size: 16pt, weight: "bold")
    block(above: 0em, below: 0.7em, it.body)
  }
  show heading.where(level: 2): it => {
    set text(size: 12pt, weight: "bold")
    block(above: 1em, below: 0.4em, it.body)
  }
  show heading.where(level: 3): it => {
    set text(size: 11pt, weight: "bold", style: "italic")
    block(above: 0.8em, below: 0.3em, it.body)
  }

  body
}

#show: document
