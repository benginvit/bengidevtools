export interface ScannedApp {
  id: string
  repoName: string
  projectName: string
  httpsPort: number | null
  launchProfile: string | null
  isRunning: boolean
  gitStatus: string
  localhostUrl: string | null
}

export interface AppStatus {
  id: string
  isRunning: boolean
}

export interface RepoInfo {
  repoName: string
  slnPath: string
}

export interface Settings {
  repoRootPath: string
}

export interface BuildEvent {
  type: 'output' | 'progress' | 'done'
  line?: string
  repo?: string
  status?: string
}
