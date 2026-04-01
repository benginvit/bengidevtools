import { useCallback, useEffect, useRef, useState } from 'react'
import {
  scanApps, getAppStatuses,
  startApp, stopApp, restartApp,
  startSelected, stopAll, startGitRefresh,
  streamAppOutput,
} from '../api'
import type { ScannedApp } from '../types'

interface AppState extends ScannedApp {
  checked: boolean
  hasException: boolean
}

export default function AppsPage() {
  const [apps, setApps]               = useState<AppState[]>([])
  const [scanning, setScanning]       = useState(false)
  const [gitLoading, setGitLoading]   = useState(false)
  const [selectedId, setSelectedId]   = useState<string | null>(null)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const scan = useCallback(async () => {
    setScanning(true)
    try {
      const data = await scanApps()
      setApps(prev => {
        const prevMap = new Map(prev.map(a => [a.id, a]))
        return data.map(a => ({
          ...a,
          checked:      prevMap.get(a.id)?.checked      ?? true,
          hasException: prevMap.get(a.id)?.hasException ?? false,
        }))
      })
    } finally { setScanning(false) }
  }, [])

  const pollStatus = useCallback(async () => {
    try {
      const statuses = await getAppStatuses()
      const map = new Map(statuses.map(s => [s.id, s]))
      setApps(prev => prev.map(a => {
        const s = map.get(a.id)
        return s ? { ...a, isRunning: s.isRunning, hasException: s.hasException } : a
      }))
    } catch { /* server offline */ }
  }, [])

  useEffect(() => { scan() }, [scan])

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
    await startSelected(apps.filter(a => a.checked && !a.isRunning).map(a => a.id))
    await pollStatus()
  }

  const handleStopAll = async () => { await stopAll(); await pollStatus() }

  const groups      = [...new Set(apps.map(a => a.repoName))]
  const checkedCount = apps.filter(a => a.checked).length
  const runningCount = apps.filter(a => a.isRunning).length

  return (
    <div className="apps-layout">
      {/* ── Sidebar ── */}
      <div className="apps-sidebar">
        <div className="apps-toolbar">
          <button className="btn" onClick={scan} disabled={scanning}>
            {scanning ? '⟳' : '⟳ Scanna'}
          </button>
          <button className="btn primary" onClick={handleStartSelected} disabled={checkedCount === 0}>
            ▶ Starta ({checkedCount})
          </button>
          <button className="btn danger" onClick={handleStopAll}>■ Stoppa alla</button>
          <button className="btn" onClick={handleGitRefresh} disabled={gitLoading || apps.length === 0} title="Uppdatera git-status">
            {gitLoading ? '⟳' : '⟳ Git'}
          </button>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0 8px', fontSize: 12 }}>
          <label className="cb">
            <input
              type="checkbox"
              checked={apps.length > 0 && apps.every(a => a.checked)}
              onChange={e => setApps(prev => prev.map(a => ({ ...a, checked: e.target.checked })))}
            />
            <span style={{ color: 'var(--muted)' }}>Välj alla</span>
          </label>
          <span style={{ marginLeft: 'auto', color: 'var(--muted)' }}>{runningCount}/{apps.length} kör</span>
        </div>

        {apps.length === 0 && !scanning && (
          <div style={{ color: 'var(--muted)', fontSize: 12 }}>
            Tryck Scanna, eller kontrollera repo-rot i Inställningar.
          </div>
        )}

        <div className="apps-list">
          {groups.map(repoName => {
            const repoApps  = apps.filter(a => a.repoName === repoName)
            const gitStatus = repoApps[0]?.gitStatus ?? '–'
            const gitClass  = gitStatus === 'Uppdaterad' ? 'ok' : gitStatus === 'Bakom' ? 'behind' : 'miss'
            const allChecked = repoApps.every(a => a.checked)

            return (
              <div key={repoName} className="app-group">
                <div className="app-group-name">
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
                    selected={app.id === selectedId}
                    onSelect={() => setSelectedId(id => id === app.id ? null : app.id)}
                    onCheck={checked => setApps(prev => prev.map(a => a.id === app.id ? { ...a, checked } : a))}
                    onRefresh={pollStatus}
                  />
                ))}
              </div>
            )
          })}
        </div>
      </div>

      {/* ── Debug console ── */}
      <div className="apps-console">
        {selectedId
          ? <DebugConsole key={selectedId} id={selectedId} name={apps.find(a => a.id === selectedId)?.projectName ?? selectedId} />
          : <div style={{ color: 'var(--muted)', fontSize: 12, padding: 16, fontFamily: 'var(--mono)' }}>
              // Klicka på en applikation för att se debug-output
            </div>
        }
      </div>
    </div>
  )
}

// ── App row ───────────────────────────────────────────────────────────────────

function AppRow({ app, selected, onSelect, onCheck, onRefresh }: {
  app: AppState
  selected: boolean
  onSelect: () => void
  onCheck: (v: boolean) => void
  onRefresh: () => void
}) {
  const [busy, setBusy] = useState(false)

  const act = async (fn: () => Promise<void>) => {
    setBusy(true)
    try { await fn(); await onRefresh() }
    finally { setBusy(false) }
  }

  const dotClass = app.hasException ? 'exception' : app.isRunning ? 'running' : 'stopped'

  return (
    <div
      className={`app-row ${selected ? 'app-row-selected' : ''}`}
      onClick={onSelect}
      style={{ cursor: 'pointer' }}
    >
      <input
        type="checkbox"
        checked={app.checked}
        onChange={e => { e.stopPropagation(); onCheck(e.target.checked) }}
        onClick={e => e.stopPropagation()}
        style={{ accentColor: 'var(--blue)', width: 13, height: 13, cursor: 'pointer', flexShrink: 0 }}
      />
      <div className={`app-dot ${dotClass}`} title={app.hasException ? 'Exception!' : app.isRunning ? 'Kör' : 'Stoppad'} />
      <span className="app-name">{app.projectName}</span>
      <span className="app-port">{app.httpsPort ? `:${app.httpsPort}` : ''}</span>
      <div className="app-actions" onClick={e => e.stopPropagation()}>
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

// ── Debug console ─────────────────────────────────────────────────────────────

function DebugConsole({ id, name }: { id: string; name: string }) {
  const [lines, setLines]     = useState<string[]>([])
  const logRef                = useRef<HTMLDivElement>(null)
  const sourceRef             = useRef<EventSource | null>(null)

  useEffect(() => {
    setLines([])
    sourceRef.current?.close()
    sourceRef.current = streamAppOutput(id, line => {
      setLines(prev => {
        const next = [...prev, line]
        return next.length > 2000 ? next.slice(-2000) : next
      })
    })
    return () => { sourceRef.current?.close() }
  }, [id])

  // Auto-scroll till botten
  useEffect(() => {
    const el = logRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [lines])

  const colorLine = (line: string) => {
    if (/exception|unhandled|crit:/i.test(line)) return 'var(--red)'
    if (/error|fail:/i.test(line))              return '#e88'
    if (/warn:/i.test(line))                    return 'var(--yellow)'
    if (/info:/i.test(line))                    return 'var(--text)'
    if (/dbug:|trce:/i.test(line))              return '#666'
    return 'var(--text)'
  }

  return (
    <div className="debug-console">
      <div className="debug-console-header">
        <span style={{ color: 'var(--blue)', fontWeight: 600 }}>{name}</span>
        <button className="btn sm" onClick={() => setLines([])}>Rensa</button>
      </div>
      <div className="build-log" ref={logRef}>
        {lines.length === 0
          ? <span style={{ color: 'var(--muted)' }}>Väntar på output...</span>
          : lines.map((line, i) => (
              <div key={i} style={{ color: colorLine(line) }}>{line || '\u00A0'}</div>
            ))
        }
      </div>
    </div>
  )
}
