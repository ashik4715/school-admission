import { useState } from 'react';
import { api, errorMessage } from '../api';

interface CreateClassFormProps {
  onCreated: (message: string) => void;
  onError: (message: string) => void;
}

/**
 * School admin screen: create a class. Every class *and* shift is its own class with its own
 * seat pool (e.g. "Class 6 - Morning Shift" and "Class 6 - Day Shift").
 */
export function CreateClassForm({ onCreated, onError }: CreateClassFormProps) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [seats, setSeats] = useState('120');
  const [busy, setBusy] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const totalSeats = Number(seats);

    if (!name.trim()) {
      setFieldError('Please give the class a name, e.g. "Class 6 - Morning Shift".');
      return;
    }
    if (!Number.isInteger(totalSeats) || totalSeats < 1) {
      setFieldError('Seat count must be a whole number of at least 1.');
      return;
    }

    setFieldError(null);
    setBusy(true);
    try {
      const created = await api.createClass({ name: name.trim(), totalSeats });
      onCreated(`Class "${created.name}" created with ${created.totalSeats} seats.`);
      setName('');
      setSeats('120');
      setOpen(false);
    } catch (error) {
      onError(errorMessage(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="admin card">
      <button
        type="button"
        className="admin__toggle"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
      >
        <span className="admin__icon" aria-hidden="true">＋</span>
        <span>
          <strong>School admin</strong>
          <span className="admin__sub">Create a new class or shift</span>
        </span>
        <span className="admin__chevron" aria-hidden="true">{open ? '▲' : '▼'}</span>
      </button>

      {open && (
        <form className="admin__form" onSubmit={submit}>
          <div className="field">
            <label htmlFor="class-name">Class name</label>
            <input
              id="class-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Class 6 - Morning Shift"
              maxLength={200}
            />
          </div>

          <div className="field field--seats">
            <label htmlFor="class-seats">Total seats</label>
            <input
              id="class-seats"
              type="number"
              min={1}
              max={100000}
              value={seats}
              onChange={(event) => setSeats(event.target.value)}
            />
          </div>

          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Creating…' : 'Create class'}
          </button>

          {fieldError && <p className="field__error" role="alert">{fieldError}</p>}
        </form>
      )}
    </section>
  );
}
