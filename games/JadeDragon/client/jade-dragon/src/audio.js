const landing = new URL('./assets/audio/gao-reel-land.mp3', import.meta.url).href;
const impact = new URL('./assets/audio/deep-impact-spin.mp3', import.meta.url).href;
const plucks = [
  new URL('./assets/audio/liuqin-button.mp3', import.meta.url).href,
  new URL('./assets/audio/guzheng-button.mp3', import.meta.url).href,
];
const bells = [
  new URL('./assets/audio/bell-flam-01.mp3', import.meta.url).href,
  new URL('./assets/audio/bell-flam-02.mp3', import.meta.url).href,
];

export class GameAudio {
  context = null;
  output = null;
  buffers = new Map();
  voices = new Map();
  loading = null;

  async toggle() {
    if (this.output) {
      this.output.disconnect();
      this.output = null;
      this.voices.clear();
      return false;
    }

    let output;
    try {
      const context = this.context ??= new AudioContext();
      output = context.createGain();
      output.connect(context.destination);
      this.output = output;
      const resumed = context.resume();
      this.loading ??= Promise.all([landing, impact, ...plucks, ...bells].map(async url => {
        try {
          const response = await fetch(url);
          if (!response.ok) return;
          const buffer = await context.decodeAudioData(await response.arrayBuffer());
          this.buffers.set(url, buffer);
        } catch {
          // A missing sample must never interrupt a round.
        }
      }));
      await resumed;
      void this.loading.then(() => {
        if (this.output === output) this.button();
      });
    } catch {
      if (output && this.output === output) {
        output.disconnect();
        this.output = null;
      }
    }
    return Boolean(this.output);
  }

  pick(sounds) {
    const available = sounds.filter(url => this.buffers.has(url));
    return available[Math.floor(Math.random() * available.length)];
  }

  play(url, group) {
    const buffer = this.buffers.get(url);
    if (!this.output || !buffer || this.context.state !== 'running') return;

    try {
      this.startSample(buffer, group);
    } catch {
      // Sound playback must never interrupt a round or a control.
    }
  }

  startSample(buffer, group) {
    const now = this.context.currentTime;
    const previous = this.voices.get(group);
    if (previous) {
      previous.gain.gain.setValueAtTime(1, now);
      previous.gain.gain.linearRampToValueAtTime(0, now + .02);
      previous.source.stop(now + .02);
    }
    const source = this.context.createBufferSource();
    const gain = this.context.createGain();
    source.buffer = buffer;
    source.connect(gain);
    gain.connect(this.output);
    source.onended = () => {
      source.disconnect();
      gain.disconnect();
      if (this.voices.get(group)?.source === source) this.voices.delete(group);
    };
    source.start();
    this.voices.set(group, { source, gain });
  }

  button() { this.play(this.pick(plucks), 'button'); }
  spin() { this.play(impact, 'spin'); }
  land() { this.play(landing, 'landing'); }
  special() { this.play(this.pick(bells), 'special'); }

  note(frequency, duration, volume, delay) {
    if (!this.output || this.context.state !== 'running') return;
    const ctx = this.context;
    const time = ctx.currentTime + delay;
    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();
    oscillator.frequency.setValueAtTime(frequency, time);
    gain.gain.setValueAtTime(0, time);
    gain.gain.linearRampToValueAtTime(volume, time + .008);
    gain.gain.exponentialRampToValueAtTime(.0001, time + duration);
    oscillator.connect(gain);
    gain.connect(this.output);
    oscillator.onended = () => {
      oscillator.disconnect();
      gain.disconnect();
    };
    oscillator.start(time);
    oscillator.stop(time + duration + .02);
  }

  win(big = false) {
    const notes = big ? [523, 659, 784, 1047, 1319] : [523, 659, 784];
    try {
      notes.forEach((frequency, index) => {
        this.note(frequency, .6, .055, index * .11);
        this.note(frequency * 2, .3, .008, index * .11);
      });
    } catch {
      // Sound playback must never interrupt a round.
    }
  }
}
