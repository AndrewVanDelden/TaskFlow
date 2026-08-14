// jsdom (used by vitest) parses `@layer` blocks but does not apply their rules when computing
// style — see setup.ts for why this matters. Tailwind v4 wraps every generated utility class in
// `@layer utilities`, so without this, no getComputedStyle assertion against a Tailwind class
// would ever see real styling in tests. This unwraps `@layer name { ... }` blocks to their plain
// top-level contents and drops bare layer-order declarations (`@layer a, b;`), preserving rule
// order for the simple, non-competing utility assertions this project's tests need.
export function stripCssLayers(css: string): string {
  const out: string[] = []
  let i = 0

  while (i < css.length) {
    const atIndex = css.indexOf('@layer', i)
    if (atIndex === -1) {
      out.push(css.slice(i))
      break
    }
    out.push(css.slice(i, atIndex))

    const semiIndex = css.indexOf(';', atIndex)
    const braceIndex = css.indexOf('{', atIndex)

    if (semiIndex === -1 && braceIndex === -1) {
      // Unterminated @layer token — no `;` or `{` before EOF. Nothing can be meaningfully
      // unwrapped, so leave the malformed remainder as-is and stop scanning.
      out.push(css.slice(atIndex))
      break
    }

    if (braceIndex === -1 || (semiIndex !== -1 && semiIndex < braceIndex)) {
      // Bare layer-order declaration, e.g. `@layer theme, base, utilities;` — drop it.
      i = semiIndex + 1
      continue
    }

    let depth = 0
    let j = braceIndex
    for (; j < css.length; j++) {
      if (css[j] === '{') depth++
      else if (css[j] === '}') {
        depth--
        if (depth === 0) break
      }
    }

    out.push(css.slice(braceIndex + 1, j))
    i = j + 1
  }

  return out.join('')
}
