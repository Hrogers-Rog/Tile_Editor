# Portable Runtime Notices

The Windows portable edition contains a frozen 64-bit CPython runtime so the
desktop Tile Editor can run without a system Python installation. It also
contains the runtime libraries required by the editor: pygame-ce, NumPy, Pillow,
Requests, SciPy, Certifi, Charset Normalizer, IDNA, and urllib3.

The `PortableRuntime/licenses` folder contains the Python license and the license
or package metadata distributed with each bundled dependency. PyInstaller is
used only to assemble the application; its bootloader license notice is included
there as well.

The portable runtime is used only by the desktop editor. Railroader still loads
`Hrogers.TileEditorBridge.dll` normally through Unity Mod Manager.
