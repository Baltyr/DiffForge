# DiffForge

Comparador de archivos de texto en Avalonia + .NET 8 (Windows, Linux, macOS).

```
src/DiffForge.Core     motor puro: documento fiel byte a byte, gramáticas, alineación, importancia, reportes
src/DiffForge.App      UI Avalonia + AvaloniaEdit: vista Text Compare, comandos, diálogos
tests/DiffForge.Core.Tests
docs/ROADMAP.md               fases
docs/PARIDAD-TEXT-COMPARE.md  checklist de funcionalidades de Text Compare
```

```powershell
dotnet build
dotnet test
dotnet run --project src/DiffForge.App -- archivoA.txt archivoB.txt
```

Línea de comandos: `DiffForge izq der [/title1=…] [/title2=…] [/ro] [/ro1] [/ro2]` o `DiffForge sesion.dfsession`.
Configuración: `%APPDATA%\DiffForge\settings.json`. Atajos: Ayuda ▸ Atajos de teclado (F1).
