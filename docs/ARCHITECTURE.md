# HabitFlow — Arquitectura y reglas de negocio

Documento vivo. Cada decisión (ADR) indica el problema, la decisión y la alternativa descartada.

## 1. Vista general

```
frontend (React + Vite)  ──/api (mismo origen: proxy de Vite o nginx)──▶  HabitFlow.Api (ASP.NET Core)
                                                                              │
                                                     HabitFlow.Application (casos de uso, DTO, validación)
                                                                              │
                                                     HabitFlow.Domain (entidades, motor de rachas, reglas)
                                                                              │
                                                     HabitFlow.Infrastructure (EF Core + PostgreSQL, Identity,
                                                                               correo, worker de recordatorios)
```

| Proyecto | Responsabilidad | Depende de |
|---|---|---|
| `HabitFlow.Domain` | Entidades e invariantes, frecuencias, motor de rachas/periodos (puro, sin E/S). | — |
| `HabitFlow.Application` | Servicios de caso de uso, DTO, validadores FluentValidation, consultas EF. | Domain, EF Core (abstracciones) |
| `HabitFlow.Infrastructure` | `AppDbContext`, configuraciones, migraciones, Identity, SMTP, `BackgroundService` de recordatorios. | Application |
| `HabitFlow.Api` | Controllers, autenticación por cookie, antiforgery, middleware de errores, rate limiting, Swagger, health. | Application, Infrastructure |
| `HabitFlow.Tests` | xUnit: unitarias (dominio, tiempo) e integración (API real + PostgreSQL real). | todos |

### ADR-01 — Sin repositorios genéricos
`DbSet<T>` ya es repositorio y `DbContext` ya es unidad de trabajo. La capa de aplicación usa `IAppDbContext`
(interfaz con los `DbSet`) y consultas LINQ proyectadas a DTO. Envolverlo en `IRepository<T>` añadiría código sin
aportar aislamiento real (las consultas dependen igualmente de EF).

### ADR-02 — Aislamiento por usuario con filtros globales (deny by default)
Cada entidad privada lleva `UserId` y un filtro global `UserId == usuarioActual`. Si no hay usuario autenticado el
filtro compara con `Guid.Empty` y no devuelve nada. Un recurso ajeno responde **404** (no 403) para no revelar su
existencia. El `UserId` jamás se lee del cuerpo de la petición: sale de la cookie de sesión validada.
Excepciones explícitas y documentadas: retos y objetivos compartidos leen datos de otros participantes con
`IgnoreQueryFilters()` **solo** tras comprobar la pertenencia, y proyectan únicamente cifras agregadas.

### ADR-03 — Autenticación por cookie de sesión de Identity + antiforgery
- Cookie `habitflow.auth`: `HttpOnly`, `Secure`, `SameSite=Strict`, expiración deslizante de 14 días.
- El navegador nunca ve un token: no hay nada que robar desde `localStorage` vía XSS.
- CSRF: además de `SameSite=Strict`, toda petición no segura (`POST/PUT/PATCH/DELETE`) a `/api` exige la cabecera
  `X-XSRF-TOKEN` validada por `IAntiforgery` (patrón *double submit*: cookie legible `XSRF-TOKEN` + cookie interna
  HttpOnly). El token se renueva tras login/logout porque va ligado a la identidad.
- La API responde 401/403 (sin redirecciones HTML).
- Cambiar la contraseña actualiza el *security stamp*: el resto de sesiones se invalidan en la siguiente validación
  (cada 5 minutos como máximo).
- Bloqueo tras 5 intentos fallidos (15 min) y rate limiting por IP en los endpoints de autenticación.
- Alternativa descartada: JWT en `localStorage` (expuesto a XSS) o JWT+refresh en cookies (más piezas sin ventaja
  para un cliente de mismo origen).

### ADR-04 — Fechas: instante global vs. fecha local
| Dato | Tipo .NET | Tipo PostgreSQL | Significado |
|---|---|---|---|
| `CreatedAt`, `UpdatedAt`, `NextFireAt`… | `DateTimeOffset` (UTC) | `timestamptz` | Instante global. |
| `HabitLog.LocalDate`, `StartDate`, `EffectiveFrom` | `DateOnly` | `date` | Día de calendario local, sin zona. |
| `Reminder.LocalTime` | `TimeOnly` | `time` | Hora de reloj de pared en la zona del recordatorio. |
| Zonas | `string` IANA (`Europe/Madrid`) | `varchar(64)` | Validada con `TimeZoneInfo` (ICU en Windows, tzdata en Linux). |

- **Zona efectiva de un hábito** = `Habit.TimeZoneId ?? User.TimeZoneId`. «Hoy» para un hábito es la fecha local
  actual en esa zona.
- Un registro pertenece a una fecha local, no a un instante: si el usuario viaja, su historial no se mueve.
- Se usan los tipos de la BCL (`DateOnly`, `TimeOnly`, `TimeZoneInfo`) en lugar de NodaTime: Npgsql los mapea de forma
  nativa y los casos de horario de verano que necesitamos (horas inexistentes/ambiguas) se resuelven en un único
  helper probado (`ZonedTime`).

### ADR-05 — Valores derivados calculados al leer
Rachas, cumplimiento, progreso de objetivos y estadísticas **no se almacenan**. Se calculan a partir de registros y
versiones de frecuencia, que son la única fuente de verdad. El motor es determinista y puro (tests sin BD).
Coste: un hábito diario durante 10 años son ~3 650 filas `(fecha, cantidad, completado)` → trivial. Si algún día
hiciera falta caché, se añadiría como proyección reconstruible, nunca como fuente de verdad.
La única excepción es `HabitLog.IsCompleted`, que se guarda para poder agregar en SQL; se recalcula en el servidor
en cada escritura del registro y cuando cambia la versión de frecuencia del mismo día (ver §3.4).

## 2. Modelo de datos

```
AspNetUsers (AppUser) 1─* Habit 1─* HabitSchedule
                      │        1─* HabitPause
                      │        1─* HabitLog
                      │        *─1 HabitCategory (predeterminadas: UserId NULL; propias: UserId)
                      ├─* HabitLogChange (auditoría de registros)
                      ├─* Goal 1─* GoalProgressEntry
                      │        *─* Habit (GoalHabit)
                      ├─* Reminder ─? Habit | Goal
                      ├─* Notification ─? Reminder
                      ├─* Challenge (creador) 1─* ChallengeParticipant *─1 AppUser / Habit
                      └─* SharedGoal (propietario) 1─* SharedGoalParticipant / SharedGoalInvitation /
                                                       SharedGoalContribution
```

Claves: UUID v7 generados en la aplicación (ordenables por tiempo, sin colisiones entre instancias). Concurrencia
optimista con `xmin` en `Habit` y `Goal` (cambios de configuración concurrentes → 409).

| Entidad | Campos clave | Restricciones / índices | Borrado |
|---|---|---|---|
| `AppUser` | `DisplayName`, `TimeZoneId`, `EmailRemindersEnabled` | email único (Identity) | Borrar la cuenta borra en cascada todos sus datos. |
| `HabitCategory` | `Name`, `Color`, `Icon`, `UserId?` | único `(UserId, NormalizedName)` | Restrict si hay hábitos. Las predeterminadas no se borran. |
| `Habit` | `Name`, `Description?`, `CategoryId`, `Color`, `Icon`, `Type` (Binary/Quantity), `Unit?`, `StartDate`, `TimeZoneId?`, `ArchivedAt?` | índice `(UserId, ArchivedAt)` | Solo un hábito archivado puede borrarse definitivamente (con confirmación). |
| `HabitSchedule` | `EffectiveFrom`, `Kind`, `WeekdaysMask?`, `TimesPerWeek?`, `IntervalDays?`, `TargetValue?` | único `(HabitId, EffectiveFrom)`; `CHECK` por tipo | Cascada con el hábito. |
| `HabitPause` | `StartDate`, `EndDate?` (inclusiva; null mientras está archivado) | `CHECK EndDate ≥ StartDate` | Cascada con el hábito. |
| `HabitLog` | `LocalDate`, `Quantity?`, `IsCompleted`, `Note?` | **único `(HabitId, LocalDate)`**; índice `(UserId, LocalDate)` | Cascada con el hábito. |
| `HabitLogChange` | `HabitId`, `LocalDate`, `Action`, cantidades/estado antes y después | índice `(UserId, OccurredAt)` | Cascada con el hábito. |
| `Goal` | `Name`, `Description?`, `MetricType`, `TargetValue`, `Unit`, `StartDate`, `Deadline?`, `Status`, `CompletedAt?` | índice `(UserId, Status)` | Cascada de entradas y vínculos. |
| `GoalHabit` | `GoalId`, `HabitId` | PK compuesta | Cascada por ambos lados. |
| `GoalProgressEntry` | `LocalDate`, `Amount`, `Note?` | índice `(GoalId, LocalDate)` | Cascada con el objetivo. |
| `Reminder` | `Title`, `Description?`, `LocalTime`, `DaysMask`, `TimeZoneId`, `IsActive`, `Channel`, `HabitId?`/`GoalId?`, `NextFireAt?`, `LastFiredAt?` | `CHECK` (no ambos vínculos); índice `(IsActive, NextFireAt)` | Se borra al borrar su hábito/objetivo. |
| `Notification` | `Title`, `Body`, `Kind`, `ReminderId?`, `OccurrenceAt?`, `ReadAt?`, `EmailStatus` | **único `(ReminderId, OccurrenceAt)`** (disparo idempotente) | `ReminderId` → SET NULL. |
| `Challenge` | `CreatorId?`, `Name`, `StartDate`, `EndDate`, `Rule`, `TargetValue`, `Unit?`, `JoinCode` | código único; `CHECK` fechas y meta | Creador → SET NULL (el reto sigue para los demás). |
| `ChallengeParticipant` | `UserId`, `HabitId?`, `JoinedAt`, `LeftOn?` | **único `(ChallengeId, UserId)`** | Usuario → cascada; hábito → SET NULL. |
| `SharedGoal` | `Name`, `Unit`, `CollectiveTarget`, `IndividualTarget?`, `StartDate`, `Deadline?`, `Status` | `xmin`; `CHECK` meta y fechas | Cascada de participantes, invitaciones y aportaciones. |
| `SharedGoalParticipant` | `UserId`, `Role` (Owner/Member), `JoinedAt`, `LeftAt?` | único `(SharedGoalId, UserId)` | Usuario → cascada. |
| `SharedGoalInvitation` | `NormalizedEmail`, `InvitedByUserId`, `Status`, `RespondedAt?` | único `(SharedGoalId, NormalizedEmail)` **solo si `Pending`** | Cascada con el objetivo. |
| `SharedGoalContribution` | `UserId`, `LocalDate`, `Amount`, `Note?` (privada) | `CHECK Amount > 0` | Usuario → cascada (sus datos se borran con su cuenta). |

Días de la semana: máscara de bits `smallint` validada (lunes = bit 0 … domingo = bit 6). Es un valor atómico
(conjunto acotado de 7 elementos), consultable con operadores de bits y verificado con `CHECK (mask BETWEEN 1 AND 127)`.
No se usan listas separadas por comas ni JSON.

Seeds: las categorías predeterminadas van en una migración (`HasData`, GUID fijos). Los datos de demostración se crean
con un comando explícito (`seed-demo`), solo para la cuenta `demo@habitflow.local`, y se niega a ejecutarse en
`Production`. No hay contraseñas reales en el repositorio.

## 3. Reglas de negocio

### 3.1 Frecuencias (`ScheduleKind`)
| Tipo | Parámetros | Unidad de periodo | ¿Programado el día d? |
|---|---|---|---|
| `Daily` | — | día | siempre |
| `Weekdays` | `WeekdaysMask` (1–127) | día | si el día de la semana de d está en la máscara |
| `EveryNDays` | `IntervalDays` (2–365) | día | si `(d − EffectiveFrom) mod N = 0` (ancla = inicio de la versión) |
| `TimesPerWeek` | `TimesPerWeek` (1–7) | semana | cualquier día cuenta; se exige una cuota semanal |

Los días cubiertos por una `HabitPause` (hábito archivado) nunca están programados, sea cual sea la frecuencia.

**Semana** = semana ISO, de **lunes a domingo**, en todo el producto (rachas, estadísticas, retos, objetivos).

### 3.2 Versionado de la frecuencia (sin reescritura retroactiva)
- Un hábito tiene una o más `HabitSchedule`. La versión aplicable al día d es la de mayor `EffectiveFrom ≤ d`.
- Editar frecuencia o meta diaria crea una versión con `EffectiveFrom = hoy` (fecha local del hábito). Si ya existe
  una versión con esa fecha, se sustituye (varias ediciones el mismo día = la última gana). Si el hábito aún no ha
  empezado (`StartDate > hoy`), se sustituye la versión inicial.
- Los días anteriores siguen evaluándose con la versión antigua → rachas y porcentajes históricos no cambian.
- **Archivar** abre una `HabitPause` desde hoy y marca `ArchivedAt`. **Restaurar** la cierra en ayer (si se archiva y
  restaura el mismo día, la pausa desaparece). Las versiones de frecuencia no se tocan, así que restaurar recupera las
  reglas exactas. Los días en pausa no están programados: no rompen ni suman racha.
  *Decisión revisada durante la implementación*: modelar el archivo como una versión «en pausa» perdía la frecuencia
  si se archivaba el mismo día del inicio; las pausas como intervalos separados no tienen ese problema y permitirían
  en el futuro pausas voluntarias (vacaciones).
- `StartDate` solo se puede cambiar mientras el hábito no tenga registros; en ese caso el historial de versiones se
  reinicia a una única versión.
- El tipo (`Binary`/`Quantity`) no se puede cambiar tras crear el hábito (cambiaría el significado del historial). La
  unidad sí (es solo una etiqueta).

### 3.3 Estado de un día para un hábito
Con `hoy` = fecha local del hábito:
- `NotScheduled`: antes de `StartDate`, pausado o día no programado (frecuencias por día).
- `Future`: d > hoy.
- `Completed`: hay registro y cumple la meta de la versión vigente ese día.
- `Partial`: hay registro con cantidad > 0 que no alcanza la meta (solo hábitos cuantificables).
- `Pending`: d = hoy, programado y sin completar. **Hoy nunca es un fallo** mientras no termine el día.
- `Missed`: d < hoy, programado y sin completar.
- Para `TimesPerWeek` los días no se marcan como fallidos individualmente: se evalúa la semana (§3.5).

Un registro en un día no programado se muestra como «extra»: cuenta para cuotas semanales y objetivos, pero no
rompe ni alarga rachas diarias.

### 3.4 Registros
- **Un registro por hábito y día local** (índice único). Escritura con `PUT /habits/{id}/logs/{fecha}`:
  idempotente (repetirla deja el mismo estado), valores **absolutos**.
- Binario: `completed: true|false`. Cuantificable: `quantity ≥ 0` (máx. 1 000 000, 2 decimales); el día se completa
  si `quantity ≥ meta` de la versión vigente (si la versión no tiene meta, si `quantity > 0`).
- **Estrategia cuantitativa**: un único total diario. Los botones «+N» de la interfaz envían el total resultante, no
  el incremento, para que reintentar no duplique cantidades.
- Fechas permitidas: `StartDate ≤ fecha ≤ hoy`. No se registra en hábitos archivados.
- Nota: `null` conserva la nota guardada (marcar/desmarcar desde una tarjeta no la borra), `""` la elimina y un texto
  la sustituye.
- Cada creación, modificación o borrado se audita en `HabitLogChange` (sin copiar la nota). Alimenta «Actividad
  reciente» y permite revisar correcciones retroactivas.
- Dos `PUT` simultáneos al mismo día: el índice único lo impide; el servidor reintenta como actualización.

### 3.5 Rachas
El motor (`HabitTimeline` en Domain) convierte versiones + registros en una secuencia cronológica de **periodos**:
- Frecuencias por día: cada día programado es un periodo (`Completed`, `Missed`, `Pending`).
- `TimesPerWeek`: cada tramo semanal cubierto por esa versión es un periodo. Meta del tramo =
  `ceil(N × díasDisponibles / 7)` (prorrateo para semanas incompletas: inicio del hábito, cambio de versión, pausa).
  Éxito en cuanto `completados ≥ meta`; `InProgress` mientras la semana actual no termine; `Missed` si termina sin
  alcanzarla.

Reglas:
1. Una racha es una serie de periodos consecutivos exitosos **de la misma unidad** (días o semanas).
2. Un periodo fallido rompe la racha. Días no programados, pausas y el periodo en curso son neutros.
3. Pasar de una frecuencia diaria a una semanal (o viceversa) empieza una nueva serie: no se suman días y semanas.
4. **Racha actual** = la serie abierta al final de la secuencia, si su unidad coincide con la de la versión vigente
   (si el hábito está en pausa, la de la última versión activa).
5. **Mejor racha** = la serie más larga de la unidad actual en todo el historial.
6. El cálculo es determinista: mismas versiones + mismos registros + misma fecha «hoy» → mismo resultado.

También se devuelven: último día completado, porcentaje de cumplimiento (§3.6) y, para cuotas semanales, el
progreso de la semana actual (`completados / meta`, días restantes).

### 3.6 Fórmulas de estadísticas (no se mezclan)
| Indicador | Fórmula |
|---|---|
| **Cumplimiento de días programados** | días programados completados / días programados ya cerrados (+ hoy solo si ya está completado). Solo frecuencias por día. |
| **Cumplimiento de cuotas semanales** | semanas con cuota alcanzada / semanas cerradas (+ la actual si ya alcanzó la cuota). |
| **Cumplimiento combinado del periodo** | (días programados completados + Σ min(completados, meta) de tramos semanales) / (días programados + Σ metas semanales), sobre periodos cerrados o ya cumplidos. |
| **Cumplimiento diario (gráfico, panel)** | por día: hábitos por día completados / hábitos por día programados. Los hábitos semanales se muestran aparte («Esta semana x/N»). |
| **Progreso cuantitativo acumulado** | Σ cantidades registradas en el rango (por hábito, en su unidad). |
| **Consistencia por día de la semana** | cumplimiento de días programados agrupado por lunes…domingo. |
| **Comparación entre periodos** | cumplimiento combinado del rango vs. el rango inmediatamente anterior de igual duración. |

### 3.7 Objetivos
| Métrica | Valor actual |
|---|---|
| `Manual` | Σ entradas de progreso (fecha ≥ inicio). |
| `HabitCompletions` | nº de registros completados de los hábitos vinculados entre inicio y min(fin, hoy). |
| `HabitQuantity` | Σ cantidades de los hábitos vinculados (todos cuantificables y con la misma unidad). |
| `HabitStreak` | mejor racha del hábito vinculado dentro de la ventana del objetivo (un único hábito). |

- Porcentaje = `min(100, actual / meta × 100)`; si `actual > meta` se informa `exceededBy` (la interfaz muestra
  «+X sobre la meta» sin barras por encima del 100 %).
- Estados: `Active` ↔ `Paused`, → `Completed`, → `Cancelled` (los dos últimos son finales; se puede reabrir un
  objetivo completado). Alcanzar la meta no completa el objetivo automáticamente: la API informa `targetReached` y
  el usuario lo confirma (evita escrituras implícitas en lecturas).
- Tras acumular progreso se pueden cambiar nombre, descripción, meta y fecha límite (el porcentaje se recalcula); la
  métrica y los hábitos vinculados no (cambiarían el significado del progreso).
- Las entradas manuales solo se aceptan en objetivos activos.

### 3.8 Recordatorios
- Regla: hora local + días + zona IANA. Se persiste `NextFireAt` (instante UTC del próximo disparo).
- Horario de verano: si la hora local no existe (salto adelante), se dispara en el primer instante válido posterior
  (02:30 → 03:30). Si es ambigua (salto atrás), se usa la primera ocurrencia (offset de verano).
- Worker (`ReminderDispatcher`, `BackgroundService`): cada 30 s toma recordatorios vencidos con
  `FOR UPDATE SKIP LOCKED` (seguro con varias instancias), crea la `Notification` (único por
  `(ReminderId, OccurrenceAt)` → idempotente) y calcula el siguiente `NextFireAt`.
- Disparos atrasados más de 2 h (API caída) se omiten: no se envían recordatorios viejos de golpe.
- Canales: **en la app** (siempre) y **correo** (si `Email:Mode` está configurado y el usuario lo permite; los envíos
  se reintentan hasta 3 veces). **Push no está implementado.**

### 3.9 Retos (`/challenges`)
- Duración de 1 a 31 días; no pueden empezar en el pasado. Reglas: completar el hábito N veces (N ≤ días), acumular N
  unidades (el hábito debe ser cuantificable y con la misma unidad) o completarlo todos los días (meta = nº de días).
- Se entra con un **código de 8 caracteres** (sin caracteres ambiguos) y eligiendo **un hábito propio activo**.
  Validaciones antes de aceptar: el reto no ha terminado, no participabas ya, hay plazas (50) y el hábito encaja.
- **Progreso** = registros del hábito vinculado, del propio participante, entre el inicio y el fin. Nadie puede
  modificar el resultado de otro: no existe ningún endpoint que escriba progreso.
- **Editar** (`PUT /challenges/{id}`, solo quien lo creó): nombre y descripción hasta que termina; fechas, regla, meta y
  unidad solo **antes de que empiece y mientras nadie más se haya unido** (`canEditRules`), para que a nadie le cambien
  las condiciones del reto al que se apuntó. Si quien lo creó participa, su hábito se vuelve a validar con las reglas
  nuevas. Limitación aceptada: no hay bloqueo entre una edición de reglas y una unión simultánea (ventana de
  milisegundos, siempre antes del inicio, y quien se une ve las reglas vigentes en el detalle del reto).
- **Abandonar**: el resultado queda «Abandonado» y solo cuentan los días anteriores al abandono. No se puede volver.
- **Resultado**: superado en cuanto se alcanza la meta; «no superado» si termina sin alcanzarla.
- **Privacidad**: el resto de participantes ve nombre y cifras; nunca el hábito, sus notas ni su identificador.

### 3.10 Objetivos compartidos (`/shared-goals`)
| Acción | Propietario/a | Miembro |
|---|---|---|
| Ver el objetivo, el progreso colectivo y el total de cada participante | ✓ | ✓ |
| Añadir y borrar **sus propias** aportaciones | ✓ | ✓ |
| Editar el objetivo, invitar, retirar invitaciones, quitar miembros, completar o cancelar | ✓ | ✗ |
| Ceder la propiedad a un miembro activo (`POST /shared-goals/{id}/owner`; solo con el objetivo activo) | ✓ | ✗ |
| Salir | ✗ (puede ceder la propiedad, completar o cancelar) | ✓ |

- **Siempre hay una persona propietaria.** Al cederla, quien la tenía pasa a miembro (y ya puede salir) y la nueva
  recibe una notificación. Si la propietaria **borra su cuenta**, antes del borrado (en el mismo `SaveChanges`) la
  propiedad pasa al miembro activo que se unió primero, que recibe una notificación; si no queda ningún miembro activo,
  el objetivo se elimina, porque nadie podría volver a abrirlo.

- **Invitaciones por correo**: la respuesta es la misma exista o no una cuenta con ese correo (sin enumeración). Si
  existe, recibe una notificación en la app; la ve al entrar con ese correo y puede aceptar o rechazar.
- **Progreso individual** = suma de sus aportaciones; **colectivo** = suma de todas. Si hay meta individual, cada
  participante tiene además su propio porcentaje. Los porcentajes se limitan al 100 % y el excedente se informa aparte.
- Quien sale pierde el acceso; sus aportaciones pasadas siguen contando para el grupo (forman parte del resultado
  común). Si borra su cuenta, sus aportaciones se borran con ella (sus datos son suyos).
- Las **notas** de cada aportación solo las ve su autor. Los hábitos personales no se vinculan a objetivos compartidos
  (evita exponer hábitos privados sin consentimiento).

### ADR-06 — Acceso a datos compartidos
Retos y objetivos compartidos no tienen un único dueño, así que no usan los filtros globales de propiedad. Cada
consulta exige pertenencia activa (o ser creador del reto) y responde 404 en caso contrario. El cálculo del progreso de
otros participantes desactiva el filtro de `HabitLogs` explícitamente, restringido al hábito vinculado **y** a su
dueño, y proyecta solo fecha, cantidad y cumplimiento (nunca notas).

## 4. API

Prefijo `/api/v1`. JSON camelCase, enums como texto, fechas locales `yyyy-MM-dd`, instantes ISO 8601 con offset.
Errores en formato RFC 9457 (`application/problem+json`) con extensión `code` estable y `traceId`.

| Código | Cuándo |
|---|---|
| 400 `validation_failed` | Datos inválidos (con `errors` por campo). |
| 401 / 403 | Sin sesión / token antiforgery inválido. |
| 404 `not_found` | No existe o no es tuyo. |
| 409 | Duplicado o modificación concurrente (`concurrency_conflict`). |
| 422 | Regla de negocio incumplida (p. ej. `log.future_date`). |
| 429 `rate_limited` | Demasiadas peticiones. |

La referencia completa está en Swagger (`/swagger` en desarrollo).

## 5. Operación y producción

### Configuración por entorno
| Entorno | Fuente | Particularidades |
|---|---|---|
| `Development` | `appsettings.Development.json` + *user secrets* | PostgreSQL local, migraciones al arrancar, Swagger, correo en carpeta (`mail-pickup/`), `seed-demo` permitido. |
| `Testing` | `WebApplicationFactory` | Base de datos temporal por ejecución, reloj falso, worker de recordatorios desactivado. |
| `Production` | Variables de entorno (Compose) | Logs JSON a stdout, Swagger desactivado, `seed-demo` prohibido, claves de Data Protection en volumen. |

### Observabilidad
- Logs estructurados (JSON en producción) con `TraceId`/`SpanId` en el ámbito de cada petición; la cabecera
  `X-Trace-Id` y el campo `traceId` de los errores permiten correlacionar una incidencia con sus logs.
- Nunca se registran contraseñas, tokens, notas ni correos; solo identificadores de usuario.
- `/health/live` (proceso vivo) y `/health` (incluye PostgreSQL). La imagen Docker usa `--healthcheck`.

### Migraciones
- Se generan con EF Core y se revisan en el PR. CI falla si el modelo y las migraciones no coinciden.
- `Database__MigrateOnStartup=true` aplica migraciones al arrancar: aceptable para migraciones aditivas. Si una
  migración es destructiva (borra o reescribe columnas), desactívalo y aplícala manualmente tras una copia de seguridad
  (`dotnet ef migrations script --idempotent` → revisar → ejecutar en una ventana de mantenimiento).

### Copias de seguridad y recuperación
- Copia lógica diaria: `docker compose exec db pg_dump -U habitflow -Fc habitflow > habitflow-$(date +%F).dump`.
  Guárdala fuera del servidor y conserva al menos 7 diarias y 4 semanales.
- Restauración: `docker compose exec -T db pg_restore -U habitflow -d habitflow --clean --if-exists < fichero.dump`.
- Conserva también el volumen `keys` (claves de Data Protection): si se pierde, las sesiones abiertas caducan (los datos
  no se ven afectados).
- Prueba la restauración periódicamente en un entorno aparte.

### Límites
- Peticiones de hasta 1 MB; 300 peticiones/minuto por usuario; 10 intentos de autenticación/minuto por IP; 5 correos de
  recuperación/hora por IP. Listados paginados (máx. 100) y rangos de fechas acotados (366 días; calendario 42 días).
