// Fit the complete cabinet, including its controls, without changing reel proportions.
const shell = document.querySelector('.game-shell');
let pending = false;

function fit() {
  pending = false;
  const root = document.documentElement;
  const availableHeight = root.clientHeight - 4;
  const availableWidth = root.clientWidth - 4;
  if (availableHeight <= 0 || availableWidth <= 0) return;

  // Otherwise zoom can widen an auto-width shell and make the reels taller again.
  shell.style.setProperty('--jade-fit-width', `${Math.min(1280, availableWidth)}px`);
  shell.style.removeProperty('--jade-laptop-scale');
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
document.fonts.ready.then(scheduleFit);
scheduleFit();
