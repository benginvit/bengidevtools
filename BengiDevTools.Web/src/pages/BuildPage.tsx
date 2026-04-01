import { useCallback, useEffect, useRef, useState } from 'react'
import { getRepos, startBuild } from '../api'
import type { RepoInfo } from '../types'

interface RepoState extends RepoInfo {
  selected: boolean
  status: string
}

export default function BuildPage() {
  const [repos, setRepos]             = useState<RepoState[]>([])
  const [repoLogs, setRepoLogs]       = useState<Record<string, string>>({})
  const [isBuilding, setIsBuilding]   = useState(false)
  const [noRestore, setNoRestore]     = useState(false)
  const [noAnalyzers, setNoAnalyzers] = useState(false)
  const [noDocs, setNoDocs]           = useState(false)
  const [parallel, setParallel]       = useState(false)
  const [snabb, setSnabb]             = useState(false)
  const abortRef    = useRef<AbortController | null>(null)
  const windowRefs  = useRef<Record<string, HTMLDivElement | null>>({})
  const logAreaRef  = useRef<HTMLDivElement>(null)

  const loadRepos = useCallback(async () => {
    const data = await getRepos()
    setRepos(r => {
      const prev = new Map(r.map(x => [x.repoName, x]))
      return data.map(d => ({
        ...d,
        selected: prev.get(d.repoName)?.selected ?? true,
        status:   prev.get(d.repoName)?.status   ?? '',
      }))
    })
  }, [])

  useEffect(() => { loadRepos() }, [loadRepos])

  const handleSnabb = (v: boolean) => {
    setSnabb(v)
    if (v) { setNoRestore(true); setNoAnalyzers(true); setNoDocs(true); setParallel(true) }
  }

  const scrollToRepo = (repoName: string) => {
    windowRefs.current[repoName]?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  const build = async () => {
    const selected = repos.filter(r => r.selected)
    if (selected.length === 0) return

    setIsBuilding(true)
    setRepoLogs({})
    setRepos(r => r.map(x => x.selected ? { ...x, status: '' } : x))

    abortRef.current = new AbortController()

    try {
      await startBuild(
        { repoNames: selected.map(r => r.repoName), noRestore, noAnalyzers, noDocs, parallel, snabb },
        (type, data) => {
          if (type === 'output' && data.repo) {
            setRepoLogs(prev => ({
              ...prev,
              [data.repo]: (prev[data.repo] ?? '') + (data.line ?? '') + '\n',
            }))
          }
          if (type === 'progress') {
            setRepos(r => r.map(x =>
              x.repoName === data.repo ? { ...x, status: data.status ?? '' } : x))
          }
        },
        abortRef.current.signal,
      )
    } catch (e: unknown) {
      if (e instanceof Error && e.name !== 'AbortError') {
        const errLine = '\n❌ Fel: ' + e.message + '\n'
        setRepoLogs(prev => {
          const upd = { ...prev }
          for (const r of repos.filter(x => x.selected))
            upd[r.repoName] = (upd[r.repoName] ?? '') + errLine
          return upd
        })
      }
    } finally {
      setIsBuilding(false)
    }
  }

  const cancel = () => abortRef.current?.abort()

  const succeeded = repos.filter(r => r.status === 'OK').length
  const failed    = repos.filter(r => r.status === 'FAILED').length
  const selected  = repos.filter(r => r.selected)

  return (
    <div className="build-layout">
      {/* ── Sidebar ── */}
      <div className="build-sidebar">
        <div className="build-flags">
          <label className="cb"><input type="checkbox" checked={noRestore}   onChange={e => setNoRestore(e.target.checked)}   />--no-restore</label>
          <label className="cb"><input type="checkbox" checked={noAnalyzers} onChange={e => setNoAnalyzers(e.target.checked)} />--no-analyzers</label>
          <label className="cb"><input type="checkbox" checked={noDocs}      onChange={e => setNoDocs(e.target.checked)}      />--no-docs</label>
          <label className="cb"><input type="checkbox" checked={parallel}    onChange={e => setParallel(e.target.checked)}    />Parallell</label>
          <label className="cb" style={{ color: 'var(--yellow)' }}>
            <input type="checkbox" checked={snabb} onChange={e => handleSnabb(e.target.checked)} />⚡ Snabb
          </label>
        </div>

        <div className="build-actions">
          <button className="btn primary" disabled={isBuilding} onClick={build}>
            {isBuilding ? '⟳ Bygger...' : '▶ Bygg'}
          </button>
          <button className="btn danger" disabled={!isBuilding} onClick={cancel}>■ Avbryt</button>
          <button className="btn sm" onClick={loadRepos} disabled={isBuilding} title="Ladda om repos">↺</button>
        </div>

        {(succeeded > 0 || failed > 0) && (
          <div style={{ fontSize: 12, display: 'flex', gap: 12 }}>
            {succeeded > 0 && <span style={{ color: 'var(--green)' }}>✅ {succeeded}</span>}
            {failed    > 0 && <span style={{ color: 'var(--red)'   }}>❌ {failed}</span>}
          </div>
        )}

        <div className="repo-select-actions">
          <button className="btn sm" onClick={() => setRepos(r => r.map(x => ({ ...x, selected: true  })))}>Alla</button>
          <button className="btn sm" onClick={() => setRepos(r => r.map(x => ({ ...x, selected: false })))}>Inga</button>
        </div>

        <div className="repo-list">
          {repos.length === 0 && (
            <div style={{ padding: '12px 10px', color: 'var(--muted)', fontSize: 12 }}>
              Inga repos hittade.<br />Kontrollera repo-rot i Inställningar.
            </div>
          )}
          {repos.map(repo => (
            <div
              key={repo.repoName}
              className={`repo-item ${repo.selected ? '' : 'repo-item-unselected'}`}
              onClick={() => repo.selected && scrollToRepo(repo.repoName)}
              style={{ cursor: repo.selected ? 'pointer' : 'default' }}
            >
              <label className="cb" onClick={e => e.stopPropagation()}>
                <input
                  type="checkbox"
                  checked={repo.selected}
                  onChange={e => setRepos(r => r.map(x =>
                    x.repoName === repo.repoName ? { ...x, selected: e.target.checked } : x))}
                />
                <span>{repo.repoName}</span>
              </label>
              {repo.status && (
                <span className={`repo-status ${
                  repo.status === 'OK'        ? 'ok' :
                  repo.status === 'FAILED'    ? 'failed' :
                  repo.status === 'Bygger...' ? 'active' : ''}`}>
                  {repo.status === 'OK' ? '✅' : repo.status === 'FAILED' ? '❌' : '⟳'}
                </span>
              )}
            </div>
          ))}
        </div>
      </div>

      {/* ── Per-repo log windows ── */}
      <div className="build-windows" ref={logAreaRef}>
        {selected.length === 0 && !isBuilding && (
          <div style={{ color: 'var(--muted)', padding: 16, fontFamily: 'var(--mono)', fontSize: 12 }}>
            // Välj repos och tryck Bygg
          </div>
        )}
        {selected.map(repo => (
          <RepoWindow
            key={repo.repoName}
            repoName={repo.repoName}
            status={repo.status}
            log={repoLogs[repo.repoName] ?? ''}
            ref={el => { windowRefs.current[repo.repoName] = el }}
          />
        ))}
      </div>
    </div>
  )
}

import { forwardRef, useEffect as ue, useRef as ur } from 'react'

const RepoWindow = forwardRef<HTMLDivElement, {
  repoName: string
  status: string
  log: string
}>(({ repoName, status, log }, ref) => {
  const logRef = ur<HTMLDivElement>(null)

  ue(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight
  }, [log])

  const statusClass =
    status === 'OK'        ? 'ok' :
    status === 'FAILED'    ? 'failed' :
    status === 'Bygger...' ? 'active' : ''

  return (
    <div className="repo-window" ref={ref}>
      <div className="repo-window-header">
        <span className="repo-window-name">{repoName}</span>
        {status && (
          <span className={`repo-status ${statusClass}`}>
            {status === 'OK' ? '✅ OK' : status === 'FAILED' ? '❌ FAILED' : '⟳ ' + status}
          </span>
        )}
      </div>
      <div className="build-log" ref={logRef}>
        {log || <span style={{ color: 'var(--muted)' }}>Väntar...</span>}
      </div>
    </div>
  )
})
RepoWindow.displayName = 'RepoWindow'
