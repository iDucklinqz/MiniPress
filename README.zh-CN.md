# MiniPress

[English](README.md) · 简体中文

拖入视频，点一下就能压缩的 Windows 小工具。整个程序只有一个 exe，内置了精简版 FFmpeg，双击就能用，不需要安装，也不依赖电脑上的其他软件。

## 使用

1. 运行 `MiniPress.exe`
2. 把视频拖进窗口（也可以点击选择，或者直接拖到 exe 图标上）。支持一次拖入多个视频或整个文件夹
3. 点击 **无损**、**普通** 或 **超级** 其中一个

压缩后的文件保存在原视频旁边，文件名类似 `xxx_压缩.mp4`。原视频不会被改动。如果压缩后反而变大了，程序会自动跳过这个文件。

| 模式 | 画质 | 分辨率 / 帧率 | 适合 |
|---|---|---|---|
| 无损 | 肉眼看不出区别（CRF 18） | 保持原样 | 存档 |
| 普通 | 清晰（CRF 26） | 保持原样 | 日常分享 |
| 超级 | 够看（CRF 28） | 短边最高 720p，最高 30 帧 | 微信、QQ 等有大小限制的地方 |

所有模式都输出 H.264 + AAC 编码的 MP4，几乎所有设备和聊天软件都能直接播放。

> 这里的“无损”指视觉无损。数学意义上的无损编码会让文件比原视频还大，起不到压缩的作用。

能读取的格式：MP4、MOV、MKV、WebM、AVI、WMV、FLV、TS/MTS、MPG、RM/RMVB、3GP 等。视频编码支持 H.264、H.265/HEVC、AV1、VP8/VP9、MPEG-1/2/4、WMV、RealVideo，以及录屏软件常用的无损编码（FFV1、UtVideo、HuffYUV、MagicYUV、Fraps）。

## 运行要求

Windows 10 或 11 即可。程序用到的 .NET Framework 4.8 和 UCRT 运行库都是系统自带的。

第一次运行时，程序会把内置的 FFmpeg 解压到 `%LOCALAPPDATA%\MiniPress`，大约需要一秒，之后打开就直接使用。

## 从源码构建

需要两步：

1. **编译精简版 FFmpeg**（只在第一次构建或升级 FFmpeg 时需要）。在 [MSYS2](https://www.msys2.org) 的 UCRT64 终端里，按 `tools/build-ffmpeg.sh` 开头的说明安装依赖，然后运行这个脚本，把生成的 `ffmpeg.exe` 复制到 `assets/ffmpeg.exe`
2. **编译 MiniPress**：

   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1
   ```

   生成的程序在 `dist\MiniPress.exe`。这一步不需要安装 Visual Studio，直接使用 Windows 自带的 C# 编译器。如果加上 `-NoFFmpeg` 参数，会生成一个不带 FFmpeg 的小体积版本，它会去使用电脑上已经装好的 ffmpeg.exe，适合开发调试。

| 文件 | 内容 |
|---|---|
| `src/FFmpeg.cs` | 释放内置的 FFmpeg、读取视频信息、三种模式对应的参数、执行压缩和显示进度 |
| `src/MainForm.cs` | 窗口和交互逻辑 |
| `src/Controls.cs` | 拖放区域控件 |
| `tools/build-ffmpeg.sh` | 精简版 FFmpeg（含 x264）的编译脚本 |

## 许可

MiniPress 的源码使用 MIT 许可。

`MiniPress.exe` 内置的 FFmpeg 和 x264 使用 GPL 许可，对应源码为 [ffmpeg-7.1.2](https://ffmpeg.org) 和 [x264 stable](https://code.videolan.org/videolan/x264)，编译参数见 `tools/build-ffmpeg.sh`。分发 exe 时请一并提供这两个源码包，或者它们的下载地址。
