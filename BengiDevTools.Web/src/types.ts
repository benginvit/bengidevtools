export interface AppInfo {
  name: string
  port: number
  group: string
  repoKey: string
  projectName: string
  isRunning: boolean
  gitStatus: string
  localhostUrl: string
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
