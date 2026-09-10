# ADR-0008: Autenticación con token único, sin refresh token

- **Estado:** aceptada
- **Fecha:** 2026-09-09

## Contexto

El personal interno se autentica con usuario y contraseña contra
`POST /{venueSlug}/auth/login`, que devuelve un JWT firmado con HMAC-SHA256. El token
lleva el `VenueId` como claim, y desde ahí en adelante el boliche sale del token y nunca
de la URL. Hoy dura 480 minutos (`Jwt:LifetimeMinutes`).

La pregunta era si además había que emitir un refresh token. Conviene separar los dos
problemas que se suelen mezclar bajo ese nombre:

- **Vencimiento.** Un bartender arranca el turno, se loguea una vez y trabaja toda la
  noche en el mismo dispositivo. Con 480 minutos el token le sobrevive el turno completo,
  así que hoy nadie se re-loguea a mitad de servicio. No hay fricción que resolver.
- **Revocación.** El token es autocontenido: ningún request consulta la base para saber
  si el usuario sigue habilitado. Si un administrador da de baja a un bartender a las
  23:00, ese bartender **sigue entrando hasta 8 horas**. Lo mismo vale para un cambio de
  contraseña y para un cambio de rol.

Un refresh token sólo paga cuando además se acorta el access token a minutos, y eso a su
vez sólo sirve si hay dónde guardar el refresh token de forma segura (una cookie
`httpOnly`). Hoy el token viaja en el body de la respuesta y la PWA lo va a guardar del
lado del cliente, así que agregar rotación de refresh tokens sobre ese esquema sería
maquinaria nueva sin cambiar la exposición real.

## Opciones evaluadas

| Opción | Costo | Qué resuelve |
|---|---|---|
| **Token único, sin refresh** | ninguno, ya está construido | Nada nuevo. La baja lógica tarda hasta la vida del token en surtir efecto. |
| **Security stamp** (lo que hace ASP.NET Identity) | una columna en `StaffUser`, un claim y una validación cacheada por request | Revocación real ante baja, cambio de contraseña o cambio de rol. Sin refresh tokens. |
| **Refresh token completo** | tabla de refresh tokens, rotación, detección de reuso, cookie `httpOnly`, endpoint nuevo | Ventana de exposición de minutos en vez de horas. Superficie nueva considerable. |

## Decisión

**Token único, sin refresh token.** Cuando vence, la PWA manda al usuario al login.

Aceptamos explícitamente el lag de revocación: dar de baja a un usuario interno no corta
su sesión en curso, la corta recién cuando vence el token. Para el alcance actual —un
boliche, un equipo de tres, un turno por noche— el costo de esa ventana es menor que el de
sostener estado de sesión del lado del servidor.

Consecuencias que sí asumimos ahora:

- La PWA tiene que distinguir "credenciales incorrectas" de "sesión vencida", porque ambas
  llegan como `401` y la segunda es un evento normal de fin de turno, no un error. Se
  resuelve con el `type` del problem details ([ADR-0009](0009-errores-de-la-api-como-problem-details.md)):
  `urn:drinkit:problem:auth:invalid-credentials`,
  `urn:drinkit:problem:auth:session-expired` y
  `urn:drinkit:problem:auth:authentication-required`.
- `Jwt:LifetimeMinutes` es el único parámetro que gobierna la exposición, así que es lo
  primero a bajar si el riesgo cambia.

## Cuándo revisar esta decisión

El disparador **no** es el vencimiento, es la revocación. Hay que volver acá cuando pase
cualquiera de estas:

- La baja lógica de un usuario interno tenga que surtir efecto inmediato (por ejemplo, si
  se echa a alguien en medio del turno).
- Entre más de un boliche real a la plataforma, porque ahí el personal deja de ser gente
  que se conoce entre sí.
- Se quiera cerrar sesión en todos los dispositivos, o forzar re-login tras cambiar la
  contraseña.

Y el paso siguiente en ese caso es el **security stamp**, no la rotación de refresh
tokens: cierra el mismo agujero con una fracción del código. El refresh token recién se
justifica si además se decide bajar el access token a minutos, y eso arrastra la decisión
de moverlo a una cookie `httpOnly`.

## Lo que esta decisión no cubre

El endpoint de login no tiene rate limiting. Es un oráculo de contraseñas sin freno y, como
cada intento fallido paga la derivación completa de PBKDF2 a propósito (mitigación de
timing), también es un vector de consumo de CPU. Se decidió posponerlo; se resuelve con
`Microsoft.AspNetCore.RateLimiting`, que viene en el framework y no agrega dependencia.
