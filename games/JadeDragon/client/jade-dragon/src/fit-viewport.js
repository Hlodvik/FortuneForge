// Fit the complete cabinet, including its controls, without changing reel proportions.
const shell = document.querySelector('.game-shell');
const cabinet = document.querySelector('.machine');
const scene = document.querySelector('.scene-art');
let pending = false;

function allowSpaceForDragon(width, height, availableHeight) {
  cabinet.style.removeProperty('--jade-cabinet-width');
  shell.style.removeProperty('--jade-cabinet-top');
  scene.style.removeProperty('--jade-scene-x');
  if (width <= 520 || document.fullscreenElement) return;

  // The face and its whiskers in the 1672 × 941 sanctuary image.
  const imageScale = Math.max(width / 1672, height / 941);
  const imageWidth = 1672 * imageScale;
  const imageHeight = 941 * imageScale;
  const clearance = 12;
  const centeredLeft = (width - imageWidth) / 2;
  const imageLeft = Math.max(centeredLeft, Math.min(0, clearance - 255 * imageScale));
  if (imageLeft !== centeredLeft) scene.style.setProperty('--jade-scene-x', `${100 * imageLeft / (width - imageWidth)}%`);

  const faceRight = imageLeft + 480 * imageScale;
  const clearWidth = width - 2 * (faceRight + clearance);
  if (width >= height * 1.25 || clearWidth >= width * .5) {
    cabinet.style.setProperty('--jade-cabinet-width', `${clearWidth}px`);
    return;
  }

  // Tall windows can keep a wider cabinet by putting it below the dragon's face.
  const faceBottom = (height - imageHeight) / 2 + 314 * imageScale + clearance;
  const naturalShell = shell.getBoundingClientRect();
  const cabinetTop = cabinet.getBoundingClientRect().top - naturalShell.top;
  const belowCabinetTop = Math.max(naturalShell.height, shell.scrollHeight) - cabinetTop;
  const safeTop = Math.max(faceBottom, faceBottom * belowCabinetTop / (availableHeight - faceBottom));
  shell.style.setProperty('--jade-cabinet-top', `${Math.max(0, safeTop - cabinetTop)}px`);
}

function fit() {
  pending = false;
  const root = document.documentElement;
  const availableHeight = root.clientHeight - 4;
  const availableWidth = root.clientWidth - 4;
  if (availableHeight <= 0 || availableWidth <= 0) return;

  // Otherwise zoom can widen an auto-width shell and make the reels taller again.
  shell.style.setProperty('--jade-fit-width', `${Math.min(1280, availableWidth)}px`);
  shell.style.removeProperty('--jade-laptop-scale');
  // Measuring the unscaled cabinet can briefly introduce scrollbars.
  allowSpaceForDragon(window.innerWidth, window.innerHeight, availableHeight);
  const natural = shell.getBoundingClientRect();
  let scale = Math.min(1, availableHeight / Math.max(natural.height, shell.scrollHeight),
    availableWidth / Math.max(natural.width, shell.scrollWidth));
  shell.style.setProperty('--jade-laptop-scale', String(scale));

  // Font wrapping and fractional pixels can change the final height after zoom.
  for (let pass = 0; pass < 2; pass++) {
    const actual = shell.getBoundingClientRect();
    const correction = Math.min(1, availableHeight / actual.height,
      availableWidth / Math.max(actual.width, shell.scrollWidth * scale));
    if (correction >= 0.9999) break;
    scale *= correction;
    shell.style.setProperty('--jade-laptop-scale', String(scale));
  }
}

function scheduleFit() {
  if (pending) return;
  pending = true;
  requestAnimationFrame(fit);
}

new ResizeObserver(scheduleFit).observe(shell);
new ResizeObserver(scheduleFit).observe(document.documentElement);
addEventListener('resize', scheduleFit);
document.addEventListener('fullscreenchange', scheduleFit);
document.fonts.ready.then(scheduleFit);
scheduleFit();
