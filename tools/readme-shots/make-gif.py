"""Stitches the frames readme-shots renders into the README's animation.

Pillow only (no ffmpeg): the frames are PNGs written by the C# tool, one per tick. Width is halved and
the palette quantized, which is what keeps a ~12 s animation inside a few MB — GitHub serves the file on
every README view, so size is a feature.

    python make-gif.py <frame dir> <out.gif> [width]
"""
import sys
from pathlib import Path
from PIL import Image

FPS = 10

def main() -> int:
    frames_dir = Path(sys.argv[1] if len(sys.argv) > 1 else "C:/tmp/shots")
    out = Path(sys.argv[2] if len(sys.argv) > 2 else "docs/images/demo.gif")
    width = int(sys.argv[3]) if len(sys.argv) > 3 else 700

    files = sorted(frames_dir.glob("f*.png"))
    if not files:
        print(f"no frames in {frames_dir}", file=sys.stderr)
        return 1

    frames = []
    for f in files:
        im = Image.open(f).convert("RGB")
        if im.width != width:
            im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
        # A fixed adaptive palette per frame keeps the text crisp; 128 colours is plenty for UI chrome
        # and black text, and halves the file against 256.
        frames.append(im.quantize(colors=128, method=Image.MEDIANCUT))

    out.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        out,
        save_all=True,
        append_images=frames[1:],
        duration=round(1000 / FPS),
        loop=0,
        optimize=True,
        disposal=2,
    )
    size_mb = out.stat().st_size / 1024 / 1024
    print(f"{len(frames)} frames -> {out} ({size_mb:.2f} MB, {len(frames) / FPS:.1f} s, {width}px wide)")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
