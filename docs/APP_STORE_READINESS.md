# App Store readiness — AI Fitness Coach

Fecha: 2026-10-27. Alcance: preparar la Beta privada (TestFlight) y la futura publicación. Sin funcionalidades de producto nuevas, sin multiagentes, sin RAG y sin módulos de IA nuevos.

**Estado de verificación**
- Backend: compilado y probado aquí, **191/191 tests** (14 nuevos en `ReleaseReadinessTests`).
- iOS: revisado a mano, **no compilado** (Windows, sin Xcode). Primer paso obligatorio en macOS: `xcodegen generate` + `xcodebuild test` (incluye 14 tests nuevos en `ReleaseReadinessTests.swift`).

## 1. Resumen

| Requisito | Estado | Dónde |
|---|---|---|
| Permisos de HealthKit | ✅ Verificado y mejorado | Info.plist, `HealthKitManager` |
| Privacy Labels | ✅ Definidas (rellenar en App Store Connect) | Sección 3 |
| Privacy Manifest | ✅ Añadido | `ios/AIFitnessCoach/Resources/PrivacyInfo.xcprivacy` |
| Consentimientos | ✅ Verificado; retirada de IA añadida en Ajustes | Sección 5 |
| Exportación de datos | ✅ Implementado | `GET /api/v1/account/export`, Ajustes › Exportar mis datos |
| Eliminación de cuenta | ✅ Implementado, con revocación de Sign in with Apple | `POST /api/v1/account/delete`, Ajustes › Eliminar cuenta |
| Política de privacidad / términos | ⚠️ Falta publicar la URL | `Release__PrivacyPolicyUrl`, `Release__TermsUrl` |

## 2. Permisos de HealthKit

- **Solo lectura**: pasos, distancia, energía activa y basal, frecuencia cardiaca, peso, sueño y entrenamientos. `requestAuthorization(toShare: [])`: la app no escribe en Salud.
- **Textos de permiso**: `NSHealthShareUsageDescription` ahora dice qué datos se leen, para qué y que no se usan para publicidad (App Review rechaza descripciones genéricas). `NSHealthUpdateUsageDescription` se mantiene y explica que no se escribe nada.
- **Entitlements**: `com.apple.developer.healthkit` y `healthkit.background-delivery`. No se piden Clinical Records ni `healthkit.access`.
- **Background**: `UIBackgroundModes = fetch` y `BGTaskSchedulerPermittedIdentifiers = $(PRODUCT_BUNDLE_IDENTIFIER).sync`. El identificador ya no está fijo en `com.example`.
- **Guideline 5.1.3** (datos de salud): no se usan para publicidad ni se venden; no se guardan en iCloud; se envían solo al backend propio y, si el usuario consiente, al proveedor de IA. Hay que declararlo en la política de privacidad.
- Apple no informa a la app de qué permisos de lectura se denegaron. El evento `healthkit_authorization` registra solo que la hoja se respondió (`completed`/`failed`).

## 3. Privacy Labels (App Store Connect › App Privacy)

Respuestas que coinciden con `PrivacyInfo.xcprivacy`. **Tracking: No.** No hay SDK de terceros, publicidad ni IDFA, así que no hace falta App Tracking Transparency.

| Tipo de dato (App Store Connect) | Qué es en la app | Vinculado al usuario | Tracking | Finalidad |
|---|---|---|---|---|
| Salud y forma física › Salud | Muestras de Apple Health sincronizadas | Sí | No | Funcionalidad de la app |
| Salud y forma física › Forma física | Planes, feedback de entrenamientos, objetivos, comidas | Sí | No | Funcionalidad de la app |
| Información de contacto › Nombre | Nombre de Sign in with Apple / perfil | Sí | No | Funcionalidad de la app |
| Información de contacto › Correo electrónico | Correo de Apple (puede ser de retransmisión) y correo opcional en soporte | Sí | No | Funcionalidad de la app |
| Identificadores › ID de usuario | ID interno y sujeto de Apple | Sí | No | Funcionalidad, Analítica |
| Contenido del usuario › Otro contenido | Preguntas al Coach y al Análisis | Sí | No | Funcionalidad de la app |
| Contenido del usuario › Atención al cliente | Feedback Center, reportes de errores, soporte | Sí | No | Funcionalidad de la app |
| Uso › Interacción con el producto | Pantallas abiertas, sesiones, acciones | Sí | No | Analítica, Personalización |
| Diagnóstico › Datos de fallos | Resumen de MetricKit (tipo de excepción, señal) | Sí | No | Funcionalidad de la app |
| Diagnóstico › Datos de rendimiento | Bloqueos (hangs), duración aproximada de las peticiones de IA | Sí | No | Funcionalidad de la app |
| Diagnóstico › Otros datos de diagnóstico | Códigos de error, estado de la sync, versión de app/iOS y modelo | Sí | No | Funcionalidad de la app |

Si en el futuro se añade un SDK (Sentry, Firebase…), hay que actualizar a la vez el manifiesto, esta tabla y App Store Connect, y añadir el manifiesto del SDK.

**Required Reason APIs**: el código de la app no usa `UserDefaults`, marcas de tiempo de ficheros, espacio en disco, tiempo de arranque ni APIs de teclado (`NSPrivacyAccessedAPITypes` vacío). La configuración remota se guarda en un fichero, no en `UserDefaults`, para no cambiar esto. Revisarlo en cada release: `grep -rn "UserDefaults\|systemUptime\|creationDate\|volumeAvailableCapacity" ios/`.

## 4. Privacy Manifest

`ios/AIFitnessCoach/Resources/PrivacyInfo.xcprivacy`: `NSPrivacyTracking=false`, sin dominios de tracking, con los 11 tipos de datos de la tabla. XcodeGen lo añade a *Copy Bundle Resources* por estar dentro de `AIFitnessCoach/`; el CI y `testflight.sh` comprueban que aparece en el proyecto.

## 5. Consentimientos

| Consentimiento | Cómo se pide | Cómo se retira |
|---|---|---|
| Lectura de Salud | Hoja del sistema al tocar «Conectar Apple Health» | Ajustes del iPhone › Salud › Acceso a datos (Ajustes de la app enlaza a Ajustes del sistema y lo explica) |
| Uso de IA con datos de salud | Pantalla de consentimiento antes del primer uso del Coach o del Análisis (guardado en `ai_consents` en el servidor) | En el Coach/Análisis o en **Ajustes › Retirar consentimiento de IA** (nuevo) |
| Notificaciones locales | Permiso del sistema al activar recordatorios | Ajustes del iPhone |
| Diagnóstico en reportes | Interruptor en cada formulario (activado por defecto solo en «Informar de un error») | Desactivar el interruptor |

La analítica de producto es de primera parte, no se cruza con otras apps ni empresas y no requiere ATT. Debe figurar en la política de privacidad.

## 6. Exportación de datos (portabilidad)

- `GET /api/v1/account/export` (autenticado, 5 peticiones/min). Devuelve un JSON `aifitnesscoach-export-v1` con todas las tablas del usuario: perfil, identidades, sesiones, muestras de salud, conversaciones y mensajes de IA, consentimiento, planes, entrenamientos, feedback, objetivos, nutrición, insights, eventos de producto y reportes de soporte.
- Excluye `refresh_tokens` (hashes de secretos de sesión).
- Las tablas se descubren a partir del modelo de EF, no de una lista escrita a mano: una tabla nueva con `UserId` (o hija de una tabla del usuario) entra sola. El test `EveryTableIsCoveredByExportAndDeletion` falla si alguna tabla quedara fuera.
- iOS: **Ajustes › Exportar mis datos** descarga el fichero en una carpeta temporal con protección completa y lo comparte con la hoja del sistema. La carpeta se vacía al cerrar sesión o borrar la cuenta.

## 7. Eliminación de cuenta (Guideline 5.1.1(v))

1. **Ajustes › Eliminar cuenta** → confirmación con el detalle de lo que se borra → reautenticación con **Sign in with Apple** (confirma que es el titular y obtiene un `authorizationCode` nuevo).
2. `POST /api/v1/account/delete` con ese código:
   - El backend canjea el código en `appleid.apple.com/auth/token` y revoca el token en `/auth/revoke` (client secret ES256 firmado con `Apple:TeamId`, `Apple:KeyId`, `Apple:PrivateKey`).
   - Borra el usuario con una sola sentencia; PostgreSQL elimina en cascada todas sus tablas (todas tienen `ON DELETE CASCADE` hacia `users`, directa o indirectamente).
   - Si la revocación de Apple falla o no está configurada, **la cuenta se borra igualmente** (la respuesta indica `appleRevocation`, se registra un aviso y la métrica `account_apple_revocation_problems_total`).
3. La app limpia todo lo local: sesión y Keychain, observadores y cola de HealthKit, cachés, telemetría, conversaciones y exportaciones. Vuelve a la pantalla de inicio de sesión.
4. Si se pierde la respuesta y se reintenta, un 404 se trata como «ya borrada».
5. Volver a entrar con el mismo Apple ID crea una cuenta nueva y vacía (test `DeletingTheAccountRemovesEveryRowAndEndsTheSession`).

No se borra: los datos de Apple Health del iPhone (son del usuario, no de la app) ni los logs que ya estén en el agregador (definir su retención en la política, p. ej. 30 días).

`DELETE /api/v1/account` (sin cuerpo) también funciona, sin revocación de Apple.

## 8. Notas para App Review (copiar en App Store Connect)

> AI Fitness Coach reads (never writes) Apple Health data —steps, distance, energy, heart rate, weight, sleep and workouts— to show daily progress and personalise workout plans. Health data is never used for advertising and is not stored in iCloud.
>
> Sign in: the app only supports Sign in with Apple; please use your own Apple ID. Apple Health features require a physical device with Health data.
>
> AI features (Coach, Analysis, workout plans) ask for explicit consent before any health data is sent to our AI provider. Consent can be withdrawn in Settings. The AI screens state that they do not diagnose or replace medical advice.
>
> Account deletion: Settings (gear icon on the dashboard) › Delete account. It deletes all server data and revokes the Sign in with Apple token. Data export: Settings › Export my data.

## 9. Pendiente fuera del código (bloquea la publicación, no la Beta interna)

- [ ] Publicar política de privacidad y términos (HTTPS) y configurar `Release__PrivacyPolicyUrl` / `Release__TermsUrl`. La política debe cubrir: datos de Salud, envío al proveedor de IA (DPA), analítica de primera parte, diagnóstico, retención de logs, exportación y borrado.
- [ ] Crear la clave de Sign in with Apple (Certificates, IDs & Profiles › Keys) y configurar `Apple__TeamId`, `Apple__KeyId`, `Apple__PrivateKey` en el vault.
- [ ] Rellenar App Privacy en App Store Connect con la tabla de la sección 3.
- [ ] Bundle ID real en `ios/Config/Shared.xcconfig` (`APP_BUNDLE_ID`) y `Apple__Audience` igual a ese Bundle ID.
- [ ] Clasificación por edades: el cuestionario pregunta por información médica; responder «Infrequent/Mild Medical/Treatment Information».
- [ ] Capturas, descripción y URL de soporte (`Release__SupportEmail` o web).
- [ ] Probar en un dispositivo físico: borrado de cuenta con revocación de Apple, exportación y compartir el fichero.
