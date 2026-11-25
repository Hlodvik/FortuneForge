import { mkdir, readFile, writeFile } from 'node:fs/promises'

// C# embeds this same game-owned source. The client copy is a build artifact.
await mkdir(new URL('../src/assets/', import.meta.url), { recursive: true })
for (const name of ['frontier-rules.json', 'frontier-outlaw-rules.json']) {
  const source = new URL(`../../../${name}`, import.meta.url)
  const destination = new URL(`../src/assets/${name}`, import.meta.url)
  let content = await readFile(source, 'utf8')
  if (name === 'frontier-rules.json') {
    const { bonus: retiredFeature, ...base } = JSON.parse(content)
    void retiredFeature
    content = JSON.stringify(base, null, 2) + '\n'
  }
  let current
  try { current = await readFile(destination, 'utf8') }
  catch (error) { if (error.code !== 'ENOENT') throw error }
  if (current?.replace(/\r\n?/g, '\n') !== content.replace(/\r\n?/g, '\n')) await writeFile(destination, content)
}
