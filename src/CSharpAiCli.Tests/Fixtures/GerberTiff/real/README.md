# Gerber/TIFF Real-Tool Fixture

`minimal-square.gbr` is an original C-AICLI test fixture. It was written for
this repository and contains no third-party board design or manufacturing
data. The fixture is dedicated to the public domain under CC0-1.0; its Gerber
comment also carries the SPDX identifier.

The RS-274X file uses millimetres and 2.4 coordinates. It draws a closed
10 mm by 10 mm square with a 0.2 mm circular aperture and flashes a 1 mm
circular aperture at the 5 mm, 5 mm centre point.

Expected real-spike properties are recorded in
`docs_md/spec/gerber_tiff_toolchain_gate.md`. Generated PNG/TIFF files are
evidence, not source fixtures, and are not committed during Week 58.

`verification-baseline.json` is the strict Week 64 exact-hash and metadata
baseline for this path and the frozen Week 58 toolchain.

License: CC0-1.0 (`https://creativecommons.org/publicdomain/zero/1.0/`).
