const ROWS_PER_SECOND = 11;
const ACCELERATION_SECONDS = 0.18;
const MINIMUM_ROLL_SECONDS = 0.5;
const BRAKE_SECONDS = 0.44;
const STOP_SPACING_ROWS = 2;

export function rollingDistance(time) {
  if (time < ACCELERATION_SECONDS) {
    return ROWS_PER_SECOND * time ** 2 / (2 * ACCELERATION_SECONDS);
  }
  return ROWS_PER_SECOND * (time - ACCELERATION_SECONDS / 2);
}

export function planReelStops(time, reelCount) {
  const brakeDistance = ROWS_PER_SECOND * BRAKE_SECONDS / 2;
  const firstRow = Math.ceil(rollingDistance(Math.max(time, MINIMUM_ROLL_SECONDS)) + brakeDistance);

  return Array.from({ length: reelCount }, (_, col) => {
    const row = firstRow + col * STOP_SPACING_ROWS;
    const start = (row - brakeDistance) / ROWS_PER_SECOND + ACCELERATION_SECONDS / 2;
    return { row, start, end: start + BRAKE_SECONDS };
  });
}

export function reelDistance(time, stop) {
  if (!stop || time < stop.start) return rollingDistance(time);
  if (time >= stop.end) return stop.row;

  const secondsLeft = stop.end - time;
  return stop.row - ROWS_PER_SECOND * secondsLeft ** 2 / (2 * BRAKE_SECONDS);
}
