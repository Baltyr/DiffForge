# DiffForge — Roadmap

Comparador de archivos y carpetas propio en Avalonia + .NET 8. El alcance funcional toma
como referencia el índice público del manual de una herramienta comercial del rubro
(sesiones, vistas, opciones). Es una implementación desde cero: no se reutiliza código,
binarios, íconos, temas ni formatos propietarios de terceros.

## Fase 1 — Text Compare (casi completa)
Detalle función por función en [PARIDAD-TEXT-COMPARE.md](PARIDAD-TEXT-COMPARE.md).
- [x] Motor: Myers en espacio lineal, Patience, Histogram, sin alinear, alineación manual
- [x] Reglas de importancia, gramáticas, reemplazos, texto sin importancia
- [x] Editor completo (AvaloniaEdit) con huecos, recomparación en vivo y deshacer
- [x] Navegación, búsqueda, marcadores, filtros de vista, miniatura, paneles de detalle
- [x] Sesiones, reportes, formatos de archivo, opciones, línea de comandos
- [ ] `LineSimilarity.Score` — **TODO de Allan**
- [ ] Gramática de "encabezados de página" (categoría Lines)
- [ ] Atajos personalizables

## Fase 2 — Folder Compare
- [ ] Árbol de dos carpetas, comparación por tamaño/fecha/CRC/contenido binario/reglas
- [ ] Filtros de vista (todas, diferencias, huérfanos, más nuevos) y filtros por máscara
- [ ] Operaciones: copiar, mover, borrar (a papelera), renombrar, touch, atributos
- [ ] Abrir par de archivos en Text Compare (doble clic)
- [ ] Expandir/colapsar, alineación manual de pares con distinto nombre

## Fase 3 — Merge
- [ ] Text Merge de 3 vías (base + izq + der → salida), resolución de conflictos
- [ ] Folder Merge de 3 vías
- [ ] Integración como mergetool/difftool de git (`git config diff.tool`)

## Fase 4 — Folder Sync
- [ ] Modos: actualizar izq→der, der→izq, ambos, espejo
- [ ] Vista previa de acciones antes de ejecutar; backups antes de sobrescribir

## Fase 5 — Visores especializados
- [ ] Hex Compare (binario byte a byte)
- [ ] Table Compare (CSV/Excel, claves de fila, varias hojas)
- [ ] Picture Compare (diferencia de píxeles, tolerancia)
- [ ] Media/MP3 Compare (metadatos/tags)
- [ ] Version Compare (versión de ejecutables/DLL)
- [ ] Registry Compare (Windows)
- [ ] Clipboard Compare
- [ ] Text Edit y Text Patch (aplicar/ver archivos .diff)
- [ ] Conversión externa: texto extraído de PDF/Word antes de comparar

## Fase 6 — Transversal
- [ ] Home view con sesiones guardadas (JSON propio), importar/exportar sesiones
- [ ] Formatos de archivo (gramáticas por extensión, conversión, codificación)
- [ ] Archivos comprimidos como carpetas (zip, 7z, tar)
- [ ] Remotos: FTP/SFTP, WebDAV, S3, OneDrive/Dropbox
- [ ] Snapshots de carpetas (comparar contra estado guardado)
- [ ] Reportes (HTML, texto, patch)
- [ ] Scripting / modo batch sin UI y programación de tareas
- [ ] Integración con el menú contextual del Explorador (shell extension)
- [ ] Opciones de programa: pestañas, apariencia/tema oscuro, backups, comandos externos
