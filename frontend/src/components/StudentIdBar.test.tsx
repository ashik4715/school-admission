import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { StudentIdBar, isValidStudentId } from './StudentIdBar';

describe('StudentIdBar', () => {
  it('accepts only a 17-digit birth registration number', async () => {
    const onSave = vi.fn();
    render(<StudentIdBar studentId="" onSave={onSave} />);

    const input = screen.getByLabelText(/birth registration number/i);
    await userEvent.type(input, '12345');

    expect(screen.getByText(/more digit\(s\) needed/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /use this id/i })).toBeDisabled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('saves a valid 17-digit number', async () => {
    const onSave = vi.fn();
    render(<StudentIdBar studentId="" onSave={onSave} />);

    const input = screen.getByLabelText(/birth registration number/i);
    await userEvent.type(input, '20152691234567890');
    await userEvent.click(screen.getByRole('button', { name: /use this id/i }));

    expect(onSave).toHaveBeenCalledWith('20152691234567890');
  });

  it('ignores non-digit characters typed into the field', async () => {
    render(<StudentIdBar studentId="" onSave={vi.fn()} />);

    const input = screen.getByLabelText(/birth registration number/i) as HTMLInputElement;
    await userEvent.type(input, 'abc123');

    expect(input.value).toBe('123');
  });

  it('validates the id format', () => {
    expect(isValidStudentId('20152691234567890')).toBe(true);
    expect(isValidStudentId('2015269123456789')).toBe(false);
    expect(isValidStudentId('2015269123456789a')).toBe(false);
    expect(isValidStudentId('')).toBe(false);
  });
});
