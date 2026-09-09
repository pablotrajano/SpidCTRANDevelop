import os
import re

icons_to_fetch = [
    "lock", "lock-open", "chevron-down", "chevron-up", "headset", "forward", "rotate-left"
]

base_dir = "/Users/pablotrajano/Desktop/CTRAN/Desenvolvimento/SpidJulho/SolucaoSpidJulho/Fontawesome/duotone"

for icon in icons_to_fetch:
    path = os.path.join(base_dir, f"{icon}.svg")
    if os.path.exists(path):
        with open(path, "r") as f:
            content = f.read()
            # Extract viewBox
            vb_match = re.search(r'viewBox="([^"]+)"', content)
            vb = vb_match.group(1) if vb_match else "0 0 512 512"
            # Extract paths
            paths = re.findall(r'<path[^>]*>', content)
            print(f'["{icon}"] = ("{vb}", @"{"".join(paths)}"),')
