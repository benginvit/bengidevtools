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

export async function scanApps(): Promise<ScannedApp[]> {
  return (await fetch(`${BASE}/apps/scan`)).json()
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

export function streamAppOutput(
  id: string,
  onLine: (line: string) => void,
  onDone?: () => void,
): EventSource {
  const source = new EventSource(`${BASE}/apps/output?id=${encodeURIComponent(id)}`)
  source.onmessage = (e) => onLine(JSON.parse(e.data) as string)
  source.onerror   = () => { source.close(); onDone?.() }
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
