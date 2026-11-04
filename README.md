# AI Fitness Coach

Aplicación iOS SwiftUI y API ASP.NET Core 10 para identidad Apple, sesiones JWT, sincronización HealthKit por usuario, AI Coach V3 adaptativo y gestión de objetivos/nutrición. No se incluyen multiagentes, StoreKit, suscripciones, marketplace ni RAG.

## Implementación

- Sign in with Apple: el API valida firma, issuer, audience, expiración y nonce del identity token usando claves públicas de Apple.
- Sesiones: JWT HMAC-SHA256 de corta duración, refresh tokens aleatorios persistidos solo como SHA-256, rotación de un solo uso, revocación de sesión y verificación de sesión activa en cada request autenticada.
- Datos: cada identidad Apple apunta a un UUID interno; perfiles y métricas tienen claves foráneas hacia `users`.
- HealthKit: pasos, peso, calorías activas y basales, frecuencia cardiaca, sueño y entrenamientos. El resumen de calorías totales se calcula como activas + basales.
- Sincronización: `HKAnchoredObjectQuery` procesa altas, correcciones y borrados; checkpoints se particionan por usuario/tipo. Un tombstone elimina de la cola un alta pendiente para la misma muestra y prevalece ante el envío. La cola local persistente guarda trabajos, tombstones y reintentos con backoff exponencial para errores de red, HTTP 408/429 y 5xx, protegida con `NSFileProtectionComplete` y excluida de backups. Un lote rechazado de forma permanente (4xx distintos de 401/403/404/408/429) se aparca 6 horas para que no bloquee los cambios más recientes y se descarta tras 4 rechazos (`rejectionCount`, independiente de los reintentos sin red); 401/403/404 detienen la sincronización y conservan la cola intacta. El servidor descarta solo las muestras inválidas de un lote (p. ej. con fecha futura) y las devuelve en `rejectedCount`. Solo corre un bucle de sincronización a la vez y las lecturas de HealthKit concurrentes se agrupan. Lotes de 250 altas + 250 bajas (límite del servidor: 500). Cola y cursor se escriben atómicamente; el envío se confirma y elimina localmente solo tras respuesta válida.
- Idempotencia: clave única `(user_id, source, external_id)` con UUID de muestra HealthKit estable. Reenviar una muestra sin cambios no duplica ni altera versión; una corrección actualiza el registro, `sync_version` y `updated_at`; un tombstone repetido es un no-op. `user_id` del lote debe coincidir con el JWT.
- Tokens de iOS: access y refresh tokens y sus metadatos se almacenan exclusivamente en Keychain (`AfterFirstUnlockThisDeviceOnly`, para que HealthKit pueda despertar la app con el dispositivo bloqueado; nunca salen del dispositivo). Las entradas antiguas se migran al restaurar la sesión. No se escriben en UserDefaults ni archivos.
- Sesión: la restauración carga la sesión de Keychain al arrancar (también en lanzamientos en segundo plano) antes del refresh de red; si el dispositivo estaba bloqueado se reintenta al volver a primer plano. Si una respuesta de refresh se pierde, el servidor acepta el token anterior durante 1 hora siempre que su sustituto no se haya usado; cualquier otra reutilización revoca la sesión.
- Estados y resiliencia en iOS: banner sin conexión, último resumen del Dashboard en caché cifrada (`FileProtectionComplete`, por usuario, se borra al cerrar sesión), reanudación automática de la cola al recuperar la red y refresco en segundo plano (`BGAppRefreshTask` `com.example.aifitnesscoach.sync`). Las cancelaciones no se muestran como error.
- Deep links: `aifitnesscoach://{dashboard|coach|analysis|workouts|goals|nutrition|progress}` abre la pantalla existente (tras iniciar sesión si hace falta).
- Observabilidad: crashes y bloqueos (resumen de MetricKit), errores por pantalla, uso (pantallas y acciones) y métricas de sincronización se envían a `POST /api/v1/telemetry/events`, sin valores de salud ni texto del usuario. El servidor los registra como logs estructurados y contadores (`client_events_total`, `client_crashes_total`).
- AI Coach V1: analiza perfil/objetivo, pasos, sueño, peso y tendencia de peso, frecuencia cardíaca, entrenamientos, consistencia y progreso de 7 y 30 días. Permite una recomendación general de actividad para mañana, no un plan o rutina completa. System prompt prohíbe diagnóstico/afirmaciones médicas y dirige a profesionales ante preocupaciones clínicas.
- AI Coach V2: genera planes diarios y semanales con perfil, objetivo, pasos, peso, sueño, frecuencia cardíaca y entrenamientos; aplica reducciones deterministas de intensidad/volumen cuando el sueño es inferior a 6 horas o hay al menos 3 días sin entrenar. Solo progresa el volumen cuando aumentan los minutos de entrenamiento frente a la semana previa y el feedback reciente es positivo. Las completaciones y la dificultad reportada influyen en los planes siguientes. No genera planes de nutrición ni sustituye consejo sanitario.
- Nutrición y objetivos: objetivos activos de pérdida de grasa, ganancia muscular, mantenimiento, resistencia y salud general; BMR Mifflin–St Jeor, TDEE por nivel de actividad, ajuste moderado para déficit/superávit y macros estimados (proteína 1.6–1.8 g/kg, grasa 0.8 g/kg y carbohidratos para completar energía). Calorías/macros de desayuno, comida, cena y snacks se registran por usuario y día; la aplicación conserva objetivos nutricionales diarios e historial de peso, calorías, pasos y sueño. Los cálculos son estimaciones generales; se requiere perfil de 18+ años y peso sincronizado para calcularlos.
- Contexto IA: incorpora objetivo activo, peso objetivo y actual, TDEE/calorías objetivo estimados, tendencia de peso y adherencia de los registros nutricionales. El prompt los trata como estimaciones no médicas y no genera planes nutricionales.
- AI Coach V3: compara tendencias semanales de peso, sueño, pasos, frecuencia cardiaca y gasto calórico; calcula adherencia sobre los días planificados de las últimas cuatro semanas; conserva objetivos anteriores, planes, feedback, recomendaciones y resultados. Los resultados positivos/negativos y la adherencia influyen en la progresión y el volumen de próximos planes. Las asociaciones se describen sin atribuir causalidad.
- IA: contratos y casos de uso neutrales del proveedor viven en `Application`; `Infrastructure` implementa el gateway HTTP y obtiene la credencial exclusivamente de `AI__ApiKey` (variable de entorno/secret reference del deployment; no se acepta el valor de `appsettings.json`). iOS nunca recibe claves ni llama al proveedor. Conversaciones, snapshots agregados y uso/coste se guardan en PostgreSQL. La IA se mantiene deshabilitada (`AI:Enabled=false`) hasta configurar la credencial segura, modelo y precios. El análisis de actividad aplica cuota diaria atómica por usuario y solo envía agregados de perfil/objetivo, resúmenes, tendencias y entrenamientos recientes.

## Backend

Proyectos en `backend/src/`: `AIFitnessCoach.Api`, `Application`, `Domain` e `Infrastructure`.

Migraciones:

1. `20261015100000_InitialHealthData`
2. `20261018180000_AddIdentityAndHealthSync`: crea users/identities/sessions/refresh tokens, conserva los perfiles y métricas existentes, y añade fuente, clave idempotente e índices.
3. `20261018200000_AddReliableHealthSync`: renombra la clave externa, agrega `device_id`, `sync_version`, `updated_at` y preserva índices/constraints.
4. `20261018210000_AddAIPlatform`: crea conversaciones, mensajes, snapshots de contexto y registros de uso/coste con claves foráneas, borrado en cascada e índices por usuario/fecha.
5. `20261018220000_AddWorkoutCoach`: añade planes/días/ejercicios, completaciones, feedback y recomendaciones con relaciones, checks e índices por usuario y fecha.
6. `20261018230000_AddGoalsAndNutrition`: añade goals/progress, perfil nutricional, logs de comidas, macros objetivo y resúmenes diarios con claves foráneas, checks e índices únicos por usuario/fecha.
7. `20261018230001_AddCoachV3Insights`: añade snapshots de insights, patrones, adherencia, logros y memoria/resultados de recomendaciones; backfills el historial de recomendaciones y feedback de workout existente.
8. `20261020190000_AddAIAnalysisDailyUsage`: añade cuotas diarias atómicas de análisis IA por usuario.
9. `20261024120000_AddProductAnalytics`: crea `product_events` (eventos de producto por usuario, borrado en cascada con el usuario, índices por usuario/fecha y nombre/fecha).

Rutas:

| Método | Ruta | Acceso |
|---|---|---|
| `GET` | `/health` | Público; proceso vivo (compatibilidad) |
| `GET` | `/health/live` | Público; liveness, sin dependencias |
| `GET` | `/health/ready` | Público; readiness, comprueba PostgreSQL (503 si falla) |
| `POST` | `/api/v1/telemetry/events` | JWT; hasta 50 eventos de crash/error/uso/sync por lote (202). Sesiones, pantallas, entrenamiento iniciado y notificación abierta se guardan en `product_events` |
| `GET` | `/api/v1/engagement/summary?utcOffsetMinutes=` | JWT; racha, logros, resumen semanal, adherencia (4 semanas) y recordatorios del día |
| `GET` | `/api/v1/analytics/product?from=&to=` | Cabecera `X-Analytics-Key` = `Analytics__AdminApiKey` (≥ 32 caracteres; sin clave configurada responde 404). DAU/WAU, sesiones, acciones clave, retención D1/D7/D30, adherencia, riesgo de abandono e insights. Ver `docs/PRODUCT_ANALYTICS.md` |
| `POST` | `/api/v1/auth/apple` | Público; requiere identity token y nonce |
| `POST` | `/api/v1/auth/refresh` | Público; rota el refresh token |
| `POST` | `/api/v1/auth/revoke` | Público; requiere el refresh token |
| `POST` | `/api/v1/auth/logout` | JWT; revoca la sesión actual |
| `GET`, `PUT` | `/api/v1/profile/me` | JWT |
| `GET` | `/api/v1/health-metrics` | JWT |
| `GET` | `/api/v1/health-metrics/latest` | JWT; última muestra por tipo |
| `GET` | `/api/v1/health-summary` | JWT; resumen del rango pedido |
| `POST` | `/api/v1/health-metrics/batch` | JWT; hasta 500 muestras/tombstones |
| `POST` | `/api/v1/health-metrics/sync` | JWT; alias compatible para sincronización |
| `POST` | `/api/v1/health-metrics` | JWT; métrica individual |
| `POST` | `/api/v1/ai/chat` | JWT; guarda mensaje/contexto, llama al gateway OpenAI y registra tokens/coste |
| `POST` | `/api/v1/ai/coach` | JWT; pregunta del Coach basada en perfil, objetivo, contexto de salud y conversación |
| `POST` | `/api/v1/ai/activity-analysis` | JWT; análisis estructurado de actividad con contexto agregado, persistencia de conversación y cuota diaria |
| `GET` | `/api/v1/ai/conversations` | JWT; lista conversaciones propias (`limit` 1–100) |
| `GET` | `/api/v1/ai/conversations/{id}` | JWT; detalle de una conversación propia |
| `DELETE` | `/api/v1/ai/conversations/{id}` | JWT; elimina conversación, mensajes, snapshots y tokens asociados |
| `POST` | `/api/v1/coach/workout-plan` | JWT; genera plan de 1–7 días (`goal`: `fat_loss`, `muscle_gain`, `maintenance`, `cardiovascular_endurance`, `running`, `general_health`) |
| `GET` | `/api/v1/coach/workout-plan` | JWT; último plan propio |
| `GET` | `/api/v1/coach/workout-history` | JWT; completaciones y feedback propios |
| `POST` | `/api/v1/coach/workout-feedback` | JWT; registra completado/no completado y dificultad |
| `GET` | `/api/v1/coach/recommendations` | JWT; recomendaciones propias recientes |
| `GET` | `/api/v1/coach/insights` | JWT; patrones personales, tendencias, adherencia y efectividad de recomendaciones |
| `GET` | `/api/v1/coach/progress-analysis` | JWT; cambios recientes de peso, sueño, pasos, frecuencia cardiaca y calorías |
| `GET` | `/api/v1/coach/adherence` | JWT; sesiones planificadas/completadas y tasa de adherencia de 28 días |
| `GET` | `/api/v1/coach/recommendation-history` | JWT; recomendaciones propias con el resultado o valoración más reciente |
| `POST` | `/api/v1/coach/recommendation-feedback` | JWT; registra una valoración de 1 a 5 para una recomendación propia |
| `POST` | `/api/v1/goals` | JWT; crea un objetivo y pausa el objetivo activo anterior |
| `GET` | `/api/v1/goals` | JWT; lista objetivos propios |
| `POST` | `/api/v1/nutrition/profile` | JWT; configura sexo para Mifflin–St Jeor y nivel de actividad; calcula BMR, TDEE y macros |
| `GET` | `/api/v1/nutrition/targets` | JWT; objetivos nutricionales actuales |
| `POST` | `/api/v1/nutrition/log` | JWT; registra desayuno/comida/cena/snack y actualiza resumen diario |
| `GET` | `/api/v1/nutrition/summary?date=YYYY-MM-DD` | JWT; consumos y adherencia calórica del día |
| `GET` | `/api/v1/progress` | JWT; evolución de peso, calorías, pasos y sueño y progreso del objetivo |

Cada lote incluye `userId`; cada muestra lleva `metric`, `value`, `unit`, `observedAt`, `source`, `externalId` y `deviceId`. La API valida el propietario contra `sub`, las unidades permitidas y que la fecha no supere cinco minutos en el futuro.

### Configuración backend

Configura variables de entorno/secrets:

- `ConnectionStrings__Postgres`
- `Jwt__SigningKeyBase64`: Base64 de al menos 32 bytes aleatorios.
- `Jwt__Issuer`, `Jwt__Audience`
- `Jwt__AccessTokenMinutes` (default 15), `Jwt__RefreshTokenDays` (default 30)
- `Apple__Audience`: Bundle ID de la app iOS configurada en Apple Developer.
- `AI__Enabled` (default `false`), `AI__ApiKey`, `AI__Model` (default `gpt-4o-mini`), `AI__InputCostPerMillionTokens` y `AI__OutputCostPerMillionTokens`. Los precios se configuran en USD según la tarifa publicada por OpenAI para el modelo seleccionado y se usan para estimaciones, no facturación.

- `RateLimiting__RequestsPerMinute` (default 300 por usuario/IP), `RateLimiting__AuthRequestsPerMinute` (default 30 por IP en `/auth/*`), `RateLimiting__TelemetryRequestsPerMinute` (default 20). Las respuestas 429 incluyen `Retry-After: 60`; `/health*` no se limita.
- `ForwardedHeaders__Enabled=true` solo detrás de un balanceador de confianza que sobrescriba `X-Forwarded-For` (necesario para que el límite por IP no agrupe a todos los usuarios).
- `RateLimiting__SupportRequestsPerMinute` y `RateLimiting__AccountRequestsPerMinute` (default 5): reportes de soporte y exportación/borrado de cuenta.
- `Apple__TeamId`, `Apple__KeyId`, `Apple__PrivateKey` (PEM de la clave de Sign in with Apple, `\n` admitido): necesarios para revocar el acceso de Apple al borrar la cuenta (guideline 5.1.1(v)). Sin ellos la cuenta se borra igual, pero el token de Apple no se revoca (se registra un aviso y la métrica `account_apple_revocation_problems_total`).
- `Release__Environment`, `Release__MinimumSupportedBuild`, `Release__SupportEmail`, `Release__PrivacyPolicyUrl`, `Release__TermsUrl` (HTTPS) y `FeatureFlags__<flag>=false` para apagar pantallas existentes sin publicar build. Se sirven en `GET /api/v1/app-config` (anónimo). Un flag desconocido o una URL no HTTPS hace fallar el arranque.
- `RequestTimeouts__DefaultSeconds` (30), `RequestTimeouts__AISeconds` (90), `RequestTimeouts__LongSeconds` (120, exportación): una petición que los supera se cancela y responde 504.
- `AI__CircuitBreaker__FailureThreshold` (5) y `AI__CircuitBreaker__BreakSeconds` (30): tras N fallos seguidos del proveedor de IA, las peticiones responden 503 al instante durante el periodo de corte; `/health/ready` muestra `ai_provider` como Degraded. Ver [docs/RELEASE_CANDIDATE.md](docs/RELEASE_CANDIDATE.md).
- `Beta__MaxUsers` (0 = sin tope), `Beta__KpiPublisherEnabled`, `Beta__KpiIntervalMinutes`: beta por etapas (10 → 25 → 50) y KPIs/alertas automáticos. Ver [docs/BETA_PROGRAM.md](docs/BETA_PROGRAM.md).
- `OpenTelemetry__OtlpEndpoint` (o `OTEL_EXPORTER_OTLP_ENDPOINT`), `OpenTelemetry__Protocol` (`grpc`/`http`), `OpenTelemetry__TraceSampleRatio`: exporta métricas, trazas y logs a un colector OTLP. Desactivado si no hay endpoint.
- Logs: JSON en consola con `CorrelationId` (cabecera `X-Correlation-ID`, también devuelta en cada respuesta); `appsettings.Development.json` usa formato simple. EF Core reintenta fallos transitorios de PostgreSQL (3 intentos) y el cliente de IA reintenta una vez ante 429/5xx o fallo de conexión.

No se incluye identidad local anónima. La API falla al arrancar si faltan la clave JWT o Apple audience. Si se habilita IA, también exige API key, modelo y precios positivos; configure `AI__ApiKey` mediante Azure Key Vault/secret references o Secret Manager, nunca en el repositorio ni en iOS. Para desarrollo local: `dotnet user-secrets set --project backend\src\AIFitnessCoach.Api\AIFitnessCoach.Api.csproj "AI:ApiKey" "<secret>"`; mantenga `AI:Enabled=false` hasta completar el resto de configuración.

Desarrollo Docker, PowerShell:

```powershell
$env:POSTGRES_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:JWT_SIGNING_KEY_BASE64 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:APPLE_AUDIENCE = "com.example.aifitnesscoach"
docker compose up --build
```

La API solo se expone en loopback (`127.0.0.1:8080`). Compose usa `Development` para aplicar migraciones al inicio; despliegues reales deben aplicar migraciones en un job controlado. Sustituye el Bundle ID de ejemplo por el real.

### Tests backend

```powershell
dotnet test backend\tests\AIFitnessCoach.Api.Tests\AIFitnessCoach.Api.Tests.csproj --configuration Release
```

Incluye pruebas de autenticación, firma/validación JWT, casos de uso, repositorios, idempotencia, Mifflin–St Jeor/TDEE/macros, objetivos/progreso nutricional, prompt/context builder, uso de tokens, persistencia de conversaciones y planes de entrenamiento, feedback, reglas de adaptación y recomendaciones. EF InMemory no sustituye integración contra PostgreSQL.

En iOS, el dashboard abre Coach, Rutinas y el menú Bienestar (Objetivos, Nutrición y Progreso). Nutrición permite configurar el nivel de actividad, ver objetivos estimados y registrar comidas; Progreso grafica peso, calorías, pasos y sueño. Requiere iniciar sesión; la app se comunica únicamente con el backend y no incluye credenciales OpenAI.

## iOS

Requiere macOS, Xcode 15+, iOS 17+, XcodeGen, firma/capability de HealthKit y Sign in with Apple en el App ID.

Desde `ios/`:

```sh
brew install xcodegen
xcodegen generate
xcodebuild test -project AIFitnessCoach.xcodeproj -scheme AIFitnessCoach -destination 'platform=iOS Simulator,name=iPhone 16' API_BASE_URL=https://api.example.com
```

Configuraciones (`ios/Config/*.xcconfig`): **Debug** (desarrollo, `http://localhost:8080`), **Beta** (TestFlight, esquema `AIFitnessCoach Beta`) y **Release** (App Store). El Bundle ID se define una vez en `APP_BUNDLE_ID` (`Shared.xcconfig`); el identificador de la tarea en segundo plano se deriva de él. Los builds Beta/Release requieren `API_BASE_URL` HTTPS. HealthKit con muestras reales requiere un dispositivo físico. iOS no se puede compilar desde Windows.

Build y subida a TestFlight: `ios/scripts/testflight.sh` (o el workflow `TestFlight Beta`). Ver [docs/TESTFLIGHT.md](docs/TESTFLIGHT.md).

**Instalarla en tu propio iPhone (uso personal, desde Windows):** [docs/INSTALACION_PERSONAL.md](docs/INSTALACION_PERSONAL.md). **Envío a App Store** (checklists App Store, RGPD, HealthKit, StoreKit, riesgos y bloqueadores): [docs/APP_STORE_SUBMISSION.md](docs/APP_STORE_SUBMISSION.md).

La sincronización HealthKit empieza después de iniciar sesión y completar la solicitud de autorización; tras el consentimiento, los observadores y trabajos pendientes se reanudan al reiniciar la app. Apple no permite a la app confirmar si un permiso de lectura fue denegado; las lecturas vacías también pueden significar que no existen muestras.

## Privacidad y release

- Estado de App Store readiness, Privacy Labels y notas para App Review: [docs/APP_STORE_READINESS.md](docs/APP_STORE_READINESS.md). La app incluye `PrivacyInfo.xcprivacy`, exportación de datos (`GET /api/v1/account/export`), borrado de cuenta con revocación de Apple (`POST /api/v1/account/delete`) y Feedback Center (`POST /api/v1/support/reports`), todo desde **Ajustes**.
- No registrar tokens, identity tokens, correo ni contenido HealthKit.
- Guardar `Jwt:SigningKeyBase64` y credenciales de PostgreSQL en Key Vault/secret manager; nunca en `appsettings.json`, plist o repositorio.
- Configurar TLS, backups cifrados, alertas, límites de request/rate, protección contra abuso de login y pruebas reales en PostgreSQL.
- Antes de habilitar IA, documentar el consentimiento y la transferencia de datos de salud al proveedor OpenAI, evaluar la base legal/retención y completar el DPA/configuración de datos del proveedor. El endpoint guarda snapshots con datos de perfil y salud; borrar la conversación elimina sus snapshots y uso asociado.
- El cierre de sesión detiene la entrega HealthKit y elimina muestras pendientes y checkpoints del usuario. El consentimiento de IA puede retirarse desde Ajustes.
