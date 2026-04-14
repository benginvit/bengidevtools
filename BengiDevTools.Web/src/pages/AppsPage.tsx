import { useCallback, useEffect, useRef, useState } from 'react'
import {
  scanApps, loadApps, getScanInfo, getAppStatuses,
  startApp, stopApp, restartApp,
  startSelected, stopAll, startGitRefresh,
  streamAppOutput, getLocalUser, saveLocalUser, exportLocalUserUrl,
} from '../api'
import type { ScannedApp } from '../types'

interface AppState extends ScannedApp {
  checked: boolean
  hasException: boolean
  isExternal: boolean
  pid: number
}

export default function AppsPage() {
  const [apps, setApps]               = useState<AppState[]>([])
  const [scanning, setScanning]       = useState(false)
  const [gitLoading, setGitLoading]   = useState(false)
  const [selectedId, setSelectedId]   = useState<string | null>(null)
  const [localUserEditId, setLocalUserEditId] = useState<string | null>(null)
  const [lastScanned, setLastScanned] = useState<string | null>(null)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const applyApps = useCallback((data: ScannedApp[]) => {
    setApps(prev => {
      const prevMap = new Map(prev.map(a => [a.id, a]))
      return data.map(a => ({
        ...a,
        checked:      prevMap.get(a.id)?.checked      ?? true,
        hasException: prevMap.get(a.id)?.hasException ?? false,
        isExternal:   false,
        pid:  -1,
      }))
    })
  }, [])

  // On mount: load from cache (no disk scan)
  const loadCached = useCallback(async () => {
    const [data, info] = await Promise.all([loadApps(), getScanInfo()])
    applyApps(data)
    setLastScanned(info.lastScanned)
  }, [applyApps])

  // Manual rescan button
  const scan = useCallback(async () => {
    setScanning(true)
    try {
      const data = await scanApps()
      applyApps(data)
      const info = await getScanInfo()
      setLastScanned(info.lastScanned)
    } finally { setScanning(false) }
  }, [applyApps])

  const pollStatus = useCallback(async () => {
    try {
      const statuses = await getAppStatuses()
      const map = new Map(statuses.map(s => [s.id, s]))
      setApps(prev => prev.map(a => {
        const s = map.get(a.id)
        return s ? { ...a, isRunning: s.isRunning, isExternal: s.isExternal, pid: s.pid, hasException: s.hasException, gitStatus: s.gitStatus } : a
      }))
    } catch { /* server offline */ }
  }, [])

  useEffect(() => { loadCached() }, [loadCached])

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
          <button className="btn" onClick={scan} disabled={scanning} title={lastScanned ? `Senast scannad: ${new Date(lastScanned).toLocaleString('sv-SE')}` : 'Aldrig scannad'}>
            {scanning ? '⟳' : '⟳ Scanna'}
          </button>
          {lastScanned && (
            <span style={{ fontSize: 10, color: 'var(--muted)', alignSelf: 'center' }}>
              {new Date(lastScanned).toLocaleDateString('sv-SE')}
            </span>
          )}
          <button className="btn primary" onClick={handleStartSelected} disabled={checkedCount === 0}>
            ▶ Starta ({checkedCount})
          </button>
          <button className="btn danger" onClick={handleStopAll}>■ Stoppa alla</button>
          <button className="btn" onClick={handleGitRefresh} disabled={gitLoading || apps.length === 0} title="Uppdatera git-status">
            {gitLoading ? '⟳' : '⟳ Git'}
          </button>
          <a
            className="btn"
            href={exportLocalUserUrl()}
            download="appsettings-localuser.zip"
            title="Exportera alla localuser-filer"
            style={{ textDecoration: 'none' }}
          >
            ↓ Exportera
          </a>
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
                    onEditLocalUser={() => setLocalUserEditId(app.id)}
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

      {/* ── LocalUser editor modal ── */}
      {localUserEditId && (
        <LocalUserModal
          id={localUserEditId}
          name={apps.find(a => a.id === localUserEditId)?.projectName ?? localUserEditId}
          onClose={() => setLocalUserEditId(null)}
          onSaved={() => {
            setLocalUserEditId(null)
            scan()
          }}
        />
      )}
    </div>
  )
}

// ── App row ───────────────────────────────────────────────────────────────────

function AppRow({ app, selected, onSelect, onCheck, onRefresh, onEditLocalUser }: {
  app: AppState
  selected: boolean
  onSelect: () => void
  onCheck: (v: boolean) => void
  onRefresh: () => void
  onEditLocalUser: () => void
}) {
  const [busy, setBusy] = useState(false)

  const act = async (fn: () => Promise<void>) => {
    setBusy(true)
    try { await fn(); await onRefresh() }
    finally { setBusy(false) }
  }

  const dotClass = app.hasException ? 'exception' : app.isExternal ? 'external' : app.isRunning ? 'running' : 'stopped'

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
      <div className={`app-dot ${dotClass}`} title={app.hasException ? 'Exception!' : app.isExternal ? 'Externt startad' : app.isRunning ? 'Kör' : 'Stoppad'} />
      <span className="app-name">{app.projectName}</span>
      <span className="app-port">{app.httpsPort ? `:${app.httpsPort}` : ''}</span>
      <div className="app-actions" onClick={e => e.stopPropagation()}>
        {app.isRunning && app.pid > 0 && (
          <button
            className="pid-badge"
            title="Kopiera PID (för Attach to Process i VS Code)"
            onClick={e => { e.stopPropagation(); navigator.clipboard.writeText(String(app.pid)) }}
          >
            {app.pid}
          </button>
        )}
        <button
          className="btn sm"
          title={app.hasLocalUser ? 'Redigera appsettings.localuser.json' : 'Skapa appsettings.localuser.json'}
          onClick={onEditLocalUser}
          style={{ opacity: app.hasLocalUser ? 1 : 0.4 }}
        >⚙</button>
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

// ── LocalUser modal ───────────────────────────────────────────────────────────

function LocalUserModal({ id, name, onClose, onSaved }: {
  id: string
  name: string
  onClose: () => void
  onSaved: () => void
}) {
  const [content, setContent] = useState('')
  const [exists, setExists]   = useState(false)
  const [path, setPath]       = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving]   = useState(false)
  const [error, setError]     = useState<string | null>(null)

  useEffect(() => {
    getLocalUser(id).then(r => {
      setContent(r.content ?? '{\n  \n}')
      setExists(r.exists)
      setPath(r.path)
      setLoading(false)
    })
  }, [id])

  const handleSave = async () => {
    setError(null)
    setSaving(true)
    try {
      await saveLocalUser(id, content)
      onSaved()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally { setSaving(false) }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <div>
            <div style={{ fontWeight: 600, color: 'var(--blue)' }}>{name}</div>
            <div style={{ fontSize: 10, color: 'var(--muted)', marginTop: 2 }}>appsettings.localuser.json</div>
            {path && <div style={{ fontSize: 10, color: '#555', marginTop: 1 }}>{path}</div>}
          </div>
          <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
            {!exists && <span style={{ fontSize: 11, color: 'var(--yellow)' }}>Ny fil</span>}
            <button className="btn sm primary" onClick={handleSave} disabled={saving || loading}>
              {saving ? '...' : exists ? 'Spara' : 'Skapa'}
            </button>
            <button className="btn sm" onClick={onClose}>Avbryt</button>
          </div>
        </div>
        {error && <div style={{ padding: '4px 12px', color: 'var(--red)', fontSize: 11 }}>{error}</div>}
        {loading
          ? <div style={{ padding: 16, color: 'var(--muted)', fontSize: 12 }}>Laddar...</div>
          : <textarea
              className="modal-editor"
              value={content}
              onChange={e => setContent(e.target.value)}
              spellCheck={false}
              autoFocus
            />
        }
      </div>
    </div>
  )
}

// ── Debug console ─────────────────────────────────────────────────────────────

function DebugConsole({ id, name }: { id: string; name: string }) {
  const [lines, setLines]     = useState<string[]>([])
  const logRef                = useRef<HTMLDivElement>(null)
  const ctrlRef               = useRef<AbortController | null>(null)

  useEffect(() => {
    setLines([])
    ctrlRef.current?.abort()
    ctrlRef.current = streamAppOutput(id, line => {
      setLines(prev => {
        const next = [...prev, line]
        return next.length > 2000 ? next.slice(-2000) : next
      })
    })
    return () => { ctrlRef.current?.abort() }
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
