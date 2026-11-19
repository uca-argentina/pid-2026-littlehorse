# Usuarios y contraseñas (entorno local)

> ⚠️ **Sólo para la base local de desarrollo** (Docker en tu máquina). No sirven en Azure y no son
> secretos: la del admin está en `launchSettings.json` y los de práctica se crearon a mano en tu base.

**Boliche (slug):** `bar-alfa` · **Login del personal:** `http://localhost:4200/bar-alfa/staff/login`

## Personal

| Rol | Usuario | Contraseña | Para qué |
|---|---|---|---|
| Administrador | `admin` | `dev-admin-password-not-a-secret` | El que siembra la API sola al arrancar en Development |
| Administrador | `admin.practica` | `practica123` | Admin de práctica |
| KDS (barra) | `kds.practica` | `practica123` | Tablero de la barra: preparar, listo, entregar |
| Cajero | `caja.practica` | `practica123` | Caja: cobrar en efectivo, coordinar mesas |
| Mozo | `mozo.practica` | `practica123` | Llevar pedidos a las mesas VIP |

Los de práctica se verificaron entrando con cada uno (respuesta 200).

**Ojo, para usarlos en una noche:** el KDS, el cajero y el mozo sólo trabajan si están **en el equipo
de la noche en curso**. Al crear la noche (como admin), sumalos.

## Otros usuarios que hay en la base (contraseña desconocida)

`barra.demo`, `barra.prueba`, `kds.manual.test` (KDS) y `caja.prueba` (cajero). Más de 900 `e2e.*`
que crearon los tests de punta a punta.

Las contraseñas se guardan como **hash**, así que no se pueden leer. Si necesitás entrar con alguno,
como admin andá a **Personal → el usuario → restablecer contraseña**.

## La noche de práctica (ya creada en tu base)

| Qué | Valor |
|---|---|
| Nombre | **Noche de práctica** |
| Horario | 12/11/2026 18:00 → 13/11/2026 09:00 (hora argentina) |
| Equipo | `kds.practica` (barra), `caja.practica` (cajero), `mozo.practica` (mozo) |
| Stock en la barra `kds.practica` | 50 de cada uno: Aperol Spritz, Cerveza, Fernet con Coca, Gin Tonic |

**Mesas VIP de la noche** (el cliente entra con el link y pide con el saldo):

| Mesa | Saldo | Link del cliente |
|---|---|---|
| 1 | $ 100.000 | `http://localhost:4200/bar-alfa/table/517debfe672cb7fabea478c18cbdfbf7` |
| 2 | $ 30.000 | `http://localhost:4200/bar-alfa/table/760d3d63bde922b3f126f6195b27c37c` |

La mesa 2 tiene poco saldo a propósito, para probar "cubrir la diferencia" (US-42).

**Cuando pase el horario, la noche termina** y la carta deja de tomar pedidos. Para practicar otro
día, creá una noche nueva como admin (Noches → Nueva), sumá a los tres de práctica y cargá stock.
Si al cargar stock dice que **no hay en el depósito**, primero sumá stock general al producto
(Productos → el producto → Ajustar): las noches de los tests e2e tienen tomado el de los productos.

## Base de datos

| Servidor | Usuario | Contraseña | Base |
|---|---|---|---|
| `localhost,1433` | `sa` | `DrinkIt!Local1` | `drinkit` |

Sale de `docker-compose.yml`. Ver `comandos-utiles.md`, sección 4.

## Cliente

El cliente **no tiene usuario**: entra por `http://localhost:4200/bar-alfa/menu` y pide con un nombre.

## Si la base se borra

El `admin` se vuelve a crear solo al levantar la API. Los de práctica no: crealos de nuevo desde
**Personal → Nuevo** con el admin, o con curl:

```bash
# 1. token del admin
curl -k -X POST https://localhost:7176/bar-alfa/auth/login -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"dev-admin-password-not-a-secret"}'

# 2. crear uno (Role: Kds | Cashier | Waiter | Administrator)
curl -k -X POST https://localhost:7176/staff/users -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"username":"kds.practica","password":"practica123","role":"Kds"}'
```
