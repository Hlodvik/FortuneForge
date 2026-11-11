import { cpSync, mkdirSync } from 'node:fs'

mkdirSync('dist/contracts', { recursive: true })
cpSync('src/contracts', 'dist/contracts', { recursive: true })
