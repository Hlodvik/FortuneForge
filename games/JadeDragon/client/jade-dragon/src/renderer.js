import * as T from 'three';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { loadArtwork, paintArtwork } from './living-art.js';
import { createSymbol } from './models.js';
import { planReelStops, reelDistance } from './reel-motion.js';
import { CASCADE_SYMBOL_IDS, SYMBOLS, validateGrid } from './symbols.js';
import phoenixArtwork from './artwork/phoenix.svg';
import knotArtwork from './artwork/knot.svg';

const BOARD_HEIGHT = 6.35;
const ARTWORK = {
  S07: 'dragon', S08: 'pearl', S20: 'bonus', S21: 'pearl', S22: 'dragon',
  dragon: 'dragon', pearl: 'pearl', phoenix: 'phoenix', knot: 'knot',
};
const CASCADE_AWARD_TIME = .65;
const CASCADE_CLEAR_TIME = .30;
const CASCADE_DROP_TIME = .48;
const cellKey = (reel, row) => reel + ':' + row;
// These icons fill the moving strip while the host settles the round.
const ROLLING_ICONS = ['S01', 'S02', 'S05', 'S04', 'S06', 'S03'];

export class JadeRenderer {
  constructor(host) {
    this.host = host;
    this.motion = !matchMedia('(prefers-reduced-motion: reduce)').matches;
    this.speed = 1;
    this.labels = new Map();
    this.models = new Map();
    this.artwork = new Map();
    this.visibleArtwork = new Set();
    this.reels = [];
    this.renderer = new T.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
    this.renderer.toneMapping = T.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = .92;
    host.append(this.renderer.domElement);

    this.scene = new T.Scene();
    this.camera = new T.OrthographicCamera(-5.2, 5.2, 3.5, -3.5, .1, 50);
    this.camera.position.z = 20;
    const environment = new T.PMREMGenerator(this.renderer);
    const room = new RoomEnvironment();
    this.scene.environment = environment.fromScene(room, .04).texture;
    room.dispose();
    environment.dispose();
    const halfBoard = BOARD_HEIGHT / 2;
    this.renderer.clippingPlanes = [
      new T.Plane(new T.Vector3(0, 1, 0), halfBoard),
      new T.Plane(new T.Vector3(0, -1, 0), halfBoard),
    ];
    this.scene.environmentIntensity = .5;
    this.scene.add(new T.AmbientLight('#e8ffe3', .6));
    const light = new T.DirectionalLight('#ffe7ab', 2.5);
    light.position.set(-3, 5, 8);
    this.scene.add(light);

    this.tiles = new T.Group();
    this.scene.add(this.tiles);
    this.tileMaterials = ['#102b21', '#0a3024', '#466d25'].map(color =>
      new T.MeshBasicMaterial({ color, transparent: true, opacity: .85 }),
    );
    for (const kind of ['dragon', 'pearl', 'bonus']) {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 256;
      const texture = new T.CanvasTexture(canvas);
      texture.colorSpace = T.SRGBColorSpace;
      const artwork = { ctx: canvas.getContext('2d'), texture };
      this.artwork.set(kind, artwork);
      loadArtwork(kind).then(image => {
        artwork.image = image;
        this.paintSymbolArt(kind, 0);
      }).catch(() => {
        artwork.failed = true;
        for (const reel of this.reels) {
          for (const cell of reel.cells) {
            if (ARTWORK[cell.id] === kind) this.setSymbol(cell, cell.id, true);
          }
        }
      });
    }

    const textures = new T.TextureLoader();
    for (const [kind, url] of Object.entries({ phoenix: phoenixArtwork, knot: knotArtwork })) {
      textures.load(url, texture => {
        texture.colorSpace = T.SRGBColorSpace;
        this.artwork.set(kind, { texture });
        for (const reel of this.reels) {
          for (const cell of reel.cells) {
            if (ARTWORK[cell.id] === kind) this.setSymbol(cell, cell.id, true);
          }
        }
      });
    }

    this.observer = new ResizeObserver(() => this.resize());
    this.observer.observe(host);
    this.resize();
    this.renderer.setAnimationLoop(now => this.frame(now));
    this.renderer.domElement.addEventListener('webglcontextlost', event => {
      event.preventDefault();
      this.contextLost = true;
      this.host.dispatchEvent(new Event('renderer-lost'));
    });
    document.fonts.load('700 52px "Cormorant Garamond"').then(() => this.refreshLabels()).catch(() => {});
  }

  resize() {
    const w = this.host.clientWidth;
    const h = this.host.clientHeight;
    if (!w || !h) return;
    const aspect = w / h;
    const width = Math.max(10.4, 7 * aspect);
    const height = Math.max(7, 10.4 / aspect);
    this.camera.left = -width / 2;
    this.camera.right = width / 2;
    this.camera.top = height / 2;
    this.camera.bottom = -height / 2;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
    this.layout();
  }

  layout() {
    if (!this.reels.length) return;
    const step = (this.camera.right - this.camera.left - .75) / this.reels.length;
    this.reels.forEach((reel, col) => {
      reel.group.position.x = (col - (this.reels.length - 1) / 2) * step;
      for (const tile of reel.backgrounds) tile.scale.x = (step - .05) / 1.88;
    });
  }

  refreshLabels() {
    const previous = [...this.labels.values()];
    this.labels.clear();
    for (const reel of this.reels) {
      for (const cell of reel.cells) cell.label.material.map = this.label(cell.id);
    }
    for (const texture of previous) texture.dispose();
  }

  label(id) {
    if (this.labels.has(id)) return this.labels.get(id);
    const canvas = document.createElement('canvas');
    canvas.width = 300;
    canvas.height = 48;
    const ctx = canvas.getContext('2d');
    const name = SYMBOLS[id][1].toUpperCase();
    const font = size => '700 ' + size + 'px "Cormorant Garamond", Georgia, serif';
    ctx.font = font(52);
    let metrics = ctx.measureText(name);
    const glyphHeight = metrics.actualBoundingBoxAscent + metrics.actualBoundingBoxDescent;
    if (glyphHeight > 0) {
      ctx.font = font(52 * 38 / glyphHeight);
      metrics = ctx.measureText(name);
    }
    const baseline = (canvas.height + metrics.actualBoundingBoxAscent - metrics.actualBoundingBoxDescent) / 2;
    ctx.textAlign = 'center';
    ctx.fillStyle = '#fff0bd';
    ctx.strokeStyle = '#031b12';
    ctx.lineWidth = 2;
    ctx.strokeText(name, 150, baseline, 284);
    ctx.fillText(name, 150, baseline, 284);
    const texture = new T.CanvasTexture(canvas);
    this.labels.set(id, texture);
    return texture;
  }

  rowY(row) {
    return (this.rows / 2 - .5 - row) * this.rowHeight;
  }

  makeBoard(reelCount, rows) {
    if (this.rows === rows && this.reels.length === reelCount) return;
    for (const reel of this.reels) {
      for (const cell of reel.cells) {
        cell.label.material.dispose();
        cell.sprite.material.dispose();
      }
    }
    this.tiles.clear();
    this.rows = rows;
    this.rowHeight = BOARD_HEIGHT / rows;
    this.tileGeometry?.dispose();
    this.tileGeometry = new T.PlaneGeometry(1.88, this.rowHeight - .05);

    this.reels = Array.from({ length: reelCount }, (_, col) => {
      const group = new T.Group();
      this.tiles.add(group);
      const backgrounds = Array.from({ length: rows }, (_, row) => {
        const tile = new T.Mesh(this.tileGeometry, this.tileMaterials[(col + row) % 2]);
        tile.position.set(0, this.rowY(row), -.6);
        group.add(tile);
        return tile;
      });
      // A buffer tile above the board enters as the bottom tile leaves.
      const cells = Array.from({ length: rows + 1 }, () => {
        const slot = new T.Group();
        const visual = new T.Group();
        const sprite = new T.Sprite(new T.SpriteMaterial({ transparent: true, depthWrite: false }));
        const artSize = this.rowHeight;
        sprite.scale.set(artSize, artSize, 1);
        sprite.position.y = .07;
        visual.add(sprite);
        const label = new T.Sprite(new T.SpriteMaterial({ transparent: true, depthWrite: false }));
        label.scale.set(1.7, .27, 1);
        label.position.set(0, -this.rowHeight * .40, .9);
        slot.add(visual, label);
        group.add(slot);
        return { slot, visual, sprite, label, models: new Map() };
      });
      return { group, backgrounds, cells };
    });
    this.layout();
  }

  setSymbol(cell, id, force = false) {
    if (cell.id === id && !force) return;
    cell.id = id;
    cell.label.material.map = this.label(id);
    cell.label.material.needsUpdate = true;
    if (cell.model) cell.model.visible = false;
    cell.model = null;
    const kind = ARTWORK[id];
    const artwork = this.artwork.get(kind);
    cell.sprite.visible = Boolean(artwork && !artwork.failed);
    if (cell.sprite.visible) {
      cell.sprite.material.map = artwork.texture;
      cell.sprite.material.needsUpdate = true;
      return;
    }

    const modelKind = SYMBOLS[id][0];
    if (!cell.models.has(modelKind)) {
      if (!this.models.has(modelKind)) this.models.set(modelKind, createSymbol(modelKind));
      const model = this.models.get(modelKind).clone();
      model.scale.setScalar(Math.min(.70, this.rowHeight * .43));
      model.position.y = .10;
      cell.models.set(modelKind, model);
      cell.visual.add(model);
    }
    cell.model = cell.models.get(modelKind);
    cell.model.visible = true;
    cell.model.rotation.set(0, id === 'S09' ? .45 : 0, id === 'S06' ? .20 : 0);
  }

  drawReel(col, distance, landing) {
    const wholeRows = Math.floor(distance);
    const offset = distance - wholeRows;
    const icons = CASCADE_SYMBOL_IDS.includes(this.grid[0][0]) ? CASCADE_SYMBOL_IDS : ROLLING_ICONS;
    this.reels[col].cells.forEach((cell, index) => {
      const row = index - 1;
      const stripRow = row - wholeRows;
      const landedId = landing?.grid[col][stripRow + landing.stop.row];
      const originalId = this.grid[col][stripRow];
      const icon = icons[((stripRow - col) % icons.length + icons.length) % icons.length];
      this.setSymbol(cell, landedId ?? originalId ?? icon);
      cell.slot.position.y = this.rowY(row + offset);
      cell.slot.scale.setScalar(1);
      cell.slot.visible = row >= 0 || offset > 0;
    });
  }

  setGrid(grid) {
    validateGrid(grid);
    this.grid = grid;
    this.makeBoard(grid.length, grid[0].length);
    this.reels.forEach((_, col) => this.drawReel(col, 0));
  }

  clearWins() {
    this.wins = new Set();
    this.reels.forEach((reel, col) => {
      reel.backgrounds.forEach((tile, row) => {
        tile.material = this.tileMaterials[(col + row) % 2];
      });
    });
  }

  startSpin() {
    this.clearWins();
    if (this.motion) this.animation = { last: performance.now(), time: 0, landed: 0 };
  }

  settleSpin(grid, { instant, onLand }) {
    validateGrid(grid);
    if (!this.animation || instant) {
      this.animation = null;
      this.setGrid(grid);
      if (!this.contextLost) this.renderer.render(this.scene, this.camera);
      onLand(grid.length - 1);
      return Promise.resolve();
    }
    this.makeBoard(grid.length, grid[0].length);
    const stops = planReelStops(this.animation.time, grid.length);
    return new Promise(resolve => {
      this.animation.settlement = { grid, stops, onLand, resolve };
    });
  }

  finishSpin() {
    const animation = this.animation;
    if (!animation) return;
    this.animation = null;
    const result = animation.settlement;
    this.setGrid(result?.grid ?? this.grid);
    if (!this.contextLost) this.renderer.render(this.scene, this.camera);
    if (result) {
      if (animation.landed < result.grid.length) result.onLand(result.grid.length - 1);
      result.resolve();
    }
  }

  highlight(positions) {
    this.clearWins();
    this.wins = new Set(positions.map(({ reel, row }) => cellKey(reel, row)));
    this.reels.forEach((reel, col) => {
      reel.backgrounds.forEach((tile, row) => {
        if (this.wins.has(cellKey(col, row))) tile.material = this.tileMaterials[2];
      });
    });
  }

  celebrate(result) {
    this.highlight(result.payout.paylines.flatMap(line =>
      line.matches.flatMap(p => p.match.positions),
    ));
  }

  playCascadeStep(step) {
    validateGrid(step.reels);
    if (!this.motion || this.contextLost) {
      this.setGrid(step.reels);
      return Promise.resolve();
    }
    this.highlight(step.awards.flatMap(award => award.positions));
    return new Promise(resolve => {
      this.cascade = {
        step,
        cleared: new Set(step.cleared.map(({ reel, row }) => cellKey(reel, row))),
        hasDrop: step.dropRows.some(reel => reel.some(distance => distance > 0)),
        time: 0,
        last: performance.now(),
        dropping: false,
        resolve,
      };
    });
  }

  advanceCascade(now) {
    const cascade = this.cascade;
    if (!cascade) return;
    cascade.time += Math.max(0, now - cascade.last) * this.speed / 1000;
    cascade.last = now;
    if (cascade.time < CASCADE_AWARD_TIME) return;

    const dropStart = CASCADE_AWARD_TIME + CASCADE_CLEAR_TIME;
    if (!cascade.hasDrop || cascade.time >= dropStart + CASCADE_DROP_TIME) {
      this.finishCascade();
      return;
    }
    if (cascade.time >= dropStart && !cascade.dropping) {
      this.clearWins();
      this.setGrid(cascade.step.reels);
      cascade.dropping = true;
    }
  }

  finishCascade() {
    const cascade = this.cascade;
    if (!cascade) return;
    this.cascade = null;
    this.clearWins();
    this.setGrid(cascade.step.reels);
    cascade.resolve();
  }

  poseCascadeCell(cell, col, row) {
    const cascade = this.cascade;
    if (!cascade || row < 0 || cascade.time < CASCADE_AWARD_TIME) return false;

    if (cascade.dropping) {
      const progress = (cascade.time - CASCADE_AWARD_TIME - CASCADE_CLEAR_TIME) / CASCADE_DROP_TIME;
      const distance = cascade.step.dropRows[col][row];
      cell.slot.position.y = this.rowY(row - distance * (1 - progress ** 2));
      return distance > 0;
    }
    if (cascade.cleared.has(cellKey(col, row))) {
      const progress = (cascade.time - CASCADE_AWARD_TIME) / CASCADE_CLEAR_TIME;
      cell.slot.scale.setScalar(1 - progress);
      return true;
    }
    return false;
  }

  paintSymbolArt(kind, time) {
    const artwork = this.artwork.get(kind);
    if (!artwork.image) return;
    paintArtwork(artwork.ctx, {
      image: artwork.image, width: 256, height: 256, kind, time, reduced: !this.motion,
    });
    artwork.texture.needsUpdate = true;
  }

  poseSymbol(cell, time, moving, winning) {
    const animated = this.motion && !moving;
    const lift = animated && winning ? (1 + Math.sin(time * 6)) * .05 : 0;
    cell.visual.position.y = lift;
    cell.visual.scale.setScalar(1 + lift);
    if (!cell.model) return;

    const kind = SYMBOLS[cell.id][0];
    const turn = animated && (kind === 'coin' || kind === 'jade') ? time * 1.35 : 0;
    const sway = animated ? Math.sin(time * 2.4) : 0;
    cell.model.rotation.y = (cell.id === 'S09' ? .45 : 0) + turn + sway * .12;
    cell.model.rotation.z = (cell.id === 'S06' ? .20 : 0) + sway * (kind === 'koi' ? .25 : .09);
    cell.model.position.y = .10 + sway * (kind === 'koi' ? .13 : .045);
    cell.model.position.x = kind === 'koi' ? Math.cos(time * 2.4) * (animated ? .12 : 0) : 0;
    if (animated && kind === 'lotus') cell.visual.scale.setScalar(1 + lift + sway * .06);
  }

  frame(now) {
    if (this.contextLost) return;
    this.advanceCascade(now);
    const animation = this.animation;
    const result = animation?.settlement;
    if (animation) {
      animation.time += Math.max(0, now - animation.last) * this.speed / 1000;
      animation.last = now;
    }
    this.visibleArtwork.clear();

    this.reels.forEach((reel, col) => {
      const stop = result?.stops[col];
      const moving = Boolean(animation && (!stop || animation.time < stop.end));
      if (animation) {
        const distance = reelDistance(animation.time, stop);
        this.drawReel(col, distance, stop && { grid: result.grid, stop });
      }
      if (stop && !moving && animation.landed === col) {
        animation.landed++;
        result.onLand(col);
      }
      reel.cells.forEach((cell, index) => {
        if (!cell.slot.visible) return;
        if (cell.sprite.visible) this.visibleArtwork.add(ARTWORK[cell.id]);
        const row = index - 1;
        const cascading = this.poseCascadeCell(cell, col, row);
        this.poseSymbol(cell, now / 1000 + col * .49 + index * .31, moving || cascading,
          this.wins?.has(cellKey(col, row)));
      });
    });

    if (this.motion && this.visibleArtwork.size && now - (this.lastArtFrame ?? -Infinity) >= 50) {
      this.lastArtFrame = now;
      for (const kind of this.visibleArtwork) this.paintSymbolArt(kind, now / 1000);
    }
    if (result && animation.landed === this.reels.length) this.finishSpin();
    else this.renderer.render(this.scene, this.camera);
  }
}
