<p align="center"><img src="assets/logo.png" alt="DJTube" width="160"></p>

# DJTube

**Pincha directamente con YouTube.** Pega el enlace de un vídeo (o de una lista de reproducción)
en una pletina y mezcla: DJTube baja solo el audio, sin anuncios, lo analiza (BPM y forma de onda)
y lo guarda en su biblioteca para la próxima vez. No hace falta tener la música en archivos ni
convertirla antes con otros programas: eso es lo que lo diferencia de otros programas de DJ.

> *English:* **DJ straight from YouTube.** Paste a video or playlist link onto a deck and mix: DJTube
> fetches the audio only (no ads), analyses BPM and waveform, and keeps it in its library. Two decks,
> 3-band EQ with kills, filter, sync, headphone cue and session recording. Interface in English and Spanish.

Aplicación de DJ de escritorio para Windows, sencilla y ligera, con dos platos, mezclador con
EQ de 3 bandas, filtro y VU, crossfader, pitch con key lock, SYNC, forma de onda coloreada por
frecuencias, biblioteca de canciones descargadas, grabación de la sesión y pre-escucha por auriculares.
También carga archivos locales (MP3, FLAC, WAV, AAC, M4A).

Interfaz en español e inglés (Configuración → Idioma). Cambios por versión en
[CHANGELOG.md](CHANGELOG.md).

📖 **[Manual de usuario (PDF)](docs/DJTube-Manual.pdf)**: todas las funciones explicadas paso a paso, con capturas.

## Instalación

Descarga la última versión desde [Releases](https://github.com/rboluda/DJTube/releases):

- **`DJTube-Setup-<versión>.exe`** (recomendado): instalador. No pide permisos de administrador,
  crea el acceso directo en el menú Inicio y se desinstala desde «Aplicaciones» de Windows.
- **`DJTube-<versión>-win-x64-portable.zip`**: descomprime la carpeta y ejecuta `DJTube.exe`.

No hace falta instalar nada más: el paquete lleva .NET, las librerías de audio, yt-dlp y Deno.
Requiere Windows 10 2004 o superior (64 bits). Al no estar firmado digitalmente, la primera
vez Windows SmartScreen puede avisar: «Más información» → «Ejecutar de todas formas».

## Generar los paquetes

```
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

Deja el instalador y el ZIP en `artifacts/` (necesita Inno Setup 6: `winget install JRSoftware.InnoSetup`).

## Requisitos para desarrollar

- Windows 10 2004 o superior (x64).
- SDK de .NET 10.
- Para cargar enlaces de YouTube al ejecutar desde el código: [yt-dlp](https://github.com/yt-dlp/yt-dlp)
  (`winget install yt-dlp.yt-dlp`, que instala también Deno). Los paquetes ya lo incluyen.

## Compilar y ejecutar

Desde la carpeta del proyecto:

```
dotnet build DJTube.slnx
dotnet test DJTube.slnx
dotnet run --project src/DJTube.App
```

## Cargar pistas

- **Enlace de YouTube:** pégalo en el campo de la pletina y pulsa Intro (o arrástralo desde
  el navegador). Se descarga solo el audio (M4A/AAC) a `%LOCALAPPDATA%\DJTube\Cache\YouTube`;
  la siguiente vez carga al instante. Si las descargas dejan de funcionar, pulsa
  «Actualizar yt-dlp»: YouTube cambia a menudo.
- **Archivo local:** botón «ARCHIVO…» o arrastrar el archivo (MP3, FLAC, WAV, AAC, M4A).

## Biblioteca

Debajo de los platos está todo lo descargado (Ver → Biblioteca o Ctrl+L para ocultarla; el
divisor cambia su alto). Artista y título salen del título del vídeo y se pueden corregir con F2.

- **Añadir:** pega en el campo de la biblioteca el enlace de una canción o de una **lista de
  reproducción** (o suéltalo encima) y pulsa Intro. Las listas se descargan enteras y aparecen como
  una rama que se despliega con sus canciones; las demás van en «Canciones sueltas».
- **Cargar:** botones A / B de cada fila, doble clic (va al plato que no está sonando) o
  arrastrar la fila a un plato.
- **Buscar:** escribe parte del artista o del título.
- **Quitar:** Supr o menú contextual; borra el audio de la caché. En la rama de una lista, el menú
  permite actualizarla (bajar las canciones nuevas) o quitarla.
- **Tamaño de letra:** A− / A+ o Ctrl+rueda, para ver más canciones a la vez.
- El BPM se calcula solo, en segundo plano.

## Hot cues, loops y efectos

- **Hot cues:** 8 botones de color por plato. Clic en uno vacío lo fija en ese punto (en el golpe si
  está activado cuantizar); clic en uno lleno salta a él; clic derecho lo borra. Se guardan por canción.
- **Loops:** LOOP repite los tiempos elegidos (◂ ▸ cambian el tamaño), IN/OUT hacen un loop manual,
  ⟲ sale o vuelve a entrar, y ROLL repite mientras se mantiene y luego sigue donde iría la canción.
- **Efectos:** eco, reverb, flanger o phaser por plato, con cantidad y tiempos a tempo con el BPM.

## Automix, MIDI y teclado

- **Automix:** pon canciones en la cola (menú de la canción o «Automix con esta lista») y pulsa
  AUTOMIX: carga la siguiente en el otro plato y hace el fundido al mismo tempo.
- **Controladoras MIDI:** Configuración → Controladora MIDI. Pulsa «Aprender» junto a una acción y
  mueve el control de la controladora. Sirve para cualquier marca.
- **Teclado:** las teclas de los platos se cambian en Preferencias → Teclado (F1 muestra las de serie).

## Grabar la sesión

La barra de abajo graba lo que suena por el master en WAV (calidad CD), con pausa. Los archivos van a
Música\DJTube (Configuración → Preferencias para cambiarlo); al detenerla se crean también el MP3
(o M4A) y la lista de temas con sus tiempos (.txt y .cue). Se guarda en disco cada segundo: si
DJTube se cierra de golpe, al volver a abrirlo repara la grabación y avisa.

Descargar contenido de YouTube va en contra de sus condiciones de uso. Usa esta función
bajo tu responsabilidad y solo con contenido que tengas derecho a utilizar.

## Pre-escucha por auriculares

- **WASAPI** (por defecto): elige en la barra superior la «Salida» (master) y los
  «Auriculares» (otro dispositivo, p. ej. la salida de cascos de la tarjeta o unos
  auriculares USB).
- **ASIO** (Configuración → Preferencias → Modo de salida): una tarjeta de 4 canales;
  elige el par del master (p. ej. 1-2) y el de auriculares (p. ej. 3-4).
- **Una sola salida:** en «Auriculares» elige «Split en esta salida»: por el oído izquierdo suena el
  master y por el derecho la pre-escucha (con un cable divisor o unos cascos).

En el mezclador, el botón 🎧 de cada plato lo manda a los auriculares, independientemente de
faders y crossfader. El mando «MEZCLA» va de solo los platos con 🎧 (CUE) a lo mismo que el master
y «VOL» es el volumen de los auriculares.

## Mezclador

Cada canal tiene GAIN (±12 dB), HIGH / MID / LOW (de corte total a +6 dB), FILTER (a la izquierda
paso bajo, a la derecha paso alto, en el centro apagado), fader y medidor VU. Los mandos se giran
arrastrando arriba/abajo o con la rueda (Mayús: ajuste fino); doble clic los devuelve al centro y
⟲ pone a cero todo el canal. El MASTER tiene volumen, limitador y VU, y cada canción sale al mismo
volumen gracias a la ganancia automática. La tonalidad aparece en notación Camelot: en la biblioteca,
en verde las que encajan con lo que suena.

Temas de color (violeta, blanco, gris Windows 98 y azul) en Configuración → Preferencias.

## Estructura

| Proyecto | Contenido |
|---|---|
| `src/DJTube.Core` | Modelos, interfaces y lógica pura (sin dependencias de audio) |
| `src/DJTube.Audio.Bass` | Motor de audio sobre BASS (ManagedBass) |
| `src/DJTube.Sources.Local` | Fuente de pistas: archivos locales |
| `src/DJTube.Sources.YouTube` | Fuente de pistas: enlaces de YouTube (yt-dlp + caché) |
| `src/DJTube.App` | Interfaz WPF (MVVM) |
| `tests/DJTube.Core.Tests` | Tests unitarios (xUnit) |

## Traducciones

Los textos están en archivos `.resx` (inglés como idioma neutro, español en `*.es.resx`):

- `src/DJTube.App/Resources/Strings*.resx`: interfaz.
- `src/DJTube.Core/Resources/Messages*.resx`: mensajes de error de las librerías.

## Logo e icono

El logo original está en `assets/logo-original.png`. Si cambia, se regeneran el logo transparente
y el icono con:

```
dotnet run --project tools/BrandAssets -- assets/logo-original.png assets src/DJTube.App/Assets
```

## Versión

La versión del programa está en `Directory.Build.props` (`<Version>`); al compilar se le
añade el commit de git y se muestra en «Ayuda → Acerca de DJTube».

## Licencia de BASS

`lib/native/x64` incluye las DLL de [BASS](https://www.un4seen.com) y sus complementos
(bass_fx, bassmix, basswasapi, bassasio, bassflac), propiedad de Un4seen Developments.
BASS es gratuito **solo para uso no comercial**. Si quieres usar este código en un
producto con el que se obtengan ingresos (ventas, publicidad, etc.), necesitas tu propia
licencia comercial de BASS: consulta las condiciones en www.un4seen.com.
