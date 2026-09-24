# Hoodie — Character Bible

The single supplied image (`docs/reference.png`, 559 × 895 px) is the immutable identity reference.
Everything below was measured from it (pixel sampling + silhouette analysis, see `tools/render_rig.py`).
The in-app character is a vector cutout rig (`src/HoodieCompanion/Assets/rig.json`) traced from these
measurements; its silhouette overlaps the reference silhouette with **IoU ≈ 0.96**.

![reference](docs/reference.png)

## Silhouette & proportions (reference pixels)

| Feature | Measurement | Notes |
|---|---|---|
| Full figure (hood top → sole) | y 70 → 835 (**765 px**) | The unit of scale: 150 DIP in the app at 100 % size |
| Figure width (sleeve to sleeve) | x 104 → 453 (≈ 350 px) | ≈ 0.46 × height: a stout, pear-like body |
| Head / hood | y 70 → ≈ 380, widest 124 → 420 at y ≈ 250 | Head ≈ **40 %** of total height: big-head chibi proportion |
| Face opening | cream ellipse centred (244, 254), 176 × 198 px | Face sits **left of** the hood centre → the figure faces slightly screen-left |
| Hood rim | dark ring ≈ 10 px around the face; tan inner-hood crescent on top of the face | Gives the hood depth |
| Eyes | two vertical ovals at (191, 237) and (286, 239), ≈ 18 × 38 px | Only facial feature. **No mouth, no nose, no brows.** |
| Eye spacing | ≈ 95 px centre to centre (≈ 0.54 × face width) | Eyes sit slightly above the face centre line |
| Hoodie body | y ≈ 330 → 620, widening to the hem | Rounded, bell-shaped torso, darker ribbed hem band y ≈ 585 → 620 |
| Drawstrings | two cream strings, x 216–222 and 274–280, y 365 → 450 | Right string slightly longer |
| Sleeves | hang straight down beside the body, cuffs at y ≈ 590 → 610 | Hands barely visible: tiny cream crescent at each cuff |
| Pants | dark, wide, cropped; legs split at y ≈ 630; hems at y ≈ 730–745 | Clear gap between the legs |
| Socks | cream, ≈ 28 px wide, visible ≈ 35–45 px, tan shade at the top | |
| Shoes | low dark sneakers, cream sole and three cream stripes | Left shoe points left, right shoe points left-forward |
| Ground shadow | dark ellipse centred (272, 810), 424 × 70 px | Drawn semi-transparent (42 %) on the desktop — see *Adaptations* |

## Colours (sampled)

| Role | Hex |
|---|---|
| Hoodie / hood fill | `#34363E` |
| Hood inner shade | `#262930` |
| Hem band | `#25282E` |
| Pants | `#23282F` |
| Shoes | `#1B2126` |
| Outline / line art | `#15181D` (≈ 6 px at reference scale, round joins) |
| Face | `#FDF5DC` (warm off-white) |
| Inner-hood tan / sock shade | `#E3C3A0` |
| Eyes | `#26282A` |
| Strings, socks, soles, stripes | `#FBF4E2` |
| Reference background (used as the brand accent in the UI) | `#F2BD60` |

## Visual invariants (must hold in every pose)

1. One hood that fully frames an oval, featureless face with **exactly two** vertical oval eyes.
2. Dark charcoal hoodie with two cream drawstrings; darker hem band.
3. Two sleeves, two dark cropped trouser legs, two cream socks, two dark sneakers with cream soles and three stripes.
4. Big head (~40 % of height), short legs, stout body — the chibi proportion never changes.
5. Thick dark outline with round joins; flat fills; no gradients, no textures, no highlights.
6. No accessories, no extra clothing, no patterns, no colours beyond the table above.
7. The whole body is always present (no cropping, no missing parts), same scale, feet registered to the ground.

## How the rig preserves identity

* Parts are grouped (`legL`, `legR`, `torso`, `strings`, `armL`, `armR`, `head`, `face`, `eyes`, `shadow`, `item`)
  and animated **only** with pivot rotation, small translation and mild scale. Geometry never changes between
  poses, so clothing, limb count and silhouette cannot drift.
* Facing right is an exact mirror of the reference (the reference faces slightly left).
* "Look" moves the face and eyes a few pixels inside the hood; blinking scales the eyes vertically
  (closed eyes become thin lines — still the same two eyes).
* `tests/…/AnimationTests.EveryClip_KeepsBodyIntact` checks every clip at 60 Hz for collapsed scales, detached
  limbs, head drift and opacity; `RigJson_IsComplete` checks the part inventory (2 shoes, 2 sleeves, 2 eyes…).
* `HoodieCompanion.exe --render-poses sheet.png` renders every clip into a contact sheet
  (`docs/qa/pose-contact-sheet.png`) for visual review.

## Adaptations (technically essential, documented)

| Adaptation | Why |
|---|---|
| Ground shadow drawn at 42 % opacity and hidden while airborne | An opaque dark ellipse reads as a hole on real wallpapers; the shadow belongs to the ground, not to the flying body |
| "Backpack" is Hoodie's hoodie pocket (no backpack drawn) | The bible forbids adding accessories; objects are tucked into the hoodie |
| A small cream note card appears only when Hoodie holds an object (catch / inspect / reminders) | Needed to communicate "an object" diegetically; it is a prop, not clothing |
| Tiny marks next to the head (`!`, `?`, `…`, `Zzz`, sparkles, dust, heat lines) | Communicate reactions without adding facial features |
| Mild squash & stretch on landings/jumps (disabled by Reduced Motion) | Physical readability; proportions return exactly to rest |

## Character notes

Quiet, observant, curious, slightly lazy, helpful, subtly playful, independent. Entertains itself; never begs for
attention, never punishes absence, has no hunger or happiness meters. Internal drives (energy, curiosity,
playfulness, social interest, comfort) only vary its behavior and are never shown.
