# Paridad funcional — Text Compare

Inventario tomado del índice de funcionalidades del manual de referencia (comandos, ajustes de
sesión, formatos, opciones). La implementación es propia; la interfaz y los atajos son de DiffForge.

Leyenda: ✅ hecho y probado en la app · 🟢 hecho, cubierto por tests o build pero no probado a mano en la UI ·
🟡 parcial · ⛔ no disponible (depende de otro tipo de sesión o de una integración externa)

Todos los comandos 🟢 de los menús se ejecutan además en `tests/DiffForge.App.Tests` (UI sin pantalla):
en ambos paneles, dos veces seguidas y en secuencia sobre archivos límite (huecos al inicio/fin, CR, tabs,
líneas de 20 000 caracteres, unicode, sin salto final), verificando que no haya excepciones ni desalineación.

## Vista
| Función | Estado | Nota |
|---|---|---|
| Dos editores que hacen scroll juntos (vertical y horizontal) | ✅ | Sincroniza por línea, así que también funciona con ajuste de línea |
| Diseño lado a lado / arriba-abajo | 🟢 | |
| Colores: rojo importante, azul menor, fondo de línea | ✅ | Configurables por tema claro/oscuro |
| Diferencias dentro de la línea | ✅ | Por palabras y después por carácter |
| Miniatura con viewport, línea actual y clic para navegar | ✅ | |
| Detalle de texto / hex / alineación + regla | ✅ / 🟢 / 🟢 / 🟢 | |
| Información de archivo (fecha, tamaño, encoding, EOL, formato) | ✅ | |
| Ajuste de línea, espacios visibles, números de línea, sintaxis | 🟢 | |
| Fuente, aumentar/disminuir/restablecer tamaño | 🟢 | |
| Mostrar todo / diferencias / iguales / contexto / nada | 🟢 | Cada tramo oculto deja una fila "⋯" (con o sin contador) |
| Ignorar diferencias sin importancia | 🟢 | |
| Páginas web | ⛔ | Avalonia no trae un WebView |

## Edición
| Función | Estado | Nota |
|---|---|---|
| Edición completa en ambos paneles con recomparación en vivo | ✅ | Debounce de 350 ms |
| Deshacer / rehacer | ✅ | Por instantáneas; agrupa ráfagas de escritura y operaciones |
| Copiar a la derecha / izquierda / al otro lado (sección o selección) | ✅ | |
| Copiar línea a la derecha / izquierda / al otro lado | 🟢 | |
| Alinear con… | 🟢 | Comando y luego clic en la línea del otro lado |
| Aislar | 🟢 | |
| Agregar reemplazo desde la diferencia | 🟢 | |
| Aumentar/disminuir sangría, Backspace des-sangra, sangría automática | 🟢 | |
| Eliminar línea / hasta inicio o fin de línea / palabra / hasta inicio o fin de palabra | 🟢 | |
| Insertar línea antes/después, seleccionar todo, seleccionar sección | 🟢 | |
| Comparar selección con el portapapeles | 🟢 | Se abre en una pestaña nueva |
| Convertir: quitar espacios finales, espacios↔tabs, finales de línea | 🟢 | |
| Ignorado (suprimir diferencias de la selección o sección) | 🟢 | |
| Edición deshabilitada (solo lectura, zip, http, opción de sesión, /ro) | 🟢 | |

## Búsqueda y navegación
| Función | Estado | Nota |
|---|---|---|
| Siguiente/anterior sección con diferencias | ✅ | Con opción de volver al inicio |
| Siguiente/anterior diferencia (dentro de líneas largas) | 🟢 | |
| Siguiente/anterior edición y reemplazo | 🟢 | |
| Buscar: regex, mayúsculas, palabra completa, continuar al inicio, lados, cerrar al buscar | 🟢 | |
| Insertar elemento de regex desde un menú | 🟢 | |
| Seleccionar todas las líneas que coinciden | 🟡 | Marca todas las coincidencias, pero el editor no admite selección múltiple |
| Reemplazar / reemplazar todo, buscar siguiente/anterior | 🟢 | |
| Ir a línea[:columna] | 🟢 | |
| Marcadores 0–9 (alternar, ir, quitar) | 🟢 | |
| Siguientes/anteriores archivos con diferencias, copiar archivo y abrir siguiente | ⛔ | Requieren Folder Compare |

## Sesión y archivos
| Función | Estado | Nota |
|---|---|---|
| Guardar/abrir sesión (.dfsession) | 🟢 | |
| Configuración de sesión: Specs, Formato, Importancia, Alineación, Reemplazos | 🟢 | |
| Bloquear, limpiar, intercambiar lados, recargar, recomparar | 🟢 | |
| Reporte lado a lado / intercalado / parche, en HTML o texto, a archivo / portapapeles / navegador | 🟢 | El parche está cubierto por test |
| Información de la comparación | 🟢 | |
| Abrir archivo, portapapeles, arrastrar y soltar | 🟢 | |
| Origen dentro de .zip y URL http(s) | 🟢 | Solo lectura |
| Guardar, guardar como, guardar ambos, con backup | 🟢 | Round-trip byte a byte testeado |
| Abrir con aplicación asociada, mostrar en el explorador | 🟢 | |
| Comparar usando, unir archivos, comparar carpetas padre | ⛔ | Otros tipos de sesión (fases 2 y 3) |
| Text Edit / Text Patch / Check In / Check Out | ⛔ | |
| Perfiles remotos (FTP, S3, etc.) | ⛔ | Fase 6 |
| Línea de comandos: izq der /title1 /title2 /ro /ro1 /ro2, o una sesión | 🟢 | |

## Reglas (motor)
| Función | Estado | Nota |
|---|---|---|
| Alineación: sin alinear, estándar, Myers, Patience | 🟢 | Tests |
| Nunca alinear diferencias, tolerancia de desfase, emparejar por similitud | 🟡 | La similitud es el **TODO de Allan** |
| Importancia por elemento de gramática, espacios al inicio/medio/final, resto, mayúsculas | 🟢 | Tests |
| Lista de texto sin importancia (regex o literal) | 🟢 | Test |
| Las líneas huérfanas siempre son importantes; comparar finales de línea | 🟢 | Tests |
| Reemplazos (lado, regex, palabra completa, mayúsculas) | 🟢 | Test |
| Codificación forzada por sesión o por formato | 🟢 | |

## Formatos de archivo
| Función | Estado | Nota |
|---|---|---|
| Máscaras, descripción, detección automática | 🟢 | Test |
| Gramática: Básico, Delimitado (multilínea y escape), Lista, Columnas | 🟢 | Tests |
| Categoría "Lines" (encabezados de página repetidos) | ⛔ | Pendiente |
| Formatos incluidos: C#, C/C++, Java/Kotlin, JS/TS, SQL, XML/HTML, JSON, Python, PowerShell, INI/YAML, texto plano | 🟢 | |
| Conversión con programa externo (%s %t %o), deshabilitar edición | 🟢 | No probado con un conversor real |
| Ctrl+Z como fin de archivo, límite de caracteres por línea, quitar espacios y convertir a tabs al guardar | 🟢 | |
| Tabulación, insertar espacios, líneas independientes, datos por columnas | 🟢 | |

## Opciones del programa
| Función | Estado | Nota |
|---|---|---|
| Edición de texto (sangría automática, Backspace, buscar con la palabra actual, contador de filtradas, líneas de contexto) | 🟢 | |
| Siguiente diferencia (primera al cargar, tras copiar, volver al inicio, mensaje) | ✅ / 🟢 | |
| Apariencia: tema claro/oscuro/sistema, colores por elemento, franjas | 🟢 | |
| Copias de seguridad (carpeta y sufijo) | 🟢 | |
| Atajos personalizables | ⛔ | Hoy son fijos; están en la tabla `AppCommands` |
