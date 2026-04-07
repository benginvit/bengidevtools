export interface ScannedApp {
  id: string
  repoName: string
  projectName: string
  httpsPort: number | null
  launchProfile: string | null
  isRunning: boolean
  hasLocalUser: boolean
  gitStatus: string
  localhostUrl: string | null
}

export interface AppStatus {
  id: string
  isRunning: boolean
  isExternal: boolean
  hasException: boolean
}

export interface RepoInfo {
  repoName: string
  slnPath: string
}

export interface Settings {
  repoRootPath: string
  sqlConnectionString: string
  debugScriptsPath: string
}

export interface DebugScript {
  name: string
  type: 'clean' | 'feed' | 'other'
  path: string
  relativePath: string
}

export interface Scenario {
  id: string
  name: string
  appId: string
  method: string
  url: string
  body: string
  headers: Record<string, string>
}

export interface SwaggerPath {
  method: string
  path: string
  summary?: string
}

export interface BuildEvent {
  type: 'output' | 'progress' | 'done'
  line?: string
  repo?: string
  status?: string
}
