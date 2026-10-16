import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register, RenderElement, SessionRateLimit } from 'claude-code'

import type { Face, Meter, Mood, Prefs, Quote } from '../types'
import { frame } from './art'
import type { Sprites } from './art'
import { DONE_LINES, PAT_LINES, QUOTES, greeting, pick } from './lines'

const PANE = 'xiaoke'
const TITLE = '小克'

const face = atom({ plugin: 'xiaoke', key: 'face' } as const, { base: 'idle', flash: null, nonce: 0, tool: null })
const quote = atom({ plugin: 'xiaoke', key: 'quote' } as const, null)
const meter = atom({ plugin: 'xiaoke', key: 'meter' } as const, null)
const prefs = atom({ plugin: 'xiaoke', key: 'prefs' } as const, { scale: 100, muted: false, showQuota: true })
const tick = atom({ plugin: 'xiaoke', key: 'tick' } as const, 0)

const LABEL: Record<Mood, string> = {
  idle: '发呆中',
  thinking: '思考中…',
  working: '干活中',
  happy: '开心！',
  pat: '被摸头了',
  worried: '有点担心额度',
  dizzy: '晕晕的…',
  surprised: '诶？！',
  sleepy: '打盹中 zzZ',
}

// 终端里画不了 SVG，就用颜文字。
const KAOMOJI: Record<Mood, string> = {
  idle: '( ˘ω˘ )',
  thinking: '( ･ω･)？',
  working: '(•̀ᴗ•́)و',
  happy: '(≧▽≦)',
  pat: '(〃>_<〃)',
  worried: '(；´д｀)',
  dizzy: '(@_@)',
  surprised: '(⊙o⊙)！',
  sleepy: '(－ω－) zzZ',
}

const WINDOW: Record<string, string> = { five_hour: '5 小时', seven_day: '本周', spend_limit: '花费上限' }
const WINDOW_ORDER = ['five_hour', 'seven_day', 'spend_limit']

const TOOL: Record<string, string> = {
  Bash: '终端',
  PowerShell: '终端',
  Read: '读文件',
  Edit: '改代码',
  Write: '写文件',
  NotebookEdit: '改笔记本',
  Grep: '搜代码',
  Glob: '找文件',
  WebFetch: '查网页',
  WebSearch: '上网搜',
  Agent: '派小助手',
  TodoWrite: '列计划',
  Skill: '翻技能书',
}

const SIZE_STEPS = [60, 80, 100, 125, 150, 200, 250]

const remaining = (q: { percentUsed: number }) => Math.max(0, Math.round(100 - q.percentUsed))

function fiveHourLeft(m: Meter | null): number | null {
  const q = m?.rateLimits.find(r => r.kind === 'five_hour')
  return q ? remaining(q) : null
}

function shown(f: Face, m: Meter | null): Mood {
  if (f.flash !== null) return f.flash
  const left = fiveHourLeft(m)
  if (f.base === 'idle' && left !== null && left < 15) return 'worried'
  return f.base
}

// 读数是重置前拿到的、现在已经过了重置时间：额度其实回满了，只是还没有新读数。
const wasReset = (w: { resetsAt: string | null }, readAt: number, now: number) => {
  const at = w.resetsAt === null ? NaN : Date.parse(w.resetsAt)
  return Number.isFinite(at) && at <= now && readAt < at
}

function untilReset(iso: string | null, now: number): string {
  if (iso === null) return ''
  const ms = Date.parse(iso) - now
  if (!Number.isFinite(ms)) return ''
  if (ms <= 60_000) return '马上重置'
  const minutes = Math.round(ms / 60_000)
  const days = Math.floor(minutes / 1440)
  const hours = Math.floor((minutes % 1440) / 60)
  const mins = minutes % 60
  if (days > 0) return `${days} 天 ${hours} 时后重置`
  if (hours > 0) return `${hours} 时 ${mins} 分后重置`
  return `${mins} 分后重置`
}

const bar = (percent: number, width: number) => {
  const n = Math.round((Math.min(100, Math.max(0, percent)) / 100) * width)
  return '█'.repeat(n) + '░'.repeat(width - n)
}

const levelColor = (left: number) => (left >= 50 ? 'success' : left >= 20 ? 'warning' : 'error')

const toolLabel = (tool: string) => TOOL[tool] ?? tool.replace(/^mcp__[^_]+__/, '')

// 模块里的这些只在本次加载内有效（热重载会重来）；要画出来的都放在 $.state 里。
let lastActivity = 0
let toolsRunning = 0
let isTurnRunning = false
let turnStartedAt = 0
let isWindows: boolean | null = null
let custom: string[] = []
let sprites: Sprites | null = null
const warned = new Set<string>()

// 桌面挂件（desktop/XiaoKe.exe）读模组 data\sessions\ 下每个会话各自的 <会话 id>.json，
// 汇总成一个小克：哪个会话在忙就跟着它，额度取最新的那份。
// 不放 %LOCALAPPDATA%：桌面端是打包应用，从它里面写进 AppData 的文件会被转到 Packages\Claude_*\LocalCache，
// 从桌面快捷方式打开的挂件就读不到了。
let desktopExe: string | null = null
const dataDir = ($: EngineInterface) => `${$.plugin.root}/data`
let publishQueued = false
let sessionSalt = 0
let lastEvent: { nonce: number; kind: string; chime: boolean; tool?: string } = { nonce: 0, kind: 'none', chime: false }
// tool.check 判成「要问」、还没跑完的调用：允许框弹出来了，或者（自动模式）交给了分类器。
// 挂件自己看对话记录里的权限模式，再决定要不要叫人。
const asking = new Set<string>()
// 有人在场的会话才惊动她：终端里是交互模式；桌面端、编辑器、手机打开会话时会挂上一个界面
// （$.session.surfaces() 不空、session.attach）。claude -p、SDK 之类的自动运行没有界面，
// 就不写状态、不拉挂件、不打招呼。（CLAUDE_CODE_SESSION_ATTENDED 只给工具的子进程，插件这里读不到。）
let attended = false

// 一小会儿里的多次变化合并成一次写入。
const publish = ($: EngineInterface) => {
  if (!attended || publishQueued) return
  publishQueued = true
  $.clock.after(150, () => {
    publishQueued = false
    void writeState($).catch(() => {})
  })
}

const sessionFile = async ($: EngineInterface) => {
  // /clear 之后同一个进程换了新的会话 id，所以每次都现取；取不到就用这次加载自己的编号。
  let id = `local-${sessionSalt}`
  try {
    id = await $.session.id()
  } catch {}
  return `${dataDir($)}/sessions/${id}.json`
}

// 第一次确认有人在场时：请出挂件（或打开面板）、打个招呼。
const becomeAttended = async ($: EngineInterface) => {
  if (attended) return
  attended = true
  if (desktopExe !== null) {
    void launchDesktop($, true).catch(() => {})
  } else if ((await $.store.get('closed')) !== true) {
    void $.ui.open({ id: PANE, title: TITLE }).catch(() => {})
  }
  await say($, greeting(new Date(await $.clock.now()).getHours()))
  // 刚打开时手头的额度读数可能是很久以前的，先同步一次。
  void syncUsage($).catch(() => {})
}

const writeState = async ($: EngineInterface, ended = false) => {
  const path = await sessionFile($)
  const f = await read($, face)
  const q = await read($, quote)
  const m = await read($, meter)
  const mood = shown(f, m)
  await $.fs.write(
    path,
    JSON.stringify({
      v: 2,
      ended,
      at: await $.clock.now(),
      mood,
      label: mood === 'working' && f.tool !== null ? `正在${f.tool}…` : LABEL[mood],
      tool: f.tool,
      turnRunning: isTurnRunning,
      asking: asking.size,
      // 几个会话轮流写同一个文件，台词编号带上会话开始的时间，挂件才不会认错。
      quote: q === null ? null : { text: q.text, nonce: sessionSalt + q.nonce },
      event: lastEvent,
      quota: m?.rateLimits ?? [],
      contextPercent: m?.contextPercent ?? null,
      costUsd: m?.costUsd ?? null,
      meterAt: m?.at ?? null,
    }),
  )
}

// 自定义语录和桌面挂件共用 data\quotes.txt：一行一句，# 开头的不算。
const quotesFile = ($: EngineInterface) => `${dataDir($)}/quotes.txt`

const readQuotes = async ($: EngineInterface): Promise<string[]> => {
  const file = quotesFile($)
  if (!(await $.fs.exists(file))) return []
  return (await $.fs.read(file))
    .split(/\r?\n/)
    .map(line => line.replace(/^﻿/, '').trim())
    .filter(line => line !== '' && !line.startsWith('#'))
}

const writeQuotes = async ($: EngineInterface, quotes: string[]) => {
  await $.fs.write(quotesFile($), ['# 小克的语录：一行一句（以 # 开头的行不算）。', ...quotes].join('\r\n') + '\r\n')
}

// 拉起桌面挂件；quiet 时它若已经开着就什么都不做（也不会把藏起来的她叫出来）。
const launchDesktop = async ($: EngineInterface, quiet: boolean) => {
  if (desktopExe === null) return false
  const exe = desktopExe.replace(/\//g, '\\').replace(/'/g, "''")
  const args = quiet ? " -ArgumentList '--quiet'" : ''
  const ran = await $.process.run(
    ['powershell.exe', '-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-Command', `Start-Process -FilePath '${exe}'${args}`],
    { timeoutMs: 15000 },
  )
  return ran.exitCode === 0
}

// 立绘在插件的 art/ 里：manifest.json 记着尺寸和眨眼贴片的位置，每种心情一张 WebP。
const loadSprites = async ($: EngineInterface): Promise<Sprites> => {
  const dir = `${$.plugin.root}/art`
  const manifest = JSON.parse(await $.fs.read(`${dir}/manifest.json`)) as {
    width: number
    height: number
    blink: { x: number; y: number; width: number; height: number } | null
    moods: Record<string, string>
  }
  const moods: Sprites['moods'] = {}
  for (const [mood, file] of Object.entries(manifest.moods)) {
    moods[mood as Mood] = (await $.fs.read(`${dir}/${file}`, { as: 'bytes' })).base64
  }
  const blink =
    manifest.blink === null ? null : { ...manifest.blink, base64: (await $.fs.read(`${dir}/blink.webp`, { as: 'bytes' })).base64 }
  return { width: manifest.width, height: manifest.height, moods, blink }
}

const activity = async ($: EngineInterface) => {
  lastActivity = await $.clock.now()
  const f = await read($, face)
  if (f.base === 'sleepy') await setBase($, 'idle')
}

const setBase = async ($: EngineInterface, base: Mood, tool: string | null = null) => {
  await update($, face, f => (f.base === base && f.tool === tool ? f : { ...f, base, tool }))
  publish($)
}

// 一闪而过的表情：到时间自己收回，期间又被别的表情盖掉就不管了。
const flash = async ($: EngineInterface, mood: Mood, ms: number) => {
  const next = await update($, face, f => ({ ...f, flash: mood, nonce: f.nonce + 1 }))
  publish($)
  $.clock.after(ms, () => {
    void update($, face, f => (f.nonce === next.nonce ? { ...f, flash: null } : f))
      .then(() => publish($))
      .catch(() => {})
  })
}

const say = async ($: EngineInterface, text: string) => {
  const shownQuote = await update($, quote, old => ({ text, nonce: (old?.nonce ?? 0) + 1 }))
  publish($)
  $.clock.after(6000, () => {
    void update($, quote, cur => (cur !== null && cur.nonce === shownQuote?.nonce ? null : cur)).catch(() => {})
  })
}

const sound = async ($: EngineInterface, name: 'pop' | 'chime' | 'blip') => {
  if ((await read($, prefs)).muted) return
  if (isWindows === null) isWindows = (await $.env.get('OS')) === 'Windows_NT'
  if (isWindows) {
    // Windows 终端没有播放器可用，借 PowerShell 的 SoundPlayer 播。
    const file = `${$.plugin.root}\\sounds\\${name}.wav`.replace(/'/g, "''")
    await $.process.run(
      ['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', `(New-Object System.Media.SoundPlayer '${file}').PlaySync()`],
      { timeoutMs: 8000 },
    )
    return
  }
  await $.audio.play({ asset: `sounds/${name}.wav` }, { gain: 0.7 })
}

const setPrefs = async ($: EngineInterface, change: (p: Prefs) => Prefs) => {
  const next = await update($, prefs, change)
  await $.store.set('prefs', next)
  return next
}

const writeMeter = async ($: EngineInterface, u: { rateLimits: SessionRateLimit[]; context: { percent?: number }; cost?: { usd: number } }) => {
  const at = await $.clock.now()
  const rateLimits = u.rateLimits
    .map(r => ({ kind: r.kind, percentUsed: r.percentUsed, resetsAt: r.resetsAt ?? null }))
    .sort((a, b) => WINDOW_ORDER.indexOf(a.kind) - WINDOW_ORDER.indexOf(b.kind))
  await update($, meter, () => ({ rateLimits, contextPercent: u.context.percent ?? null, costUsd: u.cost?.usd ?? null, at }))
  publish($)
}

// 额度平时只在 Claude 回话时顺带更新；不说话的时候每 5 分钟主动问一次账号用量（和应用里的用量页同一个接口），
// 用会话自己的登录凭证（插件拿不到凭证本身）。几个会话开着时靠 data\usage-sync.json 里的时间错开，只有一个去问。
const SYNC_MS = 5 * 60_000
const USAGE_URL = 'https://api.anthropic.com/api/oauth/usage'

const syncUsage = async ($: EngineInterface) => {
  if (!attended) return
  const now = await $.clock.now()
  const stamp = `${dataDir($)}/usage-sync.json`
  if (await $.fs.exists(stamp)) {
    const last = Number((JSON.parse(await $.fs.read(stamp)) as { at?: number }).at ?? 0)
    if (now - last < SYNC_MS - 15_000) return
  }
  await $.fs.write(stamp, JSON.stringify({ at: now }))
  const auth = await $.session.authorize()
  if (auth === null) return
  const r = await $.http.fetch(USAGE_URL, { auth: auth.handle, headers: { 'anthropic-beta': 'oauth-2025-04-20' } })
  if (!r.ok) return
  const body = JSON.parse(r.text) as Record<string, { utilization?: number | null; resets_at?: string | null } | null>
  const rateLimits: SessionRateLimit[] = []
  for (const kind of ['five_hour', 'seven_day'] as const) {
    const w = body[kind]
    if (w === null || w === undefined || typeof w.utilization !== 'number') continue
    // 接口给的是微秒（.189702+00:00），截成毫秒，两边解析起来都稳。
    const resetsAt = typeof w.resets_at === 'string' ? w.resets_at.replace(/(\.\d{3})\d+/, '$1') : undefined
    const percentUsed = Math.round(w.utilization * 10) / 10
    rateLimits.push(resetsAt === undefined ? { kind, percentUsed } : { kind, percentUsed, resetsAt })
  }
  if (rateLimits.length === 0) return
  await update($, meter, m => ({
    rateLimits: rateLimits.map(q => ({ kind: q.kind, percentUsed: q.percentUsed, resetsAt: q.resetsAt ?? null })),
    contextPercent: m?.contextPercent ?? null,
    costUsd: m?.costUsd ?? null,
    at: now,
  }))
  publish($)
  await checkQuota($, rateLimits)
}

const checkQuota = async ($: EngineInterface, limits: SessionRateLimit[]) => {
  for (const q of limits) {
    const left = remaining(q)
    const name = WINDOW[q.kind] ?? q.kind
    for (const level of [0, 5, 20]) {
      const key = (l: number) => `${q.kind}:${q.resetsAt ?? ''}:${l}`
      if (left > level || warned.has(key(level))) continue
      // 报过更低一档的，就不再补报高的那档。
      for (const higher of [0, 5, 20].filter(l => l >= level)) warned.add(key(higher))
      $.ui.toast(
        level === 0
          ? `小克：${name}额度用完了……人家干不动了，让我休息一下吧`
          : level === 5
            ? `小克：${name}额度快被吃光啦（只剩 ${left}%）……`
            : `小克：${name}额度只剩 ${left}% 了，悠着点用哦`,
        { timeoutMs: 8000 },
      )
      await flash($, level === 0 ? 'dizzy' : 'worried', 4000)
      await say(
        $,
        level === 0
          ? '人家干不动了，让我休息一下吧。'
          : level === 5
            ? '额度快见底了……人家会累的，先做最要紧的事好不好？'
            : '额度用掉一大半了……要、要省着点用哦。',
      )
      break
    }
  }
}

const pat = async ($: EngineInterface) => {
  await activity($)
  await flash($, 'pat', 1600)
  await say($, pick([...PAT_LINES, ...custom]))
  void sound($, 'pop').catch(() => {})
}

const talk = async ($: EngineInterface) => {
  await activity($)
  const pool = custom.length > 0 && Math.random() < 0.5 ? custom : QUOTES
  await say($, pick(pool))
  void sound($, 'blip').catch(() => {})
}

const resize = ($: EngineInterface, step: 1 | -1) =>
  setPrefs($, p => {
    const at = SIZE_STEPS.findIndex(s => s >= p.scale)
    const index = Math.min(SIZE_STEPS.length - 1, Math.max(0, (at === -1 ? SIZE_STEPS.length - 1 : at) + step))
    return { ...p, scale: SIZE_STEPS[index] ?? 100 }
  })

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'xiaoke',
      description: '召唤看板娘小克：看额度、听她说话',
      argumentHint: '[panel | add <语录> | list | clear | mute | size <60-250>]',
    })

    const stored = (await $.store.get('prefs')) as Partial<Prefs> | undefined
    if (stored !== undefined) await update($, prefs, p => ({ ...p, ...stored }))
    // 以前存在插件自己 store 里的语录，并进挂件也读的 quotes.txt。
    const old = await $.store.get('quotes')
    custom = await readQuotes($)
    if (Array.isArray(old) && old.length > 0) {
      custom = [...new Set([...custom, ...old.filter((q): q is string => typeof q === 'string')])]
      await writeQuotes($, custom)
      await $.store.delete('quotes')
    }

    try {
      sprites = await loadSprites($)
    } catch {
      sprites = null
    }
    // 立绘放在模块变量里（太大，不进 $.state）；读完让面板重画一次。
    await update($, tick, n => n + 1)

    attended = false
    lastActivity = await $.clock.now()
    sessionSalt = lastActivity * 1000
    const exe = `${$.plugin.root}/desktop/XiaoKe.exe`
    desktopExe = (await $.fs.exists(exe)) ? exe : null
    await writeMeter($, await $.session.usage())
    await update($, face, (f): Face => ({ ...f, base: 'idle', flash: null, tool: null }))

    $.clock.every(60_000, () => {
      void (async () => {
        const p = await read($, prefs)
        if (p.showQuota) await update($, tick, n => n + 1)
        const now = await $.clock.now()
        if (!isTurnRunning && toolsRunning === 0 && now - lastActivity > 15 * 60_000) await setBase($, 'sleepy')
      })().catch(() => {})
    })
    $.clock.every(SYNC_MS, () => {
      void syncUsage($).catch(() => {})
    })

    // 有桌面挂件就请她出来，应用里的面板留给 /xiaoke panel。没人在场的自动运行就都免了；
    // 界面晚一点才挂上的（桌面端常这样），等 session.attach 再说。
    const present =
      e.isInteractive ||
      (await $.session.surfaces()).length > 0 ||
      (await $.env.get('CLAUDE_CODE_SESSION_ATTENDED')) === '1'
    if (present) await becomeAttended($)

    return next(e)
  })

  on('session.attach', async ($, e, next) => {
    await becomeAttended($)
    return next(e)
  }).catch(($, e, next) => next(e))

  // 会话结束（关掉、/clear）时标记一下，挂件就不再跟着它了。
  on('session.end', async ($, e, next) => {
    if (attended) await writeState($, true)
    return next(e)
  }).catch(($, e, next) => next(e))

  on('prompt.submit', async ($, e, next) => {
    await activity($)
    return next(e)
  }).catch(($, e, next) => next(e))

  on('turn.start', async ($, e, next) => {
    isTurnRunning = true
    turnStartedAt = await $.clock.now()
    lastActivity = turnStartedAt
    await setBase($, 'thinking')
    return next(e)
  })

  on('tool.call', async ($, e, next) => {
    toolsRunning += 1
    void setBase($, 'working', toolLabel(String(e.tool))).catch(() => {})
    try {
      return await next(e)
    } finally {
      toolsRunning = Math.max(0, toolsRunning - 1)
      if (e.tool_use_id !== undefined && asking.delete(e.tool_use_id)) publish($)
      if (toolsRunning === 0) void setBase($, isTurnRunning ? 'thinking' : 'idle').catch(() => {})
    }
  }).catch(($, e, next) => next(e))

  on('tool.check', async ($, e, next) => {
    const verdict = await next(e)
    if (verdict.decision === 'ask' && e.tool_use_id !== undefined) {
      asking.add(e.tool_use_id)
      lastEvent = { nonce: await $.clock.now(), kind: 'ask', chime: false, tool: toolLabel(e.tool) }
      publish($)
    }
    return verdict
  }).catch(($, e, next) => next(e))

  on('turn.complete', async ($, e, next) => {
    const result = await next(e)
    if (e.agentId !== undefined) return result

    isTurnRunning = false
    asking.clear()
    const now = await $.clock.now()
    lastActivity = now
    await setBase($, 'idle')
    // 只有做了挺久的活才叮一声，免得每句话都响；有桌面挂件时由她来叮。
    const isLong = now - turnStartedAt > 20_000
    lastEvent = { nonce: now, kind: e.reason === 'answer' ? 'done' : e.reason, chime: e.reason === 'answer' && isLong }
    if (e.reason === 'answer') {
      await flash($, 'happy', 4000)
      if (isLong) {
        if (desktopExe === null) void sound($, 'chime').catch(() => {})
        await say($, pick(DONE_LINES))
      }
    } else if (e.reason === 'aborted') {
      await flash($, 'surprised', 2500)
      await say($, '诶？被、被打断了……人家还没说完呢。')
    } else {
      await flash($, 'dizzy', 3500)
      await say($, e.reason === 'error' ? '出错了……呜，小克有点晕乎乎的。' : '这个……小克不能帮你做，对不起嘛。')
    }
    return result
  })

  on('session.measure', async ($, e, next) => {
    await writeMeter($, e)
    if (e.changed.includes('rateLimits')) await checkQuota($, e.rateLimits)
    return next(e)
  })

  on('ui.close', { id: PANE }, async ($, e, next) => {
    if (e.origin.kind === 'person') await $.store.set('closed', true)
    return next(e)
  }).catch(($, e, next) => next(e))

  on('command.run', { command: 'xiaoke' }, async ($, e) => {
    const args = e.args.trim()
    const verb = args.split(/\s+/)[0] ?? ''
    const rest = args.slice(verb.length).trim()

    switch (verb) {
      case '':
      case 'show': {
        if (await launchDesktop($, false)) return { text: '小克在桌面上啦～（应用里的面板用 /xiaoke panel）' }
        await $.store.set('closed', false)
        await $.ui.open({ id: PANE, title: TITLE })
        await say($, pick(custom.length > 0 && Math.random() < 0.5 ? custom : QUOTES))
        return { text: '小克来啦～' }
      }
      case 'panel': {
        await $.store.set('closed', false)
        await $.ui.open({ id: PANE, title: TITLE })
        await say($, pick(custom.length > 0 && Math.random() < 0.5 ? custom : QUOTES))
        return { text: '面板打开啦～' }
      }
      case 'add': {
        if (rest === '') return { text: '用法：/xiaoke add 想让小克说的话' }
        custom = [...(await readQuotes($)), rest].slice(-200)
        await writeQuotes($, custom)
        return { text: `小克记住啦：「${rest}」（桌面上的她会在后台把这句录成语音）` }
      }
      case 'list':
        custom = await readQuotes($)
        return {
          text: custom.length === 0 ? '还没有自定义语录，用 /xiaoke add 加一句吧。' : custom.map((q, i) => `${i + 1}. ${q}`).join('\n'),
        }
      case 'clear':
        custom = []
        await writeQuotes($, [])
        return { text: '自定义语录清空啦。' }
      case 'mute': {
        const p = await setPrefs($, x => ({ ...x, muted: !x.muted }))
        return { text: p.muted ? '小克安静下来了。' : '小克的声音打开啦。' }
      }
      case 'size': {
        const n = Math.round(Number(rest))
        if (!Number.isFinite(n) || n < 60 || n > 250) return { text: '用法：/xiaoke size 60 到 250 之间的数字（百分比）' }
        await setPrefs($, p => ({ ...p, scale: n }))
        return { text: `小克现在是 ${n}% 大小。` }
      }
      default:
        return {
          text: [
            '/xiaoke              叫桌面上的小克出来',
            '/xiaoke panel        打开应用里的面板',
            '/xiaoke add <语录>   教小克一句新台词',
            '/xiaoke list         看看教过的台词',
            '/xiaoke clear        清空教过的台词',
            '/xiaoke mute         静音 / 取消静音',
            '/xiaoke size <n>     调整大小（60–250%）',
          ].join('\n'),
        }
    }
  })

  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const f = await read($, face)
    const q = await read($, quote)
    const m = await read($, meter)
    const p = await read($, prefs)
    await read($, tick)
    const now = await $.clock.now()
    const mood = shown(f, m)
    const label = mood === 'working' && f.tool !== null ? `正在${f.tool}…` : LABEL[mood]
    const { Box, Text, Button } = $.ui.resolve(e)

    // 终端画不了 SVG，立绘没读到时也一样：都用颜文字。
    let picture: RenderElement = (
      <Text color="claude" bold>
        {KAOMOJI[mood]}
      </Text>
    )
    const source = sprites === null ? null : frame(mood, sprites, mood === 'pat' || mood === 'happy' ? f.nonce : 0)
    if (e.surface !== 'terminal' && sprites !== null && source !== null) {
      const { Svg } = $.ui.resolve(e)
      const width = Math.round(2.4 * p.scale)
      picture = (
        <Svg source={source} alt={`小克（${label}）`} width={width} height={Math.round((width * sprites.height) / sprites.width)} isInteractive />
      )
    }

    const barWidth = Math.max(6, Math.min(18, e.props.bodyColumns - 24))
    const quotaRows: RenderElement[] = []
    if (p.showQuota) {
      if (m === null || (m.rateLimits.length === 0 && m.contextPercent === null)) {
        quotaRows.push(<Text dimColor>发一条消息后，小克就能看到额度啦～</Text>)
      } else {
        for (const w of m.rateLimits) {
          const fresh = wasReset(w, m.at, now)
          const left = fresh ? 100 : remaining(w)
          const reset = fresh ? '刚重置过，下次对话后更新读数' : untilReset(w.resetsAt, now)
          quotaRows.push(
            <Box flexDirection="column">
              <Box flexDirection="row" gap={1}>
                <Text>{(WINDOW[w.kind] ?? w.kind).padEnd(5, '　')}</Text>
                <Text color={levelColor(left)}>{bar(100 - left, barWidth)}</Text>
                <Text bold>已用 {100 - left}%</Text>
              </Box>
              {reset !== '' && <Text dimColor>{'　　　 '}{reset}</Text>}
            </Box>,
          )
        }
        if (m.rateLimits.length === 0) {
          quotaRows.push(<Text dimColor>没有订阅额度的读数（API Key 登录时只看得到花费）</Text>)
        }
        if (m.contextPercent !== null) {
          quotaRows.push(
            <Box flexDirection="row" gap={1}>
              <Text>上下文　</Text>
              <Text color="claude">{bar(m.contextPercent, barWidth)}</Text>
              <Text>已用 {Math.round(m.contextPercent)}%</Text>
            </Box>,
          )
        }
        if (m.costUsd !== null && m.costUsd > 0) {
          quotaRows.push(<Text dimColor>本次会话花费 ${m.costUsd.toFixed(2)}</Text>)
        }
        const age = Math.floor((now - m.at) / 60_000)
        if (age >= 3) quotaRows.push(<Text dimColor>（{age} 分钟前的读数）</Text>)
      }
    }

    return (
      <Box flexDirection="column" alignItems="center" gap={1}>
        {q !== null && (
          <Box borderStyle="round" borderColor="claude" paddingX={1}>
            <Text>{q.text}</Text>
          </Box>
        )}
        {picture}
        <Box flexDirection="row">
          <Text color="claude" bold>
            小克
          </Text>
          <Text dimColor> · {label}</Text>
        </Box>
        {quotaRows.length > 0 && (
          <Box flexDirection="column" borderStyle="round" borderColor="claude" paddingX={1}>
            {quotaRows}
          </Box>
        )}
        <Box flexDirection="row" gap={1} flexWrap="wrap" justifyContent="center">
          <Button key="pat" label="摸摸头" hotkey="p" variant="primary" onPress={() => void pat($)} />
          <Button key="talk" label="说句话" hotkey="t" onPress={() => void talk($)} />
          <Button
            key="quota"
            label={p.showQuota ? '收起额度' : '看额度'}
            hotkey="q"
            onPress={() => void setPrefs($, x => ({ ...x, showQuota: !x.showQuota }))}
          />
          <Button key="smaller" label="缩小" hotkey="s" onPress={() => void resize($, -1)} />
          <Button key="bigger" label="放大" hotkey="b" onPress={() => void resize($, 1)} />
          <Button key="mute" label={p.muted ? '开声音' : '静音'} hotkey="m" onPress={() => void setPrefs($, x => ({ ...x, muted: !x.muted }))} />
        </Box>
      </Box>
    )
  })
}
