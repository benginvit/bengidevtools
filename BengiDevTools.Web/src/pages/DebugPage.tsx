import { useEffect, useRef, useState } from 'react'
import {
  executeSql, getSwagger, getScenarios, saveScenario, deleteScenario,
  getDebugScripts,
} from '../api'
import type { Scenario, SwaggerPath } from '../types'

// A "Job" is either a SQL shortcut or an API call
interface Job {
  id: string
  name: string
  type: 'sql' | 'api'
  // SQL
  sql: string
  // API
  appId: string
  method: string
  url: string
  body: string
}

const EMPTY_JOB: Job = { id: '', name: '', type: 'sql', sql: '', appId: '', method: 'GET', url: '', body: '' }

// Map Scenario ↔ Job (reuse same backend storage)
function toJob(s: Scenario): Job {
  return {
    id:     s.id,
    name:   s.name,
    type:   (s.headers?.['x-job-type'] as 'sql' | 'api') ?? 'api',
    sql:    s.headers?.['x-sql'] ?? '',
    appId:  s.appId,
    method: s.method,
    url:    s.url,
    body:   s.body,
  }
}

function toScenario(j: Job): Scenario {
  return {
    id:      j.id || crypto.randomUUID(),
    name:    j.name,
    appId:   j.appId,
    method:  j.method,
    url:     j.url,
    body:    j.body,
    headers: { 'x-job-type': j.type, 'x-sql': j.sql },
  }
}

export default function DebugPage() {
  const [jobs,        setJobs]        = useState<Job[]>([])
  const [selected,    setSelected]    = useState<Job | null>(null)
  const [form,        setForm]        = useState<Job>({ ...EMPTY_JOB })
  const [swagger,     setSwagger]     = useState<SwaggerPath[]>([])
  const [loadingSwagger, setLoadingSwagger] = useState(false)
  const [sqlResult,   setSqlResult]   = useState<SqlResult | null>(null)
  const [apiResult,   setApiResult]   = useState<ApiResult | null>(null)
  const [running,     setRunning]     = useState(false)
  const [saving,      setSaving]      = useState(false)

  // Running apps for the app picker
  const appsRef = useRef<{ id: string; projectName: string; httpsPort: number | null }[]>([])

  const loadJobs = async () => {
    const scenarios = await getScenarios()
    setJobs(scenarios.map(toJob))
  }

  useEffect(() => {
    loadJobs()
    fetch('/api/apps/status').then(r => r.json()).then((statuses: { id: string; isRunning: boolean }[]) =>
      fetch('/api/apps/scan').then(r => r.json()).then((apps: typeof appsRef.current) => {
        appsRef.current = apps.filter(a => statuses.find(s => s.id === a.id)?.isRunning)
      })
    )
  }, [])

  const selectJob = (j: Job) => {
    setSelected(j)
    setForm({ ...j })
    setSqlResult(null)
    setApiResult(null)
    setSwagger([])
  }

  const handleNew = (type: 'sql' | 'api') => {
    const j: Job = { ...EMPTY_JOB, id: crypto.randomUUID(), type }
    setSelected(j)
    setForm({ ...j })
    setSqlResult(null)
    setApiResult(null)
    setSwagger([])
  }

  const handleSave = async () => {
    setSaving(true)
    const s = toScenario(form.id ? form : { ...form, id: crypto.randomUUID() })
    await saveScenario(s)
    await loadJobs()
    setSelected(toJob(s))
    setForm(toJob(s))
    setSaving(false)
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Ta bort jobbet?')) return
    await deleteScenario(id)
    await loadJobs()
    if (selected?.id === id) { setSelected(null); setForm({ ...EMPTY_JOB }) }
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
    setSqlResult(null)
    setApiResult(null)

    if (form.type === 'sql') {
      const r = await executeSql(form.sql)
      setSqlResult(r)
    } else {
      const start = Date.now()
      try {
        const res = await fetch(form.url, {
          method: form.method,
          headers: { 'Content-Type': 'application/json' },
          body: ['GET', 'HEAD'].includes(form.method) ? undefined : form.body || undefined,
        })
        const text = await res.text()
        setApiResult({ ok: res.ok, status: res.status, statusText: res.statusText, body: text, ms: Date.now() - start })
      } catch (e) {
        setApiResult({ ok: false, status: 0, statusText: 'Nätverksfel', body: String(e), ms: Date.now() - start })
      }
    }

    setRunning(false)
  }

  const sqlJobs = jobs.filter(j => j.type === 'sql')
  const apiJobs = jobs.filter(j => j.type === 'api')

  // Import existing SQL scripts as jobs
  const handleImportScripts = async () => {
    const scripts = await getDebugScripts()
    for (const s of scripts) {
      const content = await fetch(`/api/debug/script?path=${encodeURIComponent(s.path)}`).then(r => r.json())
      const job: Job = { id: crypto.randomUUID(), name: s.name.replace('.sql', ''), type: 'sql', sql: content.content, appId: '', method: 'GET', url: '', body: '' }
      await saveScenario(toScenario(job))
    }
    await loadJobs()
  }

  return (
    <div className="debug-page">
      <div className="debug-layout">
        {/* ── Sidebar ── */}
        <div className="debug-sidebar">
          <div style={{ display: 'flex', gap: 4, marginBottom: 8 }}>
            <button className="btn sm primary" onClick={() => handleNew('sql')} title="Nytt SQL-jobb">🗄 SQL</button>
            <button className="btn sm primary" onClick={() => handleNew('api')} title="Nytt API-jobb">⚡ API</button>
            <button className="btn sm" onClick={handleImportScripts} title="Importera SQL-filer från disk" style={{ marginLeft: 'auto' }}>↑ Imp</button>
          </div>

          {sqlJobs.length > 0 && (
            <div className="debug-script-group">
              <div className="debug-group-label">🗄 SQL</div>
              {sqlJobs.map(j => (
                <JobRow key={j.id} job={j} selected={selected?.id === j.id}
                  onSelect={() => selectJob(j)} onDelete={() => handleDelete(j.id)}
                  onRun={async () => { selectJob(j); setRunning(true); setSqlResult(null); const r = await executeSql(j.sql); setSqlResult(r); setRunning(false) }}
                />
              ))}
            </div>
          )}

          {apiJobs.length > 0 && (
            <div className="debug-script-group">
              <div className="debug-group-label">⚡ API</div>
              {apiJobs.map(j => (
                <JobRow key={j.id} job={j} selected={selected?.id === j.id}
                  onSelect={() => selectJob(j)} onDelete={() => handleDelete(j.id)}
                  onRun={async () => {
                    selectJob(j)
                    setRunning(true); setApiResult(null)
                    const start = Date.now()
                    try {
                      const res = await fetch(j.url, { method: j.method, headers: { 'Content-Type': 'application/json' }, body: ['GET','HEAD'].includes(j.method) ? undefined : j.body || undefined })
                      const text = await res.text()
                      setApiResult({ ok: res.ok, status: res.status, statusText: res.statusText, body: text, ms: Date.now() - start })
                    } catch (e) {
                      setApiResult({ ok: false, status: 0, statusText: 'Nätverksfel', body: String(e), ms: Date.now() - start })
                    }
                    setRunning(false)
                  }}
                />
              ))}
            </div>
          )}

          {jobs.length === 0 && (
            <div style={{ color: 'var(--muted)', fontSize: 11, padding: 4, lineHeight: 1.6 }}>
              Skapa ett jobb med knapparna ovan.<br />
              SQL = kör mot databasen.<br />
              API = anrop mot en körande app.
            </div>
          )}
        </div>

        {/* ── Editor / Runner ── */}
        <div className="debug-editor-panel">
          {selected ? (
            <div className="scenario-form">
              {/* Header row */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span style={{ fontSize: 11, color: 'var(--muted)' }}>{form.type === 'sql' ? '🗄 SQL' : '⚡ API'}</span>
                <input
                  className="input"
                  style={{ flex: 1 }}
                  value={form.name}
                  onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                  placeholder="Namn på jobbet..."
                />
                <button className="btn sm primary" onClick={handleRun} disabled={running || (form.type === 'sql' ? !form.sql.trim() : !form.url.trim())}>
                  {running ? '⟳' : '▶ Kör'}
                </button>
                <button className="btn sm" onClick={handleSave} disabled={saving}>
                  {saving ? '...' : '💾'}
                </button>
                <button className="btn sm danger" onClick={() => selected?.id && handleDelete(selected.id)} title="Ta bort">✕</button>
              </div>

              {/* SQL editor */}
              {form.type === 'sql' && (
                <textarea
                  className="debug-sql-editor"
                  style={{ flex: 1, minHeight: 200 }}
                  value={form.sql}
                  onChange={e => setForm(f => ({ ...f, sql: e.target.value }))}
                  spellCheck={false}
                  placeholder="-- Skriv SQL här..."
                  autoFocus
                />
              )}

              {/* API form */}
              {form.type === 'api' && (
                <>
                  <div className="scenario-row">
                    <label className="form-label" style={{ width: 60, flexShrink: 0 }}>App</label>
                    <select className="input" value={form.appId}
                      onChange={e => { setForm(f => ({ ...f, appId: e.target.value })); setSwagger([]) }}>
                      <option value="">— välj körande app —</option>
                      {appsRef.current.map(a => (
                        <option key={a.id} value={a.id}>{a.projectName} :{a.httpsPort}</option>
                      ))}
                    </select>
                    <button className="btn sm" onClick={handleLoadSwagger} disabled={!form.appId || loadingSwagger}>
                      {loadingSwagger ? '⟳' : '⚡ Swagger'}
                    </button>
                  </div>

                  {swagger.length > 0 && (
                    <div className="scenario-row">
                      <label className="form-label" style={{ width: 60, flexShrink: 0 }}>Endpoint</label>
                      <select className="input" defaultValue="" onChange={e => {
                        const selected = swagger.find(p => `${p.method} ${p.path}` === e.target.value)
                        if (!selected) return
                        const app = appsRef.current.find(a => a.id === form.appId)
                        const base = app?.httpsPort ? `https://localhost:${app.httpsPort}` : ''
                        setForm(f => ({
                          ...f,
                          method: selected.method,
                          url: base + selected.path,
                          body: selected.exampleBody ?? f.body,
                        }))
                      }}>
                        <option value="">— välj endpoint —</option>
                        {swagger.map(p => (
                          <option key={p.method + p.path} value={`${p.method} ${p.path}`}>
                            {p.method} {p.path}{p.summary ? ` — ${p.summary}` : ''}
                            {p.exampleBody ? ' ✦' : ''}
                          </option>
                        ))}
                      </select>
                    </div>
                  )}

                  <div className="scenario-row">
                    <select className="input" style={{ width: 90, flexShrink: 0 }} value={form.method}
                      onChange={e => setForm(f => ({ ...f, method: e.target.value }))}>
                      {['GET','POST','PUT','PATCH','DELETE'].map(m => <option key={m}>{m}</option>)}
                    </select>
                    <input className="input" value={form.url} onChange={e => setForm(f => ({ ...f, url: e.target.value }))}
                      placeholder="https://localhost:7801/api/..." />
                  </div>

                  {!['GET','HEAD'].includes(form.method) && (
                    <textarea
                      className="debug-sql-editor"
                      style={{ flex: 1, minHeight: 120 }}
                      value={form.body}
                      onChange={e => setForm(f => ({ ...f, body: e.target.value }))}
                      placeholder={'{\n  \n}'}
                      spellCheck={false}
                    />
                  )}
                </>
              )}

              {/* Results */}
              {sqlResult  && <SqlResultView  result={sqlResult}  />}
              {apiResult  && <ApiResultView  result={apiResult}  />}
            </div>
          ) : (
            <div style={{ color: 'var(--muted)', fontSize: 12, padding: 20, lineHeight: 1.8 }}>
              Välj ett jobb i sidebaren eller skapa ett nytt.<br />
              <span style={{ color: '#555' }}>▶</span> i listan kör jobbet direkt utan att öppna det.
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Job row ───────────────────────────────────────────────────────────────────

function JobRow({ job, selected, onSelect, onDelete, onRun }: {
  job: Job; selected: boolean
  onSelect: () => void; onDelete: () => void; onRun: () => void
}) {
  return (
    <div className={`debug-script-row ${selected ? 'selected' : ''}`} onClick={onSelect}>
      {job.type === 'api' && (
        <span className="debug-method-badge" data-method={job.method}>{job.method}</span>
      )}
      <span className="debug-script-name">{job.name || (job.type === 'api' ? job.url : '(namnlöst)') }</span>
      <button className="debug-run-btn" title="Kör" onClick={e => { e.stopPropagation(); onRun() }}>▶</button>
      <button className="debug-delete-btn" title="Ta bort" onClick={e => { e.stopPropagation(); onDelete() }}>✕</button>
    </div>
  )
}

// ── Result views ──────────────────────────────────────────────────────────────

type SqlResult = {
  success: boolean; error?: string
  results?: Array<{ type: 'select'; columns: string[]; rows: Record<string, unknown>[] } | { type: 'nonquery'; rowsAffected: number }>
}

function SqlResultView({ result }: { result: SqlResult }) {
  if (!result.success)
    return <div className="sql-result error" style={{ flexShrink: 0 }}>❌ {result.error}</div>
  return (
    <div className="sql-result" style={{ flexShrink: 0 }}>
      {result.results?.map((r, i) =>
        r.type === 'nonquery' ? (
          <div key={i} className="sql-result-info">✅ {r.rowsAffected} rad(er) påverkade</div>
        ) : (
          <div key={i} className="sql-result-table-wrap">
            <table className="sql-table">
              <thead><tr>{r.columns.map(c => <th key={c}>{c}</th>)}</tr></thead>
              <tbody>{r.rows.map((row, ri) => (
                <tr key={ri}>{r.columns.map(c => <td key={c}>{String(row[c] ?? '')}</td>)}</tr>
              ))}</tbody>
            </table>
            <div className="sql-result-info">{r.rows.length} rad(er)</div>
          </div>
        )
      )}
    </div>
  )
}

type ApiResult = { ok: boolean; status: number; statusText: string; body: string; ms: number }

function ApiResultView({ result }: { result: ApiResult }) {
  let pretty = result.body
  try { pretty = JSON.stringify(JSON.parse(result.body), null, 2) } catch { /* leave as-is */ }
  return (
    <div className={`sql-result ${result.ok ? '' : 'error'}`} style={{ flexShrink: 0 }}>
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
