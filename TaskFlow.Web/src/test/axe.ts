import { expect } from 'vitest'
import * as matchers from 'vitest-axe/matchers'
import { axe } from 'vitest-axe'
// Type-only: augments Vitest's `Assertion` interface with `toHaveNoViolations` so it type-checks
// (see vitest-axe README, "With TypeScript") — the runtime matcher itself comes from expect.extend below.
import 'vitest-axe/extend-expect'

expect.extend(matchers)

export { axe }
