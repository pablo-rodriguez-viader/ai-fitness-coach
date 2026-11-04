# Preparación Beta privada — AI Fitness Coach

Fecha: 2026-10-24. Objetivo: uso diario durante 30 días sin soporte manual. Sin funcionalidades, pantallas, RAG, embeddings, multiagentes ni StoreKit nuevos.

**Estado de verificación**
- Backend: compilado y probado en este entorno, 136/136 tests (108 previos + 28 nuevos).
- iOS: cambios revisados a mano pero **no compilados** (Windows, sin Xcode). El primer paso obligatorio es compilar y ejecutar los tests en macOS.

## 1. Flujos completados

| Flujo | Estado | Cómo se validó |
|---|---|---|
| Registro / login con Apple | OK | E2E backend (JWT real, sesión en BD); iOS: cancelar la hoja de Apple ya no muestra error |
| Recuperación de sesión | OK | Restauración en dos fases, sesión offline conservada, reintento si el dispositivo estaba bloqueado, recuperación de refresh perdido (1 h) |
| HealthKit (permiso + observadores) | OK | Observadores registrados al arrancar, también en lanzamiento en segundo plano |
| Sincronización | OK | Cola persistente, un solo bucle, lotes ≤ 500, lotes rechazados aparcados, reanudación al volver la red y en `BGAppRefreshTask` |
| Dashboard | OK | Estados cargando / sin datos / error / offline con caché |
| AI Coach | OK (requiere IA habilitada) | Consentimiento, conversación, historial, feedback, borrar |
| Análisis IA | OK (requiere IA habilitada) | Consentimiento con reintento correcto |
| Entrenamientos | OK (requiere IA habilitada) | E2E: generar plan → feedback → historial → plan actual |
| Nutrición | OK | E2E: estado vacío guiado para usuario nuevo, registro de comidas |
| Objetivos / Progreso | OK | E2E: listas vacías válidas; estado de objetivos refrescado |
| Cerrar sesión | OK | E2E: el token queda revocado; iOS pide confirmación y limpia caché, cola y telemetría |

## 2. Flujos rotos encontrados (todos corregidos)

**Bloqueantes**
1. **La app iOS no compilaba**: `HealthSyncError` declarado dos veces; `SessionManager` usaba una extensión `private` de otro archivo; `AuthenticationView` llamaba a una función `async` sin `await`; el módulo se llamaba `AI_Fitness_Coach` (por `PRODUCT_NAME` con espacios), así que `@testable import AIFitnessCoach` fallaba.
2. **TestFlight rechazaría el build**: `CFBundleVersion`/`CFBundleShortVersionString` vacíos; `UIBackgroundModes` con un valor inválido (`healthkit`).
3. **Pantalla raíz sin redibujar** (probable): la raíz observaba `AppDependencies` pero no `SessionManager`, así que la app podía quedarse en "Restaurando sesión…" o en el login tras iniciar sesión.
4. **Sincronización bloqueada para siempre**: una sola muestra inválida (p. ej. fecha futura) hacía que el servidor rechazara el lote con 400; la app lo dejaba primero en la cola y lo reenviaba en cada intento.
5. **Lanzamiento en segundo plano con el iPhone bloqueado**: Keychain `WhenUnlocked` → la app caía en "Backend no configurado" o perdía la sesión hasta matarla; los observadores solo se registraban desde la UI, así que las entregas en segundo plano no hacían nada.
6. **Cierre de sesión forzado**: si se perdía la respuesta de un refresh, el siguiente se trataba como robo de token y se revocaba la sesión.

**Funcionales**
7. Nutrición para un usuario nuevo abría en error "HTTP 400" en lugar de un estado vacío guiado.
8. "Reintentar" en Coach y Análisis no hacía nada si lo que había fallado era la carga del consentimiento.
9. Las cancelaciones (cerrar una hoja, `URLError.cancelled`) se mostraban como errores.
10. Al caducar la sesión, el usuario volvía al login sin explicación y los datos del usuario anterior seguían en memoria.
11. Al crear un objetivo, el anterior seguía mostrándose como "Active".
12. En Entrenamientos, si fallaba un endpoint se vaciaba toda la pantalla.
13. Seis observadores de HealthKit disparaban a la vez: dos bucles subían el mismo lote y el estado se corrompía.
14. Al conectar Salud sin red se mostraba error aunque las muestras ya estaban en cola.
15. Backend: el rate limiting se aplicaba después de la autorización (el tráfico anónimo o con token inválido no tenía límite) y `/auth/*` no tenía límite; un JSON mal formado devolvía 500.

**Huérfanos o inaccesibles (sin cambios, por el alcance)**
- `ChatViewModel`, `AIChatService`, `ConversationStore` (solo se vacía), `ChatRepository.sendMessage`/`fetchConversations`, `fetchSuggestedCoachQuestions`; `/api/v1/ai/chat` y `/api/v1/ai/coach` devuelven un error *Problem* fijo.
- `BehaviorInsightsView` nunca se presenta.
- `WorkoutPlanViewModel.complete(day:)` y `/workouts/complete` no se usan desde la UI.
- No existe ninguna pantalla para editar el perfil ni para borrar la cuenta.
- El texto del consentimiento remitía a "Ajustes", una pantalla que no existe (texto corregido).

## 3. Cambios implementados

**Backend**
- `SyncHealthMetricsUseCase(skipInvalidItems:)`: el endpoint de dispositivo descarta solo las muestras inválidas y las cuenta en `rejectedCount`. El modo estricto se mantiene para el resto de llamadas. Un lote de borrados con `deviceId` mixtos ya no se rechaza.
- Refresh token: `RefreshToken.CanRecoverLostRotation` (ventana de 1 h, solo si el sustituto nunca se usó). La rotación se ejecuta dentro de la estrategia de reintento de EF.
- `EnableRetryOnFailure` para PostgreSQL. El cliente de IA reintenta una vez ante 429/5xx o fallo de conexión.
- Rate limiting: límite global por usuario/IP, `/auth/*` limitado por IP, política `telemetry`. El limitador corre antes de la autorización y `/health*` queda excluido.
- `/health/live` y `/health/ready` (comprueba PostgreSQL, sin filtrar detalles). `/health` se conserva.
- `X-Correlation-ID` en cada respuesta, logs JSON con scope, niveles de log ajustados (EF y ASP.NET en Warning) y `BadHttpRequestException` → 400.
- `POST /api/v1/telemetry/events`: logs estructurados y contadores `client_events_total`, `client_crashes_total` y `health_sync_metrics_rejected`.
- `ApiPipeline.UseApiPipeline()`: el mismo pipeline en producción y en los tests E2E.

**iOS**
- Corregidos los 4 errores de compilación y la configuración de TestFlight (`MARKETING_VERSION`, `CURRENT_PROJECT_VERSION`, `PRODUCT_MODULE_NAME`, `ITSAppUsesNonExemptEncryption`, orientación, `fetch`, `BGTaskSchedulerPermittedIdentifiers`, esquema de URL).
- Sincronización: un único bucle, agrupación de lecturas de HealthKit, lotes 250 + 250, lotes rechazados aparcados 6 h y descartados tras 4 rechazos (`rejectionCount`, compatible con las colas ya guardadas), y 401/403/404 detienen la sincronización sin tocar la cola.
- Sesión: Keychain `AfterFirstUnlockThisDeviceOnly` con migración, arranque independiente de la UI, `restoreSessionIfNeeded` y tarea en segundo plano con expiración durante el refresh. Si la sesión caduca se avisa al usuario y se limpia el estado.
- Estados: `NetworkMonitor` + `OfflineBanner`, caché cifrada del resumen del Dashboard, "Datos guardados el…", estado de sincronización, cancelaciones silenciosas, mensajes offline uniformes, consentimiento "comprobando…" y botón Cerrar en todas las hojas.
- Deep links `aifitnesscoach://…` hacia las pantallas existentes, `BGAppRefreshTask` para subir la cola pendiente y confirmación al cerrar sesión (avisa si hay datos sin enviar).
- Telemetría: `AppTelemetry` (persistida, por lotes, acotada), `CrashDiagnostics` → `crash_report`/`hang_report`, eventos de uso y de sincronización.

## 4. Tests añadidos

| Tipo | Backend | iOS |
|---|---|---|
| Smoke | `BetaSmokeTests` (8): arranque con la composición real, health probes, 401 en endpoints protegidos, correlation id, JSON mal formado, rate limit de auth, probes sin límite, configuración inválida | — |
| End to end / User journey | `BetaUserJourneyTests` (7): alta → perfil → sync → resumen; plan → feedback → historial; IA deshabilitada → 503; nutrición vacía; logout revoca; aislamiento entre usuarios; telemetría | `BetaJourneyTests` (7): relanzar offline con caché, offline sin caché → reintentar, conectar Salud sin red, caché por usuario borrada al salir, reintentos de Coach y Análisis, cancelación sin error |
| Regression | `BetaRegressionTests` (13): sync tolerante, recuperación de refresh, reintentos de IA, validación de telemetría | `SyncRegressionTests` (8): cola no bloqueada, descarte tras rechazos, offline no descarta, 401 conserva la cola, concurrencia, límite de lote, cola antigua, `rejectedCount`; `AppStateTests` (9): clasificación de errores, guía de nutrición, deep links, telemetría |

Actualizado: `testPermanentServerErrorRemainsQueuedWithoutAutomaticRetry` → `testPermanentServerErrorKeepsChangesQueuedButParked` (cambio de comportamiento intencionado).

## 5. Riesgos para la Beta

1. **iOS sin compilar aquí.** Pueden aparecer errores menores de compilación o avisos de concurrencia en Xcode.
2. **La IA es obligatoria para 3 flujos** (Coach, Análisis, Entrenamientos). Con `AI:Enabled=false` devuelven 503 y la app muestra "no disponible". Requiere `AI__ApiKey`, precios y consentimiento/DPA documentados.
3. ~~**La telemetría necesita un destino**~~ — Resuelto (2026-10-27): exportador OpenTelemetry (OTLP) activable con `OpenTelemetry__OtlpEndpoint`. Ver `TESTFLIGHT.md` §6.
4. **Tests sobre EF InMemory**: `FOR UPDATE`, la estrategia de reintentos y la rotación de refresh no están probados contra PostgreSQL real.
5. **El rate limiting es en memoria por instancia**: con varias réplicas, los límites se multiplican.
6. **La cola de HealthKit usa `FileProtectionComplete`**: con el iPhone bloqueado no se puede escribir. Se reintenta al desbloquear (sin duplicados por idempotencia).
7. **`POST /nutrition/log` no es idempotente**: un corte de red tras guardar puede duplicar una comida si el usuario reintenta.
8. **Ventana de 1 h de reutilización del refresh token**: compromiso entre robustez y detección de robo.
9. ~~**Sin borrado de cuenta en la app**~~ — Resuelto (2026-10-27): Ajustes › Eliminar cuenta, con revocación de Sign in with Apple. Ver `APP_STORE_READINESS.md` §7.
10. **Identificadores `com.example.*`** — Ahora se definen en un único sitio (`APP_BUNDLE_ID` en `ios/Config/Shared.xcconfig`); la tarea en segundo plano se deriva del Bundle ID. Falta poner el valor real.

## 6. Checklist Beta Ready

**Build y distribución**
- [ ] `xcodegen generate` + `xcodebuild test` en verde en macOS
- [ ] Bundle ID real, App ID con HealthKit (+ background delivery) y Sign in with Apple
- [ ] Identificador de `BGTaskScheduler` actualizado al bundle real
- [ ] `CURRENT_PROJECT_VERSION` incrementado en cada subida (CI: `$GITHUB_RUN_NUMBER`)
- [ ] `API_BASE_URL` HTTPS de producción en el esquema Release
- [ ] Probado en un dispositivo físico: permisos de Salud, bloqueo/desbloqueo, modo avión, reinicio del iPhone

**Backend**
- [ ] Secrets en vault: `Jwt__SigningKeyBase64`, `ConnectionStrings__Postgres`, `AI__ApiKey`
- [ ] `AI__Enabled=true`, modelo y precios configurados
- [ ] Migraciones aplicadas con un job controlado (no `Development`)
- [ ] Probes: liveness `/health/live`, readiness `/health/ready`
- [ ] `ForwardedHeaders__Enabled=true` si hay balanceador de confianza
- [ ] Una réplica, o rate limiting distribuido
- [ ] TLS, backups cifrados de PostgreSQL y restauración probada

**Observabilidad**
- [ ] Logs JSON enviados a un agregador, con búsqueda por `CorrelationId`
- [ ] Alertas: tasa de 5xx, `/health/ready` caído, `client_crashes_total` > 0, `sync_failed`/`sync_dead_lettered`, picos de 429
- [ ] Panel diario: usuarios activos (`app_foreground`), `screen_view`, `sync_completed`, `coach_message_sent`

**Privacidad y producto**
- [ ] Política de privacidad y textos de consentimiento de Salud/IA revisados
- [ ] DPA con el proveedor de IA
- [ ] Decidir el borrado de cuenta antes de la revisión externa
- [ ] Instrucciones para testers (deep links, qué reportar) y canal de feedback

> Continuación (2026-10-27): App Store readiness, observabilidad, soporte y TestFlight en [`APP_STORE_READINESS.md`](APP_STORE_READINESS.md) y [`TESTFLIGHT.md`](TESTFLIGHT.md).
