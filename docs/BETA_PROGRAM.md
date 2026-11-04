# Beta privada por etapas: 10 → 25 → 50 usuarios

Fecha: 2026-10-28. Alcance: medir la beta y dejarla funcionar sola. Sin funcionalidades, módulos de IA, multiagentes, RAG ni capacidades de entrenamiento nuevas.

**Estado de verificación**
- Backend: compilado y probado aquí, **214/214 tests** en Release (12 nuevos en `BetaProgramTests`).
- iOS: revisado a mano, **no compilado** (Windows, sin Xcode). En macOS: `xcodegen generate` + `xcodebuild test` (6 tests nuevos en `BetaProgramTests.swift`).

## 1. Flujos verificados

| Flujo | Estado | Referencia |
|---|---|---|
| Onboarding (perfil) | ✅ Carga, error con reintento, formulario, offline | `RELEASE_CANDIDATE.md` §1, `UserProfileViewModelTests` |
| Login / Sign in with Apple | ✅ Cancelar no muestra error; sesión restaurada sin red; 🆕 «beta completa» con mensaje claro | `BetaUserJourneyTests`, `BetaProgramTests` |
| HealthKit | ✅ Permiso, observadores en segundo plano, cola persistente; 🆕 detección de permisos probablemente denegados | `HealthSyncTests`, `SyncRegressionTests` |
| Dashboard | ✅ Todos los estados y caché offline; 🆕 aviso de feedback beta | `BetaJourneyTests` |
| AI Coach | ✅ Consentimiento, reintentos, feedback por respuesta, circuit breaker | `CoachViewModelTests`, `ReleaseCandidateTests` |
| Entrenamientos | ✅ Plan, sesión, valoración con estado propio | `BetaUserJourneyTests` |
| Nutrición | ✅ Estado vacío guiado, errores de carga y de guardado separados | `AppStateTests` |
| Progreso | ✅ Offline, Sync Pending, vacío | `ReleaseCandidateTests` |

Todo lo anterior se revisó en las iteraciones previas (`BETA_READINESS.md`, `RELEASE_CANDIDATE.md`); en esta solo cambian el login (beta completa) y el Dashboard (aviso de feedback).

## 2. Métricas

Todas en `GET /api/v1/analytics/product` (cabecera `X-Analytics-Key`), dentro del bloque nuevo **`beta`**, y publicadas cada hora sin intervención (§4).

| Pedido | Campo | Definición |
|---|---|---|
| Usuarios activos diarios | `beta.activeUsers.daily` (y `averageDaily`) | Usuarios con algún evento de uso el último día (UTC) |
| Usuarios activos semanales | `beta.activeUsers.weekly` | Últimos 7 días |
| Usuarios activos mensuales | `beta.activeUsers.monthly` 🆕 | Últimos 30 días; `dailyOverMonthly` = DAU/MAU |
| Retención D1 / D7 / D30 | `beta.retention.d1/d7/d30` | Retención clásica: activos exactamente N días después del primer día. Solo cuentan usuarios con antigüedad ≥ N días (`eligibleUsers`) |
| Entrenamientos iniciados | `beta.workouts.started` | Sesiones abiertas (`workout_started`), una por usuario y día de entrenamiento |
| Entrenamientos completados | `beta.workouts.completed` | Registrados por el servidor al guardar la valoración o completar |
| Abandonos 🆕 | `beta.workouts.abandoned`, `abandonmentRate` | Sesión abierta sin completar en las 24 h siguientes. Las abiertas hace menos de 24 h van a `inProgress` y no cuentan |
| Consultas realizadas | `beta.coach.queries` (`coachQueries`, `analysisQueries`) | Respuestas entregadas; los reintentos del mismo `client_request_id` cuentan una vez |
| Consultas repetidas 🆕 | `repeatUsers`, `repeatedQueries`, `duplicateQuestions` | Usuarios que vuelven a preguntar, consultas tras la primera y la **misma pregunta** repetida por el mismo usuario. Se compara una huella SHA-256 de la pregunta normalizada; el texto no se guarda en analytics |
| Feedback positivo / negativo 🆕 | `positiveFeedback`, `negativeFeedback`, `positiveRate` | Valoraciones 👍/👎 de respuestas del Coach (evento `coach_feedback` del servidor) |
| Sincronizaciones exitosas / fallidas 🆕 | `beta.healthKit.syncsSucceeded/syncsFailed/syncSuccessRate`, `syncsDeadLettered` | Resultados de la cola de HealthKit en el iPhone |
| Permisos rechazados 🆕 | `usersPermissionLikelyDenied`, `authorizationFailures` | iOS no revela qué lecturas se denegaron. Tras el permiso (y en cada arranque) la app cuenta los tipos pedidos con alguna muestra en 30 días; `0` = **probablemente denegado** o sin datos. `authorizationFailures` = la hoja de Salud falló |

**Errores** (bloque `beta.stability`): `crashFreeUsersRate`, `crashes`, `hangs`, `errors`, `errorsByArea` (top 10) y `byBuild` (crashes, bloqueos y errores por versión de la app). Crash, error y sync tracking ya existían (MetricKit, `error`, `sync_*`); ahora además se **guardan**, así que el informe muestra la estabilidad sin agregador de logs.

Los eventos de sync, crash y error se generan solos (en segundo plano o al arrancar), así que **no cuentan como actividad**: un usuario que no abre la app no aparece como activo aunque su iPhone sincronice. Hay un test que lo comprueba.

## 3. Crecer de 10 a 25 a 50 sin tocar código

1. **Tope de cuentas**: `Beta__MaxUsers=10`. Un Apple ID nuevo por encima del tope recibe `403 beta_full` y la app muestra «La beta privada está completa por ahora». Quien ya tiene cuenta siempre puede volver a entrar. Para la siguiente etapa basta con cambiar la variable a 25 y luego a 50 y reiniciar. Con `0` no hay tope.
2. **TestFlight**: un grupo de testers externos por etapa (`Beta 10`, `Beta 25`, `Beta 50`) o un enlace público con límite de testers igual a la etapa. La primera build pasa Beta App Review; las siguientes de la misma versión normalmente no.
3. **Señal para crecer**: la alerta `beta_capacity` salta al 90 % del tope. Criterios orientativos para pasar de etapa (decídelos antes de empezar):

| Etapa | Mínimo para pasar a la siguiente |
|---|---|
| 10 | 7 días con crash-free ≥ 99 %, sync ≥ 95 %, ninguna alerta abierta |
| 25 | Lo anterior + D7 ≥ 35 %, feedback positivo del Coach ≥ 70 % |
| 50 | Lo anterior + D30 medible, abandono de entrenamientos ≤ 50 % |

**Capacidad**: 50 usuarios están muy por debajo de los límites (300 peticiones/min por usuario, una instancia y PostgreSQL básico). Las cuotas de IA por usuario (20 análisis y 30 000 tokens de Coach al día) acotan el coste. En el peor caso, 50 × 30 000 = 1,5 M tokens/día: menos de 1 USD/día con `gpt-4o-mini` a las tarifas configuradas. El informe carga los eventos en memoria (tope de 500 000), suficiente para la beta.

## 4. Sin intervención manual

| Qué | Cómo |
|---|---|
| KPIs cada hora 🆕 | `BetaKpiPublisher` calcula el bloque `beta` cada `Beta__KpiIntervalMinutes` (60) y lo publica como métricas `beta_*`: usuarios activos, retención, abandono, feedback, sync, crash-free, plazas libres y alertas activas. Se exportan por OpenTelemetry si está configurado, y además deja un resumen en el log |
| Alertas 🆕 | Cada umbral superado se registra como `Beta alert <código>` (Warning). Códigos: `crash_free_users` (< 99 %), `sync_success` (< 95 % con ≥ 20 syncs), `sync_dead_lettered` (> 0), `workout_abandonment` (> 50 % con ≥ 10), `coach_feedback` (< 70 % positivo con ≥ 10), `healthkit_permissions` (> 20 % de conectados sin datos, con ≥ 5), `beta_capacity` (≥ 90 % del tope). Configura una regla en el agregador sobre `beta_active_alerts > 0` o sobre ese mensaje |
| Feedback beta 🆕 | En builds de TestFlight, tras 3 días de uso el Dashboard pide comentarios. Sale como mucho 3 veces, con 14 días entre una y otra, y deja de salir si el tester los envía. Se apaga con `FeatureFlags__beta_feedback=false`. Los comentarios llegan a la bandeja de soporte (`TESTFLIGHT.md` §8) |
| Logging beta 🆕 | Cada lote de telemetría lleva el entorno (`beta`/`production`) y aparece en el log junto a versión y build. Los builds beta envían la telemetría cada 5 eventos en vez de cada 20, así que los crashes y errores llegan en minutos |
| Apagar una pantalla rota | Feature flags (`TESTFLIGHT.md` §4) |
| Caída del proveedor de IA | Circuit breaker y readiness Degraded (`RELEASE_CANDIDATE.md` §2) |

## 5. Configuración nueva

| Variable | Defecto | Uso |
|---|---|---|
| `Beta__MaxUsers` | 0 (sin tope) | 10 → 25 → 50 |
| `Beta__KpiPublisherEnabled` | true | Publicador de KPIs |
| `Beta__KpiIntervalMinutes` | 60 (5–1440) | Frecuencia |
| `FeatureFlags__beta_feedback` | true | Aviso de feedback beta |

## 6. Limitaciones

- «Permisos rechazados» es una estimación: un usuario sin datos en Salud se cuenta igual que uno que denegó el acceso.
- La huella de las preguntas detecta repeticiones exactas (normalizando mayúsculas, espacios y puntuación de los extremos), no preguntas parecidas.
- El tope de usuarios puede superarse en 1 si dos personas se registran en el mismo instante.
- Los KPIs, el tope y el circuit breaker son por instancia: la beta asume una sola réplica (ver `BETA_READINESS.md` §5).
