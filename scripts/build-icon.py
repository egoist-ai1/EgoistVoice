#!/usr/bin/env python3
"""Собирает фирменную иконку Egoist Voice: низкополигональный микрофон без фона.

Раньше скрипт вырезал фон из готовой картинки, сгенерированной на стороне. Это значило, что
иконку нельзя было изменить, не имея того же исходника. Теперь марка рисуется здесь сеткой
граней, и этот файл — единственный источник правды: `assets/EgoistVoice.ico`,
`assets/EgoistVoice.png` и мастер генерируются из него.

Запуск:  python3 scripts/build-icon.py
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

# Рисуем крупно и уменьшаем: сглаживание получается из передискретизации, а не из кривых —
# грани при этом остаются прямыми, как и задумано.
CANVAS = 2048
ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"

# Фирменная палитра. Тёмный кант нужен, чтобы марка не растворялась на светлом фоне, светлые
# грани — чтобы читалась на чёрном. Одного красного для «видно везде» не хватает.
INK = (26, 10, 12, 255)
SHADOW = (122, 12, 24, 255)
DEEP = (168, 18, 32, 255)
BRAND = (225, 29, 47, 255)
BRIGHT = (255, 74, 88, 255)
HIGHLIGHT = (255, 158, 166, 255)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def ramp(t: float):
    """Оттенок грани по её освещённости."""
    t = max(0.0, min(1.0, t))
    stops = [(0.00, HIGHLIGHT), (0.22, BRIGHT), (0.52, BRAND), (0.78, DEEP), (1.00, SHADOW)]
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return lerp(c0, c1, (t - t0) / (t1 - t0))
    return stops[-1][1]


# Направление света: слева, спереди и чуть сверху. Нормировано.
LIGHT_X, LIGHT_Z = -0.55, 0.835


def cylinder_shade(points, axis_x, radius, top, bottom, tilt=0.0):
    """
    Освещённость грани так, как если бы она лежала на боковой поверхности цилиндра.

    Плоская заливка по такой модели и даёт низкополигональный вид: грань берёт один тон,
    посчитанный по её центру, а соседняя — заметно другой, потому что нормаль повернулась.
    Заливка по расстоянию до центра фигуры этого не даёт: получается гладкий градиент.
    """
    mx = sum(p[0] for p in points) / len(points)
    my = sum(p[1] for p in points) / len(points)

    u = max(-1.0, min(1.0, (mx - axis_x) / radius))
    normal_z = math.sqrt(max(0.0, 1.0 - u * u))
    lambert = u * LIGHT_X + normal_z * LIGHT_Z

    # Вертикальный спад: низ фигуры темнее верха, иначе цилиндр читается как плоская лента.
    depth = 0.0 if bottom == top else max(0.0, min(1.0, (my - top) / (bottom - top)))
    intensity = 0.16 + 0.84 * max(0.0, lambert) - depth * 0.30 + tilt

    return max(0.0, min(1.0, 1.0 - intensity))


def capsule_mesh(draw, cx, cy_top, cy_bottom, radius, rows, columns, grille=False):
    """
    Капсула, набранная сеткой граней: строки по высоте, столбцы поперёк. Каждая ячейка режется
    на два треугольника, и каждый заливается своим тоном — отсюда огранка.
    """
    top = cy_top - radius
    bottom = cy_bottom + radius

    def half_width(y):
        if y < cy_top:
            return math.sqrt(max(0.0, radius * radius - (cy_top - y) ** 2))
        if y > cy_bottom:
            return math.sqrt(max(0.0, radius * radius - (y - cy_bottom) ** 2))
        return radius

    levels = [top + (bottom - top) * index / rows for index in range(rows + 1)]

    for row in range(rows):
        y0, y1 = levels[row], levels[row + 1]
        w0, w1 = half_width(y0), half_width(y1)

        # Решётка не рисуется поверх капсулы, а гасит целые ряды граней. Полосы поверх формы
        # уже превращали капсулу то в лицо, то в линованный лист.
        tilt = -0.10 if grille and row in (1, 3) else 0.0

        for column in range(columns):
            t0, t1 = column / columns, (column + 1) / columns
            a = (cx - w0 + 2 * w0 * t0, y0)
            b = (cx - w0 + 2 * w0 * t1, y0)
            c = (cx - w1 + 2 * w1 * t1, y1)
            d = (cx - w1 + 2 * w1 * t0, y1)

            # Диагональ чередуется — иначе все грани смотрят одинаково и сетка читается
            # как штриховка, а не как огранка.
            if (row + column) % 2 == 0:
                triangles = ((a, b, c), (a, c, d))
            else:
                triangles = ((a, b, d), (b, c, d))

            for triangle in triangles:
                draw.polygon(
                    triangle,
                    fill=ramp(cylinder_shade(triangle, cx, radius, top, bottom, tilt)))


def stadium(cx, cy_top, cy_bottom, radius, segments=9):
    """
    Капсула микрофона: скруглённая сверху и снизу форма, набранная отрезками.

    Экранные координаты растут вниз, поэтому верхняя дуга — это углы от 180° до 360°, где синус
    отрицателен и сам поднимает точки вверх. Знак здесь один раз уже был перепутан, и капсула
    вывернулась в пару рогов.
    """
    points = []
    for index in range(segments + 1):
        angle = math.pi + index * math.pi / segments
        points.append((cx + radius * math.cos(angle), cy_top + radius * math.sin(angle)))
    for index in range(segments + 1):
        angle = index * math.pi / segments
        points.append((cx + radius * math.cos(angle), cy_bottom + radius * math.sin(angle)))
    return points


def prism(draw, draw_top, draw_bottom, cx, half_top, half_bottom, radius, columns, tilt=0.0):
    """Ножка и основание: тот же цилиндр, но короткий и без скруглений."""
    for column in range(columns):
        t0, t1 = column / columns, (column + 1) / columns
        a = (cx - half_top + 2 * half_top * t0, draw_top)
        b = (cx - half_top + 2 * half_top * t1, draw_top)
        c = (cx - half_bottom + 2 * half_bottom * t1, draw_bottom)
        d = (cx - half_bottom + 2 * half_bottom * t0, draw_bottom)
        for triangle in (((a, b, c), (a, c, d)) if column % 2 == 0 else ((a, b, d), (b, c, d))):
            draw.polygon(
                triangle,
                fill=ramp(cylinder_shade(triangle, cx, radius, draw_top, draw_bottom, tilt)))


def arc_band(draw, cx, cy, inner, outer, start_deg, end_deg, steps=16, tilt=0.0):
    """Держатель: кольцевой сектор из четырёхугольных граней."""
    top = cy - outer
    bottom = cy + outer
    for index in range(steps):
        a0 = math.radians(start_deg + (end_deg - start_deg) * index / steps)
        a1 = math.radians(start_deg + (end_deg - start_deg) * (index + 1) / steps)
        quad = [
            (cx + outer * math.cos(a0), cy + outer * math.sin(a0)),
            (cx + outer * math.cos(a1), cy + outer * math.sin(a1)),
            (cx + inner * math.cos(a1), cy + inner * math.sin(a1)),
            (cx + inner * math.cos(a0), cy + inner * math.sin(a0)),
        ]
        draw.polygon(quad, fill=ramp(cylinder_shade(quad, cx, outer, top, bottom, tilt)))


def build_mark(compact: bool) -> Image.Image:
    """
    Рисует марку. В компактном варианте огранка грубее, а линии толще: на шестнадцати пикселях
    мелкие грани превращаются в грязь, и силуэт важнее богатства.
    """
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    s = CANVAS / 1024  # геометрия задана в тысячной сетке

    cx = 512 * s
    radius = (178 if compact else 165) * s

    # Центры скруглений капсулы. Сама капсула занимает от capsule_top - radius до
    # capsule_bottom + radius, то есть примерно от 90 до 620 в тысячной сетке.
    capsule_top = 268 * s
    capsule_bottom = 452 * s

    # Держатель обнимает капсулу: внутренний радиус лишь немного больше её собственного.
    # Широкая дуга превращает микрофон в кубок — это уже проверено на первой версии.
    arc_outer = (262 if compact else 252) * s
    arc_inner = (208 if compact else 210) * s
    arc_from, arc_to = (12, 168) if compact else (16, 164)

    stem_half = (52 if compact else 42) * s
    stem_top = capsule_bottom + arc_outer * 0.92
    stem_bottom = 878 * s
    base_half = (188 if compact else 178) * s
    base_top = (872 if compact else 880) * s
    base_bottom = 946 * s

    arc_band(draw, cx, capsule_bottom, arc_inner, arc_outer, arc_from, arc_to,
             steps=9 if compact else 20, tilt=-0.06)

    prism(draw, stem_top, stem_bottom, cx, stem_half, stem_half, stem_half * 1.6,
          columns=2 if compact else 4, tilt=-0.04)

    prism(draw, base_top, base_bottom, cx, base_half, base_half * 0.92, base_half * 1.5,
          columns=3 if compact else 6, tilt=-0.02)

    # Капсула рисуется последней: она главная фигура и должна перекрывать держатель.
    capsule_mesh(draw, cx, capsule_top, capsule_bottom, radius,
                 rows=4 if compact else 8,
                 columns=3 if compact else 6,
                 grille=not compact)

    return image


def add_outline(mark: Image.Image, thickness: int) -> Image.Image:
    """
    Тёмный кант по внешнему краю. Без него ярко-красная марка сливается со светлым фоном,
    а требование было — читаться на любом.
    """
    grown = mark.getchannel("A").filter(ImageFilter.MaxFilter(thickness * 2 + 1))
    outline = Image.new("RGBA", mark.size, INK)
    outline.putalpha(grown)
    return Image.alpha_composite(outline, mark)


def trim_to_square(image: Image.Image, margin: float) -> Image.Image:
    """Вписывает марку в квадрат с малым полем: «максимально крупно», но без обрезки краёв."""
    cropped = image.crop(image.getbbox())
    side = round(max(cropped.size) / (1 - 2 * margin))
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(cropped, ((side - cropped.width) // 2, (side - cropped.height) // 2))
    return square


SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

# Ниже этого размера берётся упрощённая марка: огранка и решётка перестают читаться и мешают.
COMPACT_BELOW = 40


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    (ROOT / "artifacts").mkdir(exist_ok=True)

    detailed = trim_to_square(add_outline(build_mark(compact=False), round(CANVAS * 0.0075)), 0.035)
    compact = trim_to_square(add_outline(build_mark(compact=True), round(CANVAS * 0.019)), 0.025)

    detailed.resize((1024, 1024), Image.LANCZOS).save(ASSETS / "EgoistVoice-icon-master.png")
    detailed.resize((512, 512), Image.LANCZOS).save(ASSETS / "EgoistVoice.png")

    frames = [
        (compact if size < COMPACT_BELOW else detailed).resize((size, size), Image.LANCZOS)
        for size in SIZES
    ]
    frames[-1].save(ASSETS / "EgoistVoice.ico", format="ICO",
                    sizes=[(size, size) for size in SIZES],
                    append_images=frames[:-1])

    # Превью: как иконка ложится на светлый и на тёмный фон. Проверять глазами нужно оба —
    # марка без фона обязана работать и там, и там.
    width = sum(size + 20 for size in SIZES) + 20
    strip = Image.new("RGBA", (width, 208), (0, 0, 0, 0))
    strip.paste(Image.new("RGBA", (width, 104), (245, 245, 247, 255)), (0, 0))
    strip.paste(Image.new("RGBA", (width, 104), (11, 11, 13, 255)), (0, 104))
    x = 20
    for size, frame in zip(SIZES, frames):
        strip.paste(frame, (x, 52 - size // 2), frame)
        strip.paste(frame, (x, 156 - size // 2), frame)
        x += size + 20
    strip.save(ROOT / "artifacts" / "icon-preview.png")

    print("готово: " + ", ".join(str(size) for size in SIZES))


if __name__ == "__main__":
    main()
