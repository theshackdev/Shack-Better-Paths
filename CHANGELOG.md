# Changelog

## 1.1.0
- **Paved road now only paints stone** over the ground and never changes its height, so a path graded with Pathen keeps its grade. It paints along the same strip as Pathen, and by default **only where the ground is already dirt or paved** (new setting "Paved road only on paths"). **Shift** paints a round patch where you aim. It replaces vanilla Paved road (setting "Paved road paints"; off gives vanilla back).
- **Build camera support (Shack Magic Cam):** in the camera, each click connects to your previous click instead of starting at your feet. The first click marks the start; a click more than 8 m away, or leaving the camera, starts a new path.
- **Stone in nearby chests counts** for Paved road (CraftFromContainers), and when you're short it paves as far as your stone covers instead of refusing.
- Paved road's marker shows on the ground where you aim.
- **The server and every player need 1.1.x.** 1.0.x and 1.1.x can't be mixed, so update both together.

## 1.0.0
- First release. The hoe's Pathen and Paved road grade an even slope from your feet to where you aim: flat across, blended edges, no steps between clicks. Pathen paints dirt, Paved road paves; the grass is cleared. Shift gives the vanilla tool. Paved road takes 1 stone per 2 m. Settings sync from the server.
- **The server and every player need this mod.**
