export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly code?: string;
  readonly correlationId?: string;
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

  return new ApiProblemError({
    status: response.status,
    ...(type !== undefined ? { type } : {}),
    ...(title !== undefined ? { title } : {}),
    ...(detail !== undefined ? { detail } : {}),
    ...(instance !== undefined ? { instance } : {}),
    ...(code !== undefined ? { code } : {}),
    ...(correlationId !== undefined ? { correlationId } : {}),
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
