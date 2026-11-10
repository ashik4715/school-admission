import type { Hold, SchoolClass } from '../types';
import { EmptyState, SeatBar, Spinner } from './ui';

interface ClassListProps {
  classes: SchoolClass[];
  holds: Record<string, Hold>;
  loading: boolean;
  error: string | null;
  hasStudentId: boolean;
  applyingClassId: string | null;
  onApply: (schoolClass: SchoolClass) => void;
  onRefresh: () => void;
}

export function ClassList({
  classes,
  holds,
  loading,
  error,
  hasStudentId,
  applyingClassId,
  onApply,
  onRefresh
}: ClassListProps) {
  if (loading && classes.length === 0) {
    return <Spinner label="Loading classes…" />;
  }

  if (error && classes.length === 0) {
    return (
      <EmptyState
        icon="⚠️"
        title="Classes could not be loaded"
        hint={error}
      />
    );
  }

  if (classes.length === 0) {
    return (
      <EmptyState
        icon="🏫"
        title="No classes yet"
        hint="A school admin can create the first class from the panel above."
      />
    );
  }

  return (
    <div className="grid">
      {classes.map((schoolClass) => {
        const myHold = holds[schoolClass.id];
        const full = schoolClass.availableSeats <= 0;

        let actionLabel = 'Apply for a seat';
        let disabledReason: string | null = null;

        if (!hasStudentId) {
          disabledReason = 'Enter your birth registration number above to apply.';
        } else if (myHold?.status === 'CONFIRMED') {
          actionLabel = 'Seat confirmed';
          disabledReason = 'You already have a confirmed seat in this class.';
        } else if (myHold?.status === 'HELD') {
          actionLabel = 'Application pending';
          disabledReason = 'You already have an active application for this class.';
        } else if (full) {
          actionLabel = 'Class full';
          disabledReason = 'All seats are taken. Try again later — seats free up when holds expire.';
        }

        const busy = applyingClassId === schoolClass.id;

        return (
          <article key={schoolClass.id} className={`classcard ${full ? 'is-full' : ''}`}>
            <header className="classcard__head">
              <h3 className="classcard__name">{schoolClass.name}</h3>
              <div className={`classcard__available ${full ? 'is-none' : ''}`}>
                <span className="classcard__available-num">{schoolClass.availableSeats}</span>
                <span className="classcard__available-label">free</span>
              </div>
            </header>

            <SeatBar
              total={schoolClass.totalSeats}
              held={schoolClass.heldSeats}
              confirmed={schoolClass.confirmedSeats}
            />

            <dl className="classcard__stats">
              <div>
                <dt>Total</dt>
                <dd>{schoolClass.totalSeats}</dd>
              </div>
              <div>
                <dt><span className="dot dot--held" aria-hidden="true" />On hold</dt>
                <dd>{schoolClass.heldSeats}</dd>
              </div>
              <div>
                <dt><span className="dot dot--confirmed" aria-hidden="true" />Confirmed</dt>
                <dd>{schoolClass.confirmedSeats}</dd>
              </div>
            </dl>

            <div className="classcard__footer">
              <button
                type="button"
                className="btn btn--primary"
                disabled={busy || disabledReason !== null}
                title={disabledReason ?? undefined}
                onClick={() => onApply(schoolClass)}
              >
                {busy ? 'Applying…' : actionLabel}
              </button>
              {disabledReason && <p className="classcard__hint">{disabledReason}</p>}
            </div>
          </article>
        );
      })}

      {error && (
        <div className="grid__status">
          <span className="muted">{error}</span>
          <button type="button" className="btn btn--ghost btn--sm" onClick={onRefresh}>Retry</button>
        </div>
      )}
    </div>
  );
}
