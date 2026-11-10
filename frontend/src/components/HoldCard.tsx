import type { Hold, HoldStatus } from '../types';
import { formatDuration, formatTime, useNow } from '../utils/time';
import { Badge } from './ui';

export interface HoldCardProps {
  hold: Hold;
  /** Server clock offset in ms (see useServerClock). */
  offsetMs?: number;
  holdDurationSeconds?: number;
  onConfirm: (hold: Hold) => void;
  onCancel: (hold: Hold) => void;
  busyAction?: 'confirm' | 'cancel' | null;
  /** Injectable for tests. */
  now?: number;
}

/** Status as the student should see it right now (HELD past its deadline is EXPIRED). */
export function effectiveStatus(hold: Hold, nowMs: number): HoldStatus {
  if (hold.status === 'HELD' && new Date(hold.expiresAt).getTime() <= nowMs) return 'EXPIRED';
  return hold.status;
}

export function HoldCard({
  hold,
  offsetMs = 0,
  holdDurationSeconds = 600,
  onConfirm,
  onCancel,
  busyAction = null,
  now
}: HoldCardProps) {
  const ticking = useNow(1000);
  const nowMs = now ?? ticking + offsetMs;

  const status = effectiveStatus(hold, nowMs);
  const remainingMs = new Date(hold.expiresAt).getTime() - nowMs;
  const totalMs = holdDurationSeconds * 1000;
  const remainingPct = Math.max(0, Math.min(100, (remainingMs / totalMs) * 100));
  const canPay = status === 'HELD' && remainingMs > 0;
  const urgent = canPay && remainingMs <= 60_000;

  return (
    <article className={`holdcard holdcard--${status.toLowerCase()}`}>
      <header className="holdcard__head">
        <div>
          <h3 className="holdcard__name">{hold.className}</h3>
          <p className="holdcard__meta">
            {status === 'HELD' && <>Apply now, pay by {formatTime(hold.expiresAt)}</>}
            {status === 'CONFIRMED' && <>Admission confirmed. No further action needed.</>}
            {status === 'EXPIRED' && <>The payment window closed and the seat was released.</>}
            {status === 'CANCELLED' && <>You cancelled this application.</>}
          </p>
        </div>
        <Badge status={status} />
      </header>

      {status === 'HELD' && (
        <div className="countdown">
          <div className="countdown__row">
            <span className="countdown__label">
              {remainingMs > 0 ? 'Seat reserved for you' : 'Window closed'}
            </span>
            <span className={`countdown__value ${urgent ? 'is-urgent' : ''}`} aria-live="off">
              {formatDuration(remainingMs)}
            </span>
          </div>
          <div className="countdown__track">
            <span
              className={`countdown__bar ${urgent ? 'is-urgent' : ''}`}
              style={{ width: `${remainingPct}%` }}
            />
          </div>
        </div>
      )}

      <footer className="holdcard__actions">
        <button
          type="button"
          className="btn btn--primary"
          disabled={!canPay || busyAction !== null}
          onClick={() => onConfirm(hold)}
          title={canPay ? undefined : 'The payment window for this application is closed.'}
        >
          {busyAction === 'confirm' ? 'Confirming…' : 'Admission fee paid — confirm'}
        </button>

        <button
          type="button"
          className="btn btn--ghost"
          disabled={status !== 'HELD' || remainingMs <= 0 || busyAction !== null}
          onClick={() => onCancel(hold)}
        >
          {busyAction === 'cancel' ? 'Cancelling…' : 'Cancel application'}
        </button>
      </footer>
    </article>
  );
}
