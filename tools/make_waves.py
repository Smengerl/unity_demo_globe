import sys
import numpy as np
from PIL import Image

out = sys.argv[1]
N = 256
rng = np.random.default_rng(7)

# tileable height field: random phases with a 1/f^1.4 spectrum
fx = np.fft.fftfreq(N)[:, None]
fy = np.fft.fftfreq(N)[None, :]
f = np.sqrt(fx ** 2 + fy ** 2)
f[0, 0] = 1.0
amp = 1.0 / f ** 1.4
amp[0, 0] = 0.0
phase = rng.uniform(0, 2 * np.pi, (N, N))
h = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
h /= h.std()

# periodic central differences
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
k = 0.35  # slope scale, the shader multiplies by _WaveStrength
n = np.stack([-gx * k, gy * k, np.ones_like(h)], -1)   # +y of the image = north
n /= np.linalg.norm(n, axis=-1, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(f"{out}/waves.png")
print("waves.png", float(np.abs(n[..., :2]).max()))
