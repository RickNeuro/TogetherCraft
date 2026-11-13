# Visual assets

## Minecraft fox face icon

- Header icon and browser favicon use `FoxFace.png`, the 64×64 fox face image listed in Minecraft Wiki's [Mob faces category](https://minecraft.fandom.com/wiki/Category:Mob_faces).
- Image URL: https://static.wikia.nocookie.net/minecraft_gamepedia/images/8/88/FoxFace.png/revision/latest?cb=20190213180659
- The footer credits and links to the [FoxFace.png file page](https://minecraft.fandom.com/wiki/File:FoxFace.png).
- The image is an existing Minecraft Wiki / Mojang image, not original TogetherCraft artwork. Confirm applicable usage terms before a public deployment.

## Cooler illustration

- `wwwroot/img/fan-cooler.svg` is based on the SVG supplied by the project owner.
- The SVG exposes separate `fan-housing` and `fan-rotor` groups so the frame can remain still while the blades rotate.
- The monitoring page uses the rotor animation when RPM is positive, pauses it when a sensor reports zero, and keeps a low-speed visual rotation when RPM data is unavailable.
- The animation respects the user's reduced-motion preference.
