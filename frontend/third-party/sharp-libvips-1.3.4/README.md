# Sharp/libvips native license bundle

This bundle applies to @img/sharp-libvips-linuxmusl-x64 1.3.4 (libvips 8.18.7), distributed with Sharp 0.35.5.
Sharp uses this LGPL-3.0-or-later native library and links dynamically to libvips-cpp.so.8.18.7.
GNU/GPL-3.0.txt and GNU/LGPL-3.0.txt contain the complete GNU license texts.
Original component copyright notices and license alternatives are preserved under native/ and rust/.
The unchanged upstream licensing table is UPSTREAM-README.md; exact component versions are in versions.json.
The Rust notice set covers the normal/build dependency closure of librsvg-c after the upstream build's source edits, and conservatively includes build-time crate notices.
The original platform's MIT license does not replace these component licenses.

Matching sources: sharp-libvips-1.3.4-matching-source.tar.gz contains exact source archives, patches, pinned build scripts and Cargo-lock-verified crate sources.
Download: https://github.com/KirillSachkov/education-platform/releases/download/third-party-sharp-libvips-1.3.4/sharp-libvips-1.3.4-matching-source.tar.gz
SHA256: 7d4d9a75a0dc2a9af400a673847156155d025b7e5e7f720143432964a7fe5d58
Size: 175037945 bytes. The archive retains the original component licenses.
Source provenance, patches, crate checksums and build directions are inside the archive.

To replace the native library, build an interface-compatible Linux musl x64 libvips-cpp.so.8.18.7 from the matching sources, retaining its SONAME and C++ ABI.
Mount the replacement read-only at /app/node_modules/@img/sharp-libvips-linuxmusl-x64/lib/libvips-cpp.so.8.18.7, or copy it into a derived image at that path.
The Sharp addon resolves that shared library at runtime through its relative RPATH.
Users may modify this library and reverse engineer the combined work to debug their modifications under the applicable license.
The container does not impose a signature check on replacement libraries.
An interface-compatible replacement must retain the C++ ABI and SONAME.
