<p align="center">
  <img src="src/DiffForge.App/Assets/diffforge-256.png" width="96" alt="Logo de DiffForge" />
</p>

<h1 align="center">DiffForge</h1>

<p align="center">Comparador de archivos de texto para Windows, Linux y macOS · Avalonia + .NET 8</p>

## Funciones
- Dos editores alineados con huecos, scroll sincronizado y miniatura de diferencias.
- Diferencias dentro de la línea (por palabra y por carácter); importantes en rojo y menores en azul.
- Reglas de importancia: espacios, mayúsculas, comentarios y cadenas por gramática, texto sin importancia y reemplazos.
- Alineación Myers, Patience y Histogram, sin alinear, o manual ("Alinear con…").
- Edición en vivo con recomparación, deshacer/rehacer, copiar secciones entre lados y guardado byte a byte.
- Búsqueda con regex, marcadores, filtros (diferencias, iguales, contexto), sesiones `.dfsession` y reportes HTML, texto o parche.
- Tema claro/oscuro/sistema. Detalle función por función en [docs/PARIDAD-TEXT-COMPARE.md](docs/PARIDAD-TEXT-COMPARE.md).

## Uso
```
DiffForge izq der [/title1=…] [/title2=…] [/ro] [/ro1] [/ro2]
DiffForge sesion.dfsession
```
Atajos: Ayuda ▸ Atajos de teclado (F1). Configuración en `%APPDATA%\DiffForge\settings.json`
(`DIFFFORGE_HOME` la redirige, útil para modo portable).

## Desarrollo
```powershell
dotnet build
dotnet test
dotnet run --project src/DiffForge.App -- archivoA.txt archivoB.txt
```

```
src/DiffForge.Core            motor: documento fiel byte a byte, gramáticas, alineación, importancia, reportes
src/DiffForge.App             UI Avalonia + AvaloniaEdit: vista Text Compare, comandos, diálogos
tests/DiffForge.Core.Tests    tests del motor
tests/DiffForge.App.Tests     tests de UI sin pantalla (Avalonia.Headless): todos los comandos, casos borde
build/                        publicación e instalador
```

## Publicar
```powershell
./build/publish.ps1            # tests + dist/win-x64/DiffForge.exe (autocontenido) + .zip portable
iscc build/DiffForge.iss       # opcional, con Inno Setup 6: instalador dist/DiffForge-<versión>-setup.exe
```

El logo es vectorial (`Services/AppIcon.cs`); para regenerar `Assets/`:
```powershell
$env:DIFFFORGE_ASSETS = "src/DiffForge.App/Assets"; dotnet test tests/DiffForge.App.Tests --filter Branding
```
