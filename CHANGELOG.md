# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y
[versionado semántico](https://semver.org/lang/es/). La versión se define en
`Directory.Build.props`.

## [0.5.0] - 2026-10-17

### Añadido
- Pre-escucha por auriculares: botón 🎧 por plato en el mezclador, mezcla de auriculares
  (solo CUE ↔ igual que el master) y volumen de auriculares.
- Desplegable «Auriculares» junto a «Salida» para elegir el dispositivo de pre-escucha.
- Dos modos de salida (Configuración → Preferencias):
  - WASAPI: master y auriculares en dispositivos distintos. La ruta de auriculares corrige
    sola la deriva entre los dos relojes (remuestreo de ±1 % como máximo).
  - ASIO: una tarjeta multicanal, master y auriculares en pares de canales (p. ej. 1-2 y 3-4).
- Estado de la salida de auriculares en la barra superior. Si falla, el master sigue sonando.

### Cambiado
- Cada plato se reparte con splitters de BassMix hacia el master y los auriculares; el de
  auriculares solo lee lo que ya ha pedido el master, así que nunca lo frena ni lo adelanta.

## [0.4.0] - 2026-10-17

### Añadido
- Barra de menú: Archivo, Configuración, Ver y Ayuda.
- Ventana de preferencias (idioma, salida de audio, carpeta de caché).
- Interfaz en español e inglés, seleccionable al momento sin reiniciar. La primera vez
  se elige según el idioma de Windows.
- Las preferencias se guardan en `%APPDATA%\DJTube\settings.json` (idioma, salida,
  zoom de la onda y curva del crossfader).
- Ventanas «Acerca de» (versión, compilación, librerías) y «Atajos de teclado».
- Atajos: Ctrl+1 / Ctrl+2 (cargar archivo), Ctrl++ / Ctrl+- (zoom), Ctrl+, (preferencias), F1.
- Número de versión en el título de la ventana.
- Barra de título oscura.
- Registro de errores inesperados en `%LOCALAPPDATA%\DJTube\errors.log`; un error de la
  interfaz ya no cierra el programa.

## [0.3.0] - 2026-10-17

### Añadido
- Detección de BPM y rejilla de beats en segundo plano al cargar.
- SYNC: iguala el tempo (con medio y doble tempo) y alinea los golpes si los dos platos suenan.
- Vista ampliada A/B al estilo AtomixMP3 con líneas de beat, y forma de onda completa por plato.
- Miniatura del vídeo de YouTube encima del título.
- Corrección del BPM detectado (½ y ×2).

## [0.2.1] - 2026-10-17

### Añadido
- Carga de pistas desde enlaces de YouTube (yt-dlp) con caché local y progreso de descarga.

## [0.2.0] - 2026-10-17

### Añadido
- Dos platos, mezclador con faders de canal y crossfader (lineal, corte rápido, potencia constante).
- Pitch fader ±8 % / ±16 % y key lock.

## [0.1.0] - 2026-10-17

### Añadido
- Motor de audio sobre BASS con salida WASAPI y un plato: carga de archivos locales,
  play/pausa, cue estilo CDJ y posición.
