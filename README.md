# VacationApp

Proyecto inicial de **.NET MAUI con .NET 10** para solicitar vacaciones y dar seguimiento a su aprobación. La interfaz está en español y ofrece acceso por perfil: empleado, líder, Recursos Humanos y nómina.

Esta versión es una **demostración local**: los perfiles comparten datos dentro de una instalación de la app. No incluye servidor, sincronización entre dispositivos ni autenticación de producción.

## Qué puedes probar

| Perfil | Funciones |
| --- | --- |
| Empleado | Consultar saldo, crear solicitudes y revisar su estado e historial. |
| Líder | Revisar solicitudes de su equipo, aprobarlas o rechazarlas con motivo. |
| Recursos Humanos | Revisar solicitudes aprobadas por el líder y emitir la aprobación final o un rechazo con motivo. |
| Nómina | Consultar solicitudes aprobadas y registrar que fueron procesadas. |

El flujo es **empleado → líder → Recursos Humanos → nómina**. Un rechazo del líder o de RH termina la solicitud y libera los días reservados.

Las reglas de ejemplo cuentan días de lunes a viernes, incluidos ambos extremos; no descuentan festivos. El saldo inicial es de **20 días por año**, definido en los datos del perfil y sin representar una regla legal. Se impiden fechas pasadas, periodos superpuestos y solicitudes que excedan el saldo. La fecha actual y el año del saldo corresponden a la zona horaria local del dispositivo. Los periodos que cruzan años se descuentan por separado en cada año.

## Cuentas de demostración

Todas usan la contraseña **`Vacaciones2026!`**. Son credenciales públicas de prueba; no deben utilizarse en producción ni para datos personales reales.

| Correo | Nombre | Perfil |
| --- | --- | --- |
| `ana@vacation.local` | Ana García | Empleado |
| `luis@vacation.local` | Luis Hernández | Empleado |
| `lider@vacation.local` | Laura Martínez | Líder de Ana y Luis |
| `rh@vacation.local` | Sofía Ramírez | Recursos Humanos |
| `nomina@vacation.local` | Carlos López | Nómina |

Al cerrar sesión puedes entrar con otra cuenta para recorrer el flujo completo. La sesión vive en memoria; los datos demo, las solicitudes y su historial permanecen en `vacation-data.json` dentro de `FileSystem.AppDataDirectory`. Borrar los datos de la app reinicia la demostración. El archivo local no es una base de datos compartida ni almacenamiento cifrado.

## Estructura

```text
VacationApp.slnx
src/
  VacationApp/               # Interfaz MAUI y configuración por plataforma
  VacationApp.Core/          # Perfiles, reglas, autenticación demo y persistencia
tests/
  VacationApp.Core.Tests/    # Pruebas de reglas y permisos
scripts/                    # Preparación y verificación del entorno en la nube
docs/
  flujo-y-validacion.md      # Estados, reglas y recorrido manual
```

## Preparar el entorno en la nube

El SDK está fijado en `global.json` a **.NET 10.0.401**. En Linux, el destino MAUI es Android. Desde la raíz del repositorio:

```bash
bash scripts/setup-cloud.sh
source scripts/cloud-env.sh
bash scripts/check.sh
```

El script de preparación instala las herramientas y el SDK de Android requeridos. `cloud-env.sh` configura las rutas para la terminal actual; vuelve a cargarlo al abrir otra terminal. `check.sh` ejecuta las pruebas del núcleo y compila la aplicación para Android.

También puedes ejecutar los pasos por separado:

```bash
dotnet test tests/VacationApp.Core.Tests/VacationApp.Core.Tests.csproj
dotnet build src/VacationApp/VacationApp.csproj -f net10.0-android
```

Compilar Android verifica el proyecto y sus recursos; probar la interfaz requiere un emulador o dispositivo. Con uno conectado:

```bash
dotnet build src/VacationApp/VacationApp.csproj -t:Run -f net10.0-android
```

También puedes instalar el APK generado por la compilación Debug en un dispositivo o emulador conectado por ADB:

```bash
adb install -r src/VacationApp/bin/Debug/net10.0-android/com.aldokkotsu.vacationapp-Signed.apk
```

Este APK usa una firma de desarrollo. Para publicar la app necesitas una compilación de distribución y una clave de firma propia.

## Descargar el APK desde GitHub

El flujo [Build Android APK](.github/workflows/android-apk.yml) prepara las herramientas con versiones fijadas, ejecuta las pruebas del núcleo y compila el APK instalable. Se ejecuta al actualizar la rama `codex/android-demo` y también admite ejecución manual desde GitHub Actions cuando el flujo está disponible en la rama predeterminada.

1. Inicia sesión en GitHub con una cuenta que tenga acceso al repositorio y abre [Actions de VacationApp](https://github.com/AldOkkotsu/VacationApp/actions/workflows/android-apk.yml).
2. Abre una ejecución de **Build Android APK** que haya terminado correctamente.
3. En **Artifacts**, descarga **VacationApp-Android-APK**. GitHub lo entrega como ZIP; los artefactos se conservan durante 14 días.
4. Extrae `com.aldokkotsu.vacationapp-Signed.apk` del ZIP y ábrelo en tu teléfono Android. Permite la instalación desde el navegador o administrador de archivos si Android lo solicita.

El APK es una demostración con firma de desarrollo. Si no aparece un artefacto, revisa que la ejecución haya terminado correctamente; la subida falla si la compilación no produjo el APK. La compilación y las pruebas del núcleo no sustituyen la prueba de la interfaz en un dispositivo.

## Desarrollo en otros equipos

- **Windows:** instala una versión de Visual Studio compatible con .NET 10 y la carga de trabajo .NET MAUI. Abre `VacationApp.slnx`, elige `VacationApp` y selecciona Windows o un emulador Android.
- **macOS:** instala .NET 10, las cargas MAUI necesarias y una versión de Xcode compatible con ellas para iOS y Mac Catalyst. Android requiere su SDK y un emulador o dispositivo.
- **Linux:** usa Android; MAUI no incluye un destino de escritorio Linux.

Los destinos de la app se seleccionan según el sistema: Android siempre; Windows en Windows; iOS y Mac Catalyst en macOS. Publicar en tiendas requiere configuración y firma de distribución.

## Validación y siguientes pasos

Consulta [el flujo y el recorrido de validación](docs/flujo-y-validacion.md) para probar los cuatro perfiles. Las pruebas automatizadas cubren el núcleo y los permisos; la validación visual debe hacerse en un dispositivo o emulador.

En el entorno en la nube se verificaron **25 pruebas aprobadas** y la **compilación Android sin advertencias ni errores**. No se realizó la validación de la interfaz en un dispositivo o emulador.

Para usar esta base con una organización real faltan una API y base de datos compartidas, identidad y permisos verificados por el servidor, políticas de vacaciones de la organización, protección de datos y auditoría. Tampoco se incluyen notificaciones, exportación ni integraciones con nómina.
