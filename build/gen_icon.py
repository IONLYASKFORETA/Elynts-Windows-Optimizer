"""Writes src/Elynt.App/Assets/elynt.ico: the app's red "E" mark for the window, taskbar, tray and Start menu (#107).

Run from the repo root after changing the drawing below:

    python3 build/gen_icon.py src/Elynt.App/Assets/elynt.ico

Plain Python with no packages, so anyone can run it and the same drawing always gives the same file. The mark is a
rounded square in Theme.Red with a white block "E" drawn from rectangles (no font, so it looks the same everywhere).
Each size is drawn on its own, with the E's edges on whole pixels so it stays sharp at 16 px; the corners are smoothed
by sampling each pixel 8x8 times. Sizes up to 64 px are stored as bitmaps, which every Windows icon reader takes, and
256 px as a PNG, as Windows itself does.
"""
import struct, sys, zlib

RED = (0xE5, 0x16, 0x2D)  # Theme.Red
WHITE = (0xFF, 0xFF, 0xFF)
# Windows' small icon at 100-200% scaling, then the larger ones Explorer and the Start menu use.
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SAMPLES = 8

# The drawing, as fractions of the icon's width.
MARGIN = 0.03  # around the square
CORNER = 0.22  # the square's corner radius
E_LEFT, E_RIGHT, E_TOP, E_BOTTOM = 0.31, 0.71, 0.22, 0.78
E_STEM = 0.15  # the upright's width
E_BAR = 0.135  # each bar's height
E_MIDDLE_RIGHT = 0.65  # the middle bar is a little shorter


def draw(size):
    """The icon at one size, as rows of (r, g, b, a) with straight (not premultiplied) alpha."""
    margin = round(size * MARGIN)
    lo, hi = margin, size - margin
    radius = size * CORNER

    def in_square(x, y):
        # Distance into the nearest corner's circle, when the point is in a corner's box.
        cx = min(max(x, lo + radius), hi - radius)
        cy = min(max(y, lo + radius), hi - radius)
        return lo <= x <= hi and lo <= y <= hi and (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2

    # The E's rectangles on whole pixels: left, top, right, bottom.
    left, right = round(size * E_LEFT), round(size * E_RIGHT)
    top, bottom = round(size * E_TOP), round(size * E_BOTTOM)
    stem = max(2, round(size * E_STEM))
    bar = max(2, round(size * E_BAR))
    middle_top = round((top + bottom - bar) / 2)
    e = [
        (left, top, left + stem, bottom),
        (left, top, right, top + bar),
        (left, middle_top, round(size * E_MIDDLE_RIGHT), middle_top + bar),
        (left, bottom - bar, right, bottom),
    ]

    def in_e(x, y):
        return any(l <= x < r and t <= y < b for l, t, r, b in e)

    rows = []
    for py in range(size):
        row = []
        for px in range(size):
            square = white = 0
            for sy in range(SAMPLES):
                for sx in range(SAMPLES):
                    x, y = px + (sx + 0.5) / SAMPLES, py + (sy + 0.5) / SAMPLES
                    if in_square(x, y):
                        square += 1
                        white += in_e(x, y)
            if square == 0:
                row.append((0, 0, 0, 0))
                continue
            share = white / square
            r, g, b = (round(c1 * (1 - share) + c2 * share) for c1, c2 in zip(RED, WHITE))
            row.append((r, g, b, round(255 * square / SAMPLES ** 2)))
        rows.append(row)
    return rows


def bitmap(rows):
    """An icon entry as a 32-bit bitmap: the header, the colours bottom row first, then the 1-bit transparency mask."""
    size = len(rows)
    colours = b"".join(bytes((b, g, r, a)) for row in reversed(rows) for r, g, b, a in row)
    mask_row = (size + 31) // 32 * 4
    mask = bytearray()
    for row in reversed(rows):
        bits = bytearray(mask_row)
        for x, (_, _, _, a) in enumerate(row):
            if a == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(colours) + len(mask), 0, 0, 0, 0)
    return header + colours + bytes(mask)


def png(rows):
    """An icon entry as a PNG, for the 256 px size."""
    size = len(rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    raw = b"".join(b"\0" + b"".join(bytes(p) for p in row) for row in rows)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main(out_path):
    images = [(size, png(draw(size)) if size >= 256 else bitmap(draw(size))) for size in SIZES]
    offset = 6 + 16 * len(images)
    directory = struct.pack("<HHH", 0, 1, len(images))
    for size, data in images:
        side = 0 if size >= 256 else size  # 0 means 256
        directory += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(out_path, "wb") as f:
        f.write(directory + b"".join(data for _, data in images))
    print(f"{out_path}: {', '.join(str(s) for s in SIZES)} px, {offset} bytes")


if __name__ == "__main__":
    main(sys.argv[1])
