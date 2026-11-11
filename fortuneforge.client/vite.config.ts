import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { midnightIsolation } from './build/midnightIsolation.js'
import { neonIsolation } from './build/neonIsolation.js'
import { goldIsolation } from './build/goldIsolation.js'
import { slotPackageIsolation } from './build/slotPackageIsolation.js'

// A distinct asset namespace prevents browsers from reusing legacy catalogue
// chunks after a Hosting release. The content hash still changes per build.
const buildAssetVersion = 'game-catalog-v1'
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    neonIsolation(repositoryRoot),
    midnightIsolation(repositoryRoot),
    goldIsolation(repositoryRoot),
    ...slotPackageIsolation(repositoryRoot),
  ],
  // Game clients are linked source packages. Resolve their peer React imports
  // from this host app so every game shares the same React runtime.
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  // Route loaders are intentionally one-module-per-game. Do not crawl every
  // lazy loader during dev startup: an unavailable sibling package must only
  // fail when its own route is opened.
  optimizeDeps: {
    noDiscovery: true,
    include: ['react', 'react-dom', 'react-dom/client', 'scheduler'],
  },
  build: {
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Keep reusable runtime code out of the application entry chunk. A
        // game importing React or a neutral widget must not import the lobby.
        codeSplitting: {
          includeDependenciesRecursively: false,
          groups: [
            { name: 'react-runtime', test: /[/\\]node_modules[/\\](react|react-dom|scheduler)([/\\]|$)|\0rolldown\//, priority: 30 },
            { name: 'slot-client-core', test: /[/\\]packages[/\\]slot-client-core[/\\]/, priority: 20 },
            { name: 'slot-game-runtime', test: /[/\\]packages[/\\]slot-game-runtime[/\\]/, priority: 20 },
            { name: 'client-presentation', test: /[/\\]packages[/\\]client-presentation[/\\]/, priority: 20 },
            { name: 'wukong', test: /[/\\]games[/\\]Wukong[/\\]client[/\\]/, priority: 10 },
            { name: 'rainbow-realm', test: /[/\\]games[/\\]RainbowRealm[/\\]client[/\\]/, priority: 10 },
            { name: 'pirates-fortune', test: /[/\\]games[/\\]PiratesFortune[/\\]client[/\\]/, priority: 10 },
            { name: 'gods-of-olympus', test: /[/\\]games[/\\]GodsOfOlympus[/\\]client[/\\]/, priority: 10 },
            { name: 'reel-riches', test: /[/\\]games[/\\]ReelRiches[/\\]client[/\\]/, priority: 10 },
            { name: 'high-noon-fortune', test: /[/\\]games[/\\]HighNoonFortune[/\\]client[/\\]/, priority: 10 },
            { name: 'royal-draw', test: /[/\\]games[/\\]RoyalDraw[/\\]client[/\\]/, priority: 10 },
            { name: 'arcane-archives', test: /[/\\]games[/\\]ArcaneArchives[/\\]client[/\\]/, priority: 10 },
            { name: 'cosmic-fortune', test: /[/\\]games[/\\]CosmicFortune[/\\]client[/\\]/, priority: 10 },
            { name: 'ocean-odyssey', test: /[/\\]games[/\\]OceanOdyssey[/\\]client[/\\]/, priority: 10 },
            { name: 'samurai-fortune', test: /[/\\]games[/\\]SamuraiFortune[/\\]client[/\\]/, priority: 10 },
            { name: 'candy-carnival', test: /[/\\]games[/\\]CandyCarnival[/\\]client[/\\]/, priority: 10 },
            { name: 'phantom-manor', test: /[/\\]games[/\\]PhantomManor[/\\]client[/\\]/, priority: 10 },
            { name: 'nordic-legends', test: /[/\\]games[/\\]NordicLegends[/\\]client[/\\]/, priority: 10 },
            { name: 'desert-treasures', test: /[/\\]games[/\\]DesertTreasures[/\\]client[/\\]/, priority: 10 },
            { name: 'robot-revolution', test: /[/\\]games[/\\]RobotRevolution[/\\]client[/\\]/, priority: 10 },
            { name: 'neon-nights', test: /[/\\]games[/\\]NeonNights[/\\]client[/\\]/, priority: 10 },
            { name: 'midnight-vault', test: /[/\\]games[/\\]MidnightVault[/\\]client[/\\]/, priority: 10 },
            { name: 'gold-foundry', test: /[/\\]games[/\\]GoldFoundry[/\\]client[/\\]/, priority: 10 },
          ],
        },
        entryFileNames: `assets/[name]-${buildAssetVersion}-[hash].js`,
        chunkFileNames: `assets/[name]-${buildAssetVersion}-[hash].js`,
        assetFileNames: `assets/[name]-${buildAssetVersion}-[hash][extname]`,
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: process.env.FORTUNEFORGE_API_URL ?? 'http://localhost:5150',
        changeOrigin: true,
      },
    },
  },
})
