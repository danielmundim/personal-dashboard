import { DestroyRef, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';

/**
 * An httpResource that re-fetches on an interval. The previous value stays visible while
 * reloading, so tiles never flash empty on a wall display. `url` may be reactive (e.g. built from a
 * component input); returning undefined skips the request.
 */
export function pollingResource<T>(url: string | (() => string | undefined), intervalMs: number) {
  const resource = httpResource<T>(typeof url === 'string' ? () => url : url);
  const timer = setInterval(() => resource.reload(), intervalMs);
  inject(DestroyRef).onDestroy(() => clearInterval(timer));
  return resource;
}
