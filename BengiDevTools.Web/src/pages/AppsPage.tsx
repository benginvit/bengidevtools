import { useCallback, useEffect, useRef, useState } from 'react'
import {
  getApps, startApp, stopApp, restartApp,
  startAll, stopAll, startGitRefresh,
} from '../api'
import type { AppInfo } from '../types'

export default function AppsPage() {
  const [apps, setApps]           = useState<AppInfo[]>([])
  const [gitLoading, setGitLoading] = useState(false)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const load = useCallback(async () => {
    try { setApps(await getApps()) } catch { /* server kanske inte kör */ }
  }, [])

  useEffect(() => {
    load()
    pollRef.current = setInterval(load, 2000)
    return () => { if (pollRef.current) clearInterval(pollRef.current) }
  }, [load])

  const groups = [...new Set(apps.map(a => a.group))]

  const handleGitRefresh = () => {
    setGitLoading(true)
    startGitRefresh(
      (name, status) => setApps(prev =>
        prev.map(a => a.name === name ? { ...a, gitStatus: status } : a)),
      () => setGitLoading(false),
    )
  }

  const runningCount = apps.filter(a => a.isRunning).length

  return (
    <div>
      <div className="apps-toolbar">
        <button className="btn primary" onClick={() => startAll().then(load)}>▶ Starta alla</button>
        <button className="btn danger"  onClick={() => stopAll().then(load)}>■ Stoppa alla</button>
        <button className="btn" onClick={handleGitRefresh} disabled={gitLoading}>
          {gitLoading ? '⟳ Hämtar...' : '⟳ Git-status'}
        </button>
        <span style={{ marginLeft: 'auto', color: 'var(--muted)', fontSize: 12 }}>
          {runningCount} / {apps.length} kör
        </span>
      </div>

      {groups.map(group => (
        <div key={group} className="app-group">
          <div className="app-group-name">{group}</div>
          {apps.filter(a => a.group === group).map(app => (
            <AppRow key={app.name} app={app} onRefresh={load} />
          ))}
        </div>
      ))}
    </div>
  )
}

function AppRow({ app, onRefresh }: { app: AppInfo; onRefresh: () => void }) {
  const [busy, setBusy] = useState(false)

  const act = async (fn: () => Promise<void>) => {
    setBusy(true)
    try { await fn(); await onRefresh() }
    finally { setBusy(false) }
  }

  const gitClass =
    app.gitStatus === 'Uppdaterad' ? 'ok' :
    app.gitStatus === 'Bakom'      ? 'behind' : 'miss'

  return (
    <div className="app-row">
      <div className={`app-dot ${app.isRunning ? 'running' : 'stopped'}`} title={app.isRunning ? 'Kör' : 'Stoppad'} />
      <span className="app-name">
        {app.isRunning
          ? <a href={app.localhostUrl} target="_blank" rel="noreferrer" style={{ color: 'inherit', textDecoration: 'none' }}>{app.name}</a>
          : app.name}
      </span>
      <span className="app-port">:{app.port}</span>
      <span className={`git-status ${gitClass}`}>{app.gitStatus}</span>
      <div className="app-actions">
        {!app.isRunning
          ? <button className="btn sm" disabled={busy} onClick={() => act(() => startApp(app.name))}>▶</button>
          : <>
              <button className="btn sm" disabled={busy} onClick={() => act(() => restartApp(app.name))}>⟳</button>
              <button className="btn sm danger" disabled={busy} onClick={() => act(() => stopApp(app.name))}>■</button>
            </>
        }
      </div>
    </div>
  )
}
