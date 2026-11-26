#!/usr/bin/env bash
# Builds the small, fully static ffmpeg.exe that MiniPress embeds (assets/ffmpeg.exe.gz).
#
# Run in an MSYS2 "UCRT64" shell that has:
#   pacman -S mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-nasm mingw-w64-ucrt-x86_64-pkgconf \
#             mingw-w64-ucrt-x86_64-dav1d make diffutils
# Usage:
#   tools/build-ffmpeg.sh <dir containing ffmpeg-7.1.2.tar.xz and x264-stable.tar.bz2> <work dir>
# Official project sites (download the versioned source archives yourself):
#   FFmpeg 7.1.2 from https://ffmpeg.org
#   x264 stable from https://code.videolan.org/videolan/x264
# Place ffmpeg-7.1.2.tar.xz and x264-stable.tar.bz2 in the first argument directory.
#
# Only what MiniPress needs is enabled: decoders for common video/audio, the MP4 muxer,
# libx264 + the native AAC encoder, and the few filters the app uses.
set -euo pipefail
set -f # component names like pcm_* are patterns for ffmpeg's configure, not file globs

FFMPEG_VERSION=7.1.2
SRC=$(realpath "$1")
WORK=$(realpath -m "$2")
PREFIX="$WORK/prefix"
JOBS=$(nproc)
mkdir -p "$WORK"
cd "$WORK"

echo "== x264"
rm -rf x264 && mkdir x264
tar -xf "$SRC/x264-stable.tar.bz2" -C x264 --strip-components=1
(
  cd x264
  ./configure --prefix="$PREFIX" --enable-static --disable-cli --enable-strip \
    --bit-depth=8 --chroma-format=420 --enable-win32thread \
    --disable-opencl --disable-avs --disable-swscale --disable-lavf --disable-ffms --disable-gpac --disable-lsmash
  make -j"$JOBS"
  make install
)

echo "== ffmpeg $FFMPEG_VERSION"
rm -rf ffmpeg && mkdir ffmpeg
tar -xf "$SRC/ffmpeg-$FFMPEG_VERSION.tar.xz" -C ffmpeg --strip-components=1

VIDEO_DECODERS="h264 hevc libdav1d vp8 vp9 mpeg4 msmpeg4v1 msmpeg4v2 msmpeg4v3 mpeg1video mpeg2video
  h263 h263p h263i flv vp6 vp6a vp6f wmv1 wmv2 wmv3 wmv3image vc1 vc1image mjpeg prores dvvideo
  theora rv10 rv20 rv30 rv40 svq3 rawvideo
  ffv1 utvideo huffyuv ffvhuff magicyuv fraps dnxhd qtrle"
AUDIO_DECODERS="aac aac_latm mp3 mp3float mp2 mp2float mp1 mp1float ac3 eac3 opus vorbis flac alac
  pcm_* adpcm_ms adpcm_ima_wav adpcm_ima_qt wmav1 wmav2 wmapro amrnb amrwb cook ra_144 ra_288 dca truehd mlp"
DEMUXERS="mov matroska avi flv live_flv asf mpegts mpegtsraw mpegps mpegvideo rm ogg wav mp3 aac ac3 eac3
  flac h264 hevc m4v ivf dv"
FILTERS="scale fps format null crop transpose hflip vflip
  trim aformat aresample anull atrim"

enable_list() { local kind=$1; shift; for n in $*; do printf -- "--enable-%s=%s " "$kind" "$n"; done; }

(
  cd ffmpeg
  # shellcheck disable=SC2046
  PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig:${PKG_CONFIG_PATH:-}" ./configure \
    --prefix="$PREFIX" \
    --pkg-config-flags=--static \
    --extra-cflags="-I$PREFIX/include" \
    --extra-ldflags="-L$PREFIX/lib -static -static-libgcc" \
    --enable-gpl --enable-libx264 --enable-libdav1d \
    --enable-static --disable-shared \
    --disable-autodetect --enable-w32threads \
    --disable-everything --disable-network --disable-doc --disable-debug \
    --disable-ffplay --disable-ffprobe --disable-avdevice --disable-postproc \
    --enable-protocol=file --enable-protocol=pipe \
    --enable-encoder=libx264 --enable-encoder=aac \
    --enable-muxer=mp4 --enable-muxer=mov \
    --enable-parsers --enable-bsfs \
    $(enable_list decoder $VIDEO_DECODERS $AUDIO_DECODERS) \
    $(enable_list demuxer $DEMUXERS) \
    $(enable_list filter $FILTERS)
  make -j"$JOBS" ffmpeg.exe
  strip ffmpeg.exe
)

cp ffmpeg/ffmpeg.exe "$WORK/ffmpeg.exe"
ls -l "$WORK/ffmpeg.exe"
