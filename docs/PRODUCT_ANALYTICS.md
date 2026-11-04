# Analítica de producto — Beta cerrada

Para quien evalúa la beta: qué se mide, cómo se calcula y cómo leer el informe para decidir si el producto aporta valor.

## Cómo obtener el informe

```sh
curl -H "X-Analytics-Key: $ANALYTICS_ADMIN_API_KEY" \
  "https://<api>/api/v1/analytics/product?from=2026-11-01&to=2026-11-30"
```

- `from`/`to` son opcionales: por defecto, los últimos 30 días (máximo 120). Los días son UTC.
- Hay que configurar `Analytics__AdminApiKey` (32 caracteres o más) como secreto. Sin clave configurada, el endpoint responde 404.
- El informe identifica a los usuarios solo por su UUID interno; no incluye nombres, correos ni valores de salud.

## Fuentes de datos

Todo sale de la tabla `product_events`.

| Evento | Origen | Cuándo |
|---|---|---|
| `session_start` | App | Primer paso a primer plano y cada vuelta tras ≥ 30 min en segundo plano. Los lanzamientos en segundo plano (HealthKit) no cuentan. |
| `screen_view` | App | Al abrir Dashboard, Coach, Análisis, Entrenamientos, Nutrición, Objetivos, Progreso, Resumen semanal o Logros |
| `workout_started` | App | Al abrir la sesión de un entrenamiento |
| `notification_opened` | App | Al tocar un recordatorio |
| `workout_plan_generated`, `workout_feedback_submitted`, `workout_completed` | API | Cuando el plan o el feedback se guardan correctamente |
| `coach_query` (`kind=coach` o `analysis`) | API | Respuesta del Coach o del Análisis IA entregada |
| `goal_created`, `goal_completed`, `meal_logged` | API | Objetivo creado, objetivo de peso alcanzado (se evalúa al pedir el resumen de engagement) o comida registrada |

Las acciones se registran en el servidor, así que el cliente no puede perderlas ni inventarlas. Los eventos de la app se guardan en un buffer en disco y se envían por lotes: pueden llegar con retraso, pero conservan su hora real (dentro de una ventana de 35 días).

## Definiciones

- **Usuario activo (día):** tiene al menos un evento ese día. **DAU** = media diaria del periodo. **WAU** = usuarios únicos en los últimos 7 días del periodo. **Stickiness** = DAU de esos 7 días / WAU.
- **Sesiones:** número de `session_start`, y sesiones por usuario activo.
- **Entrenamientos completados:** días de entrenamiento distintos completados. **Consultas al Coach:** únicas por `client_request_id`, de modo que los reintentos no cuentan dos veces.
- **Retención D1/D7/D30 (clásica):** porcentaje de usuarios activos exactamente N días después de su primer día. Solo cuentan los usuarios con al menos N días de antigüedad. También se calcula una versión **rolling** (activos el día N o más tarde) y el desglose por cohorte del primer día.
- **Adherencia** (por usuario, promedio entre usuarios del periodo):
  - Entrenamiento: días planificados completados / días planificados.
  - Nutrición: días con alguna comida / días desde la primera comida registrada. Es nula si el usuario nunca registra comidas.
  - Objetivos: días con progreso (entrenamiento, comida, feedback o pregunta al Coach) / días con objetivo activo.
- **Riesgo de abandono:** compara los últimos 7 días con los 7 anteriores. Puntúa cuatro señales:
  - Menos días activos (−50 % o más): 30 puntos.
  - Menos entrenamientos completados (−50 %) o menos pasos (−30 %): 25 puntos.
  - Menos interacciones (−50 %): 25 puntos.
  - Días sin actividad: ≥ 3 días suma 20 puntos; ≥ 7 días, 45.

  Medio = 30 puntos o más; alto = 60 o más. Los usuarios con menos de 2 semanas de antigüedad solo se evalúan por inactividad.
- **Insights:**
  - Funciones más usadas: usuarios únicos por función.
  - Funciones abandonadas: usuarios que la probaron hace ≥ 14 días, siguen usando la app pero no la han vuelto a usar en 14 días.
  - Recomendaciones con más adherencia: por tipo de recomendación, porcentaje seguido de un entrenamiento completado en los 7 días siguientes.

  Los insights son asociaciones, no prueban causalidad.

## Lo que ve el usuario

- **Racha:** días seguidos con progreso. Abrir la app no cuenta. La racha de ayer se mantiene hasta que termina el día de hoy, en la zona horaria del usuario.
- **Logros (10):** primer entrenamiento, 10 entrenamientos, rachas de 3/7/30 días, primera pregunta al Coach, 5 valoraciones, comidas en 7 días, primer objetivo y objetivo cumplido.
- **Resumen semanal:** últimos 7 días comparados con la semana anterior, más la adherencia de las últimas 4 semanas.
- **Recordatorios locales** (sin servidor de push):
  - Entrenamiento pendiente a las 18:00 si hay uno planificado hoy sin completar.
  - Objetivo diario a las 20:00 si hay un objetivo activo y hoy no hay progreso.
  - Resumen del domingo a las 19:00.

  Se reprograman al abrir la app, al cerrar cualquier pantalla y al volver a primer plano. Se cancelan al cerrar sesión.

## Cómo evaluar el valor en 30 días

Umbrales orientativos para una beta cerrada de fitness; ajústalos antes de empezar y no después de ver los datos.

| Pregunta | Métrica | Señal positiva |
|---|---|---|
| ¿Vuelven? | Retención D7 / D30 (clásica) | ≥ 35 % / ≥ 20 % |
| ¿Es un hábito? | Stickiness (DAU/WAU) | ≥ 0,3 |
| ¿Entrenan con el plan? | Adherencia de entrenamiento | ≥ 0,6 |
| ¿El Coach aporta? | Uso de `coach` y su tasa de abandono | Uso ≥ 40 % de activos y abandono < 50 % |
| ¿Qué recomendaciones funcionan? | `recommendationsByAdherence` | Tipos con seguimiento > media |
| ¿Quién se va? | `dropout.atRisk` cada semana | Contactar o entrevistar a los de riesgo alto |

Revisión semanal recomendada: informe de los últimos 7 días + informe acumulado desde el inicio.

## Limitaciones conocidas

- El informe carga los eventos en memoria (tope de 500.000): adecuado para una beta, no para producción a escala.
- Los días de producto son UTC; los de usuario usan el desfase horario que envía la app.
- Los objetivos sin peso objetivo (p. ej. "Salud general") nunca se completan automáticamente.
- Las acciones previas a esta versión no tienen eventos: la historia empieza al desplegar la migración `AddProductAnalytics`.

## Bloque `beta` (2026-10-28)

El informe incluye ahora `beta`: MAU, D1/D7/D30, abandonos de entrenamiento, consultas repetidas y feedback del Coach, resultados de sync y permisos de HealthKit, estabilidad por build, capacidad y alertas. Los eventos de sync, crash y error se guardan pero no cuentan como actividad. Definiciones en [`BETA_PROGRAM.md`](BETA_PROGRAM.md).
