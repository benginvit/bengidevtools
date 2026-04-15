import type { ScannedApp, AppStatus, RepoInfo, Settings } from './types'

const BASE = '/api'

// ─── Settings ─────────────────────────────────────────────────────────────────

export async function getSettings(): Promise<Settings> {
  return (await fetch(`${BASE}/settings`)).json()
}

export async function saveSettings(settings: Settings): Promise<Settings> {
  return (await fetch(`${BASE}/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  })).json()
}

// ─── Repos (Build page) ───────────────────────────────────────────────────────

export async function getRepos(): Promise<RepoInfo[]> {
  return (await fetch(`${BASE}/repos`)).json()
}

// ─── Apps ─────────────────────────────────────────────────────────────────────

// GET = returnera cache, POST = tvinga ny scan
export async function loadApps(): Promise<ScannedApp[]> {
  return (await fetch(`${BASE}/apps/scan`)).json()
}

export async function scanApps(): Promise<ScannedApp[]> {
  return (await fetch(`${BASE}/apps/scan`, { method: 'POST' })).json()
}

export async function getScanInfo(): Promise<{ count: number; lastScanned: string | null }> {
  return (await fetch(`${BASE}/apps/scan/info`)).json()
}

export async function getAppStatuses(): Promise<AppStatus[]> {
  return (await fetch(`${BASE}/apps/status`)).json()
}

export async function startApp(id: string): Promise<void> {
  await fetch(`${BASE}/apps/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id }),
  })
}

export async function stopApp(id: string): Promise<void> {
  await fetch(`${BASE}/apps/stop`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id }),
  })
}

export async function restartApp(id: string): Promise<void> {
  await fetch(`${BASE}/apps/restart`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id }),
  })
}

export async function startSelected(ids: string[]): Promise<void> {
  await fetch(`${BASE}/apps/start-selected`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(ids),
  })
}

export async function stopAll(): Promise<void> {
  await fetch(`${BASE}/apps/stop-all`, { method: 'POST' })
}

export async function getLocalUser(id: string): Promise<{ content: string | null; path: string; exists: boolean }> {
  return (await fetch(`${BASE}/apps/localuser?id=${encodeURIComponent(id)}`)).json()
}

export async function saveLocalUser(id: string, content: string): Promise<void> {
  const res = await fetch(`${BASE}/apps/localuser?id=${encodeURIComponent(id)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'text/plain' },
    body: content,
  })
  if (!res.ok) throw new Error(await res.text())
}

export function exportLocalUserUrl(): string {
  return `${BASE}/apps/localuser/export`
}

// ─── Debug ────────────────────────────────────────────────────────────────────

import type { DebugScript, Scenario, SwaggerPath } from './types'

export async function getDebugScripts(): Promise<DebugScript[]> {
  return (await fetch(`${BASE}/debug/scripts`)).json()
}

export async function getDebugScript(path: string): Promise<{ content: string }> {
  return (await fetch(`${BASE}/debug/script?path=${encodeURIComponent(path)}`)).json()
}

export async function saveDebugScript(path: string, content: string): Promise<void> {
  await fetch(`${BASE}/debug/script?path=${encodeURIComponent(path)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'text/plain' },
    body: content,
  })
}

export async function newDebugScript(name: string, type: string): Promise<{ path: string }> {
  return (await fetch(`${BASE}/debug/scripts/new`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, type }),
  })).json()
}

export async function deleteDebugScript(path: string): Promise<void> {
  await fetch(`${BASE}/debug/script?path=${encodeURIComponent(path)}`, { method: 'DELETE' })
}

export async function executeSql(sql: string): Promise<{
  success: boolean; error?: string
  results?: Array<
    | { type: 'select'; columns: string[]; rows: Record<string, unknown>[] }
    | { type: 'nonquery'; rowsAffected: number }
  >
}> {
  return (await fetch(`${BASE}/debug/execute-sql`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql }),
  })).json()
}

export async function getSwagger(appId: string): Promise<SwaggerPath[]> {
  try {
    const res = await fetch(`${BASE}/debug/swagger?appId=${encodeURIComponent(appId)}`)
    const json = await res.json()
    if (json.error) return []

    const paths: SwaggerPath[] = []
    for (const [path, methods] of Object.entries(json.paths ?? {})) {
      for (const method of Object.keys(methods as object)) {
        if (!['get','post','put','patch','delete'].includes(method)) continue
        const op = (methods as Record<string, {
          summary?: string
          requestBody?: { content?: { 'application/json'?: { schema?: unknown; example?: unknown } } }
        }>)[method]

        let exampleBody: string | undefined
        if (['post','put','patch'].includes(method)) {
          const jsonContent = op.requestBody?.content?.['application/json']
          if (jsonContent?.example !== undefined) {
            exampleBody = JSON.stringify(jsonContent.example, null, 2)
          } else if (jsonContent?.schema) {
            exampleBody = JSON.stringify(generateExample(jsonContent.schema as JsonSchema, json), null, 2)
          }
        }

        paths.push({ method: method.toUpperCase(), path, summary: op.summary, exampleBody })
      }
    }
    return paths.sort((a, b) => a.path.localeCompare(b.path))
  } catch { return [] }
}

// ── JSON Schema → example object ──────────────────────────────────────────────

interface JsonSchema {
  type?: string
  properties?: Record<string, JsonSchema>
  items?: JsonSchema
  $ref?: string
  example?: unknown
  default?: unknown
  enum?: unknown[]
  format?: string
  nullable?: boolean
}

function resolveRef(ref: string, spec: Record<string, unknown>): JsonSchema {
  // e.g. #/components/schemas/Foo
  const parts = ref.replace('#/', '').split('/')
  let node: unknown = spec
  for (const p of parts) node = (node as Record<string, unknown>)[p]
  return (node ?? {}) as JsonSchema
}

function generateExample(schema: JsonSchema, spec: Record<string, unknown>, depth = 0): unknown {
  if (depth > 5) return null
  if (schema.$ref) schema = resolveRef(schema.$ref, spec)
  if (schema.example !== undefined) return schema.example
  if (schema.default  !== undefined) return schema.default
  if (schema.enum?.length)           return schema.enum[0]

  switch (schema.type) {
    case 'object': {
      const obj: Record<string, unknown> = {}
      for (const [k, v] of Object.entries(schema.properties ?? {}))
        obj[k] = generateExample(v, spec, depth + 1)
      return obj
    }
    case 'array':
      return [generateExample(schema.items ?? { type: 'string' }, spec, depth + 1)]
    case 'integer':
    case 'number':
      return schema.format === 'float' || schema.format === 'double' ? 0.0 : 0
    case 'boolean': return false
    case 'string':
      if (schema.format === 'date-time') return new Date().toISOString()
      if (schema.format === 'date')      return new Date().toISOString().split('T')[0]
      if (schema.format === 'uuid')      return '00000000-0000-0000-0000-000000000000'
      return ''
    default:
      return null
  }
}

export async function getScenarios(): Promise<Scenario[]> {
  return (await fetch(`${BASE}/debug/scenarios`)).json()
}

export async function saveScenario(scenario: Scenario): Promise<void> {
  await fetch(`${BASE}/debug/scenarios`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(scenario),
  })
}

export async function deleteScenario(id: string): Promise<void> {
  await fetch(`${BASE}/debug/scenarios/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

export function streamAppOutput(
  id: string,
  onLine: (line: string) => void,
): AbortController {
  const ctrl = new AbortController()
  let offset = 0
  ;(async () => {
    while (!ctrl.signal.aborted) {
      try {
        const res = await fetch(
          `${BASE}/apps/lines?id=${encodeURIComponent(id)}&offset=${offset}`,
          { signal: ctrl.signal },
        )
        const { lines } = await res.json() as { lines: string[]; total: number }
        for (const line of lines) onLine(line)
        offset += lines.length
      } catch { break }
      await new Promise(r => setTimeout(r, 800))
    }
  })()
  return ctrl
}

export function startCheckoutAll(
  onUpdate: (repoName: string, branch: string, message: string) => void,
  onDone: () => void,
): EventSource {
  const source = new EventSource(`${BASE}/apps/git-checkout-all`)
  source.onmessage = (e) => {
    const { repoName, branch, message } = JSON.parse(e.data)
    onUpdate(repoName, branch, message)
  }
  source.addEventListener('done', () => { source.close(); onDone() })
  source.onerror = () => { source.close(); onDone() }
  return source
}

export function startGitRefresh(
  onUpdate: (repoName: string, status: string) => void,
  onDone: () => void,
): EventSource {
  const source = new EventSource(`${BASE}/apps/git-refresh`)
  source.onmessage = (e) => {
    const { repoName, status } = JSON.parse(e.data)
    onUpdate(repoName, status)
  }
  source.addEventListener('done', () => { source.close(); onDone() })
  source.onerror = () => { source.close(); onDone() }
  return source
}

// ─── Build ────────────────────────────────────────────────────────────────────

export interface BuildRequest {
  repoNames: string[]
  noRestore: boolean
  noAnalyzers: boolean
  noDocs: boolean
  parallel: boolean
  snabb: boolean
}

export async function startBuild(
  req: BuildRequest,
  onEvent: (type: string, data: Record<string, string>) => void,
  signal: AbortSignal,
): Promise<void> {
  const response = await fetch(`${BASE}/build/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
    signal,
  })

  const reader = response.body!.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''
    for (const line of lines) {
      if (line.startsWith('data: ')) {
        try { onEvent(JSON.parse(line.slice(6)).type, JSON.parse(line.slice(6))) } catch { }
      }
    }
  }
}
