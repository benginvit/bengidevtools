import { useCallback, useEffect, useRef, useState } from 'react'
import { getRepos, startBuild } from '../api'
import type { RepoInfo } from '../types'

interface RepoState extends RepoInfo {
  selected: boolean
  status: string
}

export default function BuildPage() {
  const [repos, setRepos]         = useState<RepoState[]>([])
  const [log, setLog]             = useState('')
  const [isBuilding, setIsBuilding] = useState(false)
  const [noRestore, setNoRestore]   = useState(false)
  const [noAnalyzers, setNoAnalyzers] = useState(false)
  const [noDocs, setNoDocs]         = useState(false)
  const [parallel, setParallel]     = useState(false)
  const [snabb, setSnabb]           = useState(false)
  const abortRef  = useRef<AbortController | null>(null)
  const logRef    = useRef<HTMLDivElement>(null)

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

  useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight
  }, [log])

  const handleSnabb = (v: boolean) => {
    setSnabb(v)
    if (v) { setNoRestore(true); setNoAnalyzers(true); setNoDocs(true); setParallel(true) }
  }

  const build = async () => {
    const selected = repos.filter(r => r.selected)
    if (selected.length === 0) return

    setIsBuilding(true)
    setLog('')
    setRepos(r => r.map(x => x.selected ? { ...x, status: '' } : x))

    abortRef.current = new AbortController()

    try {
      await startBuild(
        {
          repoNames: selected.map(r => r.repoName),
          noRestore, noAnalyzers, noDocs, parallel, snabb,
        },
        (type, data) => {
          if (type === 'output')   setLog(l => l + (data.line ?? '') + '\n')
          if (type === 'progress') setRepos(r => r.map(x =>
            x.repoName === data.repo ? { ...x, status: data.status ?? '' } : x))
        },
        abortRef.current.signal,
      )
    } catch (e: unknown) {
      if (e instanceof Error && e.name !== 'AbortError')
        setLog(l => l + '\n❌ Fel: ' + e.message + '\n')
    } finally {
      setIsBuilding(false)
    }
  }

  const cancel = () => abortRef.current?.abort()

  const succeeded = repos.filter(r => r.status === 'OK').length
  const failed    = repos.filter(r => r.status === 'FAILED').length

  return (
    <div className="build-layout">
      {/* Sidebar */}
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
          <button className="btn sm" onClick={loadRepos} disabled={isBuilding}>↺</button>
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
          {repos.map(repo => (
            <div key={repo.repoName} className="repo-item">
              <label className="cb">
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
                  repo.status === 'OK'       ? 'ok' :
                  repo.status === 'FAILED'   ? 'failed' :
                  repo.status === 'Bygger...' ? 'active' : ''}`}>
                  {repo.status === 'OK' ? '✅' : repo.status === 'FAILED' ? '❌' : '⟳'}
                </span>
              )}
            </div>
          ))}
        </div>
      </div>

      {/* Log */}
      <div className="build-log" ref={logRef}>{log || '// Välj repos och tryck Bygg'}</div>
    </div>
  )
}
