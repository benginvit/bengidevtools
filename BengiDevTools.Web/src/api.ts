import type { AppInfo, RepoInfo, Settings } from './types'

const BASE = '/api'

// ─── Settings ─────────────────────────────────────────────────────────────────

export async function getSettings(): Promise<Settings> {
  const r = await fetch(`${BASE}/settings`)
  return r.json()
}

export async function saveSettings(settings: Settings): Promise<Settings> {
  const r = await fetch(`${BASE}/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings),
  })
  return r.json()
}

// ─── Repos ────────────────────────────────────────────────────────────────────

export async function getRepos(): Promise<RepoInfo[]> {
  const r = await fetch(`${BASE}/repos`)
  return r.json()
}

// ─── Apps ─────────────────────────────────────────────────────────────────────

export async function getApps(): Promise<AppInfo[]> {
  const r = await fetch(`${BASE}/apps`)
  return r.json()
}

export async function startApp(name: string): Promise<void> {
  await fetch(`${BASE}/apps/${encodeURIComponent(name)}/start`, { method: 'POST' })
}

export async function stopApp(name: string): Promise<void> {
  await fetch(`${BASE}/apps/${encodeURIComponent(name)}/stop`, { method: 'POST' })
}

export async function restartApp(name: string): Promise<void> {
  await fetch(`${BASE}/apps/${encodeURIComponent(name)}/restart`, { method: 'POST' })
}

export async function startAll(): Promise<void> {
  await fetch(`${BASE}/apps/start-all`, { method: 'POST' })
}

export async function stopAll(): Promise<void> {
  await fetch(`${BASE}/apps/stop-all`, { method: 'POST' })
}

// SSE – git refresh. Returnerar en EventSource; stäng den när klar.
export function startGitRefresh(
  onUpdate: (name: string, status: string) => void,
  onDone: () => void,
): EventSource {
  const source = new EventSource(`${BASE}/apps/git-refresh`)
  source.onmessage = (e) => {
    const { name, status } = JSON.parse(e.data)
    onUpdate(name, status)
  }
  source.addEventListener('done', () => {
    source.close()
    onDone()
  })
  source.onerror = () => {
    source.close()
    onDone()
  }
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

// Streaming POST – cancella bygget via abortController.abort()
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
        try {
          const parsed = JSON.parse(line.slice(6))
          onEvent(parsed.type, parsed)
        } catch { /* ignorera ogiltiga rader */ }
      }
    }
  }
}
