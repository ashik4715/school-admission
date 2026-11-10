import { useEffect, useState } from 'react';
import { api } from '../api';

export const DEFAULT_HOLD_DURATION_SECONDS = 600;

interface ServerClock {
  /** Add this to Date.now() to get the server's idea of "now" (ms). */
  offsetMs: number;
  holdDurationSeconds: number;
}

/**
 * The countdown must match the backend's deadline, so the clock offset between this browser
 * and the server is measured once from /health. If the endpoint is unreachable the UI falls
 * back to local time and the default 10 minute duration.
 */
export function useServerClock(): ServerClock {
  const [clock, setClock] = useState<ServerClock>({
    offsetMs: 0,
    holdDurationSeconds: DEFAULT_HOLD_DURATION_SECONDS
  });

  useEffect(() => {
    let alive = true;
    const requestedAt = Date.now();

    api
      .health()
      .then((info) => {
        if (!alive) return;
        const serverTime = new Date(info.serverTimeUtc).getTime();
        if (Number.isNaN(serverTime)) return;
        setClock({
          offsetMs: serverTime - requestedAt,
          holdDurationSeconds: info.holdDurationSeconds > 0 ? info.holdDurationSeconds : DEFAULT_HOLD_DURATION_SECONDS
        });
      })
      .catch(() => {
        /* keep defaults; the countdown still works from local time */
      });

    return () => {
      alive = false;
    };
  }, []);

  return clock;
}
