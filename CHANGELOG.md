# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y
[versionado semántico](https://semver.org/lang/es/). La versión se define en
`Directory.Build.props`.

## [Sin publicar]

### Añadido
- Manual de usuario en PDF con capturas (`docs/DJTube-Manual.pdf`), enlazado desde el README.
- Variable de entorno `DJTUBE_DATA`: si se define, DJTube guarda en esa carpeta preferencias, caché,
  listas y grabaciones (para llevar la biblioteca en un USB o probar sin tocar los datos habituales).

### Corregido
- Al añadir la primera canción a una biblioteca vacía, las columnas Artista y Título se quedaban
  encogidas.
- «1 canción» en lugar de «1 canciones» (biblioteca y cola del Automix).

## [0.10.0] - 2026-10-18

### Añadido
- Hot cues: 8 por plato (clic para fijar o saltar, clic derecho para borrar), guardados por
  canción y marcados en las ondas.
- Loops sin cortes: automático de 1/8 a 32 tiempos, manual IN/OUT, mitad/doble, reloop y loop roll.
- Volumen igualado: el análisis mide la sonoridad (LUFS) y cada canción se lleva al mismo
  volumen (ganancia automática, objetivo configurable).
- Sección master: volumen, limitador de picos (la salida y la grabación no saturan) y VU.
- Tonalidad de cada canción en notación Camelot, en la pletina y en la biblioteca; en verde las
  que encajan con lo que suena (mezcla armónica).
- Buscar en YouTube desde la biblioteca (botón «YouTube» o Intro) y cargar, añadir o pre-escuchar
  los resultados.
- Pre-escucha 🎧 de cualquier canción solo por los auriculares, sin ocupar un plato.
- Efectos por plato a tempo: eco, reverb, flanger y phaser.
- Historial de la sesión (Ctrl+H) y, al grabar, lista de temas (.txt y .cue) junto a la grabación;
  conversión automática a MP3 320 kbps o M4A.
- Automix: cola de canciones con fundidos al mismo tempo y en fase.
- Controladoras MIDI con modo «aprender» (cualquier marca): platos, mezclador, EQ, efectos, hot
  cues, loops, jog, biblioteca, grabación y Automix.
- Temas de color: violeta, blanco, gris Windows 98 y azul.
- Preferencias por pestañas: búfer de audio, limitador, ganancia automática, rango de pitch,
  cuantizar, bloquear el plato que suena, Automix, formato de grabación, límite de caché y atajos
  de teclado personalizables.
- Auriculares en modo split para quien tiene una sola salida (izquierda master, derecha
  pre-escucha).

### Cambiado
- La caché de análisis pasa a la versión 2 (con sonoridad y tonalidad); las canciones ya
  analizadas se completan solas en segundo plano.

## [0.9.0] - 2026-10-17

### Añadido
- Grabación de la sesión: barra bajo la biblioteca con grabar, pausa y detener, tiempo, tamaño,
  espacio libre y nivel. Graba exactamente lo que suena por el master en WAV (calidad CD) en
  Música\DJTube (se puede cambiar en Preferencias).
- La grabación está blindada: se vuelca al disco cada segundo, se guarda sola al salir, al apagar
  Windows o al actualizar, y si DJTube se cierra de golpe se repara al volver a abrirlo. Al llegar
  al límite del formato WAV sigue en un archivo nuevo.
- Listas de reproducción de YouTube: pega el enlace de una lista (o de una canción) en el campo de
  la biblioteca, o suéltalo encima, y se descarga entera, de dos en dos, con progreso y cancelar.
  Los «Mix» automáticos se limitan a sus 50 primeras canciones y se pide confirmación con listas
  muy grandes.
- La biblioteca se muestra en árbol: cada lista es una rama que se despliega con sus canciones, en
  su orden; las que no son de ninguna lista van en «Canciones sueltas». Menú de la rama: actualizar
  la lista, quitarla o quitarla con sus canciones. Una canción puede estar en varias listas.
- El BPM de las canciones descargadas se calcula en segundo plano, sin cargarlas en un plato.
- Tamaño de letra de la biblioteca (botones A− / A+ o Ctrl+rueda) para ver más canciones a la vez.
- Rueda del ratón en faders, crossfader y mandos, también con el clic pulsado (Mayús: ajuste fino).

### Cambiado
- Más contraste: paneles con borde, botones y campos con filo, y brillo al pasar el ratón.
- El estado de yt-dlp y su botón pasan a la barra superior; la barra de abajo deja sitio a la biblioteca.
- README: lo que distingue a DJTube, pinchar directamente con YouTube.

## [0.8.0] - 2026-10-17

### Añadido
- Biblioteca bajo los platos con todo lo descargado: miniatura, artista, título, duración y BPM,
  con búsqueda. Se carga con los botones A / B, con doble clic (al plato que no suena) o
  arrastrando la fila a un plato. F2 edita artista y título; Supr la quita (y borra el audio).
  Ver → Biblioteca (Ctrl+L) la oculta, y el divisor ajusta su alto.
- Artista y título se separan a partir del título de YouTube (quitando «Official Video», «Lyrics»,
  «HD»…); las descargas anteriores se convierten solas.
- Mezclador con GAIN (±12 dB), EQ de 3 bandas HIGH / MID / LOW (de corte total a +6 dB), FILTER
  (paso bajo / paso alto) y medidor VU estéreo por canal. Mandos giratorios: arrastrar o rueda,
  doble clic al centro; ⟲ pone a cero el canal. La pre-escucha también suena ecualizada.

### Cambiado
- La forma de onda se colorea por frecuencias (graves en rojo, medios en verde, agudos en azul),
  en la vista ampliada y en la de cada pletina.
- La onda y el BPM se guardan en caché: volver a cargar una canción es instantáneo, y las
  correcciones de BPM (½ / ×2) se recuerdan.
- Pletinas más compactas: ½, ×2 y SYNC están junto a CUE y PLAY. La mezcla y el volumen de
  auriculares son mandos giratorios.

## [0.7.0] - 2026-10-17

### Añadido
- Aviso de actualizaciones: al arrancar (y desde Ayuda → «Buscar actualizaciones…») DJTube
  consulta la última release publicada en GitHub y, si hay una versión nueva, lo indica en una
  franja bajo el menú con «Novedades», «Actualizar ahora» y «Más tarde».
- Actualización automática: descarga el instalador con progreso, comprueba su huella SHA-256,
  cierra DJTube, instala en silencio y lo vuelve a abrir. Si hay música sonando, pide confirmación.
  Solo acepta descargas de las releases de `rboluda/DJTube`.
- En la versión portable, el botón abre la página de descarga.
- Preferencias: «Buscar actualizaciones al iniciar».

## [0.6.1] - 2026-10-17

### Añadido
- Instalador (`DJTube-Setup-<versión>.exe`, Inno Setup) y ZIP portable que no necesitan instalar
  nada más: llevan .NET, las librerías de audio, yt-dlp y Deno. El instalador no pide permisos de
  administrador, crea el acceso directo y se desinstala desde «Aplicaciones».
- `build/publish.ps1`: genera los dos paquetes en `artifacts/`.
- `THIRD-PARTY-NOTICES.txt` con las licencias de los componentes incluidos.

### Cambiado
- El ejecutable se llama `DJTube.exe`.
- DJTube usa primero el yt-dlp y el Deno de su carpeta `tools`.

## [0.6.0] - 2026-10-17

### Añadido
- Logo de DJTube: icono del programa (ejecutable, barra de título y barra de tareas), junto al
  menú y en «Acerca de».
- `assets/`: logo original, logo con fondo transparente e icono `.ico` con todos los tamaños.
- `tools/BrandAssets`: genera el logo transparente y el icono a partir de `assets/logo-original.png`.

### Cambiado
- Nueva paleta sacada del logo: fondos morado oscuro, plato A en naranja, plato B en violeta y
  marcas de cue en amarillo (también en las formas de onda).

## [0.5.1] - 2026-10-17

### Cambiado
- ASIO: si la tarjeta no tiene los canales elegidos para auriculares, el master suena igual y
  solo se avisa del problema de auriculares (probado con ASIO4ALL).
- «Acerca de» indica que el programa está desarrollado por Raúl Boluda (también en los datos
  del ejecutable).

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
