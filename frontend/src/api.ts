import type { CreateClassInput, HealthInfo, Hold, SchoolClass } from './types';

/**
 * Base URL of the backend. Set VITE_API_BASE_URL at build/run time to point somewhere else
 * (e.g. http://localhost:8080). Left empty it uses the same origin, and Vite/nginx proxy the
 * API paths to the backend.
 */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export class ApiError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
  }
}

const FALLBACK_MESSAGES: Record<number, string> = {
  400: 'Some of the information sent was not valid. Please check it and try again.',
  404: 'That item could not be found. It may have been removed.',
  409: 'That action is not possible right now.',
  500: 'The server ran into a problem. Please try again in a moment.'
};

interface RequestOptions {
  method?: 'GET' | 'POST' | 'DELETE';
  body?: unknown;
  studentId?: string | null;
  signal?: AbortSignal;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, studentId, signal } = options;

  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (studentId) headers['X-Student-Id'] = studentId;

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal
    });
  } catch (error) {
    if ((error as Error)?.name === 'AbortError') throw error;
    throw new ApiError(
      'NETWORK_ERROR',
      `Cannot reach the admission server${API_BASE_URL ? ` at ${API_BASE_URL}` : ''}. Please check that the backend is running and try again.`,
      0
    );
  }

  if (response.status === 204) return undefined as T;

  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = null;
    }
  }

  if (!response.ok) {
    const data = payload as { error?: string; message?: string } | null;
    throw new ApiError(
      data?.error ?? `HTTP_${response.status}`,
      data?.message ?? FALLBACK_MESSAGES[response.status] ?? `Request failed (${response.status}).`,
      response.status
    );
  }

  return payload as T;
}

export const api = {
  health: (signal?: AbortSignal) => request<HealthInfo>('/health', { signal }),

  listClasses: (signal?: AbortSignal) => request<SchoolClass[]>('/classes', { signal }),

  getClass: (classId: string, signal?: AbortSignal) =>
    request<SchoolClass>(`/classes/${encodeURIComponent(classId)}`, { signal }),

  createClass: (input: CreateClassInput) =>
    request<SchoolClass>('/classes', { method: 'POST', body: input }),

  createHold: (classId: string, studentId: string) =>
    request<Hold>(`/classes/${encodeURIComponent(classId)}/holds`, { method: 'POST', studentId }),

  getHold: (holdId: string, signal?: AbortSignal) =>
    request<Hold>(`/holds/${encodeURIComponent(holdId)}`, { signal }),

  confirmHold: (holdId: string, studentId: string) =>
    request<{ holdId: string; status: string }>(`/holds/${encodeURIComponent(holdId)}/confirm`, {
      method: 'POST',
      studentId
    }),

  cancelHold: (holdId: string, studentId: string) =>
    request<void>(`/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE', studentId }),

  myHolds: (studentId: string, signal?: AbortSignal) =>
    request<Hold[]>('/me/holds', { studentId, signal })
};

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/** Message that is always safe to show to the user. */
export function errorMessage(error: unknown): string {
  if (isApiError(error)) return error.message;
  if (error instanceof Error && error.message) return error.message;
  return 'Something went wrong. Please try again.';
}
