# Third-party notices

`MiniPress.exe` contains a statically linked FFmpeg build. MiniPress unpacks that build to `%LOCALAPPDATA%\MiniPress` and runs it as a separate program. The build includes the following projects.

| Component | Version | License | Source |
|---|---|---|---|
| [FFmpeg](https://ffmpeg.org) | 7.1.2 | LGPL-2.1-or-later. This build is configured with `--enable-gpl`, so the FFmpeg binary as a whole is **GPL-2.0-or-later** | `ffmpeg-7.1.2.tar.xz`, attached to each release ([upstream](https://ffmpeg.org)) |
| [x264](https://www.videolan.org/developers/x264.html) | 0.165 (stable branch) | GPL-2.0-or-later | `x264-stable.tar.bz2`, attached to each release |
| [dav1d](https://code.videolan.org/videolan/dav1d) | 1.5.5 | BSD-2-Clause, see [licenses/dav1d-BSD-2-Clause.txt](licenses/dav1d-BSD-2-Clause.txt) | [code.videolan.org/videolan/dav1d](https://code.videolan.org/videolan/dav1d) |
| MinGW-w64 runtime and GCC runtime | from MSYS2 UCRT64 | Permissive licenses / GCC Runtime Library Exception | [mingw-w64.org](https://www.mingw-w64.org) |

The full text of the GNU General Public License v2 is in [licenses/GPL-2.0.txt](licenses/GPL-2.0.txt).

The exact configuration used to build the FFmpeg binary is [tools/build-ffmpeg.sh](tools/build-ffmpeg.sh). Running that script on the source archives attached to a release rebuilds the FFmpeg inside that release's `MiniPress.exe`.

MiniPress's own source code (everything else in this repository) is licensed under the [MIT License](LICENSE).
