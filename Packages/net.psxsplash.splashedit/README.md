# SplashEdit

A Unity editor package for building PlayStation 1 games. Design your scenes in Unity, write game logic in Lua, and export to real PS1 hardware or emulator with one click.

## Documentation

Full documentation, tutorials, and Lua API reference at **[psxsplash.github.io/docs](https://psxsplash.github.io/docs/)**.

## Requirements

- Unity 6000.0+ (Universal Render Pipeline project)
- Windows or Linux
- Git

Everything else (MIPS compiler, PCSX-Redux, mkpsxiso) is downloaded automatically through the Control Panel.

## Installation

1. Download the latest `.tgz` from [Releases](https://github.com/psxsplash/splashedit/releases)
2. In Unity: **Window -> Package Manager -> + -> Add package from tarball**
3. Open **PlayStation 1 -> SplashEdit Control Panel** (Ctrl+Shift+L)
4. Install dependencies from the Dependencies tab
5. Add scenes, click **Build & Run**

See the [installation guide](https://psxsplash.github.io/docs/getting-started/installation/) for the full walkthrough.

## Features

- Visual scene editing in Unity
- Automatic texture quantization and VRAM packing
- Lua scripting with full API (entities, UI, audio, cutscenes, animations)
- Navigation mesh generation via DotRecast
- Room/portal occlusion for interior scenes
- Audio conversion to PS1 SPU ADPCM
- Multi-scene support with persistent data
- One-click build to emulator, real hardware (serial), or ISO
- Loading screens

## Contributing

This has been a one-person project and it has grown over my head. Pull requests are very welcome. See the [contributing guide](https://psxsplash.github.io/docs/reference/contributing/) for areas where help is needed.

If you build something with SplashEdit, please share it on [PSX.Dev](https://psx.dev) or the Bandwidth Discord server!

## License

See [LICENSE](LICENSE) for details.
