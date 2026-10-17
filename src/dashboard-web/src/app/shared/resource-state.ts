import { HttpResourceRef } from '@angular/common/http';

/** Maps a resource onto the tile's loading/error/ready states, keeping stale data while reloading. */
export function tileState(r: HttpResourceRef<unknown>): 'ready' | 'loading' | 'error' {
  if (r.hasValue()) return 'ready';
  return r.error() ? 'error' : 'loading';
}

/**
 * Reads a resource's value without throwing. A resource in the error state throws from value(),
 * which would break the whole template; tiles and the header strip should just see "no data".
 */
export function valueOf<T>(r: HttpResourceRef<T | undefined>): T | undefined {
  return r.hasValue() ? r.value() : undefined;
}
