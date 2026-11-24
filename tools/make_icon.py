"""生成程序图标 app.ico（多尺寸，Windows 资源图标）。

输出位置固定为仓库里的 <仓库根>\src\app.ico，按本脚本所在目录推算，
因此在任何工作目录下执行都可以（不再依赖某台机器的绝对路径）。
先写临时文件再原子替换，生成失败不会把现有图标弄坏。
"""
import os

from PIL import Image, ImageDraw

S = 1024
BG = (0, 166, 62, 255)
WHITE = (255, 255, 255, 255)
WHITE_DIM = (255, 255, 255, 165)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 底板：圆角方块
d.rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.22), fill=BG)

# 后面那个窗口（半透明白）
back = [int(S * 0.18), int(S * 0.24), int(S * 0.64), int(S * 0.64)]
d.rounded_rectangle(back, radius=int(S * 0.06), fill=WHITE_DIM)

# 用底色把前景窗口和背景窗口之间留出缝隙
gap = int(S * 0.035)
front = [int(S * 0.36), int(S * 0.40), int(S * 0.84), int(S * 0.82)]
d.rounded_rectangle(
    [front[0] - gap, front[1] - gap, front[2] + gap, front[3] + gap],
    radius=int(S * 0.06) + gap,
    fill=BG,
)

# 前面的窗口（纯白），带一条顶栏
d.rounded_rectangle(front, radius=int(S * 0.06), fill=WHITE)
bar_h = int(S * 0.085)
d.rounded_rectangle(
    [front[0], front[1], front[2], front[1] + bar_h + int(S * 0.06)],
    radius=int(S * 0.06),
    fill=(0, 150, 56, 255),
)
d.rectangle(
    [front[0], front[1] + bar_h, front[2], front[1] + bar_h + int(S * 0.06)],
    fill=(0, 150, 56, 255),
)

# 窗口里的三条“段落线”，暗示内容/模型列表
line_x0 = front[0] + int(S * 0.05)
line_x1 = front[2] - int(S * 0.05)
y = front[1] + bar_h + int(S * 0.075)
for i, w in enumerate((1.0, 0.86, 0.62)):
    lh = int(S * 0.035)
    d.rounded_rectangle(
        [line_x0, y, line_x0 + int((line_x1 - line_x0) * w), y + lh],
        radius=lh // 2,
        fill=(0, 166, 62, 200),
    )
    y += int(S * 0.075)

sizes = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]
frames = [img.resize((s, s), Image.LANCZOS) for s in sizes]

# 仓库根 = tools\ 的上一级；从任何工作目录运行都写回仓库自己的 src\app.ico
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = os.path.join(root, "src", "app.ico")
os.makedirs(os.path.dirname(out), exist_ok=True)

tmp = out + ".tmp"
try:
    frames[0].save(tmp, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])
    os.replace(tmp, out)
finally:
    if os.path.exists(tmp):
        os.remove(tmp)

print("saved", out)
