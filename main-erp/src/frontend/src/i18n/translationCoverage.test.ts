import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { extname, join } from 'node:path'
import test from 'node:test'
import fa from './locales/fa.ts'
import en from './locales/en.ts'

test('every literal UI translation key exists in Persian and English', () => {
  const sourceRoot = join(process.cwd(), 'src')
  const keys = new Set<string>()
  for (const file of sourceFiles(sourceRoot)) {
    if (file.endsWith('.test.ts')) continue
    const source = readFileSync(file, 'utf8')
    for (const match of source.matchAll(/\bt\(\s*['"]([^'"]+)['"]/g)) keys.add(match[1])
  }

  const missingFa = [...keys].filter(key => !hasKey(fa, key)).sort()
  const missingEn = [...keys].filter(key => !hasKey(en, key)).sort()
  assert.deepEqual(missingFa, [], `Missing Persian keys: ${missingFa.join(', ')}`)
  assert.deepEqual(missingEn, [], `Missing English keys: ${missingEn.join(', ')}`)
})

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return ['.ts', '.tsx'].includes(extname(entry.name)) ? [path] : []
  })
}

function hasKey(source: unknown, key: string) {
  let current: unknown = source
  for (const segment of key.split('.')) {
    if (!current || typeof current !== 'object' || !(segment in current)) return false
    current = (current as Record<string, unknown>)[segment]
  }
  return typeof current === 'string'
}
