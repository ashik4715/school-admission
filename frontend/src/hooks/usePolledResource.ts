import { useCallback, useEffect, useRef, useState } from 'react';
import { errorMessage } from '../api';

interface PolledResource<T> {
  data: T;
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

/**
 * Fetches a resource immediately and then on an interval, without any page reload.
 * Overlapping requests are cancelled so a slow response can never overwrite a newer one.
 */
export function usePolledResource<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  initial: T,
  intervalMs: number,
  enabled = true
): PolledResource<T> {
  const [data, setData] = useState<T>(initial);
  const [loading, setLoading] = useState(enabled);
  const [error, setError] = useState<string | null>(null);

  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const controllerRef = useRef<AbortController | null>(null);
  const aliveRef = useRef(true);

  const load = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    try {
      const result = await fetcherRef.current(controller.signal);
      if (!aliveRef.current || controller.signal.aborted) return;
      setData(result);
      setError(null);
    } catch (caught) {
      if (!aliveRef.current || controller.signal.aborted) return;
      if ((caught as Error)?.name === 'AbortError') return;
      setError(errorMessage(caught));
    } finally {
      if (aliveRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!enabled) {
      setLoading(false);
      return;
    }

    aliveRef.current = true;
    setLoading(true);
    void load();
    const timer = window.setInterval(() => void load(), intervalMs);

    return () => {
      aliveRef.current = false;
      controllerRef.current?.abort();
      window.clearInterval(timer);
    };
  }, [enabled, intervalMs, load]);

  return { data, loading, error, refresh: load };
}
