import { useState } from 'react'
import AppsPage from './pages/AppsPage'
import BuildPage from './pages/BuildPage'
import SettingsPage from './pages/SettingsPage'

type Tab = 'apps' | 'build' | 'settings'

export default function App() {
  const [tab, setTab] = useState<Tab>('apps')

  return (
    <>
      <nav className="nav">
        <button className={tab === 'apps'     ? 'active' : ''} onClick={() => setTab('apps')}>Applikationer</button>
        <button className={tab === 'build'    ? 'active' : ''} onClick={() => setTab('build')}>Bygga</button>
        <button className={tab === 'settings' ? 'active' : ''} onClick={() => setTab('settings')}>Inställningar</button>
      </nav>
      <main className="page">
        {tab === 'apps'     && <AppsPage />}
        {tab === 'build'    && <BuildPage />}
        {tab === 'settings' && <SettingsPage />}
      </main>
    </>
  )
}
