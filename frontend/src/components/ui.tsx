import type { ReactNode } from 'react';
import type { HoldStatus } from '../types';

export function Badge({ status }: { status: HoldStatus }) {
  const labels: Record<HoldStatus, string> = {
    HELD: 'Seat held',
    CONFIRMED: 'Confirmed',
    EXPIRED: 'Expired',
    CANCELLED: 'Cancelled'
  };

  return <span className={`badge badge--${status.toLowerCase()}`}>{labels[status]}</span>;
}

interface SeatBarProps {
  total: number;
  held: number;
  confirmed: number;
}

/** Visual split of a class' seats: confirmed (solid) + held (striped) + free. */
export function SeatBar({ total, held, confirmed }: SeatBarProps) {
  const safeTotal = Math.max(total, 1);
  const confirmedPct = Math.min(100, (confirmed / safeTotal) * 100);
  const heldPct = Math.min(100 - confirmedPct, (held / safeTotal) * 100);

  return (
    <div className="seatbar" role="img" aria-label={`${confirmed} confirmed, ${held} on hold, ${Math.max(0, total - confirmed - held)} free of ${total} seats`}>
      <span className="seatbar__part seatbar__part--confirmed" style={{ width: `${confirmedPct}%` }} />
      <span className="seatbar__part seatbar__part--held" style={{ width: `${heldPct}%` }} />
    </div>
  );
}

interface NoticeProps {
  kind: 'success' | 'error' | 'info';
  children: ReactNode;
  onDismiss?: () => void;
}

export function Notice({ kind, children, onDismiss }: NoticeProps) {
  return (
    <div className={`notice notice--${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      <span className="notice__icon" aria-hidden="true">
        {kind === 'success' ? '✓' : kind === 'error' ? '!' : 'i'}
      </span>
      <div className="notice__text">{children}</div>
      {onDismiss && (
        <button type="button" className="notice__close" onClick={onDismiss} aria-label="Dismiss message">
          ×
        </button>
      )}
    </div>
  );
}

export function EmptyState({ title, hint, icon = '🗂' }: { title: string; hint?: string; icon?: string }) {
  return (
    <div className="empty">
      <div className="empty__icon" aria-hidden="true">{icon}</div>
      <h3>{title}</h3>
      {hint && <p>{hint}</p>}
    </div>
  );
}

export function Spinner({ label }: { label?: string }) {
  return (
    <span className="spinner" role="status" aria-live="polite">
      <span className="spinner__dot" aria-hidden="true" />
      {label ?? 'Loading…'}
    </span>
  );
}
