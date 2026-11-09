import { copyFileSync, cpSync, mkdirSync } from 'node:fs'
mkdirSync('dist', { recursive: true })
copyFileSync('src/styles.css', 'dist/styles.css')
cpSync('src/assets', 'dist/assets', { recursive: true })
cpSync('src/styles', 'dist/styles', { recursive: true })
