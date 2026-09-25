# Controls

All bindings live in `Assets/EarthVR/Input/Resources/EarthVRInputActions.inputactions`. Gameplay code consumes `IEarthVRInput`, not device buttons. Rebind there without changing navigation code.

## Controller defaults

| Hand/control | Logical action | Behavior |
|---|---|---|
| Dominant/right thumbstick | Scale-and-fly | Flight uses adaptive speed; Grounded mode keeps feet on the surface and converts vertical intent into gradual scale changes |
| Either trigger | 3D Cone Drag | Point at visible terrain, hold trigger, then aim the selected point where it should move; distant points move proportionally faster. Releasing while moving briefly carries the world's momentum, decaying to a stop. If the ray misses any terrain (ocean, a loading gap, or aiming past the horizon), it falls back to where the ray crosses the planet, so a grab practically never fails |
| Both triggers together | Two-handed grab | Grab a point with each hand to pan, rotate, and zoom the world in one gesture: spreading your hands apart zooms in, twisting them yaws the world, and moving them pans it — the same combined gesture Google Earth VR uses |
| Either grip | Rotate | Hold and turn the controller to yaw Earth around you; pitch and roll are ignored so the ground stays underneath |
| Right shoulder (Steam Frame) | Flight boost | Hold in Flight mode for 8× travel speed; it never grabs, rotates, or scales Earth |
| Left View / pause button (Steam Frame) | OpenMenu | Show/hide the wrist menu, which starts hidden |
| Left B / secondary | ResetView | Restore a comfortable upright orientation |
| Point at Sun/Moon + trigger | Time of day | Drag along the physically valid solar path for the current date and map location; the interaction owns the trigger until release |

Keyboard test fallbacks: `W`/`S` fly forward/reverse, `R` resets upright, and `G` toggles movement mode. These are diagnostic conveniences for a non-VR Play Mode smoke test, not the primary locomotion scheme.

In Flight mode, aimed thumbstick movement is normal three-dimensional flight with altitude-adaptive speed. In Grounded mode, controller pitch continuously controls scaling and a terrain ray steers movement toward the point under the pointer. Forward travel fades to zero near a straight-up/down aim, making a straight-down gesture scale-only; at shallower angles, movement and scaling blend together without reversing heading. Normal scaling runs at 1.6 doublings per second at full input, scaling keeps the user's feet planted, and the cached terrain height remains valid when Earth scale changes.

New sessions start at local solar noon for the current date and starting location, ensuring the map initially loads in daylight. A white guide draws only the above-horizon arc: the Sun's arc by day and the Moon handle's antipodal arc by night. It is hidden until the user looks toward the active body, starts appearing within 10 degrees, and reaches full visibility within 3 degrees. The arc is brightest beside the active body and falls off with path distance. The Sun or Moon grows when either controller is accurately aimed at its enlarged invisible hit area, providing grab-ready feedback. Trigger-dragging the body chooses time along its calculated arc and captures the trigger until release. At night the larger, crater-shaded Moon makes returning to daylight straightforward.

Directional sunlight, cool moon fill, soft shadows, ambient/reflection intensity, warm sunset grading, exposure, procedural sky gradients, stars, and a subtle Milky Way respond to solar elevation. The sky shader uses Unity's stereo instancing macros, and the Sun/Moon handles sit 500 metres away to minimize binocular disparity.

Controller pointers are low-opacity white, retain a visible angular width over long distances, stop at the first physics surface they hit, and show a translucent white hit marker on that surface. Google photogrammetry physics meshes must remain enabled for exact point grabbing. Earth drag stores exactly one selected point in ECEF coordinates for the entire trigger hold. The stabilized pointer ray and amplified push/pull depth determine that fixed point's horizontal target each frame, while player height remains fixed so pulling Earth underneath cannot lift the user. The navigation transform is solved directly without a second easing stage. Ground correction temporarily yields to the grab solver so it cannot fight the drag.

Google Photorealistic 3D Tiles use a lower screen-space-error target than before (6 px on PC, 10 px on Frame), sibling preloading, 12 concurrent loads, larger caches, and no horizon fog culling. This requests sharper and more persistent distant geometry, subject to the detail present in Google's source data, available bandwidth, memory, and headset performance.

The rounded wrist menu uses a dark translucent dashboard, cyan mode badge, grouped status and diagnostics cards, compact control hints, and high-contrast interactive states. It contains recenter, movement mode, a comfort vignette toggle, search, and performance-detail controls. Open or close it with the left View/pause button, then point the dominant-controller ray at a control and press trigger. There is no thumbstick turning.

A peripheral comfort vignette is available but off by default. When enabled from the wrist menu, it darkens gradually toward the edges of view as the camera's actual physical speed through space increases (not raw geographic speed, which says nothing about how fast the view visually moves once the world is scaled), and fades back out once movement slows or stops.

In Flight mode, forward speed eases down automatically as the ground below gets close, so a fast, low pass over terrain settles instead of flying straight into a hillside. The shoulder flight boost now spools its speed multiplier in and out over a short, tunable ease rather than switching instantly. A procedural, non-spatial soaring-wind bed follows actual flight speed and stick intent: it grows louder and brighter as speed increases, then fades smoothly when movement stops or Grounded mode is selected.

Grounded mode keeps at least 1.5 cm of physical clearance above the currently loaded collision surface. If scaling or a newly refined photogrammetry tile places a roof or terrain above the tracking floor, the floor is moved clear in the same rendered frame; corrections toward lower ground remain smoothed for comfort.
