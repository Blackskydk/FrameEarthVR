# Steam Frame movement and hand UI checks

Rebuild the APK and upload it again with the SteamOS Devkit Client, using the
same title and Lepton runtime. These changes require a new installed build.

- Look toward the left-hand globe position. The globe and four round controls
  should appear without opening the full menu. Looking away hides them after
  0.2 seconds. Reveal requires looking within 12 degrees of the globe;
  remaining within 18 degrees retains it. Looking directly at the revealed
  controls or an open menu keeps them visible; an active drag also retains it.
- Confirm the Flight/Grounded, Vignette, Favorite, and Open Menu controls sit
  to the right of Earth without covering the enlarged globe. Open Menu shows
  the main panel further to the right; Close Menu hides that panel.
- Inspect the main, search, and places panels while moving your head and hand.
  Each should face you from its own center, rather than tilting with the globe.
  An open menu should hide when you look away, then return on deliberate focus.
- Turn your head slowly across a city and inspect both eyes' viewport edges.
  Android now includes a non-rendering terrain loading camera covering the
  union of both eye frusta with a 20% tangent margin. Check whether edge pop-in
  improves, and monitor memory and tile-loading overhead during fast turns.
- Open Search and point at each keyboard key. The hovered key should have a
  bright yellow background, dark lettering, and a white outline. Move away
  and confirm its normal appearance returns. Check the state controls too.
- Confirm both colored controller grips, the right pointer beam, and the
  globe texture render in both eyes on Android, including after restarting.
- Walk in Grounded mode across flat terrain and rooftop boundaries at normal
  speed and with boost. Slight downward controller aim must not change scale.
  Aim almost straight up/down and use the stick to deliberately change scale.
- Move while tiles load/refine, then stop. Floor correction should ease rather
  than jump, and should never step onto a hand UI collider or reuse a distant
  asynchronous height sample. Large height changes now settle more slowly.
- Try globe travel, planetary overview, and Flight/Grounded switching. Travel
  and overview should suppress hand UI; controls should return afterward.
- Compare the same viewpoint and scale against Windows after tiles finish
  loading. Android now uses the same maximum screen-space error (6), full
  render scale (1.0), and a 1536 MB terrain cache (previously 10, 0.9, and
  1024 MB). Lower screen-space error requests finer terrain at a given distance;
  the camera distance limit is already shared with Windows and fog culling is
  disabled. Check frame time and memory while moving through a dense city;
  the higher-detail profile increases GPU, streaming, and memory demand.

C# compilation has been checked against the project's Android and editor
assembly references. Shader compilation, stereo rendering, and comfort must
still be validated in an actual Unity APK build on the headset.
