const reloadMarkerKey = 'ptw.chunk-load-recovery-at';
const reloadLoopWindowMs = 60_000;

interface ChunkLoadRecoveryDependencies {
  readonly storage: Pick<Storage, 'getItem' | 'setItem'>;
  readonly reload: () => void;
  readonly now: () => number;
}

const defaultDependencies = (): ChunkLoadRecoveryDependencies => ({
  storage: globalThis.sessionStorage,
  reload: () => globalThis.location.reload(),
  now: () => Date.now(),
});

export function isStaleChunkError(value: unknown): boolean {
  const message = errorMessage(value).toLowerCase();
  return [
    'failed to fetch dynamically imported module',
    'error loading dynamically imported module',
    'importing a module script failed',
    'chunkloaderror',
    'loading chunk',
  ].some((fragment) => message.includes(fragment));
}

export function recoverFromStaleChunk(
  error: unknown,
  dependencies: ChunkLoadRecoveryDependencies = defaultDependencies(),
): boolean {
  if (!isStaleChunkError(error)) return false;

  const now = dependencies.now();
  const lastAttempt = Number(dependencies.storage.getItem(reloadMarkerKey));
  if (Number.isFinite(lastAttempt) && now - lastAttempt < reloadLoopWindowMs) return false;

  dependencies.storage.setItem(reloadMarkerKey, String(now));
  dependencies.reload();
  return true;
}

function errorMessage(value: unknown): string {
  if (value instanceof Error) return value.message;
  if (typeof value === 'string') return value;
  if (typeof value === 'object' && value !== null && 'error' in value) {
    return errorMessage((value as { error: unknown }).error);
  }
  return '';
}
