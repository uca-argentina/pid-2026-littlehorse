# Pruebas de punta a punta

Manejan un navegador de verdad contra la API y la base reales. No hay dobles de prueba ni
respuestas simuladas: si la prueba pasa, el flujo funciona.

Hoy cubren el ingreso del personal (US-01):

| Prueba                                    | Qué verifica                                                                |
| ----------------------------------------- | --------------------------------------------------------------------------- |
| `rejects a wrong password…`               | El mensaje de error, y que el usuario se conserve y la contraseña se borre. |
| `opens the staff area…`                   | El ingreso entra al área del personal y muestra el rol que sale del token.  |
| `keeps the session open when reloaded`    | La sesión sobrevive a un refresco de la tablet.                             |
| `closes the session and blocks the area…` | Cerrar la sesión vuelve a bloquear el área del personal.                    |

## Requisitos

Son los mismos que para desarrollar, más el navegador de Playwright. Ninguna versión se
elige a mano: salen de archivos versionados y las herramientas las leen solas.

| Qué                    | Versión                         | De dónde sale                                   |
| ---------------------- | ------------------------------- | ----------------------------------------------- |
| .NET SDK               | 10.0.400                        | `global.json`                                   |
| Node.js                | `^22.22.3`, `^24.15.0` o `>=26` | `engines` de `frontend/package.json`            |
| pnpm                   | 12.3.4                          | `packageManager` de `frontend/package.json`     |
| Docker Desktop         | cualquiera reciente             | corre SQL Server local                          |
| `@playwright/test`     | 1.63.0 (Apache-2.0)             | `devDependencies`                               |
| `@types/node`          | 22.x (MIT)                      | `devDependencies`, tipos de Node para los specs |
| Chromium de Playwright | la que pide esa versión         | se baja con `e2e:install`                       |

## Configuración inicial

Una sola vez por máquina, desde la raíz del repositorio:

```bash
pnpm --prefix frontend install           # incluye Playwright
dotnet restore backend/DrinkIt.slnx
dotnet dev-certs https --trust           # la API sirve https con certificado de desarrollo
pnpm --prefix frontend run e2e:install   # baja el Chromium de Playwright (~100 MB)
```

El navegador **no** se guarda en el repositorio ni en `node_modules`: va a una caché del
sistema operativo, compartida entre proyectos. Por eso es un paso aparte de `install`.

## Correrlas

La base tiene que estar levantada. Nada más: la API y el servidor de desarrollo de Angular
los arranca la propia corrida.

```bash
docker compose up -d                        # SQL Server en localhost,1433

pnpm --prefix frontend run e2e              # la suite completa, sin ventana
pnpm --prefix frontend run e2e:headed       # igual, pero viendo el navegador
pnpm --prefix frontend run e2e:ui           # modo interactivo, para depurar un test
pnpm --prefix frontend run e2e:report       # abre el informe de la última corrida
```

Para correr una sola prueba, se filtra por nombre:

```bash
pnpm --prefix frontend exec playwright test -g "closes the session"
```

## Qué levanta cada corrida

1. **Antes de todo**, comprueba que haya algo escuchando en el puerto de SQL Server. Si no lo
   hay, corta en dos segundos diciendo el comando que lo arregla.
2. **Arranca la API** con `dotnet run`, sin perfil de lanzamiento. El perfil abre la
   referencia de la API en un navegador, que es útil a mano y es ruido en medio de una
   corrida. Las variables que el perfil pondría se pasan explícitamente.
3. **Arranca el servidor de Angular** con `pnpm start`.
4. Si la API o el servidor de Angular **ya están corriendo, los reutiliza**. Es el caso
   normal: alguien con la app abierta que quiere verificar un flujo.

La primera corrida es lenta porque compila, aplica migraciones y siembra. Las siguientes
tardan segundos.

## Datos y credenciales

El boliche y el administrador los siembra la API al arrancar en Development. El boliche es
`bar-alfa` y el usuario es `admin`.

**La contraseña no está escrita en los tests.** Se lee del perfil de arranque de la API, que
es de donde la toma la semilla. Dos copias de la misma contraseña se desincronizan, y el
fallo después parece una pantalla rota en vez de configuración vieja.

## Cuando algo falla

| Síntoma                                        | Qué pasa                                                                                                        |
| ---------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `pnpm: command not found`                      | Hay una versión de Node activa donde pnpm no está instalado. Cambiá a la que pide `engines` y volvé a intentar. |
| `No SQL Server listening on localhost:1433`    | Falta `docker compose up -d`.                                                                                   |
| `address already in use` en el 7176            | Quedó una API vieja ocupando el puerto. Cerrala y volvé a correr.                                               |
| `browserType.launch: Executable doesn't exist` | Falta `pnpm --prefix frontend run e2e:install`.                                                                 |
| La API no arranca y habla del certificado      | Falta `dotnet dev-certs https --trust`.                                                                         |

Una prueba que falla deja captura y video en `test-results/`, y el informe navegable en
`playwright-report/`. Se abre con `pnpm --prefix frontend run e2e:report`, así que no hace
falta reproducir el fallo para entenderlo. Las dos carpetas están en el `.gitignore`.

## Escribir una prueba nueva

- **El nombre completa la oración que empieza el `describe`**, en minúscula y sin `should`:
  `test('opens the staff area for the seeded administrator')` se lee como
  `Staff login > opens the staff area for the seeded administrator`. Es la misma convención
  que los unitarios del frontend, y por el mismo motivo: acá no hay un método bajo prueba
  que nombrar. Ver `CLAUDE.md`.
- **Los elementos se buscan por rol y por texto visible**, nunca por clase de CSS ni por id.
  Un test que se rompe al renombrar una clase no estaba probando lo que le importa al usuario.
- **Las afirmaciones son sobre lo que ve la persona en el boliche**, no sobre el estado
  interno de la aplicación.
- **Nada de esperas por tiempo.** Las afirmaciones de Playwright ya reintentan solas.

## Todavía no

Las pruebas no corren en la integración continua: ese job necesita SQL Server y el backend en
el runner, y es una decisión aparte. Por ahora se corren a mano antes de abrir un PR.
