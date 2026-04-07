import { useEffect, useState } from 'react'
import { getSettings, saveSettings } from '../api'

export default function SettingsPage() {
  const [repoRootPath,        setRepoRootPath]        = useState('C:\\TFS\\Repos')
  const [sqlConnectionString, setSqlConnectionString] = useState('Server=localhost;Integrated Security=true;TrustServerCertificate=true;')
  const [debugScriptsPath,    setDebugScriptsPath]    = useState('')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getSettings().then(s => {
      setRepoRootPath(s.repoRootPath)
      setSqlConnectionString(s.sqlConnectionString)
      setDebugScriptsPath(s.debugScriptsPath)
    })
  }, [])

  const handleSave = async () => {
    await saveSettings({ repoRootPath, sqlConnectionString, debugScriptsPath })
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  return (
    <div className="settings-form">
      <div className="section-header">Inställningar</div>

      <div className="form-group">
        <label className="form-label">Repo-rot</label>
        <input
          className="input"
          value={repoRootPath}
          onChange={e => setRepoRootPath(e.target.value)}
          placeholder="C:\TFS\Repos"
        />
      </div>

      <div className="section-header" style={{ marginTop: 20 }}>Debug</div>

      <div className="form-group">
        <label className="form-label">SQL-anslutning</label>
        <input
          className="input"
          value={sqlConnectionString}
          onChange={e => setSqlConnectionString(e.target.value)}
          placeholder="Server=localhost;Integrated Security=true;TrustServerCertificate=true;"
        />
        <div style={{ fontSize: 11, color: 'var(--muted)', marginTop: 4 }}>
          Windows Auth är default. Ändra till User Id=...;Password=... vid behov.
        </div>
      </div>

      <div className="form-group">
        <label className="form-label">Debug-scripts mapp</label>
        <input
          className="input"
          value={debugScriptsPath}
          onChange={e => setDebugScriptsPath(e.target.value)}
          placeholder="C:\Dev\DebugScripts"
        />
        <div style={{ fontSize: 11, color: 'var(--muted)', marginTop: 4 }}>
          Undermappar <code>clean/</code> och <code>feed/</code> skapas automatiskt.
        </div>
      </div>

      <div>
        <button className="btn primary" onClick={handleSave}>Spara</button>
        {saved && <span style={{ marginLeft: 10, color: 'var(--green)', fontSize: 12 }}>✅ Sparat</span>}
      </div>
    </div>
  )
}
