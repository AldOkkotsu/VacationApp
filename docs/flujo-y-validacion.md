# Flujo y validación de la demostración

## Estados y responsables

| Estado del núcleo | Responsable del siguiente paso | Acción permitida |
| --- | --- | --- |
| `PendingLeader` | Líder asignado al empleado | Aprobar hacia RH o rechazar con motivo. |
| `PendingHumanResources` | Recursos Humanos | Aprobar o rechazar con motivo. |
| `Approved` | Nómina | Registrar el procesamiento. |
| `RegisteredByPayroll` | — | Consultar el registro final. |
| `Rejected` | — | Consultar el motivo y el historial. |

Una solicitud nueva queda en `PendingLeader`. El líder sólo puede revisar solicitudes de su equipo. Cada transición exige el perfil y el estado adecuados; un empleado no puede aprobar su propia solicitud. Sólo los empleados crean solicitudes.

## Reglas de ejemplo

- La fecha inicial debe ser igual o posterior a la fecha local actual del dispositivo; la fecha final debe ser igual o posterior a la inicial. El año predeterminado para consultar el saldo también usa esa zona horaria local.
- Se cuentan lunes a viernes, incluyendo las fechas inicial y final cuando sean días hábiles. Los fines de semana no consumen saldo; no existe calendario de festivos.
- Un periodo debe contener al menos un día hábil y debe caber en el saldo disponible de cada año afectado.
- El saldo de ejemplo es de 20 días anuales. No se arrastra saldo de un año a otro.
- Las solicitudes pendientes reservan días; las aprobadas y registradas por nómina consumen días. Un rechazo libera la reserva.
- Un empleado no puede tener periodos superpuestos en solicitudes pendientes, aprobadas o registradas. Una solicitud rechazada no bloquea las fechas.
- Rechazar requiere un motivo. Los motivos y comentarios admiten como máximo 500 caracteres.

Las reglas son una base técnica de demostración. La política de la empresa, la legislación aplicable, el calendario laboral y la zona horaria de referencia de la organización deben definirse antes de usarlas con empleados reales. Las marcas de tiempo del historial se guardan en UTC y se muestran en la hora local del dispositivo.

## Recorrido manual

Usa una instalación de la app con las cuentas indicadas en el [README](../README.md). Empieza con un periodo futuro de dos días hábiles sin solicitudes previas que se superpongan.

1. Entra como `ana@vacation.local`. Consulta el saldo, crea la solicitud y comprueba que aparece pendiente del líder. El saldo reservado debe aumentar en dos días.
2. Cierra sesión y entra como `lider@vacation.local`. Localiza la solicitud de Ana, apruébala y comprueba que pasa a Recursos Humanos.
3. Entra como `rh@vacation.local`, revisa la solicitud y apruébala. Los días pasan de reservados a usados.
4. Entra como `nomina@vacation.local` y registra el procesamiento. Comprueba el estado final.
5. Vuelve a la cuenta de Ana. Comprueba el estado y el historial de las tres acciones.
6. Crea otra solicitud con un periodo distinto y recházala desde la cuenta del líder. Comprueba que pide un motivo, que éste aparece en el historial y que se liberan los días reservados.
7. Repite el rechazo desde Recursos Humanos con otra solicitud que el líder haya aprobado.
8. Entra como Luis y comprueba que no puede consultar las solicitudes de Ana desde su perfil de empleado.
9. Intenta crear un periodo pasado, uno sin días hábiles, uno que se superponga con una solicitud activa y otro que exceda el saldo. Cada intento debe mostrar un mensaje y conservar los datos anteriores.
10. Cierra y vuelve a abrir la app. Comprueba que la sesión requiere un nuevo acceso y que las solicitudes siguen guardadas.

Este recorrido requiere un emulador o dispositivo; una compilación exitosa no valida por sí sola la navegación ni el comportamiento visual.

## Datos y alcance

La autenticación local de demostración comprueba una contraseña pública compartida por las cuentas de prueba. Permite recorrer los perfiles; no protege una cuenta real. La sesión se elimina al cerrar sesión o terminar el proceso.

`JsonVacationRepository` guarda los datos demo, las solicitudes y su historial en un archivo local mediante sustitución atómica. La aplicación utiliza una instancia de `VacationService` por archivo y proceso. No hay coordinación entre procesos, servidor, sincronización, recuperación remota ni cifrado del archivo. Cada instalación tiene sus propios datos: para probar las aprobaciones, cambia de cuenta en la misma instalación.

No introduzcas datos de empleados reales en esta demostración. El backend futuro debe validar la identidad, los permisos y cada transición, incluso si la interfaz ya realiza esas comprobaciones.
