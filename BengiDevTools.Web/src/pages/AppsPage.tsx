import { useCallback, useEffect, useRef, useState } from 'react'
import {
  scanApps, getAppStatuses,
  startApp, stopApp, restartApp,
  startSelected, stopAll, startGitRefresh,
} from '../api'
import type { ScannedApp } from '../types'

interface AppState extends ScannedApp {
  checked: boolean
}

export default function AppsPage() {
  const [apps, setApps]             = useState<AppState[]>([])
  const [scanning, setScanning]     = useState(false)
  const [gitLoading, setGitLoading] = useState(false)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const scan = useCallback(async () => {
    setScanning(true)
    try {
      const data = await scanApps()
      setApps(prev => {
        const prevMap = new Map(prev.map(a => [a.id, a]))
        return data.map(a => ({
          ...a,
          checked: prevMap.get(a.id)?.checked ?? true,
        }))
      })
    } finally {
      setScanning(false) }
  }, [])

  // Poll running-status var 2:a sekund (ingen re-scan)
  const pollStatus = useCallback(async () => {
    try {
      const statuses = await getAppStatuses()
      const map = new Map(statuses.map(s => [s.id, s.isRunning]))
      setApps(prev => prev.map(a => ({
        ...a,
        isRunning: map.get(a.id) ?? a.isRunning,
      })))
    } catch { /* server kanske inte kör */ }
  }, [])

  useEffect(() => {
    scan()
  }, [scan])

  useEffect(() => {
    pollRef.current = setInterval(pollStatus, 2000)
    return () => { if (pollRef.current) clearInterval(pollRef.current) }
  }, [pollStatus])

  const handleGitRefresh = () => {
    setGitLoading(true)
    startGitRefresh(
      (repoName, status) => setApps(prev =>
        prev.map(a => a.repoName === repoName ? { ...a, gitStatus: status } : a)),
      () => setGitLoading(false),
    )
  }

  const handleStartSelected = async () => {
    const ids = apps.filter(a => a.checked && !a.isRunning).map(a => a.id)
    await startSelected(ids)
    await pollStatus()
  }

  const handleStopAll = async () => {
    await stopAll()
    await pollStatus()
  }

  const groups = [...new Set(apps.map(a => a.repoName))]
  const checkedCount = apps.filter(a => a.checked).length
  const runningCount = apps.filter(a => a.isRunning).length

  return (
    <div>
      {/* Toolbar */}
      <div className="apps-toolbar">
        <button className="btn" onClick={scan} disabled={scanning}>
          {scanning ? '⟳ Scannar...' : '⟳ Scanna'}
        </button>
        <button className="btn primary" onClick={handleStartSelected} disabled={checkedCount === 0}>
          ▶ Starta valda ({checkedCount})
        </button>
        <button className="btn danger" onClick={handleStopAll}>■ Stoppa alla</button>
        <button className="btn" onClick={handleGitRefresh} disabled={gitLoading || apps.length === 0}>
          {gitLoading ? '⟳ Git...' : '⟳ Git-status'}
        </button>
        <label className="cb" style={{ marginLeft: 8 }}>
          <input
            type="checkbox"
            checked={apps.length > 0 && apps.every(a => a.checked)}
            onChange={e => setApps(prev => prev.map(a => ({ ...a, checked: e.target.checked })))}
          />
          <span style={{ color: 'var(--muted)', fontSize: 12 }}>Alla</span>
        </label>
        <span style={{ marginLeft: 'auto', color: 'var(--muted)', fontSize: 12 }}>
          {runningCount} / {apps.length} kör
        </span>
      </div>

      {apps.length === 0 && !scanning && (
        <div style={{ color: 'var(--muted)', fontSize: 12, padding: '8px 0' }}>
          Tryck Scanna för att hitta körbara projekt, eller kontrollera repo-rot i Inställningar.
        </div>
      )}

      {groups.map(repoName => {
        const repoApps  = apps.filter(a => a.repoName === repoName)
        const gitStatus = repoApps[0]?.gitStatus ?? '–'
        const gitClass  = gitStatus === 'Uppdaterad' ? 'ok' : gitStatus === 'Bakom' ? 'behind' : 'miss'
        const allChecked = repoApps.every(a => a.checked)

        return (
          <div key={repoName} className="app-group">
            <div className="app-group-name" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <label className="cb" onClick={e => e.stopPropagation()}>
                <input
                  type="checkbox"
                  checked={allChecked}
                  onChange={e => setApps(prev =>
                    prev.map(a => a.repoName === repoName ? { ...a, checked: e.target.checked } : a))}
                />
              </label>
              <span>{repoName}</span>
              <span className={`git-status ${gitClass}`} style={{ marginLeft: 'auto', fontWeight: 400 }}>
                {gitStatus}
              </span>
            </div>

            {repoApps.map(app => (
              <AppRow
                key={app.id}
                app={app}
                onCheck={checked => setApps(prev => prev.map(a => a.id === app.id ? { ...a, checked } : a))}
                onRefresh={pollStatus}
              />
            ))}
          </div>
        )
      })}
    </div>
  )
}

function AppRow({
  app,
  onCheck,
  onRefresh,
}: {
  app: AppState
  onCheck: (v: boolean) => void
  onRefresh: () => void
}) {
  const [busy, setBusy] = useState(false)

  const act = async (fn: () => Promise<void>) => {
    setBusy(true)
    try { await fn(); await onRefresh() }
    finally { setBusy(false) }
  }

  return (
    <div className="app-row">
      <input
        type="checkbox"
        checked={app.checked}
        onChange={e => onCheck(e.target.checked)}
        style={{ accentColor: 'var(--blue)', width: 13, height: 13, cursor: 'pointer', flexShrink: 0 }}
      />
      <div className={`app-dot ${app.isRunning ? 'running' : 'stopped'}`} />
      <span className="app-name">
        {app.isRunning && app.localhostUrl
          ? <a href={app.localhostUrl} target="_blank" rel="noreferrer" style={{ color: 'inherit', textDecoration: 'none' }}>{app.projectName}</a>
          : app.projectName}
      </span>
      <span className="app-port">{app.httpsPort ? `:${app.httpsPort}` : ''}</span>
      <div className="app-actions">
        {!app.isRunning
          ? <button className="btn sm" disabled={busy} onClick={() => act(() => startApp(app.id))}>▶</button>
          : <>
              <button className="btn sm" disabled={busy} onClick={() => act(() => restartApp(app.id))}>⟳</button>
              <button className="btn sm danger" disabled={busy} onClick={() => act(() => stopApp(app.id))}>■</button>
            </>
        }
      </div>
    </div>
  )
}
