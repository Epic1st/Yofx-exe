/**
 * One field-level rejection from a `422` response. `path` names the offending
 * request member, so a form can show the message beside the field that caused it.
 */
export interface ApiValidationError {
  readonly path: string;
  readonly code: string;
  readonly message: string;
}

export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly code?: string;
  readonly correlationId?: string;
  /** Present only when the service returned a per-field rejection list. */
  readonly errors?: readonly ApiValidationError[];
}

export class ApiProblemError extends Error {
  constructor(readonly problem: ProblemDetails) {
    super(problem.title ?? 'The service could not complete the request.');
    this.name = 'ApiProblemError';
  }

  get status(): number {
    return this.problem.status;
  }
}

function optionalString(source: Record<string, unknown>, field: string): string | undefined {
  const value = source[field];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function boundedText(value: unknown, maximumLength: number): string | null {
  return typeof value === 'string' && value.length > 0 && value.length <= maximumLength
    ? value
    : null;
}

/**
 * Reads the `errors` extension of a problem document. Both shapes the platform can
 * emit are accepted: a list of `{ path, code, message }` records, and the dictionary
 * form `{ field: ["message", …] }`. Malformed entries are dropped rather than shown.
 */
function optionalValidationErrors(
  source: Record<string, unknown>,
): readonly ApiValidationError[] | undefined {
  const raw = source.errors;
  const errors: ApiValidationError[] = [];

  if (Array.isArray(raw)) {
    for (const entry of raw.slice(0, 200)) {
      if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) {
        continue;
      }
      const record = entry as Record<string, unknown>;
      const path = boundedText(record.path, 400);
      const message = boundedText(record.message, 2_000);
      if (path === null || message === null) {
        continue;
      }
      errors.push({ path, code: boundedText(record.code, 200) ?? 'INVALID', message });
    }
  } else if (typeof raw === 'object' && raw !== null) {
    for (const [path, messages] of Object.entries(raw as Record<string, unknown>).slice(0, 200)) {
      if (path.length === 0 || path.length > 400 || !Array.isArray(messages)) {
        continue;
      }
      for (const entry of messages.slice(0, 20)) {
        const message = boundedText(entry, 2_000);
        if (message !== null) {
          errors.push({ path, code: 'INVALID', message });
        }
      }
    }
  }

  return errors.length === 0 ? undefined : errors;
}

export async function toApiProblem(response: Response): Promise<ApiProblemError> {
  let payload: Record<string, unknown> = {};
  const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';

  if (contentType.includes('application/problem+json') || contentType.includes('application/json')) {
    try {
      const parsed: unknown = await response.json();
      if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
        payload = parsed as Record<string, unknown>;
      }
    } catch {
      payload = {};
    }
  }

  const type = optionalString(payload, 'type');
  const title = optionalString(payload, 'title');
  const detail = optionalString(payload, 'detail');
  const instance = optionalString(payload, 'instance');
  const code = optionalString(payload, 'code');
  const correlationId = optionalString(payload, 'correlationId');
  const errors = optionalValidationErrors(payload);

  return new ApiProblemError({
    status: response.status,
    ...(type !== undefined ? { type } : {}),
    ...(title !== undefined ? { title } : {}),
    ...(detail !== undefined ? { detail } : {}),
    ...(instance !== undefined ? { instance } : {}),
    ...(code !== undefined ? { code } : {}),
    ...(correlationId !== undefined ? { correlationId } : {}),
    ...(errors !== undefined ? { errors } : {}),
  });
}

export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiProblemError && (error.status === 401 || error.status === 403);
}

export function userFacingProblem(error: unknown): string {
  if (error instanceof ApiProblemError) {
    const correlation = error.problem.correlationId
      ? ` Reference ${error.problem.correlationId}.`
      : '';
    return `${error.problem.title ?? error.message}${correlation}`;
  }
  if (error instanceof Error && error.name === 'ContractViolationError') {
    return error.message;
  }
  if (error instanceof TypeError) {
    return 'The ControlPlane service could not be reached.';
  }
  return 'The dashboard could not be loaded.';
}
