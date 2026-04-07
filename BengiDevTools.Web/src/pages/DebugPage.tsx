import { useCallback, useEffect, useRef, useState } from 'react'
import {
  getDebugScripts, getDebugScript, saveDebugScript, newDebugScript, deleteDebugScript,
  executeSql, getSwagger, getScenarios, saveScenario, deleteScenario,
} from '../api'
import type { DebugScript, Scenario, SwaggerPath } from '../types'

type SubTab = 'sql' | 'scenarios'

export default function DebugPage() {
  const [subTab, setSubTab] = useState<SubTab>('sql')

  return (
    <div className="debug-page">
      <div className="debug-subtabs">
        <button className={subTab === 'sql'       ? 'active' : ''} onClick={() => setSubTab('sql')}>SQL Scripts</button>
        <button className={subTab === 'scenarios' ? 'active' : ''} onClick={() => setSubTab('scenarios')}>API Scenarion</button>
      </div>
      {subTab === 'sql'       && <SqlPanel />}
      {subTab === 'scenarios' && <ScenariosPanel />}
    </div>
  )
}

// ── SQL Panel ─────────────────────────────────────────────────────────────────

function SqlPanel() {
  const [scripts,     setScripts]     = useState<DebugScript[]>([])
  const [selectedPath, setSelected]   = useState<string | null>(null)
  const [content,     setContent]     = useState('')
  const [dirty,       setDirty]       = useState(false)
  const [saving,      setSaving]      = useState(false)
  const [running,     setRunning]      = useState(false)
  const [result,      setResult]      = useState<SqlResult | null>(null)
  const [newName,     setNewName]     = useState('')
  const [newType,     setNewType]     = useState<'clean' | 'feed'>('feed')
  const [showNew,     setShowNew]     = useState(false)

  const load = useCallback(async () => {
    setScripts(await getDebugScripts())
  }, [])

  useEffect(() => { load() }, [load])

  const selectScript = async (path: string) => {
    setSelected(path)
    setResult(null)
    const r = await getDebugScript(path)
    setContent(r.content)
    setDirty(false)
  }

  const handleSave = async () => {
    if (!selectedPath) return
    setSaving(true)
    await saveDebugScript(selectedPath, content)
    setDirty(false)
    setSaving(false)
  }

  const handleRun = async () => {
    setRunning(true)
    setResult(null)
    const r = await executeSql(content)
    setResult(r)
    setRunning(false)
  }

  const handleNew = async () => {
    if (!newName.trim()) return
    const r = await newDebugScript(newName.trim(), newType)
    await load()
    setShowNew(false)
    setNewName('')
    selectScript(r.path)
  }

  const handleDelete = async (path: string) => {
    if (!confirm('Ta bort filen?')) return
    await deleteDebugScript(path)
    if (selectedPath === path) { setSelected(null); setContent('') }
    await load()
  }

  const groups = [
    { type: 'clean', label: 'Rensa' },
    { type: 'feed',  label: 'Feed'  },
    { type: 'other', label: 'Övrigt' },
  ]

  return (
    <div className="debug-layout">
      {/* Sidebar */}
      <div className="debug-sidebar">
        <button className="btn sm primary" style={{ margin: '0 0 8px' }} onClick={() => setShowNew(v => !v)}>
          + Ny fil
        </button>

        {showNew && (
          <div className="debug-new-form">
            <input
              className="input"
              placeholder="filnamn"
              value={newName}
              onChange={e => setNewName(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleNew()}
              autoFocus
            />
            <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
              <select className="input" value={newType} onChange={e => setNewType(e.target.value as 'clean' | 'feed')}
                style={{ flex: 1 }}>
                <option value="clean">Rensa</option>
                <option value="feed">Feed</option>
              </select>
              <button className="btn sm primary" onClick={handleNew}>OK</button>
              <button className="btn sm" onClick={() => setShowNew(false)}>✕</button>
            </div>
          </div>
        )}

        {groups.map(g => {
          const items = scripts.filter(s => s.type === g.type)
          if (items.length === 0) return null
          return (
            <div key={g.type} className="debug-script-group">
              <div className="debug-group-label">{g.label}</div>
              {items.map(s => (
                <div
                  key={s.path}
                  className={`debug-script-row ${selectedPath === s.path ? 'selected' : ''}`}
                  onClick={() => selectScript(s.path)}
                >
                  <span className="debug-script-name">{s.name}</span>
                  <button
                    className="debug-delete-btn"
                    onClick={e => { e.stopPropagation(); handleDelete(s.path) }}
                    title="Ta bort"
                  >✕</button>
                </div>
              ))}
            </div>
          )
        })}

        {scripts.length === 0 && (
          <div style={{ color: 'var(--muted)', fontSize: 11, padding: 4 }}>
            Inga scripts. Kontrollera Debug-scripts mapp i Inställningar.
          </div>
        )}
      </div>

      {/* Editor */}
      <div className="debug-editor-panel">
        {selectedPath ? (
          <>
            <div className="debug-editor-toolbar">
              <span style={{ color: 'var(--muted)', fontSize: 11 }}>
                {selectedPath.split(/[/\\]/).slice(-2).join('/')}
                {dirty && <span style={{ color: 'var(--yellow)', marginLeft: 6 }}>●</span>}
              </span>
              <div style={{ display: 'flex', gap: 6 }}>
                <button className="btn sm" onClick={handleSave} disabled={saving || !dirty}>
                  {saving ? '...' : 'Spara'}
                </button>
                <button className="btn sm primary" onClick={handleRun} disabled={running || !content.trim()}>
                  {running ? '⟳ Kör...' : '▶ Kör'}
                </button>
              </div>
            </div>
            <textarea
              className="debug-sql-editor"
              value={content}
              onChange={e => { setContent(e.target.value); setDirty(true) }}
              spellCheck={false}
              placeholder="-- Skriv SQL här..."
            />
            {result && <SqlResultView result={result} />}
          </>
        ) : (
          <div style={{ color: 'var(--muted)', fontSize: 12, padding: 16 }}>
            Välj ett script i sidebaren, eller skapa ett nytt.
          </div>
        )}
      </div>
    </div>
  )
}

type SqlResult = { success: boolean; error?: string; results?: SqlBatchResult[] }
type SqlBatchResult =
  | { type: 'select'; columns: string[]; rows: Record<string, unknown>[] }
  | { type: 'nonquery'; rowsAffected: number }

function SqlResultView({ result }: { result: SqlResult }) {
  if (!result.success)
    return <div className="sql-result error">❌ {result.error}</div>

  return (
    <div className="sql-result">
      {result.results?.map((r, i) =>
        r.type === 'nonquery' ? (
          <div key={i} className="sql-result-info">✅ {r.rowsAffected} rad(er) påverkade</div>
        ) : (
          <div key={i} className="sql-result-table-wrap">
            <table className="sql-table">
              <thead>
                <tr>{r.columns.map(c => <th key={c}>{c}</th>)}</tr>
              </thead>
              <tbody>
                {r.rows.map((row, ri) => (
                  <tr key={ri}>
                    {r.columns.map(c => <td key={c}>{String(row[c] ?? '')}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="sql-result-info">{r.rows.length} rad(er)</div>
          </div>
        )
      )}
    </div>
  )
}

// ── Scenarios Panel ───────────────────────────────────────────────────────────

const EMPTY_SCENARIO: Scenario = {
  id: '', name: '', appId: '', method: 'GET', url: '', body: '', headers: {},
}

function ScenariosPanel() {
  const [scenarios,   setScenarios]   = useState<Scenario[]>([])
  const [selected,    setSelected]    = useState<Scenario | null>(null)
  const [form,        setForm]        = useState<Scenario>({ ...EMPTY_SCENARIO })
  const [swagger,     setSwagger]     = useState<SwaggerPath[]>([])
  const [runResult,   setRunResult]   = useState<RunResult | null>(null)
  const [running,     setRunning]     = useState(false)
  const [saving,      setSaving]      = useState(false)
  const [loadingSwagger, setLoadingSwagger] = useState(false)
  const appsRef = useRef<{ id: string; projectName: string; httpsPort: number | null; isRunning: boolean }[]>([])

  useEffect(() => {
    getScenarios().then(setScenarios)
    // Fetch running apps for the app picker
    fetch('/api/apps/status').then(r => r.json()).then((statuses: { id: string; isRunning: boolean }[]) => {
      fetch('/api/apps/scan').then(r => r.json()).then((apps: typeof appsRef.current) => {
        appsRef.current = apps.filter(a => statuses.find(s => s.id === a.id)?.isRunning)
      })
    })
  }, [])

  const selectScenario = (s: Scenario) => {
    setSelected(s)
    setForm({ ...s })
    setRunResult(null)
    setSwagger([])
  }

  const handleNew = () => {
    const s: Scenario = { ...EMPTY_SCENARIO, id: crypto.randomUUID() }
    setSelected(s)
    setForm({ ...s })
    setRunResult(null)
    setSwagger([])
  }

  const handleSave = async () => {
    setSaving(true)
    const s = form.id ? form : { ...form, id: crypto.randomUUID() }
    await saveScenario(s)
    setScenarios(await getScenarios())
    setSelected(s)
    setForm(s)
    setSaving(false)
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Ta bort scenario?')) return
    await deleteScenario(id)
    setScenarios(await getScenarios())
    if (selected?.id === id) { setSelected(null); setForm({ ...EMPTY_SCENARIO }) }
  }

  const handleLoadSwagger = async () => {
    if (!form.appId) return
    setLoadingSwagger(true)
    const r = await getSwagger(form.appId)
    setSwagger(r)
    setLoadingSwagger(false)
  }

  const handleRun = async () => {
    setRunning(true)
    setRunResult(null)
    const start = Date.now()
    try {
      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        ...form.headers,
      }
      const res = await fetch(form.url, {
        method: form.method,
        headers,
        body: ['GET', 'HEAD'].includes(form.method) ? undefined : form.body || undefined,
      })
      const text = await res.text()
      setRunResult({ ok: res.ok, status: res.status, statusText: res.statusText, body: text, ms: Date.now() - start })
    } catch (e: unknown) {
      setRunResult({ ok: false, status: 0, statusText: 'Network error', body: String(e), ms: Date.now() - start })
    }
    setRunning(false)
  }

  return (
    <div className="debug-layout">
      {/* Sidebar */}
      <div className="debug-sidebar">
        <button className="btn sm primary" style={{ margin: '0 0 8px' }} onClick={handleNew}>
          + Nytt scenario
        </button>
        {scenarios.map(s => (
          <div
            key={s.id}
            className={`debug-script-row ${selected?.id === s.id ? 'selected' : ''}`}
            onClick={() => selectScenario(s)}
          >
            <span className="debug-method-badge" data-method={s.method}>{s.method}</span>
            <span className="debug-script-name" style={{ flex: 1 }}>{s.name || s.url || 'Namnlöst'}</span>
            <button
              className="debug-delete-btn"
              onClick={e => { e.stopPropagation(); handleDelete(s.id) }}
              title="Ta bort"
            >✕</button>
          </div>
        ))}
        {scenarios.length === 0 && (
          <div style={{ color: 'var(--muted)', fontSize: 11, padding: 4 }}>
            Inga scenarion sparade ännu.
          </div>
        )}
      </div>

      {/* Form */}
      <div className="debug-editor-panel">
        {selected ? (
          <div className="scenario-form">
            <div className="scenario-row">
              <label className="form-label" style={{ width: 80 }}>Namn</label>
              <input className="input" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder="Beskriv scenariot" />
            </div>

            <div className="scenario-row">
              <label className="form-label" style={{ width: 80 }}>App</label>
              <select className="input" value={form.appId} onChange={e => { setForm(f => ({ ...f, appId: e.target.value })); setSwagger([]) }}>
                <option value="">— välj app —</option>
                {appsRef.current.map(a => (
                  <option key={a.id} value={a.id}>{a.projectName} :{a.httpsPort}</option>
                ))}
              </select>
              <button className="btn sm" onClick={handleLoadSwagger} disabled={!form.appId || loadingSwagger} title="Hämta endpoints från Swagger">
                {loadingSwagger ? '⟳' : '⚡ Swagger'}
              </button>
            </div>

            {swagger.length > 0 && (
              <div className="scenario-row">
                <label className="form-label" style={{ width: 80 }}>Endpoint</label>
                <select className="input" onChange={e => {
                  const [method, path] = e.target.value.split(' ', 2)
                  const app = appsRef.current.find(a => a.id === form.appId)
                  const base = app?.httpsPort ? `https://localhost:${app.httpsPort}` : ''
                  setForm(f => ({ ...f, method, url: base + path }))
                }}>
                  <option value="">— välj endpoint —</option>
                  {swagger.map(p => (
                    <option key={p.method + p.path} value={`${p.method} ${p.path}`}>
                      {p.method} {p.path}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <div className="scenario-row">
              <label className="form-label" style={{ width: 80 }}>Metod</label>
              <select className="input" style={{ width: 100 }} value={form.method} onChange={e => setForm(f => ({ ...f, method: e.target.value }))}>
                {['GET','POST','PUT','PATCH','DELETE'].map(m => <option key={m}>{m}</option>)}
              </select>
            </div>

            <div className="scenario-row">
              <label className="form-label" style={{ width: 80 }}>URL</label>
              <input className="input" value={form.url} onChange={e => setForm(f => ({ ...f, url: e.target.value }))} placeholder="https://localhost:7801/api/..." />
            </div>

            {!['GET','HEAD'].includes(form.method) && (
              <div className="scenario-col">
                <label className="form-label">Body (JSON)</label>
                <textarea
                  className="debug-sql-editor"
                  style={{ minHeight: 120, maxHeight: 240 }}
                  value={form.body}
                  onChange={e => setForm(f => ({ ...f, body: e.target.value }))}
                  placeholder={'{\n  \n}'}
                  spellCheck={false}
                />
              </div>
            )}

            <div style={{ display: 'flex', gap: 6, marginTop: 8 }}>
              <button className="btn sm primary" onClick={handleRun} disabled={running || !form.url}>
                {running ? '⟳ Kör...' : '▶ Kör'}
              </button>
              <button className="btn sm" onClick={handleSave} disabled={saving}>
                {saving ? '...' : '💾 Spara'}
              </button>
            </div>

            {runResult && <RunResultView result={runResult} />}
          </div>
        ) : (
          <div style={{ color: 'var(--muted)', fontSize: 12, padding: 16 }}>
            Välj ett scenario eller skapa ett nytt.
          </div>
        )}
      </div>
    </div>
  )
}

type RunResult = { ok: boolean; status: number; statusText: string; body: string; ms: number }

function RunResultView({ result }: { result: RunResult }) {
  let pretty = result.body
  try { pretty = JSON.stringify(JSON.parse(result.body), null, 2) } catch { /* leave as-is */ }

  return (
    <div className={`sql-result ${result.ok ? '' : 'error'}`}>
      <div className="sql-result-info">
        <span style={{ color: result.ok ? 'var(--green)' : 'var(--red)', fontWeight: 600 }}>
          {result.status} {result.statusText}
        </span>
        <span style={{ marginLeft: 12, color: 'var(--muted)' }}>{result.ms}ms</span>
      </div>
      <pre className="sql-result-body">{pretty}</pre>
    </div>
  )
}
