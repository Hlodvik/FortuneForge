import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises'

// C# embeds this same game-owned source. The client copy is a build artifact.
await mkdir(new URL('../src/assets/', import.meta.url), { recursive: true })
for (const name of ['frontier-rules.json', 'frontier-outlaw-rules.json']) {
  const source = new URL(`../../../${name}`, import.meta.url)
  const destination = new URL(`../src/assets/${name}`, import.meta.url)
  if (name === 'frontier-rules.json') {
    const { bonus: retiredFeature, ...base } = JSON.parse(await readFile(source, 'utf8'))
    void retiredFeature
    await writeFile(destination, JSON.stringify(base, null, 2) + '\n')
  } else await copyFile(source, destination)
}
