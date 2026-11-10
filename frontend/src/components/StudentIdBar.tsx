import { useEffect, useState } from 'react';

export const STUDENT_ID_LENGTH = 17;

export function isValidStudentId(value: string): boolean {
  return value.length === STUDENT_ID_LENGTH && /^[0-9]+$/.test(value);
}

interface StudentIdBarProps {
  studentId: string;
  onSave: (studentId: string) => void;
}

/**
 * The only "login" in this system: the student's 17-digit birth registration number.
 * It is stored in localStorage so the identity survives a refresh or a browser restart.
 */
export function StudentIdBar({ studentId, onSave }: StudentIdBarProps) {
  const [draft, setDraft] = useState(studentId);
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    setDraft(studentId);
  }, [studentId]);

  const valid = isValidStudentId(draft);
  const showError = touched && draft.length > 0 && !valid;
  const saved = studentId.length > 0 && draft === studentId;

  const submit = () => {
    setTouched(true);
    if (valid) onSave(draft);
  };

  return (
    <form
      className="idbar"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <label className="idbar__label" htmlFor="student-id">
        Student birth registration number
      </label>

      <div className="idbar__row">
        <input
          id="student-id"
          className={`idbar__input ${showError ? 'is-invalid' : ''}`}
          inputMode="numeric"
          autoComplete="off"
          placeholder="17 digits, e.g. 20152691234567890"
          maxLength={STUDENT_ID_LENGTH}
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value.replace(/[^0-9]/g, '').slice(0, STUDENT_ID_LENGTH));
            setTouched(true);
          }}
          aria-invalid={showError}
          aria-describedby="student-id-help"
        />

        <button type="submit" className="btn btn--primary btn--sm" disabled={!valid || saved}>
          {saved ? 'In use' : 'Use this ID'}
        </button>
      </div>

      <p id="student-id-help" className={`idbar__help ${showError ? 'is-error' : ''}`}>
        {showError
          ? `${STUDENT_ID_LENGTH - draft.length} more digit(s) needed — the ID must be exactly ${STUDENT_ID_LENGTH} digits.`
          : saved
            ? `Applying as ${studentId}. This number is kept in this browser.`
            : `No password needed. Enter the ${STUDENT_ID_LENGTH}-digit number to apply or see your applications.`}
      </p>
    </form>
  );
}
