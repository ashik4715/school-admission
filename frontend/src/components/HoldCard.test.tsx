import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { Hold } from '../types';
import { HoldCard, effectiveStatus } from './HoldCard';

const NOW = new Date('2026-11-10T06:00:00Z').getTime();

function makeHold(overrides: Partial<Hold> = {}): Hold {
  return {
    holdId: 'h_1',
    classId: 'c_1',
    className: 'Class 6 - Morning Shift',
    status: 'HELD',
    expiresAt: new Date(NOW + 300_000).toISOString(),
    ...overrides
  };
}

describe('HoldCard', () => {
  it('shows the remaining time and keeps the pay button enabled inside the window', () => {
    const onConfirm = vi.fn();
    render(
      <HoldCard
        hold={makeHold()}
        now={NOW}
        offsetMs={0}
        holdDurationSeconds={600}
        onConfirm={onConfirm}
        onCancel={vi.fn()}
      />
    );

    expect(screen.getByText('05:00')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /confirm/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /cancel application/i })).toBeEnabled();
  });

  it('shows EXPIRED and disables both buttons once the deadline has passed', async () => {
    render(
      <HoldCard
        hold={makeHold({ expiresAt: new Date(NOW - 1_000).toISOString() })}
        now={NOW}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />
    );

    expect(screen.getByText('Expired')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /confirm/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /cancel application/i })).toBeDisabled();
  });

  it('reports success to the parent and blocks the buttons while confirming', async () => {
    const onConfirm = vi.fn();
    render(
      <HoldCard
        hold={makeHold()}
        now={NOW}
        onConfirm={onConfirm}
        onCancel={vi.fn()}
        busyAction="confirm"
      />
    );

    expect(screen.getByRole('button', { name: /confirming/i })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: /confirming/i }));
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('cannot confirm a cancelled application', () => {
    render(<HoldCard hold={makeHold({ status: 'CANCELLED' })} now={NOW} onConfirm={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.getByText('Cancelled')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /confirm/i })).toBeDisabled();
  });

  it('derives EXPIRED from the deadline even if the stored status is stale', () => {
    const hold = makeHold({ expiresAt: new Date(NOW - 5).toISOString() });
    expect(effectiveStatus(hold, NOW)).toBe('EXPIRED');
    expect(effectiveStatus(hold, NOW - 10)).toBe('HELD');
    expect(effectiveStatus({ ...hold, status: 'CONFIRMED' }, NOW)).toBe('CONFIRMED');
  });
});
