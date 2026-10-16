export type Mood = 'idle' | 'thinking' | 'working' | 'happy' | 'pat' | 'worried' | 'dizzy' | 'surprised' | 'sleepy'

// base 跟着会话的活动走（空闲、思考、干活、犯困）；flash 是一闪而过的表情（被摸、完成、出错），到时自己收回。
export type Face = { base: Mood; flash: Mood | null; nonce: number; tool: string | null }

export type Quote = { text: string; nonce: number }

export type Quota = { kind: string; percentUsed: number; resetsAt: string | null }

export type Meter = { rateLimits: Quota[]; contextPercent: number | null; costUsd: number | null; at: number }

export type Prefs = { scale: number; muted: boolean; showQuota: boolean }

declare module 'claude-code' {
  interface PluginState {
    xiaoke: {
      face: Face
      quote: Quote | null
      meter: Meter | null
      prefs: Prefs
      tick: number
    }
  }
}
