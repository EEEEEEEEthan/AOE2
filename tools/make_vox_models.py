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


# 建筑按 1 格 = 16 体素建模，尺寸贴合 BuildingTypes 的占地，渲染时按 1/16 缩放即与 footprint 对齐。
def build_town_center_hall():
    v = {}
    box(v, 0, 31, 0, 31, 0, 1, 6)    # stone base (4x4 footprint)
    box(v, 2, 29, 2, 29, 2, 12, 1)   # team-colored main hall
    for x0, x1, y0, y1 in ((0, 5, 0, 5), (26, 31, 0, 5), (0, 5, 26, 31), (26, 31, 26, 31)):
        box(v, x0, x1, y0, y1, 2, 16, 6)  # stone corner towers
    box(v, 2, 29, 2, 29, 12, 12, 10)  # gold trim
    box(v, 3, 28, 3, 28, 13, 13, 7)   # roof pyramid
    box(v, 7, 24, 7, 24, 14, 14, 7)
    box(v, 11, 20, 11, 20, 15, 15, 7)
    box(v, 14, 17, 14, 17, 16, 16, 7)
    box(v, 13, 18, 2, 2, 2, 8, 3)     # gate
    return v


def build_house():
    v = {}
    box(v, 0, 15, 0, 15, 0, 1, 6)   # stone footing
    box(v, 1, 14, 1, 14, 2, 8, 1)   # team-colored walls
    box(v, 0, 15, 0, 15, 9, 9, 7)   # roof eave
    box(v, 2, 13, 2, 13, 10, 10, 7)
    box(v, 5, 10, 5, 10, 11, 11, 7)
    box(v, 6, 9, 1, 1, 2, 6, 3)     # wooden door
    box(v, 3, 4, 1, 1, 4, 5, 10)    # windows
    box(v, 11, 12, 1, 1, 4, 5, 10)
    return v


def build_barracks():
    v = {}
    box(v, 0, 23, 0, 23, 0, 1, 6)   # stone footing
    box(v, 1, 22, 1, 22, 2, 9, 1)   # team-colored walls
    box(v, 0, 1, 0, 1, 2, 10, 6)    # corner towers
    box(v, 22, 23, 0, 1, 2, 10, 6)
    box(v, 0, 1, 22, 23, 2, 10, 6)
    box(v, 22, 23, 22, 23, 2, 10, 6)
    box(v, 0, 23, 0, 23, 10, 10, 7)  # roof eave
    box(v, 3, 20, 3, 20, 11, 11, 7)
    box(v, 8, 15, 8, 15, 12, 12, 7)
    box(v, 9, 14, 1, 1, 2, 7, 3)     # gate
    box(v, 1, 22, 1, 22, 9, 9, 10)   # gold trim
    return v


def build_wall():
    v = {}
    box(v, 0, 7, 0, 7, 0, 6, 6)      # stone body
    box(v, 0, 7, 3, 4, 4, 5, 1)      # team-colored band
    for x0, x1, y0, y1 in ((0, 1, 0, 1), (6, 7, 0, 1), (0, 1, 6, 7), (6, 7, 6, 7), (3, 4, 3, 4)):
        box(v, x0, x1, y0, y1, 7, 9, 6)  # crenellations
    return v


def build_tower():
    v = {}
    box(v, 0, 15, 0, 15, 0, 2, 6)    # footing
    box(v, 2, 13, 2, 13, 3, 20, 6)   # shaft
    box(v, 2, 13, 2, 13, 20, 21, 1)  # team-colored band
    box(v, 0, 15, 0, 15, 22, 23, 6)  # platform
    for x0, x1, y0, y1 in (
        (0, 2, 0, 2), (13, 15, 0, 2), (0, 2, 13, 15), (13, 15, 13, 15),
        (6, 9, 0, 1), (6, 9, 14, 15), (0, 1, 6, 9), (14, 15, 6, 9),
    ):
        box(v, x0, x1, y0, y1, 24, 26, 6)  # battlements
    return v


def build_market():
    v = {}
    box(v, 0, 31, 0, 23, 0, 1, 6)    # stone platform
    for x in (1, 30):
        for y in (1, 22):
            box(v, x, x, y, y, 2, 8, 3)  # wooden posts
    box(v, 0, 31, 0, 23, 9, 10, 1)   # team-colored canopy
    box(v, 0, 31, 10, 12, 9, 10, 10)  # gold canopy stripe
    box(v, 4, 10, 4, 10, 2, 4, 3)    # crates
    box(v, 20, 27, 4, 10, 2, 5, 3)
    box(v, 13, 18, 3, 8, 2, 3, 8)    # goods counter
    return v


def build_farm():
    v = {}
    box(v, 0, 23, 0, 23, 0, 0, 3)    # tilled soil (3x3 footprint)
    for y in range(2, 23, 4):
        box(v, 2, 21, y, y + 1, 1, 1, 10)  # golden wheat rows
    for x in range(2, 23, 4):
        box(v, x, x + 1, 2, 21, 1, 1, 5)  # green sprouts between rows
    for x, y in ((0, 0), (23, 0), (0, 23), (23, 23)):
        box(v, x, x, y, y, 1, 2, 1)  # team-colored corner stakes
    return v


def build_palisade_wall():
    v = {}
    for x in (0, 2, 4, 6):
        box(v, x, x + 1, 3, 4, 0, 7, 3)
    box(v, 0, 7, 3, 4, 2, 2, 3)
    box(v, 0, 7, 3, 4, 5, 6, 1)
    return v


def build_gate(door):
    v = {}
    box(v, 0, 1, 1, 6, 0, 9, 6)
    box(v, 6, 7, 1, 6, 0, 9, 6)
    box(v, 0, 1, 1, 6, 4, 5, 1)
    box(v, 6, 7, 1, 6, 4, 5, 1)
    if door:
        box(v, 0, 7, 1, 6, 7, 9, 6)
        box(v, 2, 5, 3, 4, 0, 6, 3)
        box(v, 2, 5, 3, 4, 3, 3, 10)
    return v


def build_gate_closed():
    return build_gate(True)


def build_gate_open():
    return build_gate(False)


def build_fortified_wall():
    v = {}
    box(v, 0, 7, 0, 7, 0, 8, 6)
    box(v, 0, 7, 3, 4, 4, 5, 1)
    for x0, x1, y0, y1 in (
        (0, 1, 0, 1), (3, 4, 0, 1), (6, 7, 0, 1),
        (0, 1, 6, 7), (3, 4, 6, 7), (6, 7, 6, 7),
    ):
        box(v, x0, x1, y0, y1, 9, 10, 6)
    return v


def upscale(voxels: dict, factor: int = 2) -> dict:
    """把模型升到 16 体素/格：每个体素扩成 factor³ 块，世界尺寸不变、分辨率翻倍。"""
    scaled = {}
    for (x, y, z), color in voxels.items():
        for dx in range(factor):
            for dy in range(factor):
                for dz in range(factor):
                    scaled[(x * factor + dx, y * factor + dy, z * factor + dz)] = color
    return scaled


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
        "town_center_hall.vox": build_town_center_hall(),
        "house.vox": build_house(),
        "barracks.vox": build_barracks(),
        "wall.vox": build_wall(),
        "tower.vox": build_tower(),
        "market.vox": build_market(),
        "farm.vox": build_farm(),
        "palisade_wall.vox": build_palisade_wall(),
        "gate.vox": build_gate_closed(),
        "gate_open.vox": build_gate_open(),
        "fortified_wall.vox": build_fortified_wall(),
    }
    for name, voxels in models.items():
        voxels = upscale(voxels)
        write_vox(OUT / name, voxels)
        print(f"{name}: {len(voxels)} voxels")


if __name__ == "__main__":
    main()
