#!/usr/bin/env python3
"""生成项目自建的体素单位/建筑 .vox（MagicaVoxel 格式），供表现层解析渲染。

不读取任何原作素材；模型全部由本脚本用方块拼出。调色板索引 1 是阵营色占位，
渲染时会被替换成己方/敌方队伍色。
"""

from __future__ import annotations

import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "vox"

PALETTE = {
    1: (220, 60, 50),     # team accent (replaced at render time)
    2: (220, 174, 132),   # skin
    3: (107, 71, 41),     # wood
    4: (46, 46, 52),      # dark cloth
    5: (61, 128, 51),     # leaf green
    6: (140, 140, 150),   # stone
    7: (153, 61, 46),     # roof
    8: (178, 184, 196),   # steel
    9: (230, 230, 230),   # white
    10: (200, 164, 77),   # gold
}


def box(voxels, x0, x1, y0, y1, z0, z1, color):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                voxels[(x, y, z)] = color


def build_villager():
    v = {}
    box(v, 1, 3, 1, 2, 0, 1, 4)
    box(v, 1, 3, 1, 3, 2, 4, 1)
    box(v, 0, 0, 1, 2, 2, 4, 2)
    box(v, 4, 4, 1, 2, 2, 4, 2)
    box(v, 1, 3, 1, 3, 5, 6, 2)
    box(v, 1, 3, 1, 3, 7, 7, 1)
    return v


def build_soldier():
    v = {}
    box(v, 1, 3, 1, 2, 0, 2, 4)
    box(v, 1, 3, 1, 3, 3, 5, 8)
    box(v, 1, 3, 1, 3, 3, 3, 1)
    box(v, 0, 0, 1, 2, 3, 4, 8)
    box(v, 4, 4, 1, 2, 3, 4, 8)
    box(v, 1, 3, 1, 3, 6, 7, 2)
    box(v, 1, 3, 1, 3, 8, 8, 1)
    box(v, 5, 5, 1, 1, 0, 9, 3)
    box(v, 5, 5, 1, 1, 10, 10, 8)
    return v


def build_tree():
    v = {}
    box(v, 2, 3, 2, 3, 0, 3, 3)
    box(v, 1, 4, 1, 4, 3, 5, 5)
    box(v, 0, 5, 0, 5, 5, 6, 5)
    box(v, 1, 4, 1, 4, 7, 7, 5)
    return v


def build_town_center():
    v = {}
    box(v, 0, 6, 0, 6, 0, 1, 6)
    for x in range(7):
        for y in range(7):
            if x in (0, 6) or y in (0, 6):
                box(v, x, x, y, y, 1, 4, 6)
    box(v, 0, 6, 0, 6, 5, 5, 7)
    box(v, 1, 5, 1, 5, 6, 6, 7)
    box(v, 3, 3, 0, 0, 1, 2, 1)
    box(v, 0, 6, 3, 3, 4, 4, 10)
    return v


def write_vox(path: Path, voxels: dict) -> None:
    xs = [p[0] for p in voxels]
    ys = [p[1] for p in voxels]
    zs = [p[2] for p in voxels]
    size = (max(xs) + 1, max(ys) + 1, max(zs) + 1)

    xyzi = struct.pack("<i", len(voxels))
    for (x, y, z), color in sorted(voxels.items()):
        xyzi += struct.pack("<4B", x, y, z, color)

    rgba = bytearray()
    for index in range(256):
        r, g, b = PALETTE.get(index + 1, (0, 0, 0))
        a = 255 if index + 1 in PALETTE else 0
        rgba += struct.pack("<4B", r, g, b, a)

    def chunk(name: bytes, content: bytes) -> bytes:
        return name + struct.pack("<ii", len(content), 0) + content

    size_chunk = chunk(b"SIZE", struct.pack("<iii", *size))
    xyzi_chunk = chunk(b"XYZI", xyzi)
    rgba_chunk = chunk(b"RGBA", bytes(rgba))
    children = size_chunk + xyzi_chunk + rgba_chunk
    main = b"MAIN" + struct.pack("<ii", 0, len(children)) + children
    path.write_bytes(b"VOX " + struct.pack("<i", 150) + main)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    models = {
        "villager.vox": build_villager(),
        "soldier.vox": build_soldier(),
        "tree.vox": build_tree(),
        "town_center.vox": build_town_center(),
    }
    for name, voxels in models.items():
        write_vox(OUT / name, voxels)
        print(f"{name}: {len(voxels)} voxels")


if __name__ == "__main__":
    main()
