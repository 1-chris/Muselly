# Third-party notices

Muselly itself is licensed under the MIT License. It bundles the following third-party components.

## FFmpeg

Muselly bundles an `ffmpeg` executable, which it invokes as a separate process to decode compressed audio
to PCM and to transcode to Opus (for the integrated server). It is **not** linked into Muselly's own code.

- **Project:** FFmpeg — https://ffmpeg.org
- **License:** GNU Lesser General Public License, version 2.1 or later (LGPL-2.1-or-later).
  The bundled build is configured **without** `--enable-gpl` and **without** `--enable-nonfree`, so it is
  LGPL — not GPL — and contains no non-free components. It statically links **libopus** (see below) to
  enable Opus encoding.
- **Source:** the exact released source for the bundled version is available at
  https://ffmpeg.org/releases/ (e.g. `ffmpeg-<version>.tar.xz`).
- **How it was built:** reproducibly, from that source, by [`scripts/build-ffmpeg.sh`](scripts/build-ffmpeg.sh)
  in this repository. That script is the complete corresponding build recipe.
- The FFmpeg licence text is shipped next to the binary (`COPYING.LGPLv2.1`).

Because Muselly only executes FFmpeg as a standalone program (no static or dynamic linking of FFmpeg's
libraries into Muselly), Muselly's MIT licensing is unaffected, and the LGPL obligations are satisfied by
redistributing the unmodified, separately-licensed binary together with its licence text and a pointer to
its source and build recipe above.

## libopus

Statically linked into the bundled `ffmpeg` binary to provide Opus encoding for the integrated server's
on-the-fly transcoding.

- **Project:** Opus — https://opus-codec.org
- **License:** BSD 3-Clause (LGPL-compatible; does not affect FFmpeg's LGPL classification).
- **Source / build:** fetched and built reproducibly by [`scripts/build-ffmpeg.sh`](scripts/build-ffmpeg.sh).

## Mono.Nat

A NuGet library used by `Muselly.Server` to optionally open the server's port on the router via UPnP/NAT-PMP.

- **Project:** Mono.Nat — https://github.com/alanmcgovern/Mono.Nat
- **License:** MIT.

## Noto Sans JP

The browser head (`Muselly.Web`) embeds the **Noto Sans JP** font so Japanese (and other CJK) text renders in
the WebAssembly sandbox, where the OS's fonts are not available to the renderer.

- **Project:** Noto Sans CJK / Noto Sans JP — https://github.com/notofonts/noto-cjk
- **License:** SIL Open Font License 1.1 (OFL-1.1).
- The font file is redistributed unmodified at `Muselly.Web/Assets/Fonts/NotoSansJP-Regular.ttf`.
