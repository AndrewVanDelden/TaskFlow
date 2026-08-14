import { describe, it, expect } from 'vitest'
import { stripCssLayers } from './stripCssLayers'

describe('stripCssLayers', () => {
  it('drops a bare layer-order declaration', () => {
    expect(stripCssLayers('@layer theme, base, utilities;\n.a { color: red; }')).toBe(
      '\n.a { color: red; }',
    )
  })

  it('unwraps a single-layer block to its plain contents', () => {
    expect(stripCssLayers('@layer utilities {\n  .a { color: red; }\n}')).toBe(
      '\n  .a { color: red; }\n',
    )
  })

  it('unwraps multiple layer blocks and preserves rules with nested braces', () => {
    const input = '@layer theme { :root { --x: 1; } }\n@layer utilities { .a { color: red; } .b { color: blue; } }'
    const result = stripCssLayers(input)

    expect(result).toContain(':root { --x: 1; }')
    expect(result).toContain('.a { color: red; }')
    expect(result).toContain('.b { color: blue; }')
    expect(result).not.toContain('@layer')
  })

  it('leaves content outside any @layer untouched', () => {
    expect(stripCssLayers('.plain { color: green; }')).toBe('.plain { color: green; }')
  })
})
