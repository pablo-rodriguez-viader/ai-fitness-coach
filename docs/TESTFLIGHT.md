# Beta privada en TestFlight — release candidate

Cómo generar, distribuir y vigilar la versión candidata **0.9.0** para la Beta privada.

## 1. Configuraciones de build

| Configuración | Esquema | `APP_ENVIRONMENT` | Backend | Uso |
|---|---|---|---|---|
| Debug | `AIFitnessCoach` | `development` | `http://localhost:8080` | Simulador y desarrollo |
| **Beta** | `AIFitnessCoach Beta` | `beta` | `API_BASE_URL` HTTPS de beta | **TestFlight** |
| Release | `AIFitnessCoach` (archive) | `production` | `API_BASE_URL` HTTPS de producción | App Store |

- Los valores están en `ios/Config/*.xcconfig`. En un xcconfig, `//` empieza un comentario: escribe las URL como `https:/$()/api-beta.tudominio.com`, o pásalas por línea de comandos.
- Beta es un build *release* (optimizado, con dSYM) con la condición de compilación `BETA`. Usa el **mismo Bundle ID** que Release: en TestFlight se prueba la misma app que se publicará.
- El entorno se ve en **Ajustes › Acerca de** y viaja en los reportes de error.

## 2. Requisitos (una sola vez)

1. App ID real con **HealthKit** (+ Background Delivery) y **Sign in with Apple**. Escribe el Bundle ID en `APP_BUNDLE_ID` (`ios/Config/Shared.xcconfig`) y en `Apple__Audience` del backend.
2. App creada en App Store Connect con ese Bundle ID.
3. Clave de API de App Store Connect (rol *App Manager*): `ASC_KEY_ID`, `ASC_ISSUER_ID` y el fichero `.p8`. La firma es automática con esta clave: no hay que instalar certificados ni perfiles.
4. Backend de beta desplegado con HTTPS, migraciones aplicadas (incluida `AddSupportReports`) y la configuración de la sección 5.

## 3. Generar y subir el build

En macOS, desde `ios/`:

```sh
export API_BASE_URL=https://api-beta.tudominio.com
export TEAM_ID=ABCDE12345
export APP_BUNDLE_ID=com.tuempresa.aicoach
export ASC_KEY_ID=XXXXXXXXXX ASC_ISSUER_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
export ASC_KEY_PATH=~/keys/AuthKey_XXXXXXXXXX.p8
./scripts/testflight.sh
```

El script valida la configuración (HTTPS, Bundle ID que no sea `com.example`, claves), genera el proyecto, comprueba que el Privacy Manifest está incluido, ejecuta los tests unitarios, archiva con la configuración **Beta**, exporta y sube a App Store Connect. El número de build es un timestamp UTC (`BUILD_NUMBER` para fijarlo). `UPLOAD=0` genera solo el `.ipa`.

**En CI**: el workflow **TestFlight Beta** (`.github/workflows/testflight.yml`) se lanza a mano o con un tag `beta-*`. Primero pasa los tests del backend. Necesita, en el entorno `testflight` de GitHub:
- Secrets: `APPLE_TEAM_ID`, `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` (contenido del `.p8`).
- Variables: `BETA_API_BASE_URL`, `APP_BUNDLE_ID`.

El CI normal compila además la configuración Beta en cada PR.

Tras el procesado (10-30 min): App Store Connect › TestFlight › grupo de testers internos o externos. Para testers externos, la primera build pasa por **Beta App Review**: usa las notas de `docs/APP_STORE_READINESS.md` §8. El cumplimiento de exportación ya está declarado (`ITSAppUsesNonExemptEncryption = false`).

## 4. Feature flags

Interruptores para **ocultar pantallas que ya existen** si fallan durante la beta, sin publicar otro build. No añaden funcionalidad.

| Flag | Oculta |
|---|---|
| `coach` | Botón Coach y deep link `aifitnesscoach://coach` |
| `activity_analysis` | Análisis IA |
| `workouts` | Rutinas |
| `nutrition` | Nutrición |
| `goals` | Objetivos |
| `progress` | Progreso |
| `engagement` | Racha, Resumen semanal, Logros |
| `feedback_center` | Comentarios, reportes de error y soporte en Ajustes |
| `data_export` | Exportar mis datos |

- Se configuran en el backend: `FeatureFlags__nutrition=false`. Todos están activos por defecto.
- Con `AI__Enabled=false`, `coach`, `activity_analysis` y `workouts` se apagan solos (la app oculta las entradas en vez de mostrar «no disponible»).
- La app lee `GET /api/v1/app-config` al arrancar y al volver a primer plano (como mucho cada 5 min) y guarda la última respuesta. Sin conexión usa la última conocida; si nunca la ha recibido, todo está activo.
- Un deep link o recordatorio a una pantalla apagada no abre nada y registra `screen_unavailable`.
- **El borrado de cuenta no tiene flag**: siempre está disponible (requisito del App Store).
- `Release__MinimumSupportedBuild`: los builds con un número menor muestran un aviso para actualizar.

## 5. Configuración del backend para la beta

| Variable | Valor |
|---|---|
| `Release__Environment` | `beta` |
| `Release__SupportEmail` | Correo de soporte de la beta |
| `Release__PrivacyPolicyUrl`, `Release__TermsUrl` | URLs HTTPS |
| `Analytics__AdminApiKey` | ≥ 32 caracteres. Da acceso al informe de producto y a la bandeja de soporte |
| `Apple__TeamId`, `Apple__KeyId`, `Apple__PrivateKey` | Revocación de Sign in with Apple al borrar la cuenta |
| `OpenTelemetry__OtlpEndpoint` | Colector OTLP (Grafana Cloud, Datadog, Honeycomb…) |
| `AI__Enabled` y el resto de `AI__*` | Según `README.md` |

## 6. Observabilidad

**Crash reporting**: MetricKit entrega en el siguiente arranque los fallos y bloqueos de la sesión anterior → `crash_report` / `hang_report` (tipo de excepción, señal, build) → métrica `client_crashes_total`. Los informes simbolizados completos están en **Xcode › Organizer › Crashes** y en App Store Connect › TestFlight › Crashes (el script sube los dSYM).

**Error tracking**: `error` (zona, tipo, dominio y código), `api_request_errors_total`, logs con `CorrelationId`.

**HealthKit tracking** (nuevo): `healthkit_authorization` (hoja respondida o error), `healthkit_observer_update` (como mucho uno por tipo y hora, indica si llegó en primer o segundo plano: confirma que el *background delivery* funciona), `healthkit_query_failed` (fase `observer`, `background_delivery` o `read`, con dominio/código).

**Sync tracking**: `sync_completed`, `sync_failed`, `sync_dead_lettered`, `sync_background_refresh`; en el servidor `health_sync_*` (lotes, insertadas, duplicadas, rechazadas, errores).

**AI tracking** (nuevo en la app): `ai_request` con `feature` (`coach`, `activity_analysis`, `workout_plan`), `outcome` (`ok`, `offline`, `unavailable`, `failed`, `cancelled`) y `duration_bucket`; `ai_consent_changed`. En el servidor: `ai_requests_total`, `ai_requests_failed_total`, `ai_request_duration`, tokens, `ai_provider_timeouts_total`, `coach_messages_failed_total`, `ai_quota_rejections_total`.

**Cuenta y soporte** (nuevo): `account_exports_total`, `account_deletions_total`, `account_apple_revocation_problems_total`, `support_reports_total{kind}`.

Todo se exporta por OTLP cuando `OpenTelemetry__OtlpEndpoint` está configurado (métricas `AIFitnessCoach.*`, trazas de ASP.NET Core/HttpClient con muestreo del 20 % y logs). Ningún evento lleva valores de salud ni texto del usuario.

**Alertas recomendadas**: `client_crashes_total` > 0 en una build nueva; `ai_requests_failed_total / ai_requests_total` > 10 % en 15 min; `sync_failed` o `healthkit_query_failed` en > 5 % de usuarios activos; `/health/ready` caído; 5xx > 1 %; cualquier `account_apple_revocation_problems_total` con `outcome=Failed`.

## 7. Analytics

Ya implementados; se leen en `GET /api/v1/analytics/product` (cabecera `X-Analytics-Key`). Detalle en `docs/PRODUCT_ANALYTICS.md`.

| Métrica pedida | Campo del informe |
|---|---|
| DAU | `metrics.averageDailyActiveUsers` y `metrics.daily` |
| WAU | `metrics.weeklyActiveUsers` (y `metrics.stickiness` = DAU/WAU) |
| Retención | `retention.classic` / `retention.rolling` (D1, D7, D30) y `retention.cohorts` |
| Entrenamientos completados | `metrics.workoutsCompleted` |
| Consultas al Coach | `metrics.coachQueries` |
| Objetivos completados | `metrics.goalsCompleted` |

## 8. Soporte

En **Ajustes › Ayuda y comentarios**:
- **Enviar comentarios** (Feedback Center): valoración de 1 a 5, tema y mensaje.
- **Informar de un error**: tema, descripción y diagnóstico técnico opcional (versión, dispositivo, conexión, estado de la sync, flags apagados, últimos eventos; nunca valores de salud ni conversaciones).
- **Contactar con soporte**: mensaje y correo opcional para responder; además, enlace `mailto:` a `Release__SupportEmail`.

Se guardan en `support_reports` (máx. 20 por usuario y día; se borran con la cuenta). Bandeja del equipo:

```sh
curl -H "X-Analytics-Key: $ANALYTICS_ADMIN_API_KEY" \
  "https://<api>/api/v1/support/reports?kind=bug&since=2026-11-01T00:00:00Z&limit=100"
```

Los testers también pueden enviar capturas desde TestFlight («Compartir beta»); aparecen en App Store Connect › TestFlight › Feedback.

## 9. Instrucciones para testers (enviar con la invitación)

1. Instala TestFlight y acepta la invitación. Entra con Sign in with Apple.
2. Conecta Apple Health y permite todos los tipos de datos.
3. Usa la app a diario: entrenamientos, comidas, Coach.
4. ¿Algo falla? **Ajustes (⚙︎) › Informar de un error** con el diagnóstico activado, o haz una captura y compártela desde TestFlight.
5. Puedes exportar o borrar tus datos en cualquier momento desde Ajustes.

## 10. Checklist de la release candidate

**Antes de subir**
- [ ] `xcodegen generate` + `xcodebuild test` en verde en macOS (los cambios iOS no se han compilado en Windows)
- [ ] `dotnet test` en verde (214/214 en este entorno). Ver también `RELEASE_CANDIDATE.md`
- [ ] Bundle ID real, `API_BASE_URL` de beta HTTPS y migración `AddSupportReports` aplicada
- [ ] Backend de beta con `Release__*`, `Analytics__AdminApiKey`, `Apple__TeamId/KeyId/PrivateKey` y `OpenTelemetry__OtlpEndpoint`

**En un iPhone físico con el build de TestFlight**
- [ ] Login con Apple → perfil → conectar Salud → datos en el Dashboard
- [ ] Bloquear el iPhone 1 h y comprobar `healthkit_observer_update` con `app_state=background`
- [ ] Coach, Análisis y plan de entrenamiento → eventos `ai_request` con `outcome=ok`
- [ ] Apagar `FeatureFlags__nutrition`, volver a abrir la app y comprobar que Nutrición desaparece; volver a activarlo
- [ ] Enviar comentarios, un error (con diagnóstico) y una consulta de soporte → aparecen en la bandeja
- [ ] Exportar mis datos → el JSON se comparte y contiene el perfil y las muestras
- [ ] Retirar el consentimiento de IA en Ajustes → el Coach lo vuelve a pedir
- [ ] Eliminar cuenta → reautenticación con Apple → vuelve al login, `appleRevocation=Revoked` en los logs; volver a entrar crea una cuenta vacía
- [ ] Forzar un cierre (p. ej. matar la app durante una sync) y comprobar al día siguiente `crash_report`/`hang_report` o el Organizer
