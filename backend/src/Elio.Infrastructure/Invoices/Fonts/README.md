# Invoice fonts

Noto Sans Regular and Bold are embedded for consistent cross-platform invoice PDFs, including the Philippine peso glyph. These are unmodified static fonts from the archived upstream Noto distribution:

- https://github.com/notofonts/noto-fonts/tree/main/hinted/ttf/NotoSans
- SIL Open Font License 1.1: see LICENSE.txt in this directory.

These font assets are intentional application resources, not generated invoice artifacts. No font download occurs at runtime. This font covers Latin/Greek/Cyrillic text; full CJK/complex-script shaping is outside the current renderer/font coverage.
