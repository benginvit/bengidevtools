import { useEffect, useState } from 'react'
import { getSettings, saveSettings } from '../api'

export default function SettingsPage() {
  const [repoRootPath, setRepoRootPath] = useState('C:\\TFS\\Repos')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getSettings().then(s => setRepoRootPath(s.repoRootPath))
  }, [])

  const handleSave = async () => {
    await saveSettings({ repoRootPath })
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
      <div>
        <button className="btn primary" onClick={handleSave}>Spara</button>
        {saved && <span style={{ marginLeft: 10, color: 'var(--green)', fontSize: 12 }}>✅ Sparat</span>}
      </div>
    </div>
  )
}
