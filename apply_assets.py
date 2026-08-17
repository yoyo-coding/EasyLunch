"""
Replace placeholder assets with the new StartPage icons.
Generates the right sizes from StartPage_Icon_light_1024.png and StartPage_Icon_dark_1024.png.
"""
from PIL import Image
import os

ASSETS_DIR = r"f:\StartPage\StartPage\Assets"
LIGHT_SRC = r"f:\StartPage\StartPage_Icon_light_1024.png"
DARK_SRC = r"f:\StartPage\StartPage_Icon_dark_1024.png"

# (filename, source_image, target_size, mode)
# Square150x150Logo.scale-200 = 300x300
# Square44x44Logo.scale-200 = 88x88
# Square44x44Logo.targetsize-24_altform-unplated = 24x24
# StoreLogo = 50x50
# LockScreenLogo.scale-200 = 24x24 (or 48x48 for 200% scaling)
# SplashScreen.scale-200 = 2480x1200?  actually keep aspect for splash
# Wide310x150Logo.scale-200 = 620x300
JOBS = [
    ("Square150x150Logo.scale-200.png", "light", 300, 300),
    ("Square44x44Logo.scale-200.png", "light", 88, 88),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", "light", 24, 24),
    ("StoreLogo.png", "light", 50, 50),
    ("LockScreenLogo.scale-200.png", "light", 48, 48),
    ("Wide310x150Logo.scale-200.png", "light", 620, 300),
    # SplashScreen handled separately (scaled from wide)
]

LIGHT = Image.open(LIGHT_SRC).convert("RGBA")
DARK = Image.open(DARK_SRC).convert("RGBA")


def fit_cover(img, w, h):
    """Scale image to fully cover (w,h) then center-crop."""
    sw, sh = img.size
    scale = max(w / sw, h / sh)
    new_w = int(round(sw * scale))
    new_h = int(round(sh * scale))
    scaled = img.resize((new_w, new_h), Image.LANCZOS)
    left = (new_w - w) // 2
    top = (new_h - h) // 2
    return scaled.crop((left, top, left + w, top + h))


def fit_contain(img, w, h, background=(0, 0, 0, 0)):
    """Scale image to fit inside (w,h) preserving aspect, transparent padding."""
    sw, sh = img.size
    scale = min(w / sw, h / sh)
    new_w = int(round(sw * scale))
    new_h = int(round(sh * scale))
    scaled = img.resize((new_w, new_h), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), background)
    ox = (w - new_w) // 2
    oy = (h - new_h) // 2
    canvas.paste(scaled, (ox, oy), scaled)
    return canvas


def pad_to_wide(img, target_w, target_h, background=(245, 248, 252, 255)):
    """Center icon on a wide canvas (light bg)."""
    canvas = Image.new("RGBA", (target_w, target_h), background)
    # Make icon fit within 60% of target_h
    inner_h = int(target_h * 0.72)
    inner_w = inner_h  # square icon
    icon = fit_contain(img, inner_w, inner_h)
    ox = (target_w - inner_w) // 2
    oy = (target_h - inner_h) // 2
    canvas.paste(icon, (ox, oy), icon)
    return canvas


def pad_to_splash(img, target_w, target_h):
    """Center icon on splash background (Mica-like dark) for the splash screen."""
    bg = (32, 38, 55, 255)
    canvas = Image.new("RGBA", (target_w, target_h), bg)
    inner_h = int(target_h * 0.28)
    inner_w = inner_h
    icon = fit_contain(img, inner_w, inner_h)
    ox = (target_w - inner_w) // 2
    oy = (target_h - inner_h) // 2
    canvas.paste(icon, (ox, oy), icon)
    return canvas


for name, src_key, w, h in JOBS:
    src = LIGHT if src_key == "light" else DARK
    if name == "Wide310x150Logo.scale-200.png":
        out = pad_to_wide(src, w, h)
    else:
        out = fit_cover(src, w, h)
    out_path = os.path.join(ASSETS_DIR, name)
    out.save(out_path, "PNG")
    print(f"Wrote: {out_path}  ({w}x{h})")

# Splash screen: 620x300 (200% of 310x150 splash is 620x300)
splash = pad_to_splash(DARK, 620, 300)
splash_path = os.path.join(ASSETS_DIR, "SplashScreen.scale-200.png")
splash.save(splash_path, "PNG")
print(f"Wrote: {splash_path}  (620x300)")

print("\nAll assets updated.")
