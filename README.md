# DJTube

Aplicación de DJ de escritorio para Windows, sencilla y ligera, con dos platos,
crossfader, pitch con key lock, SYNC, forma de onda y pre-escucha por auriculares.

## Requisitos

- Windows 10 2004 o superior (x64).
- SDK de .NET 10.

## Compilar y ejecutar

```
dotnet build DJTube.slnx
dotnet test DJTube.slnx
dotnet run --project src/DJTube.App
```

## Estructura

| Proyecto | Contenido |
|---|---|
| `src/DJTube.Core` | Modelos, interfaces y lógica pura (sin dependencias de audio) |
| `src/DJTube.Audio.Bass` | Motor de audio sobre BASS (ManagedBass) |
| `src/DJTube.Sources.Local` | Fuente de pistas: archivos locales |
| `src/DJTube.App` | Interfaz WPF (MVVM) |
| `tests/DJTube.Core.Tests` | Tests unitarios (xUnit) |

## Licencia de BASS

`lib/native/x64` incluye las DLL de [BASS](https://www.un4seen.com) y sus complementos
(bass_fx, bassmix, basswasapi, bassasio, bassflac), propiedad de Un4seen Developments.
BASS es gratuito **solo para uso no comercial**. Si quieres usar este código en un
producto con el que se obtengan ingresos (ventas, publicidad, etc.), necesitas tu propia
licencia comercial de BASS: consulta las condiciones en www.un4seen.com.
