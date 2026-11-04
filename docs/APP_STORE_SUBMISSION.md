# Envío a App Store — Release Candidate definitiva

Fecha: 2026-10-29. Alcance: dejar la app lista para revisión sin funcionalidades, módulos de IA, multiagentes, RAG ni capacidades de producto nuevas.

> **Uso actual: personal, no comercial.** Para usarla tú mismo no hace falta enviarla a revisión: basta con TestFlight (`INSTALACION_PERSONAL.md`). Este documento queda listo para el día en que quieras publicarla.

**Estado de verificación**
- Backend: compilado y probado aquí, **223/223 tests** en Release (9 nuevos en `GdprTests`).
- iOS: revisado a mano, **no compilado** (Windows, sin Xcode). Hay 10 tests nuevos en `GdprConsentTests.swift`. Se compila con el workflow de GitHub Actions o en un Mac.

## Cambios hechos en esta RC

| Área | Cambio |
|---|---|
| Permisos | Quitado **distancia caminando/corriendo** de HealthKit (se pedía y nunca se leía). Quitado `.badge` de notificaciones (nunca se usa). Quitada `NSHealthUpdateUsageDescription` (la app no escribe en Salud) |
| HealthKit | Texto de permiso con los tipos exactos y su uso. Explicación visible **antes** de la hoja del sistema |
| RGPD | Consentimiento explícito para **datos de salud** (art. 9.2.a) y para **analítica y diagnóstico**, guardados en el servidor con la versión del texto (`user_consents`). El servidor rechaza subidas de Salud sin consentimiento (403 `health_consent_required`). La telemetría no se envía hasta que el usuario decide y se descarta si dice que no |
| RGPD | Borrado por categoría sin cerrar la cuenta: **datos de Salud** (además desconecta la sync) y **datos del Coach/IA** (además retira el consentimiento). Ambos desde Ajustes › Consentimientos y datos |
| RGPD | **Retención automática** diaria: eventos 13 meses, reportes de soporte 2 años, contexto enviado a la IA 90 días, sesiones terminadas 30 días |
| IA | El texto de consentimiento nombra a **OpenAI** como proveedor externo y dice qué se envía (guideline 5.1.2(i)). El consentimiento pasa a la **versión 2**: los anteriores dejan de valer y se vuelve a preguntar. El nombre del usuario ya no se envía a la IA en ningún flujo |
| Accesibilidad | Colores de estado (error, aviso, éxito) con contraste ≥ 4,5:1 en claro y oscuro; banner offline legible (antes ~2,2:1) |
| Localización | Catálogo de cadenas (`Localizable.xcstrings`, español base) y extracción automática activadas. El inglés queda preparado en `ios/Localization/en.lproj`, fuera del target (ver riesgo R4) |

## 1. Checklist App Store

**Binario y configuración**
- [x] Privacy Manifest (`PrivacyInfo.xcprivacy`): sin tracking, 11 tipos de datos y ninguna Required Reason API
- [x] `ITSAppUsesNonExemptEncryption = false` (solo HTTPS)
- [x] Configuraciones Debug / Beta / Release; `API_BASE_URL` HTTPS obligatorio en Beta y Release
- [x] Bundle ID en un solo sitio (`APP_BUNDLE_ID`); tarea en segundo plano derivada de él
- [x] dSYM subidos (crashes simbolizados en Xcode Organizer y TestFlight)
- [ ] Bundle ID real y App ID con HealthKit (+ background delivery) y Sign in with Apple
- [ ] Icono de la app (1024×1024) y pantalla de lanzamiento (`UILaunchScreen` vacía hoy)
- [ ] Primera compilación con Xcode y tests de iOS en verde

**Permisos** — todos los que se piden, para qué y en qué pantalla:

| Permiso | Qué se pide | Función que lo usa | Pantalla |
|---|---|---|---|
| HealthKit, lectura | Pasos | Resumen diario, tendencias, objetivos, contexto de IA | Dashboard (Conectar Apple Health), Progreso, Coach |
| HealthKit, lectura | Energía activa | Calorías del día, TDEE | Dashboard, Nutrición |
| HealthKit, lectura | Energía en reposo | Calorías totales (activa + reposo) | Dashboard, Nutrición |
| HealthKit, lectura | Frecuencia cardiaca | Pulso medio, contexto de entrenamiento | Dashboard, Rutinas |
| HealthKit, lectura | Peso | Peso actual, BMR/TDEE, progreso del objetivo | Dashboard, Nutrición, Progreso, Objetivos |
| HealthKit, lectura | Sueño | Horas de sueño, recuperación | Dashboard, Progreso, Rutinas |
| HealthKit, lectura | Entrenamientos | Historial, recuperación y adaptación del plan | Rutinas, Coach |
| HealthKit, background delivery | Avisos de datos nuevos | Sincronización en segundo plano | — (segundo plano) |
| Notificaciones | Alertas y sonido | Recordatorios locales (entrenamiento 18:00, objetivo 20:00, resumen del domingo) | Dashboard › Activar recordatorios |
| Sign in with Apple | Nombre y correo (opcionales) | Cuenta | Login |
| Background fetch | `BGAppRefreshTask` | Subir la cola pendiente de Salud | — (segundo plano) |

Sin cámara, micrófono, ubicación, fotos, contactos, Bluetooth, tracking (ATT) ni escritura en Salud.

**App Store Connect**
- [ ] App Privacy rellenado con la tabla de `APP_STORE_READINESS.md` §3
- [ ] URL de política de privacidad (obligatoria con HealthKit) y URL de soporte
- [ ] Clasificación por edades: «Información médica o de tratamiento: poco frecuente/leve»
- [ ] Capturas de iPhone 6,9" y 6,5", descripción y palabras clave
- [ ] Notas para revisión: `APP_STORE_READINESS.md` §8 (actualizar con «la IA usa OpenAI»)
- [ ] Categoría: Salud y forma física

**Guidelines revisadas**

| Guideline | Estado |
|---|---|
| 1.4.1 (seguridad física y médica) | ✅ Avisos de «no diagnostica» en Coach, Análisis, Nutrición y Dashboard; el backend valida los planes antes de aceptarlos |
| 2.1 (completitud) | ⚠️ La revisión necesita un backend de producción en marcha y la IA habilitada |
| 2.3 (metadatos exactos) | ✅ Solo español mientras el inglés no esté completo |
| 4.8 (opciones de login) | ✅ Solo Sign in with Apple |
| 5.1.1 (datos, consentimiento, borrado de cuenta, exportación) | ✅ |
| 5.1.1(v) (revocar Sign in with Apple al borrar la cuenta) | ✅ Implementado; ⚠️ requiere la clave de Apple configurada |
| 5.1.2(i) (IA de terceros) | ✅ OpenAI nombrado; consentimiento explícito v2 |
| 5.1.3 (salud) | ✅ Sin publicidad ni venta de datos, sin iCloud, consentimiento explícito |
| 3.1 (compras) | ✅ No aplica: no hay compras (ver §4) |

## 2. Checklist RGPD

| Requisito | Estado | Cómo |
|---|---|---|
| Consentimiento explícito, salud (art. 9.2.a) | ✅ Nuevo | Hoja antes de conectar Salud; registro con versión del texto en `user_consents`; el servidor rechaza subidas sin consentimiento |
| Consentimiento explícito, IA | ✅ v2 | Por pantalla (Coach y Análisis), nombra a OpenAI y qué se envía |
| Consentimiento, analítica y diagnóstico | ✅ Nuevo | Pregunta única en el Dashboard e interruptor en Ajustes; sin decisión no se envía nada |
| Prueba del consentimiento (art. 7.1) | ✅ | Decisión, versión del texto y fecha en el servidor; se exportan con la cuenta |
| Retirada del consentimiento | ✅ | Ajustes (analítica, Salud, IA) y botón de privacidad en el Coach |
| Acceso y portabilidad (art. 15 y 20) | ✅ | Ajustes › Exportar mis datos (JSON con todas las tablas) |
| Supresión (art. 17) | ✅ | Cuenta completa, o solo Salud o solo IA |
| Limitación del plazo de conservación (art. 5.1.e) | ✅ Nuevo | Purga diaria automática (`Retention__*`) |
| Minimización (art. 5.1.c) | ✅ | Permisos mínimos; la IA recibe resúmenes, nunca nombre, correo ni muestras crudas |
| Encargados del tratamiento (art. 28) | ⚠️ | DPA con OpenAI y con el hosting: pendiente fuera del código |
| Transferencias internacionales | ⚠️ | OpenAI (EE. UU.): documentar la base (Data Privacy Framework o cláusulas contractuales tipo) en la política |
| Registro de actividades / EIPD (art. 30 y 35) | ⚠️ | Datos de salud a escala → la EIPD probablemente es obligatoria antes del lanzamiento comercial |
| Política de privacidad | ❌ | Falta redactarla y publicarla |

## 3. Checklist HealthKit (revisión de Apple)

- [x] Solo lectura y solo los 7 tipos que se usan (tabla del §1)
- [x] `NSHealthShareUsageDescription` concreta: qué se lee, para qué y que no se usa para publicidad
- [x] Sin `NSHealthUpdateUsageDescription` (no se escribe)
- [x] Explicación en la app antes de la hoja de permisos y enlace para cambiarlos (Ajustes)
- [x] La app funciona si se deniega el permiso (estado «sin datos visibles», no un error)
- [x] Datos de Salud no usados para publicidad, no vendidos, no en iCloud
- [x] Datos de Salud solo se comparten con terceros (OpenAI) con consentimiento explícito aparte
- [x] Desconectar y borrar los datos de Salud del servidor sin cerrar la cuenta
- [x] Entitlements: `healthkit` y `healthkit.background-delivery`; sin Clinical Records
- [x] Política de privacidad obligatoria → pendiente (bloqueador B2)
- [ ] Probar en un iPhone físico: denegar todo, conceder parcialmente, revocar en Ajustes de iOS

## 4. Checklist StoreKit

| Punto | Estado |
|---|---|
| Restaurar compras | **No aplica**: no hay compras. La guideline 3.1.1 solo exige «Restaurar» cuando existen productos restaurables |
| Entitlements | No hay entitlement de In-App Purchase ni código StoreKit |
| Suscripciones | **No existen**. La app es gratuita; no hay pantallas «premium» ni contenido bloqueado |
| App Store Connect | No crear productos de compra; precio «Gratis» |
| Cuando se monetice | Sería una funcionalidad nueva (fuera del alcance actual): StoreKit 2, `Transaction.currentEntitlements`, botón «Restaurar compras», validación en el servidor (App Store Server API y notificaciones V2), página de suscripción con precio, duración y renovación, y enlaces a términos y privacidad (guideline 3.1.2) |

## 5. Riesgos restantes

| # | Riesgo | Impacto | Mitigación |
|---|---|---|---|
| R1 | iOS sin compilar desde estos cambios | Errores de compilación en el primer build | Workflow de GitHub Actions o Xcode; corregir antes de subir |
| R2 | Coste de IA con app gratuita | Coste sin ingresos | Cuotas por usuario (20 análisis y 30 000 tokens/día), circuit breaker, `Beta__MaxUsers` |
| R3 | Borrar los datos de IA también borra el uso de tokens del día | El usuario podría saltarse la cuota diaria borrando sus datos | Aceptable para uso personal; en comercial, guardar el uso de cuota aparte |
| R4 | Inglés incompleto: unas 500 cadenas de la app y textos del backend en español (resúmenes, logros, respuestas de IA) | No se puede anunciar inglés | Lanzar solo en español, o traducir el catálogo y añadir `Accept-Language` al backend |
| R5 | Una sola instancia: rate limit, circuit breaker y KPIs en memoria | No escala horizontalmente | Suficiente para uso personal y beta; Redis si se escala |
| R6 | Tests sobre EF InMemory: `ON CONFLICT` / `FOR UPDATE` no probados contra PostgreSQL | Fallos solo en producción | Probar contra PostgreSQL real (docker compose) antes de publicar |
| R7 | Los consentimientos de IA v1 dejan de valer | Los usuarios actuales vuelven a ver el consentimiento | Intencionado (5.1.2(i)) |
| R8 | Usuarios ya conectados a Salud sin consentimiento de datos de salud | Su sincronización se pausa hasta que acepten | Tarjeta en el Dashboard; la cola no se pierde |
| R9 | «Permisos rechazados» es una estimación (iOS no lo revela) | Métrica aproximada | Documentado en `BETA_PROGRAM.md` |
| R10 | Iconos decorativos con tamaño fijo (llama, envío) | No crecen con Dynamic Type | El texto sí escala; mejora menor |

## 6. Bloqueadores de lanzamiento (App Store comercial)

| # | Bloqueador | Quién |
|---|---|---|
| B1 | Compilar iOS y tests en verde (nunca compilado en este entorno) | Desarrollo (CI/Mac) |
| B2 | Política de privacidad y términos publicados (HTTPS) y configurados (`Release__PrivacyPolicyUrl`, `Release__TermsUrl`) | Legal/producto |
| B3 | Backend de producción con HTTPS, PostgreSQL con backups y migraciones aplicadas (incluidas `AddSupportReports` y `AddUserConsents`) | Operaciones |
| B4 | Clave de Sign in with Apple configurada (`Apple__TeamId/KeyId/PrivateKey`) para revocar el token al borrar la cuenta | Operaciones |
| B5 | Bundle ID real, App ID con capacidades, app creada en App Store Connect | Cuenta de desarrollador |
| B6 | DPA con OpenAI y con el hosting, y base de la transferencia internacional | Legal |
| B7 | Icono de la app | Diseño |

Ninguno bloquea el **uso personal** salvo B1, B3 (un backend accesible por HTTPS) y B5 (con TestFlight).

## 7. Cambios necesarios antes de enviar a revisión

1. Compilar con Xcode y corregir lo que salga; ejecutar los 91 tests de iOS.
2. Probar en un iPhone físico la checklist de `RELEASE_CANDIDATE.md` §5 y además:
   - Conectar Salud: aparece primero la hoja de consentimiento y después la de Apple.
   - Decir «Ahora no» al consentimiento: la app funciona sin datos de Salud.
   - Ajustes › Desconectar Salud y borrar sus datos: vuelve a «Conectar Apple Health» y el servidor responde 403 a cualquier subida.
   - Ajustes › Borrar datos del Coach: el Coach vuelve a pedir consentimiento.
   - Rechazar «Datos de uso y diagnóstico»: no llega telemetría (comprobar en los logs).
   - VoiceOver en Dashboard, Coach y Ajustes; tamaño de texto «Accesibilidad XL»; modo oscuro.
3. Publicar la política de privacidad: datos de Salud, OpenAI, analítica, plazos de retención (§2), derechos y contacto.
4. Configurar en producción: `Release__*`, `Apple__*`, `Retention__*` (o los valores por defecto), `Privacy__RequireHealthConsent=true` (por defecto), `OpenTelemetry__OtlpEndpoint`.
5. Aplicar las migraciones `AddSupportReports` y `AddUserConsents`.
6. App Store Connect: App Privacy, clasificación por edades, capturas, notas de revisión mencionando OpenAI.
7. Decidir el idioma: solo español (recomendado para la primera versión) o completar el inglés (R4).
8. Subir con `ios/scripts/testflight.sh` y, tras la prueba interna, «Enviar para revisión».
