import { useCallback, useEffect, useMemo, useState } from 'react';
import { api, errorMessage } from './api';
import { ClassList } from './components/ClassList';
import { CreateClassForm } from './components/CreateClassForm';
import { HoldCard } from './components/HoldCard';
import { StudentIdBar } from './components/StudentIdBar';
import { EmptyState, Notice, Spinner } from './components/ui';
import { usePolledResource } from './hooks/usePolledResource';
import { useServerClock } from './hooks/useServerClock';
import type { Hold, SchoolClass } from './types';

const STORAGE_KEY = 'school-admission:student-id';

function formatHoldWindow(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)} sec`;
  const minutes = Math.round(seconds / 60);
  return `${minutes} min`;
}
const CLASS_POLL_MS = 3_000;
const HOLD_POLL_MS = 2_000;

type Tab = 'classes' | 'mine';
type BusyAction = 'confirm' | 'cancel' | null;

interface Toast {
  kind: 'success' | 'error' | 'info';
  text: string;
}

function readStoredStudentId(): string {
  try {
    return window.localStorage.getItem(STORAGE_KEY) ?? '';
  } catch {
    return '';
  }
}

export default function App() {
  const [studentId, setStudentId] = useState<string>(readStoredStudentId);
  const [tab, setTab] = useState<Tab>('classes');
  const [toast, setToast] = useState<Toast | null>(null);
  const [applyTarget, setApplyTarget] = useState<string | null>(null);
  const [busyHold, setBusyHold] = useState<{ id: string; action: BusyAction } | null>(null);

  const clock = useServerClock();

  const classes = usePolledResource<SchoolClass[]>(
    useCallback((signal) => api.listClasses(signal), []),
    [],
    CLASS_POLL_MS
  );

  const myHolds = usePolledResource<Hold[]>(
    useCallback(
      (signal) => (studentId ? api.myHolds(studentId, signal) : Promise.resolve([])),
      [studentId]
    ),
    [],
    HOLD_POLL_MS,
    studentId.length > 0
  );

  // Toast auto-dismiss.
  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), toast.kind === 'error' ? 9000 : 6000);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const saveStudentId = (value: string) => {
    setStudentId(value);
    try {
      window.localStorage.setItem(STORAGE_KEY, value);
    } catch {
      /* private mode: identity just won't persist */
    }
    setToast({ kind: 'info', text: 'Identity saved. Your applications are shown under "My applications".' });
  };

  /** One hold per class, so the "apply" button knows what this student already has. */
  const holdsByClass = useMemo(() => {
    const map: Record<string, Hold> = {};
    for (const hold of myHolds.data) {
      const current = map[hold.classId];
      const rank = (status: Hold['status']) =>
        status === 'CONFIRMED' ? 0 : status === 'HELD' ? 1 : status === 'CANCELLED' ? 2 : 3;
      if (!current || rank(hold.status) < rank(current.status)) map[hold.classId] = hold;
    }
    return map;
  }, [myHolds.data]);

  const handleApply = async (schoolClass: SchoolClass) => {
    if (!studentId) {
      setToast({ kind: 'error', text: 'Enter your 17-digit birth registration number first.' });
      return;
    }

    setApplyTarget(schoolClass.id);
    try {
      const hold = await api.createHold(schoolClass.id, studentId);
      setToast({
        kind: 'success',
        text: `Seat held in "${schoolClass.name}". Pay the admission fee before the timer runs out.`
      });
      setTab('mine');
      await Promise.all([classes.refresh(), myHolds.refresh()]);
      void hold;
    } catch (error) {
      setToast({ kind: 'error', text: errorMessage(error) });
      await Promise.all([classes.refresh(), myHolds.refresh()]);
    } finally {
      setApplyTarget(null);
    }
  };

  const handleConfirm = async (hold: Hold) => {
    setBusyHold({ id: hold.holdId, action: 'confirm' });
    try {
      await api.confirmHold(hold.holdId, studentId);
      setToast({ kind: 'success', text: `Admission confirmed for "${hold.className}". Congratulations!` });
    } catch (error) {
      setToast({ kind: 'error', text: errorMessage(error) });
    } finally {
      setBusyHold(null);
      await Promise.all([classes.refresh(), myHolds.refresh()]);
    }
  };

  const handleCancel = async (hold: Hold) => {
    setBusyHold({ id: hold.holdId, action: 'cancel' });
    try {
      await api.cancelHold(hold.holdId, studentId);
      setToast({ kind: 'info', text: `Application for "${hold.className}" cancelled. The seat is free again.` });
    } catch (error) {
      setToast({ kind: 'error', text: errorMessage(error) });
    } finally {
      setBusyHold(null);
      await Promise.all([classes.refresh(), myHolds.refresh()]);
    }
  };

  const activeCount = myHolds.data.filter((hold) => hold.status === 'HELD').length;

  return (
    <div className="app">
      <header className="hero">
        <div className="hero__inner">
          <div className="hero__brand">
            <span className="hero__logo" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
                <path d="M22 9.5 12 4 2 9.5l10 5.5 10-5.5Z" />
                <path d="M6 12.2V17c0 1.7 2.7 3 6 3s6-1.3 6-3v-4.8" />
              </svg>
            </span>
            <div>
              <h1>School Admission</h1>
              <p>Online enrolment · seats are given first come, first served</p>
            </div>
          </div>

          <StudentIdBar studentId={studentId} onSave={saveStudentId} />
        </div>
      </header>

      <main className="main">
        {toast && (
          <Notice kind={toast.kind} onDismiss={() => setToast(null)}>
            {toast.text}
          </Notice>
        )}

        <nav className="tabs" aria-label="Sections">
          <button
            type="button"
            className={`tabs__tab ${tab === 'classes' ? 'is-active' : ''}`}
            onClick={() => setTab('classes')}
            aria-current={tab === 'classes'}
          >
            Classes
            <span className="tabs__count">{classes.data.length}</span>
          </button>
          <button
            type="button"
            className={`tabs__tab ${tab === 'mine' ? 'is-active' : ''}`}
            onClick={() => setTab('mine')}
            aria-current={tab === 'mine'}
          >
            My applications
            {activeCount > 0 && <span className="tabs__count tabs__count--live">{activeCount}</span>}
          </button>

          <button
            type="button"
            className="tabs__refresh"
            onClick={() => {
              void classes.refresh();
              void myHolds.refresh();
            }}
          >
            ⟳ Refresh
          </button>
        </nav>

        {tab === 'classes' && (
          <section className="section">
            <CreateClassForm
              onCreated={(message) => {
                setToast({ kind: 'success', text: message });
                void classes.refresh();
              }}
              onError={(message) => setToast({ kind: 'error', text: message })}
            />

            <h2 className="section__title">Open classes</h2>

            {classes.loading && classes.data.length === 0 ? (
              <Spinner label="Loading classes…" />
            ) : (
              <ClassList
                classes={classes.data}
                holds={holdsByClass}
                loading={classes.loading}
                error={classes.error}
                hasStudentId={studentId.length > 0}
                applyingClassId={applyTarget}
                onApply={handleApply}
                onRefresh={() => void classes.refresh()}
              />
            )}
          </section>
        )}

        {tab === 'mine' && (
          <section className="section">
            <h2 className="section__title">My applications</h2>

            {!studentId ? (
              <EmptyState
                icon="🔑"
                title="Enter your birth registration number"
                hint="Add your 17-digit number at the top of the page to see your applications."
              />
            ) : myHolds.loading && myHolds.data.length === 0 ? (
              <Spinner label="Loading your applications…" />
            ) : myHolds.error && myHolds.data.length === 0 ? (
              <EmptyState icon="⚠️" title="Your applications could not be loaded" hint={myHolds.error} />
            ) : myHolds.data.length === 0 ? (
              <EmptyState
                icon="📝"
                title="No applications yet"
                hint='Open the "Classes" tab and press "Apply for a seat".'
              />
            ) : (
              <div className="stack">
                {myHolds.data.map((hold) => (
                  <HoldCard
                    key={hold.holdId}
                    hold={hold}
                    offsetMs={clock.offsetMs}
                    holdDurationSeconds={clock.holdDurationSeconds}
                    busyAction={busyHold?.id === hold.holdId ? busyHold.action : null}
                    onConfirm={handleConfirm}
                    onCancel={handleCancel}
                  />
                ))}
                {myHolds.error && (
                  <p className="grid__status">
                    <span className="muted">{myHolds.error}</span>
                  </p>
                )}
              </div>
            )}
          </section>
        )}
      </main>

      <footer className="footer">
        <span>Seat counts refresh automatically every few seconds — no reload needed.</span>
        <span className="footer__dot" aria-hidden="true">·</span>
        <span>Hold window: {formatHoldWindow(clock.holdDurationSeconds)}</span>
      </footer>
    </div>
  );
}
