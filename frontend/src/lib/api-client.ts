export const API_BASE = '/api/v1'

/** Error returned by the API as RFC 9457 problem details. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

/** Human-readable message for any thrown value, suitable for a toast. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const firstFieldError = Object.values(error.fieldErrors)[0]?.[0]
    return firstFieldError ?? error.message
  }
  if (error instanceof TypeError) {
    return 'No se pudo conectar con el servidor. Revisa tu conexión.'
  }
  return 'Ha ocurrido un error inesperado.'
}

let onUnauthorized: (() => void) | null = null

/** Called when an authenticated request gets 401 (the session expired or was revoked elsewhere). */
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

type QueryValue = string | number | boolean | null | undefined | readonly (string | number)[]

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  query?: Record<string, QueryValue>
  signal?: AbortSignal
  /** Auth endpoints: a 401 there means bad credentials, not an expired session. */
  anonymous?: boolean
}

export function buildQuery(query: Record<string, QueryValue> | undefined): string {
  if (!query) return ''
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) value.forEach((v) => params.append(key, String(v)))
    else params.append(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

/**
 * The session is an HttpOnly cookie the page cannot read. CSRF protection uses the double-submit pattern: the API
 * sets a readable XSRF-TOKEN cookie that we echo in the X-XSRF-TOKEN header on every unsafe request.
 */
export function readCsrfToken(): string | null {
  const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/)
  return match ? decodeURIComponent(match[1]) : null
}

let csrfInFlight: Promise<void> | null = null

export function ensureCsrfToken(force = false): Promise<void> {
  if (!force && readCsrfToken()) return Promise.resolve()
  csrfInFlight ??= fetch(new URL(`${API_BASE}/auth/csrf`, window.location.origin), {
    credentials: 'same-origin',
  })
    .then(() => undefined)
    .finally(() => {
      csrfInFlight = null
    })
  return csrfInFlight
}

async function parseError(response: Response): Promise<ApiError> {
  let body: { detail?: string; title?: string; code?: string; errors?: Record<string, string[]> } = {}
  try {
    body = await response.json()
  } catch {
    // Non-JSON error bodies (e.g. a proxy error page) fall back to a generic message.
  }
  const fallback =
    response.status === 429
      ? 'Demasiadas solicitudes. Espera un momento e inténtalo de nuevo.'
      : response.status >= 500
        ? 'El servidor no está disponible en este momento.'
        : 'La solicitud no se pudo completar.'
  return new ApiError(response.status, body.detail ?? body.title ?? fallback, body.code, body.errors ?? {})
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const method = options.method ?? 'GET'
  const headers: Record<string, string> = { Accept: 'application/json' }
  let body: BodyInit | undefined
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(options.body)
  }
  if (method !== 'GET') {
    await ensureCsrfToken()
    const token = readCsrfToken()
    if (token) headers['X-XSRF-TOKEN'] = token
  }
  // Same-origin request: the browser attaches the SameSite=Strict session cookie.
  const url = new URL(`${API_BASE}${path}${buildQuery(options.query)}`, window.location.origin)
  return fetch(url, { method, headers, body, signal: options.signal, credentials: 'same-origin' })
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)

  // The anti-forgery token is bound to the session; if it went stale (e.g. login in another tab), renew once.
  if (response.status === 403 && options.method && options.method !== 'GET') {
    const error = await parseError(response.clone())
    if (error.code === 'csrf_invalid') {
      await ensureCsrfToken(true)
      response = await send(path, options)
    }
  }

  if (response.status === 401 && !options.anonymous) {
    onUnauthorized?.()
  }

  if (!response.ok) {
    throw await parseError(response)
  }
  if (response.status === 204 || response.status === 202) {
    return undefined as T
  }
  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, query?: RequestOptions['query'], signal?: AbortSignal) =>
    apiRequest<T>(path, { query, signal }),
  post: <T>(path: string, body?: unknown, anonymous = false) =>
    apiRequest<T>(path, { method: 'POST', body, anonymous }),
  put: <T>(path: string, body?: unknown) => apiRequest<T>(path, { method: 'PUT', body }),
  delete: <T = void>(path: string, body?: unknown) => apiRequest<T>(path, { method: 'DELETE', body }),
}
