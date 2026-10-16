import { expect, mock, test } from 'claude-code/testing'
import type { On } from 'claude-code'

const PANE = {
  title: '小克',
  isFocused: false,
  bodyColumns: 48,
  placement: 'dock',
  scroll: { offset: 0, bodyRows: 40 },
  view: {},
} as const

const MANIFEST = JSON.stringify({
  width: 520,
  height: 488,
  blink: { x: 213, y: 104, width: 102, height: 52 },
  moods: { idle: 'idle.webp', pat: 'pat.webp' },
})

// 插件底下没有真的引擎：命令、面板、额度、读文件、放音效都在这里应答。
// surfaces 是会话挂着的界面（桌面端开着就是 ['desktop']，claude -p 是空的）；writes 记下插件写过的文件。
// auth 是会话的登录凭证句柄（null：没有订阅登录，就不去同步用量）。
function world(
  on: On,
  surfaces: ('desktop' | 'terminal')[] = ['desktop'],
  writes: string[] = [],
  auth: { handle: string; kind: 'bearer' } | null = null,
) {
  const clock = mock.clock(on, { now: Date.parse('2026-10-14T10:00:00Z') })
  mock.store(on)
  mock.env(on, { OS: 'Windows_NT' })
  on('session.start', ($, e) => ({ cwd: e.cwd }))
  on('session.attach', ($, e) => ({ clientId: e.clientId }))
  on('session.surfaces', () => ({ value: surfaces }))
  on('session.id', () => ({ value: 'sess-1' }))
  on('fs.exists', () => ({ value: false }))
  on('fs.write', ($, e) => {
    writes.push(e.path.split('\\').join('/'))
    return { value: undefined }
  })
  on('session.measure', ($, e) => ({ changed: e.changed }))
  on('command.register', ($, e) => ({ value: { command: e.name } }))
  on('session.usage', () => ({ value: { startedAt: 0, context: { window: 200_000 }, rateLimits: [] } }))
  on('fs.read', ($, e) => ({ value: e.path.endsWith('manifest.json') ? MANIFEST : { base64: 'UklGRgAAAABXRUJQ' } }))
  on('ui.open', () => ({ value: { isPlaced: true as const } }))
  on('ui.toast', () => ({ value: undefined }))
  on('process.run', () => ({ value: { exitCode: 0, stdout: '', stderr: '', isStdoutTruncated: false, isStderrTruncated: false } }))
  on('session.authorize', () => ({ value: auth }))
  return clock
}

// 账号用量接口的真实返回（截掉了无关的字段）。
const USAGE_BODY = JSON.stringify({
  five_hour: { utilization: 24.0, resets_at: '2099-10-15T00:30:00.189702+00:00' },
  seven_day: null,
  seven_day_cowork: { utilization: 0.0, resets_at: '2099-10-16T18:00:00+00:00' },
})

test('桌面端画立绘、终端画颜文字，摸头会换表情，过一会儿恢复', async ($, on) => {
  const clock = world(on)
  await $.session.start({ cwd: '.', surface: 'desktop', isInteractive: true })

  for (const surface of ['desktop', 'terminal'] as const) {
    const ui = await $.ui.mount({ plugin: 'xiaoke', surface, component: 'Pane', requestId: 'xiaoke', props: PANE })

    const portrait = await ui.find({ type: 'Svg' })
    if (surface === 'desktop') {
      expect(portrait).toBeDefined()
      expect(String(portrait?.props.source)).toContain('data:image/webp;base64,')
    } else {
      expect(portrait).toBeUndefined()
    }
    expect(await ui.find({ text: /发呆中/ })).toBeDefined()

    await ui.press({ key: 'pat' })
    expect(await ui.find({ text: /被摸头了/ })).toBeDefined()
    await clock.advance(2000)
    expect(await ui.find({ text: /发呆中/ })).toBeDefined()

    await ui.unmount()
  }
})

test('额度条和上下文一样按已用百分比画', async ($, on) => {
  world(on)
  await $.session.start({ cwd: '.', surface: 'desktop', isInteractive: true })
  await $.session.measure({
    context: { tokens: 50_000, window: 200_000, percent: 25 },
    rateLimits: [
      { kind: 'seven_day', percentUsed: 12, resetsAt: '2099-01-01T00:00:00Z' },
      { kind: 'five_hour', percentUsed: 37.5, resetsAt: '2099-01-01T00:00:00Z' },
    ],
    changed: ['context', 'rateLimits'],
  })

  const ui = await $.ui.mount({ plugin: 'xiaoke', surface: 'desktop', component: 'Pane', requestId: 'xiaoke', props: PANE })
  expect(await ui.find({ text: /已用 37%/ })).toBeDefined()
  expect(await ui.find({ text: /已用 12%/ })).toBeDefined()
  expect(await ui.find({ text: /已用 25%/ })).toBeDefined()
  await ui.unmount()
})

test('不说话也会去同步账号用量，额度条跟着更新', async ($, on) => {
  const clock = world(on, ['desktop'], [], { handle: 'h-1', kind: 'bearer' })
  const fetched: { url: string; auth?: string }[] = []
  on('http.fetch', ($, e) => {
    fetched.push({ url: e.url, auth: e.init?.auth })
    return { value: { status: 200, ok: true, headers: {}, text: USAGE_BODY } }
  })
  await $.session.start({ cwd: '.', surface: 'desktop', isInteractive: true })
  await clock.advance(500)

  expect(fetched).toEqual([{ url: 'https://api.anthropic.com/api/oauth/usage', auth: 'h-1' }])
  const ui = await $.ui.mount({ plugin: 'xiaoke', surface: 'desktop', component: 'Pane', requestId: 'xiaoke', props: PANE })
  expect(await ui.find({ text: /已用 24%/ })).toBeDefined()
  await ui.unmount()
})

// 会话文件在模组自己的 data\ 里（不在 AppData：打包的桌面端会把那里的写入转走）。
const wroteSessionFile = (writes: string[]) => writes.some(p => p.endsWith('/data/sessions/sess-1.json'))

test('桌面端会话（不是交互模式，但挂着界面）会把状态写进自己的 sessions 文件', async ($, on) => {
  const writes: string[] = []
  const clock = world(on, ['desktop'], writes)
  await $.session.start({ cwd: '.', surface: null, isInteractive: false })
  await clock.advance(500)
  expect(wroteSessionFile(writes)).toBe(true)
})

test('claude -p 这类没界面的运行不写状态，界面挂上以后才开始', async ($, on) => {
  const writes: string[] = []
  const clock = world(on, [], writes)
  await $.session.start({ cwd: '.', surface: null, isInteractive: false })
  await clock.advance(500)
  expect(wroteSessionFile(writes)).toBe(false)

  await $.session.attach({ surface: 'desktop', clientId: 'desktop:default' })
  await clock.advance(500)
  expect(wroteSessionFile(writes)).toBe(true)
})
