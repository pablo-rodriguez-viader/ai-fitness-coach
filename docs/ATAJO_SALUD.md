# Atajo «Enviar Salud a AI Coach»

Con un Apple ID gratuito, la app instalada con Sideloadly **no puede leer Salud**: Apple solo da ese
permiso a apps firmadas con una cuenta de desarrollador de pago. Este atajo hace ese trabajo: lee tus
datos de hoy en Salud y los envía a tu servidor. La app los muestra en «Tu resumen de hoy».

Se monta una sola vez (10-15 minutos). Si lo ejecutas varias veces el mismo día, se sustituyen
los totales de ese día; no se suman.

## Antes de empezar

1. En el PC, abre `.env` y copia el valor de `SHORTCUT_INGEST_KEY=` (lo que va detrás del `=`).
   Mándatelo al iPhone (nota o mensaje a ti mismo) y bórralo después.
2. En la app ya debes haber iniciado sesión y aceptado el consentimiento de datos de salud.

## 1. Crear el atajo

Abre **Atajos** → pestaña **Atajos** → **+**. Ponle de nombre **Enviar Salud a AI Coach**.

Para cada dato, añade dos acciones (busca su nombre con la lupa de abajo):

| Dato | Acción 1: «Buscar muestras de salud» | Acción 2: «Calcular estadísticas» | Nombre de la variable |
|---|---|---|---|
| Pasos | Tipo: **Pasos** · Fecha de inicio: **es hoy** | **Suma** | `Pasos` |
| Energía activa | Tipo: **Energía activa** · Fecha de inicio: **es hoy** | **Suma** | `Activa` |
| Energía en reposo | Tipo: **Energía en reposo** · Fecha de inicio: **es hoy** | **Suma** | `Reposo` |
| Frecuencia cardiaca | Tipo: **Frecuencia cardiaca** · Fecha de inicio: **es hoy** | **Promedio** | `Pulso` |

Después de cada «Calcular estadísticas» añade **Establecer variable** con el nombre de la tabla.

**Peso** (el último registrado): «Buscar muestras de salud» · Tipo **Peso** · Ordenar por **Fecha de
inicio**, **De más reciente a más antiguo** · Límite **1** → «Establecer variable» `Peso`.
Comprueba que la unidad es **kg** (Salud → Peso → Unidad).

**Sueño (opcional)**: «Buscar muestras de salud» · Tipo **Análisis del sueño** · Fecha de inicio
**en los últimos 1 días** · Valor **es Dormido** → «Obtener detalles de muestras de salud»: **Duración** →
«Calcular estadísticas»: **Suma** → «Establecer variable» `Sueño`. Si el resumen muestra un sueño absurdo,
quita el campo `sleepHours` del paso 2: el resto funciona igual.

## 2. Enviar los datos

Añade **Obtener contenido de la URL**:

- URL: `https://chamomile-endorphin-unleveled.ngrok-free.dev/api/v1/health/shortcut`
- Despliega la acción (flecha) → **Método: POST**
- **Cabeceras** → Añadir: clave `X-Shortcut-Key`, valor: la clave que copiaste del `.env`
- **Cuerpo de la solicitud: JSON**, y añade estos campos de tipo **Número**, cada uno con su variable:

| Clave | Tipo | Valor |
|---|---|---|
| `steps` | Número | `Pasos` |
| `activeEnergy` | Número | `Activa` |
| `restingEnergy` | Número | `Reposo` |
| `heartRate` | Número | `Pulso` |
| `weight` | Número | `Peso` |
| `sleepHours` | Número | `Sueño` dividido entre 3600 si sale en segundos (opcional) |

Al final añade **Mostrar resultado** con el «Contenido de la URL» para ver qué responde el servidor.

## 3. Probarlo

Pulsa ▶︎. La primera vez iOS pide permiso para leer cada tipo de dato de Salud: **Permitir**.
El resultado debe ser parecido a `{"date":"2026-11-11","saved":5,"metrics":["steps",…]}`.
Abre la app y pulsa **Actualizar** (flecha circular).

| Respuesta | Qué significa |
|---|---|
| 401 | La clave de la cabecera no coincide con `SHORTCUT_INGEST_KEY` del `.env` |
| 403 | Falta el consentimiento de datos de salud en la app (Ajustes → Privacidad) |
| 409 | Aún no has iniciado sesión en la app con tu código |
| 400 | Ningún valor válido (revisa que los campos sean de tipo **Número**) |
| No conecta | El PC está apagado, o Docker o la pestaña «túnel ngrok» no están en marcha |

## 4. Que se ejecute solo

**Atajos** → **Automatización** → **+** → **Hora del día** → por ejemplo **21:00**, **Diariamente** →
**Ejecutar inmediatamente** → **Siguiente** → elige **Enviar Salud a AI Coach**.
Puedes añadir otra a mediodía. Si quieres que se envíe al abrir la app: **App** → **AI Coach** →
**Se abre** → **Ejecutar inmediatamente** → el mismo atajo.

## Qué datos salen del iPhone

Solo los totales del día de la tabla de arriba, a tu propio servidor y a través del túnel con HTTPS.
La clave del atajo solo permite **escribir** datos en tu cuenta, no leerlos. Si la pierdes o la compartes
por error, cambia `SHORTCUT_INGEST_KEY` en el `.env`, reinicia el servidor (`docker compose up -d`) y
actualiza la cabecera del atajo.
