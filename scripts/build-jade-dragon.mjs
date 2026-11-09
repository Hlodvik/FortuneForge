import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, cpSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
const root=fileURLToPath(new URL('../',import.meta.url)),source=path.join(root,'games/EmptySlots/client/jade-dragon')
const npm=process.platform==='win32'?'npm.cmd':'npm'
function run(args){const r=spawnSync(npm,args,{cwd:source,stdio:'inherit',shell:process.platform==='win32'});if(r.status!==0)process.exit(r.status??1)}
if(!existsSync(path.join(source,'node_modules/vite')))run(['ci','--ignore-scripts'])
run(['run','build'])
const dest=path.join(root,'fortuneforge.client/public/games/jade-dragon');mkdirSync(dest,{recursive:true});cpSync(path.join(source,'dist'),dest,{recursive:true})
console.log('Jade Dragon presentation built into the host public directory.')
