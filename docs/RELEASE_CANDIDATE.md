# Release Candidate 0.9.0 (RC1) — TestFlight

Fecha: 2026-10-28. Alcance: revisar los flujos existentes, completar estados, endurecer el backend y ampliar los tests. Sin funcionalidades nuevas, sin capacidades de IA nuevas, sin multiagentes, sin RAG y sin StoreKit.

**Estado de verificación**
- Backend: compilado y probado aquí, **202/202 tests** en Release (11 nuevos en `ReleaseCandidateTests`).
- iOS: revisado a mano, **no compilado** (Windows, sin Xcode). Antes de subir: `xcodegen generate` + `xcodebuild test` en macOS (5 tests nuevos en `ReleaseCandidateTests.swift`). Después, `ios/scripts/testflight.sh` (ver `TESTFLIGHT.md`).

## 1. Estados por flujo

✅ ya existía · 🆕 añadido en esta RC · — no aplica

| Flujo | Loading | Error | Empty | Offline | Sync Pending |
|---|---|---|---|---|---|
| Login | ✅ | ✅ | — | 🆕 | — |
| Onboarding (perfil) | ✅ | ✅ reintentar | ✅ formulario | 🆕 (carga y guardado) | — |
| HealthKit (conectar) | ✅ | ✅ | ✅ «sin datos visibles» | ✅ | ✅ |
| Dashboard | ✅ | ✅ | ✅ | ✅ caché | ✅ |
| Coach | ✅ | ✅ | ✅ | ✅ | 🆕 |
| Análisis IA | ✅ | ✅ | ✅ | ✅ | 🆕 |
| Entrenamientos | ✅ | ✅ | ✅ | ✅ | 🆕 |
| Sesión de entrenamiento (valoración) | 🆕 «Guardando…» | 🆕 con «Volver a valorar» | — | 🆕 | — |
| Nutrición | ✅ (ahora arriba) | 🆕 separa carga y guardado | ✅ | ✅ y registrar desactivado | 🆕 |
| Objetivos | ✅ + 🆕 «Guardando…» | 🆕 separa carga y guardado | ✅ | 🆕 | — |
| Progreso | ✅ (ahora arriba) | ✅ | ✅ | 🆕 | 🆕 |

**Sync Pending**: `SyncPendingNotice` + `HealthSyncStatusStore`. El Dashboard ya mostraba la cola de HealthKit; ahora también lo hacen las pantallas que usan esos datos, explicando en cada una a qué afecta («las gráficas / respuestas / el plan pueden no incluir tus datos más recientes»). Si la sincronización está bloqueada, el aviso pasa a naranja y remite al Dashboard.

**Error de carga vs. de guardado**: en Objetivos y Nutrición, si fallaba un guardado aparecía «Volver a cargar», que no arreglaba nada. Ahora el botón solo sale cuando falló la carga (`loadFailed`).

**Defectos corregidos durante la revisión**
- Objetivos y Progreso mostraban valores del API en inglés (`Active`, `Paused`, `FatLoss`…): ahora «Activo», «En pausa», «Pérdida de grasa»…
- Entrenamientos mostraba el material en inglés (`Dumbbells`, `ResistanceBand`…) y el objetivo del plan sin traducir (`general_health`).
- Al valorar una sesión de entrenamiento no había ninguna respuesta en esa pantalla: el resultado o el error solo aparecían al volver atrás.
- El borrado de cuenta y el envío de reportes podían quedar a medias si el usuario salía de la app justo después de tocar: ahora piden tiempo extra a iOS (`BackgroundWork`).

## 2. Backend

| Aspecto | Estado | Detalle |
|---|---|---|
| Rate limits | ✅ verificado | Global 300/min por usuario o IP; `/auth/*` 30/min por IP; IA 10/min; telemetría 20/min; soporte y cuenta 5/min. Responde 429 con `Retry-After`; `/health*` sin límite. El smoke test recorre todas las rutas sin ningún 429. |
| Retry policies | ✅ verificado | PostgreSQL: `EnableRetryOnFailure` (3 intentos, máx. 5 s) y transacciones dentro de la estrategia. Proveedor de IA: 1 reintento ante 429/5xx o fallo de conexión, respetando `Retry-After` ≤ 5 s. Apple: sin reintentos (el borrado de cuenta no depende de Apple). |
| Health checks | ✅ + 🆕 | `/health/live` (proceso) y `/health/ready` (PostgreSQL y, nuevo, `ai_provider`). Con el circuito de IA abierto, readiness responde **Degraded con 200**: se ve en monitorización, pero no se saca la instancia del balanceador. |
| Timeouts | 🆕 | Antes, ninguna petición tenía un límite propio. Ahora hay timeouts por petición (`UseRequestTimeouts`) que responden **504**: 30 s por defecto, 90 s en las rutas de IA y 120 s en la exportación (`RequestTimeouts__DefaultSeconds/AISeconds/LongSeconds`). Ya existían 60 s para la llamada a la IA y 15 s para Apple. |
| Circuit breaker | 🆕 | Proveedor de IA (`AICircuitBreaker`). Tras 5 fallos seguidos (5xx, 429, timeout o conexión) se abre 30 s y responde 503 al instante, sin llamar al proveedor. Después deja pasar una sola petición de prueba: si va bien se cierra, si falla se vuelve a abrir. Los 4xx y la IA desactivada no lo abren. Se configura con `AI__CircuitBreaker__FailureThreshold` y `AI__CircuitBreaker__BreakSeconds`. Métricas: `ai_circuit_rejections_total` y `ai_circuit_transitions_total{state}`. |

La app ya trata el 503 como «no disponible» y el 504 como un error con reintento, así que no necesita cambios.

## 3. iOS: verificación

| Aspecto | Resultado | Cubierto por |
|---|---|---|
| Session recovery | ✅ Restauración en dos fases; sesión offline conservada; reintento si el Keychain estaba bloqueado; recuperación de un refresh perdido; aviso y limpieza al caducar | `BetaJourneyTests`, `SyncRegressionTests.testExpiredSessionStopsSyncAndKeepsChangesDue` |
| App lifecycle | ✅ `session_start` tras 30 min en segundo plano; al volver a primer plano: sync, recordatorios y config remota (máx. cada 5 min); al pasar a segundo plano se programa el background refresh y se vacía la telemetría. 🆕 Borrado de cuenta y reportes con tiempo extra en segundo plano | `AppStateTests`, `ReleaseReadinessTests.testRefreshIsThrottled` |
| Deep links | ✅ `aifitnesscoach://` hacia las 9 pantallas existentes; se guardan hasta que el Dashboard está visible (también tras iniciar sesión); los enlaces a pantallas apagadas por un flag no abren nada y registran `screen_unavailable` | `AppStateTests.testDeepLinksOpenOnlyExistingScreens`, `testRouterHoldsALinkUntilConsumed`, `EngagementTests` |
| Background refresh | ✅ `BGAppRefreshTask` (`$(PRODUCT_BUNDLE_IDENTIFIER).sync`) cada ≥ 1 h sube la cola pendiente; observadores de HealthKit registrados también en lanzamientos en segundo plano; `healthkit_observer_update` confirma la entrega | `HealthSyncTests`, `SyncRegressionTests`; **en dispositivo físico** (ver checklist) |

## 4. Tests

| Tipo | Backend (nuevos en esta RC) | iOS (nuevos en esta RC) |
|---|---|---|
| End to end | Petición de IA que se cuelga → 504 en menos de 10 s y la API sigue respondiendo; readiness Degraded con el circuito abierto | — (los E2E de iOS requieren dispositivo: checklist §5) |
| Smoke | **Todas** las rutas protegidas (más de 40, descubiertas del enrutador) responden 401 sin token, sin ningún 5xx; la lista de rutas anónimas es exactamente la esperada (una ruta nueva sin autenticación rompe el test); readiness incluye `ai_provider` | — |
| Regression | Circuit breaker: abre tras N fallos y falla rápido; prueba en half-open y cierre; una prueba fallida lo reabre; los 4xx y la IA desactivada no lo abren; configuración inválida (breaker y timeouts) falla al arrancar | Estado Sync Pending (offline vs. bloqueado, singular y plural); objetivos y material traducidos |

Totales: backend 202 tests (antes 191). iOS: 75 tests, 5 nuevos.

## 5. Checklist para subir la RC a TestFlight

1. En macOS, `xcodegen generate && xcodebuild test` en verde. Corregir lo que salga: es la primera compilación de estos cambios.
2. Backend de beta desplegado con la migración `AddSupportReports` y la configuración de `TESTFLIGHT.md` §5. Vigilar `/health/ready`.
3. `ios/scripts/testflight.sh` (o el workflow **TestFlight Beta**).
4. En un iPhone físico con el build de TestFlight, además de la checklist de `TESTFLIGHT.md` §10:
   - [ ] Modo avión en cada pantalla: aparece el aviso offline y nada se queda cargando para siempre
   - [ ] Con la cola pendiente (modo avión + nuevos pasos en Salud): el aviso de Sync Pending aparece en Progreso, Coach, Análisis, Rutinas y Nutrición, y desaparece al volver la red
   - [ ] Valorar una sesión de entrenamiento: «Guardando…» y luego «Valoración guardada»; en modo avión, error con «Volver a valorar»
   - [ ] Guardar un objetivo sin red: error sin botón «Volver a cargar»; estados en español
   - [ ] Pulsar Eliminar cuenta, confirmar con Apple y salir de la app al instante: al volver, la cuenta está borrada
   - [ ] Abrir `aifitnesscoach://progress` con la app cerrada y sin sesión: tras iniciar sesión se abre Progreso
   - [ ] Dejar el iPhone bloqueado 2 h: `sync_background_refresh` o `healthkit_observer_update` en segundo plano
5. Simular la caída de la IA en beta (p. ej. `AI__Endpoint` a un host que responda 503) y comprobar: tras 5 intentos, respuestas 503 inmediatas, `ai_provider` Degraded y recuperación en unos 30 s al restaurarlo.

## 6. Riesgos conocidos

- iOS sin compilar en este entorno (ver arriba).
- El circuit breaker y el rate limiting son **por instancia**: con varias réplicas, cada una abre su circuito y aplica sus límites por separado.
- Los timeouts por petición solo cortan el código que respeta la cancelación (las llamadas a EF y a la IA lo hacen). Además, ASP.NET no los aplica con el depurador conectado.
- Siguen vigentes los riesgos de `BETA_READINESS.md` §5: tests sobre EF InMemory, `POST /nutrition/log` no idempotente y la ventana de 1 h del refresh token.
