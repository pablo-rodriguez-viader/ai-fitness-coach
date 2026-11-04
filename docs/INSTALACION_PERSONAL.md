# Instalar AI Fitness Coach en tu iPhone (uso personal)

Hay dos formas. Para uso personal sin coste, usa la **opción gratuita**.

| | Opción gratuita (Sideloadly) | Opción de pago (TestFlight) |
|---|---|---|
| Coste | 0 € | 99 €/año (Apple Developer Program) |
| Login | Código de acceso secreto | Sign in with Apple |
| Duración de la instalación | 7 días (se renueva desde el PC) | 90 días por build |
| Sincronización de Salud | Al abrir la app o volver a ella | También en segundo plano |
| Necesitas | PC con Windows, cable USB, Apple ID gratuito | Cuenta de desarrollador |

En las dos: sin Mac (se compila en GitHub Actions) y con tu backend en tu PC.

---

## Opción gratuita

### Qué necesitas (todo gratuito)
- iPhone con iOS 17 o posterior, su cable y tu **Apple ID** normal.
- En el PC con Windows:
  - **Docker Desktop**: <https://www.docker.com/products/docker-desktop/>
  - **ngrok**: <https://ngrok.com>, con cuenta gratuita.
  - **Sideloadly**: <https://sideloadly.io>
  - **iTunes** e **iCloud** descargados de la web de Apple, **no** los de Microsoft Store. Sideloadly los necesita para ver el iPhone.
- Una cuenta de **GitHub**.

> Sin IA: OpenAI cobra por uso, así que lo gratuito es dejarla desactivada (`AI_ENABLED=false`). Dashboard, Salud, Nutrición, Objetivos y Progreso funcionan; Coach, Análisis y Rutinas se ocultan. Si un día pones saldo en OpenAI, la activas en el `.env` sin reinstalar la app.

### Paso 1 — Backend en tu PC

1. En la carpeta del proyecto, copia `.env.example` a `.env`.
2. Rellena las variables. Genera cada valor aleatorio en PowerShell con
   `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`:

   | Variable | Valor |
   |---|---|
   | `POSTGRES_PASSWORD` | aleatorio |
   | `JWT_SIGNING_KEY_BASE64` | aleatorio |
   | `PERSONAL_ACCESS_CODE` | aleatorio. **Es tu contraseña para entrar en la app**: guárdalo en tu gestor de contraseñas |
   | `APPLE_AUDIENCE` | tu identificador de app, por ejemplo `com.tunombre.aicoach`. Inventa uno único; lo usarás también en el paso 3 |
   | `BETA_MAX_USERS` | `1` (nadie más puede crear cuentas) |
   | `AI_ENABLED` | `false` |

3. Arranca:
   ```powershell
   docker compose up -d --build
   ```
   Comprueba <http://127.0.0.1:8080/health/ready>. El backend se levanta solo al reiniciar el PC si Docker Desktop arranca con Windows.
4. Dale una **URL HTTPS fija** con ngrok:
   1. Inicia sesión en ngrok, copia tu token y ejecuta `ngrok config add-authtoken TU_TOKEN`.
   2. En el panel de ngrok › *Domains*, reclama tu **dominio estático gratuito** (por ejemplo `tu-nombre.ngrok-free.app`).
   3. Ejecuta `ngrok http --url=tu-nombre.ngrok-free.app 8080` y deja la ventana abierta.
   4. Comprueba desde el navegador del iPhone `https://tu-nombre.ngrok-free.app/health/live`. Debe responder `Healthy`.

> La app solo conecta mientras el PC está encendido con Docker y ngrok en marcha. Si está apagado, verás tus últimos datos guardados y la app sincroniza al volver.

### Paso 2 — Subir el proyecto a GitHub

```powershell
git init
git add .
git status          # comprueba que NO aparece .env
git commit -m "AI Fitness Coach"
git branch -M main
git remote add origin https://github.com/TU_USUARIO/ai-fitness-coach.git
git push -u origin main
```

**Público o privado:**
- **Público**: minutos de compilación ilimitados y gratis, pero cualquiera puede ver el código. Tus claves no se suben: `.env` y `*.p8` están en `.gitignore`.
- **Privado**: el plan gratuito incluye unos 200 minutos de macOS al mes. Cada compilación tarda entre 20 y 30 minutos, así que salen unas 7 al mes. En un repositorio privado, desactiva el workflow **CI** (Actions › CI › ⋯ › Disable workflow) para que no gaste minutos en cada `push`.

### Paso 3 — Compilar el `.ipa` (sin Mac)

1. En GitHub › **Actions** › **iOS Personal (Sideloadly)** › **Run workflow**, rellena:
   - `api_base_url`: `https://tu-nombre.ngrok-free.app`
   - `bundle_id`: el mismo que `APPLE_AUDIENCE` (`com.tunombre.aicoach`)
2. Espera a que termine (entre 20 y 30 minutos). Al final de la página del run, en **Artifacts**, descarga `AIFitnessCoach-Personal-N`, un zip que contiene el `.ipa`. Descomprímelo.

> **Es la primera compilación de la app iOS.** Si falla, abre el paso que esté en rojo, copia las líneas `error:` y pégamelas: lo corrijo.

### Paso 4 — Instalar con Sideloadly

1. Conecta el iPhone al PC por cable, desbloquéalo y pulsa **Confiar** en este ordenador.
2. Abre Sideloadly:
   1. Elige tu iPhone.
   2. Arrastra el `.ipa`.
   3. Escribe tu Apple ID.
   4. Pulsa **Start**. Te pedirá la contraseña y el código de verificación en dos pasos.
3. En el iPhone:
   - **Ajustes › Privacidad y seguridad › Modo de desarrollador** → activar. El iPhone se reinicia. Esta opción aparece después del primer intento de instalación.
   - **Ajustes › General › VPN y gestión de dispositivos** → tu Apple ID → **Confiar**.
4. Abre **AI Coach**:
   1. Escribe tu `PERSONAL_ACCESS_CODE` y completa el perfil.
   2. Pulsa **Conectar Apple Health**: aparece el consentimiento y después los permisos de Apple.

### Cada 7 días

Con un Apple ID gratuito, iOS deja de abrir la app a los 7 días. Para renovarla:
- Vuelve a instalar el **mismo `.ipa`** con Sideloadly. Con el mismo Apple ID y el mismo `bundle_id` se conservan la app y sus datos, y además tus datos están en tu backend.
- O activa en Sideloadly el **refresco automático**: renueva la app por Wi-Fi mientras el PC está encendido y en la misma red.

Solo hace falta compilar de nuevo (paso 3) cuando haya cambios en la app.

### Límites del Apple ID gratuito
- Como mucho 3 apps instaladas así a la vez, y 10 identificadores de app nuevos por semana: no cambies el `bundle_id` cada vez.
- Sin Sign in with Apple ni sincronización de Salud en segundo plano: los datos se envían al abrir la app.

### Problemas frecuentes

| Síntoma | Causa probable |
|---|---|
| «Backend no configurado» al abrir | `api_base_url` vacío o sin `https://` al compilar |
| «El código de acceso no es correcto» | No coincide con `PERSONAL_ACCESS_CODE` del `.env`; si lo cambiaste, ejecuta `docker compose up -d` |
| «El servidor no tiene activado el acceso personal» | `PERSONAL_ACCESS_CODE` vacío en el `.env` |
| «La beta privada está completa» | Ya existe otra cuenta y `BETA_MAX_USERS=1` |
| La app no conecta | PC apagado, o Docker o ngrok cerrados: comprueba `…/health/live` |
| Sideloadly no ve el iPhone | iTunes o iCloud de Microsoft Store en vez de los de la web de Apple; cable o «Confiar» |
| «Unable to verify app» / la app no abre | Falta confiar en el desarrollador (paso 4.3) o han pasado los 7 días |
| No aparecen datos de Salud | Permisos en Ajustes › Salud › Acceso a datos, o consentimiento de datos de salud no aceptado (tarjeta «Sincronización en pausa») |

---

## Opción de pago (TestFlight, 99 €/año)

Con el **Apple Developer Program** tienes Sign in with Apple, sincronización en segundo plano y builds de 90 días desde TestFlight.

1. **Alta y app (una vez):**
   1. Date de alta como individual en <https://developer.apple.com/programs/enroll/>.
   2. Crea un App ID con **HealthKit** (con *Background Delivery*) y **Sign In with Apple**.
   3. Crea la app en App Store Connect con ese Bundle ID y un nombre único. No se publica.
   4. Crea una clave de **App Store Connect API** con rol App Manager: descarga el `.p8` y apunta el *Key ID* y el *Issuer ID*. Apunta también tu *Team ID*.
2. **Backend:** el mismo del paso 1. `APPLE_AUDIENCE` debe ser tu Bundle ID real, y `PERSONAL_ACCESS_CODE` puede quedarse vacío.
3. **GitHub:** crea el entorno `testflight`:
   - **Secrets**: `APPLE_TEAM_ID`, `ASC_KEY_ID`, `ASC_ISSUER_ID` y `ASC_KEY_P8` (el contenido completo del `.p8`).
   - **Variables**: `APP_BUNDLE_ID` y `BETA_API_BASE_URL`.
4. **Compilar y subir:** Actions › **TestFlight Beta** › Run workflow.
5. **Instalar:** en App Store Connect › TestFlight › Testers internos, añádete. Instala **TestFlight** en el iPhone y desde ahí la app. Cada 90 días, vuelve a lanzar el workflow.

**Con un Mac:**
1. En `ios/`, ejecuta `xcodegen generate` y abre el proyecto.
2. Elige tu equipo de pago.
3. Ejecuta el esquema **AIFitnessCoach Beta** con el iPhone conectado.

Instalada así dura 1 año. Con un Apple ID gratuito y un Mac también puedes usar el esquema **AIFitnessCoach Personal** directamente desde Xcode, con los mismos 7 días.
