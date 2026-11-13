# Visual assets

## Minecraft fox icon

- The header icon and browser favicon use the small `EntitySprite_fox.png` sprite referenced by the Minecraft Wiki fox article: https://minecraft.wiki/w/Fox
- The image is loaded from the Wiki file endpoint rather than copied into this repository.
- The footer links to the source article. The sprite remains an asset owned by its original rights holder; confirm applicable usage terms before using it on a public/commercial deployment.

## Cooler illustration

- `wwwroot/img/fan-cooler.svg` is the SVG supplied by the project owner.
- The SVG exposes separate `fan-housing` and `fan-rotor` groups so the frame can remain still while the blades rotate.
- The monitoring page uses the rotor animation when RPM is positive, pauses it when a sensor reports zero, and keeps a low-speed visual rotation when RPM data is unavailable.
- The animation respects the user's reduced-motion preference.
