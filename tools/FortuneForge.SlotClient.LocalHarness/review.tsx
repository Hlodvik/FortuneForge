import { createRoot } from 'react-dom/client'
import { createElement } from 'react'
import { attachAuthenticatedFrameTransport } from '../../fortuneforge.client/src/features/game-releases/authenticatedFrameTransport'

const game = new URLSearchParams(location.search).get('game') ?? 'pirates'
if (!['pirates', 'olympus', 'jade', 'sunstone'].includes(game)) throw Error('Choose an isolated test game.')
const prefix = `/fixture/${game}`
const health = await fetch(prefix + '/__fixture/health').then(reply => reply.json())
if (!health.localOnly || !health.testFundsOnly || !health.productionEngine || !health.projectId.startsWith('demo-slot-client-')) throw Error('Only isolated production paid testing is permitted.')
const loadAccount = () => fetch(prefix + '/__fixture/account').then(reply => reply.json())
const account = await loadAccount()
const authenticatedFetch = (path: string, init?: RequestInit) => fetch(prefix + path, init)
if (game === 'jade') {
  const frame = document.createElement('iframe')
  frame.title = 'Jade Dragon isolated paid review'; frame.style.cssText = 'width:100%;height:100svh;border:0'; frame.src = '/games/jade-dragon/index.html'
  attachAuthenticatedFrameTransport({ accountId: account.userId, source: () => frame.contentWindow, origin: location.origin, authenticatedFetch, loadAccount })
  document.getElementById('root')!.append(frame)
} else {
  const module = game === 'sunstone' ? await import('../../games/RobotRevolution/client/FortuneForge.Games.RobotRevolution.Client/src/browser')
    : game === 'pirates' ? await import('../../games/PiratesFortune/client/FortuneForge.Games.PiratesFortune.Client/src/browser')
    : await import('../../games/GodsOfOlympus/client/FortuneForge.Games.GodsOfOlympus.Client/src/browser')
  const Game = module.default
  createRoot(document.getElementById('root')!).render(createElement(Game, { account, authenticatedFetch }))
}
