# HabitFlow

Plataforma para construir hábitos y alcanzar objetivos: registra tu progreso día a día, mira tus rachas reales en un
calendario, mide objetivos con datos y comparte retos y metas con otras personas sin exponer tus hábitos privados.

> Decisiones técnicas, modelo de datos y reglas de negocio (frecuencias, rachas, fórmulas, permisos):
> [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Funcionalidades

| Área | Qué incluye |
|---|---|
| Cuenta | Registro, inicio y cierre de sesión (cookie HttpOnly + antiforgery), perfil (nombre, zona horaria, consentimiento de correo), cambio de contraseña (cierra las demás sesiones), recuperación por correo cuando hay SMTP, borrado de cuenta con todos sus datos, bloqueo tras 5 intentos. |
| Hábitos | Sí/no o cuantificables (con unidad y meta diaria opcional); frecuencias diaria, días concretos, N veces por semana y cada N días; categorías predeterminadas y propias; icono y color; zona horaria propia opcional; archivar, restaurar y borrar definitivamente (solo archivados). Los cambios de reglas se aplican desde hoy y el historial conserva las anteriores. |
| Registro | Un registro por hábito y día local, idempotente (`PUT`), con nota privada; corrección de días pasados; historial de cambios auditado. |
| Rachas | Respetan los días programados, las cuotas semanales (semanas ISO, prorrateo de semanas incompletas), las pausas y la zona horaria; hoy nunca cuenta como fallo hasta que termina. |
| Calendario | Vista mensual (react-day-picker) con estado de cada día y vista semanal hábito × día; panel del día para registrar o corregir. Distingue días vacíos, futuros, pendientes, cumplidos, parciales y fallidos. |
| Objetivos | Manuales (entradas con fecha), por cumplimientos de hábitos, por cantidad acumulada o por racha; fecha límite; pausar, reanudar, completar, reabrir y cancelar; historial de progreso; el porcentaje nunca supera el 100 % y el excedente se muestra aparte. |
| Panel | Saludo y fecha en tu zona, porcentaje del día, pendientes y completados, metas semanales, racha destacada y récord, objetivos activos, resumen semanal comparado, gráfico de 30 días, próximos recordatorios, actividad reciente y guía para usuarios nuevos. |
| Estadísticas | Indicadores separados (días programados, cuotas semanales, combinado, registros), comparación con el periodo anterior, series diaria/semanal/mensual, constancia por día de la semana, distribución por hábito, resumen de objetivos; cada gráfico tiene vista de tabla. |
| Recordatorios | Hora local + días + zona IANA (respeta el horario de verano), vinculables a hábito u objetivo; un worker persistente crea las notificaciones. |
| Retos | Retos de hasta 31 días con código de invitación, editables por quien los crea (reglas solo antes del inicio y sin otros participantes); cada participante usa su propio hábito; clasificación sin exponer hábitos ni notas; abandono con resultado congelado. |
| Apariencia | Tema claro, oscuro o «según el sistema» (menú de la cabecera), guardado en cada dispositivo y aplicado antes del primer pintado; colores del tema oscuro con contraste AA comprobado y gráficos validados para cada superficie. |
| Objetivos compartidos | Invitación por correo (sin revelar si existe la cuenta), roles propietario/miembro (la propiedad se puede ceder y pasa a otro miembro si la persona propietaria borra su cuenta), progreso individual y colectivo, aportaciones con notas privadas. |

### Canales de notificación: qué existe de verdad

| Canal | Estado |
|---|---|
| Notificación en la app (campana) | **Implementado.** El worker `ReminderDispatcher` crea las notificaciones; la app las consulta cada minuto (no hay *push* en tiempo real). |
| Correo electrónico | **Implementado, requiere SMTP.** Solo se envía si `Email:Mode` está configurado y el usuario lo permite en su perfil. Sin SMTP, la app lo indica. |
| Notificaciones *push* (navegador/móvil) | **No implementado.** |

## Stack

- **Backend:** .NET 10 (LTS), ASP.NET Core Web API (controllers), EF Core 10 + PostgreSQL 17 (Npgsql), ASP.NET Core
  Identity (cookie de aplicación), antiforgery, FluentValidation, Swagger (Swashbuckle), `BackgroundService`.
- **Frontend:** React 19, TypeScript estricto, Vite 8, React Router 7, Tailwind CSS 4, componentes estilo shadcn/ui
  sobre Radix, TanStack Query 5, React Hook Form + Zod, Recharts 3, react-day-picker 9, date-fns 4.
- **Calidad:** xUnit + WebApplicationFactory sobre PostgreSQL real, Vitest + Testing Library + MSW, Playwright,
  ESLint, Prettier, GitHub Actions.
- **Despliegue:** Docker (imágenes multietapa, usuario no root, *healthcheck*) y Docker Compose (PostgreSQL + API +
  nginx con CSP y cabeceras de seguridad).

## Puesta en marcha rápida (Docker)

Requisitos: Docker con Compose v2.

```bash
cp .env.example .env          # y cambia POSTGRES_PASSWORD
docker compose up -d --build --wait
```

Abre <http://localhost:8080> (o el puerto de `WEB_PORT`) y crea una cuenta. Las migraciones se aplican al arrancar.

Cuenta de demostración opcional (solo crea `demo@habitflow.local`, nunca toca otras cuentas, y se niega a ejecutarse
en `Production`):

```bash
docker compose run --rm -e ASPNETCORE_ENVIRONMENT=Demo api seed-demo   # contraseña: DEMO_PASSWORD (o una generada)
```

## Desarrollo local

Requisitos: .NET SDK 10, Node.js 24 (o 22.12+), PostgreSQL 17.

1. **Base de datos.** Crea un rol con permiso para crear bases (las pruebas de integración crean y borran bases
   temporales):
   ```sql
   CREATE ROLE habitflow LOGIN CREATEDB PASSWORD 'habitflow_dev';
   CREATE DATABASE habitflow OWNER habitflow;
   ```
   o con Docker: `docker run -d --name habitflow-db -p 5432:5432 -e POSTGRES_USER=habitflow -e POSTGRES_PASSWORD=habitflow_dev -e POSTGRES_DB=habitflow postgres:17-alpine`.
2. **API** (http://localhost:5090, Swagger en `/swagger`):
   ```bash
   cd backend
   dotnet run --project src/HabitFlow.Api                 # aplica migraciones en Development
   dotnet run --project src/HabitFlow.Api -- seed-demo    # demo@habitflow.local / Demo12345
   ```
   Nueva migración: `dotnet tool restore && dotnet ef migrations add <Nombre> -p src/HabitFlow.Infrastructure -s src/HabitFlow.Api -o Persistence/Migrations`.
3. **Web** (http://localhost:5173, con proxy de `/api` hacia la API):
   ```bash
   cd frontend
   npm install
   npm run dev
   ```

En desarrollo el correo se escribe como ficheros `.eml` en `backend/mail-pickup/` (útil para probar la recuperación de
contraseña sin SMTP).

## Configuración (variables de entorno de la API)

| Variable | Por defecto | Descripción |
|---|---|---|
| `ConnectionStrings__Default` | — (obligatoria) | Cadena de conexión de PostgreSQL. |
| `Database__MigrateOnStartup` | `false` (`true` en Development y Compose) | Aplica migraciones pendientes al arrancar. Ver «Migraciones» en la arquitectura. |
| `AuthCookie__Secure` | `true` | Cookies `Secure`. Los navegadores las aceptan en `http://localhost`. |
| `DataProtection__KeysPath` | — (`/app/keys` en Docker) | Carpeta persistente de claves (las sesiones sobreviven a reinicios). |
| `ForwardedHeaders__Enabled` | `false` | Actívalo solo detrás de un proxy de confianza (Compose lo hace). |
| `Cors__AllowedOrigins__0` | — | Solo si el frontend se sirve desde otro origen (no hace falta con el proxy). |
| `Swagger__Enabled` | `false` | Expone `/swagger` fuera de Development. |
| `RateLimiting__AuthPerMinute`, `__PasswordResetPerHour`, `__ApiPerMinute` | 10, 5, 300 | Límites por IP (autenticación) y por usuario (API). |
| `Email__Mode` | `Disabled` | `Smtp` (real), `Pickup` (ficheros `.eml`) o `Disabled`. |
| `Email__SmtpHost`, `__SmtpPort`, `__SmtpUsername`, `__SmtpPassword`, `__SmtpEnableSsl`, `__From`, `__PublicBaseUrl` | — | SMTP y URL pública para los enlaces de los correos. |
| `Reminders__Enabled`, `__PollSeconds`, `__MaxDelayMinutes` | `true`, 30, 120 | Worker de recordatorios. Disparos con más retraso que `MaxDelayMinutes` se omiten. |

Los secretos van en variables de entorno o *user secrets* (`dotnet user-secrets set ...` en `src/HabitFlow.Api`),
nunca en archivos versionados. `.env` está en `.gitignore`.

## Pruebas

```bash
# Backend (PostgreSQL local con el rol habitflow; otro servidor: HABITFLOW_TEST_DB="Host=...;Username=...;Password=...;Database=postgres")
cd backend
dotnet test                        # en Windows con Smart App Control: bash scripts/test.sh (reintenta DLL bloqueadas)

# Frontend
cd frontend
npm run lint && npm run typecheck && npm test

# End-to-end (contra el build de producción o Docker Compose; la API con un límite de autenticación alto)
RateLimiting__AuthPerMinute=1000 dotnet run --project backend/src/HabitFlow.Api   # en otra terminal
npm run build && npm run preview                                                  # http://localhost:4173
npm run e2e                                    # E2E_CHANNEL=msedge para usar Edge instalado
E2E_BASE_URL=http://localhost:8080 npm run e2e # contra Docker Compose (AUTH_RATE_LIMIT_PER_MINUTE=1000 en .env)
```

Notas:
- Las pruebas E2E registran varios usuarios por minuto desde la misma IP; con el límite de producción (10/min)
  recibirían 429. Por eso se ejecutan con un límite alto.
- Playwright usa un único *worker*: con dos navegadores en paralelo y la API recién arrancada, en un equipo con poca
  memoria libre las peticiones llegaron a tardar más de un minuto. Con un *worker* son estables (verificado).

Resultados verificados al cerrar esta versión (12-oct-2026):

| Suite | Resultado |
|---|---|
| Backend (xUnit: unitarias + integración sobre PostgreSQL real) | 124/124 |
| Frontend (Vitest) | 37/37, más ESLint, Prettier y `tsc` sin errores |
| E2E (Playwright, Edge) | 4/4 contra `vite preview` y 4/4 contra Docker Compose |

El *workflow* de CI (`.github/workflows/ci.yml`) reproduce estos pasos, pero **aún no se ha ejecutado en GitHub**.

## Estructura

```
backend/
  src/HabitFlow.Domain          entidades, reglas, motor de rachas (HabitTimeline), zonas horarias
  src/HabitFlow.Application     casos de uso, DTO, validadores, cálculo de estadísticas y objetivos
  src/HabitFlow.Infrastructure  EF Core, migraciones, Identity, correo, worker de recordatorios, datos demo
  src/HabitFlow.Api             controllers, cookies + antiforgery, errores, rate limiting, Swagger, health
  tests/HabitFlow.Tests         Unit/ e Integration/
frontend/
  src/app                       layout, router, providers
  src/components                UI reutilizable (botones, diálogos, tarjetas, visuales de hábitos)
  src/features/<área>           auth, dashboard, habits, habit-logs, calendar, goals, statistics, reminders,
                                challenges, shared-goals, profile (api.ts + páginas + pruebas)
  src/lib                       cliente HTTP, fechas locales, etiquetas, zonas horarias
  e2e                           Playwright
docs/ARCHITECTURE.md            ADR, modelo de datos, reglas, operación
```

## Limitaciones y trabajo pendiente

- **Push**: no hay notificaciones *push*; las de la app se consultan cada minuto.
- **CI**: el *workflow* está escrito pero no se ha ejecutado en GitHub.
- **SMTP real**: el envío por SMTP no se ha probado contra un servidor real (sí el modo `Pickup` en pruebas automáticas).
- **Objetivos compartidos**: las aportaciones son manuales; no se vinculan hábitos personales.
- **Retos**: las reglas se bloquean en cuanto empieza el reto o se une otra persona (solo se pueden cambiar nombre y
  descripción); el progreso del día en que alguien abandona no cuenta.
- **Idioma**: interfaz solo en español.
