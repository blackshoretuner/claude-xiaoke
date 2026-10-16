// 小克的立绘：art/ 里每种心情一张 WebP（以 Claude 娘立绘为参考、用 Qwen-Image 2.1 改的表情），
// 这里把它包进一张 SVG：呼吸、被摸时回弹、开心时跳一下、眨眼，再叠上爱心、星星、zzZ 之类的小特效。
// 坐标都是素材自己的像素（manifest.json 的 width × height，现在是 520 × 488）。

import type { Mood } from '../types'

export type Sprites = {
  width: number
  height: number
  moods: Partial<Record<Mood, string>>
  blink: { x: number; y: number; width: number; height: number; base64: string } | null
}

// 睁着眼的表情才眨眼。
const BLINKS: Mood[] = ['idle', 'thinking', 'working', 'worried', 'surprised']

const LINE = '#B4511F'
const CREAM = '#F6EEE2'

const sparkle = (x: number, y: number, r: number, fill: string, delay: number) =>
  `<path class="twinkle" style="animation-delay:${delay}s" d="M${x} ${y - r} Q${x} ${y} ${x + r} ${y} Q${x} ${y} ${x} ${y + r} Q${x} ${y} ${x - r} ${y} Q${x} ${y} ${x} ${y - r} Z" fill="${fill}"/>`

const heart = (x: number, y: number, s: number, delay: number) =>
  `<path class="float" style="animation-delay:${delay}s" d="M${x} ${y + s * 0.35} C${x} ${y - s * 0.25} ${x - s} ${y - s * 0.3} ${x - s} ${y + s * 0.2} C${x - s} ${y + s * 0.65} ${x - s * 0.3} ${y + s * 0.9} ${x} ${y + s * 1.2} C${x + s * 0.3} ${y + s * 0.9} ${x + s} ${y + s * 0.65} ${x + s} ${y + s * 0.2} C${x + s} ${y - s * 0.3} ${x} ${y - s * 0.25} ${x} ${y + s * 0.35} Z" fill="#FF7A8A" stroke="#C2405A" stroke-width="1.4"/>`

function effects(mood: Mood): string {
  switch (mood) {
    case 'thinking':
      return `<g fill="${CREAM}" stroke="${LINE}" stroke-width="2">
        <circle class="dot" cx="176" cy="66" r="6"/>
        <circle class="dot" style="animation-delay:.3s" cx="154" cy="42" r="8.5"/>
        <circle class="dot" style="animation-delay:.6s" cx="124" cy="16" r="12"/></g>`
    case 'working':
      return sparkle(66, 262, 11, '#FFB36B', 0) + sparkle(210, 300, 9, '#F08A3E', 0.4) + sparkle(86, 420, 8, '#FFD08A', 0.8)
    case 'happy':
      return sparkle(150, 86, 14, '#FFC64D', 0) + sparkle(410, 118, 12, '#F08A3E', 0.35) + sparkle(128, 190, 10, '#FFB36B', 0.7) + sparkle(415, 36, 9, '#FFD08A', 1)
    case 'pat':
      return heart(404, 88, 15, 0) + heart(160, 108, 12, 0.45) + heart(420, 172, 10, 0.9)
    case 'dizzy':
      return `<g transform="translate(270 22)"><g>
        ${sparkle(-70, 4, 11, '#FFC64D', 0)}${sparkle(66, -4, 9, '#F08A3E', 0.3)}${sparkle(2, 16, 8, '#FFD08A', 0.6)}
        <animateTransform attributeName="transform" type="rotate" from="0" to="360" dur="3s" repeatCount="indefinite"/></g></g>`
    case 'surprised':
      return `<g transform="rotate(-12 172 52)"><path d="M165 18 L180 18 L176 62 L169 62 Z" fill="#F08A3E" stroke="${LINE}" stroke-width="2" stroke-linejoin="round"/>
              <circle cx="172.5" cy="74" r="6" fill="#F08A3E" stroke="${LINE}" stroke-width="2"/></g>`
    case 'sleepy':
      return `<g font-family="'Comic Sans MS','Segoe UI',sans-serif" font-weight="700" fill="#F08A3E" stroke="${LINE}" stroke-width=".8">
        <text class="zz" x="186" y="96" font-size="22">Z</text>
        <text class="zz" style="animation-delay:.9s" x="162" y="66" font-size="28">Z</text>
        <text class="zz" style="animation-delay:1.8s" x="132" y="36" font-size="34">Z</text></g>`
    default:
      return ''
  }
}

const style = (w: number, h: number) => `
  .breathe{animation:breathe 4.2s ease-in-out infinite;transform-origin:${w / 2}px ${h}px}
  .slow{animation-duration:6s}
  @keyframes breathe{0%,100%{transform:translateY(0) scaleY(1)}50%{transform:translateY(1.6px) scaleY(.994)}}
  .blink{opacity:0;animation:blink 4.8s infinite}
  @keyframes blink{0%,93%{opacity:0}94%,96.5%{opacity:1}97.5%,100%{opacity:0}}
  .squish{animation:squish .62s cubic-bezier(.3,1.5,.5,1) 1;transform-origin:${w / 2}px ${h}px}
  @keyframes squish{0%{transform:scale(1,1)}22%{transform:scale(1.04,.94)}52%{transform:scale(.98,1.03)}78%{transform:scale(1.01,.99)}100%{transform:scale(1,1)}}
  .hop{animation:hop .9s ease-out 2;transform-origin:${w / 2}px ${h}px}
  @keyframes hop{0%,100%{transform:translateY(0)}30%{transform:translateY(-10px)}55%{transform:translateY(0) scale(1.02,.98)}70%{transform:translateY(-3px)}}
  .shake{animation:shake .5s ease-in-out 1;transform-origin:${w / 2}px ${h}px}
  @keyframes shake{0%,100%{transform:translateX(0)}20%{transform:translateX(-5px) rotate(-1deg)}40%{transform:translateX(5px) rotate(1deg)}60%{transform:translateX(-3px)}80%{transform:translateX(2px)}}
  .twinkle{animation:twinkle 1.4s ease-in-out infinite;transform-box:fill-box;transform-origin:center}
  @keyframes twinkle{0%,100%{opacity:.25;transform:scale(.6)}50%{opacity:1;transform:scale(1.1)}}
  .float{animation:float 1.6s ease-out infinite;opacity:0}
  @keyframes float{0%{opacity:0;transform:translateY(10px)}25%{opacity:1}100%{opacity:0;transform:translateY(-30px)}}
  .dot{animation:dot 1.5s ease-in-out infinite}
  @keyframes dot{0%,100%{opacity:.25}50%{opacity:1}}
  .zz{animation:zz 2.7s ease-in-out infinite;opacity:0}
  @keyframes zz{0%{opacity:0;transform:translate(6px,8px)}30%{opacity:1}100%{opacity:0;transform:translate(-8px,-14px)}}
`

// nonce 换了，SVG 文本就变，surface 重新载入它，被摸、开心的一次性动画就会再放一遍。
export function frame(mood: Mood, sprites: Sprites, nonce = 0): string | null {
  const base64 = sprites.moods[mood] ?? sprites.moods.idle
  if (base64 === undefined) return null
  const { width: w, height: h, blink } = sprites
  const motion = mood === 'pat' ? 'squish' : mood === 'happy' ? 'hop' : mood === 'dizzy' || mood === 'surprised' ? 'shake' : ''
  const eyes =
    blink !== null && BLINKS.includes(mood)
      ? `<image class="blink" href="data:image/webp;base64,${blink.base64}" x="${blink.x}" y="${blink.y}" width="${blink.width}" height="${blink.height}"/>`
      : ''
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}"><!--${mood}:${nonce}-->
<style>${style(w, h)}</style>
<g class="${motion}"><g class="breathe${mood === 'sleepy' ? ' slow' : ''}">
<image href="data:image/webp;base64,${base64}" width="${w}" height="${h}"/>${eyes}
</g></g>
${effects(mood)}
</svg>`
}
