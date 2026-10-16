import { formatCountdown } from './time';

describe('formatCountdown', () => {
  it('formatea m:ss y acota al total y a cero', () => {
    expect(formatCountdown(300_400, 300_000)).toBe('5:00');
    expect(formatCountdown(61_001, 300_000)).toBe('1:02');
    expect(formatCountdown(-5, 300_000)).toBe('0:00');
  });
});
