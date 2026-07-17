# TactiPaint

**Mixed-reality finger painting on your real room, with vibrotactile texture feedback.**

TactiPaint is a Meta Quest 3 / Quest 3S experience where you paint directly onto your physical
environment — your desk, your walls — using nothing but your fingers. The room is
scanned by the headset's depth sensors, your index fingers become brushes, and a pair
of bHaptics TactGloves lets you *feel* what you paint: rock crackles under your
fingertip, tile grout clicks as you cross it, ripples pulse as you sweep across them.

Built with Unity 6, the Meta XR SDK and MRUK (Mixed Reality Utility Kit).

## What it does

### Painting
- **Right index finger paints color**, left index finger paints **surface relief**
  (normal-map textures: rock, domed blobs, tile grid, waves — or smooth, which
  flattens relief away).
- Paint lands exactly on the scanned surface plane — strokes lock to the touched
  face (scene-anchor boxes or the room mesh), so painting on a table stays at one
  consistent height no matter how your finger wobbles.
- Color and relief compose: paint purple, rub relief over it and the purple gains
  bumps; recolor a textured area and it keeps its texture.
- Strokes are generated as real-time blob meshes (no LineRenderers), batched into
  one mesh per color/texture combination.

### Feeling (TactGlove haptics)
- Each relief texture has a distinct vibrotactile profile, designed around
  contrast (silence between sharp events): irregular crackle for rock, regular
  pops for blobs, hard sparse clicks for tile lines, fast directional ripples for
  waves.
- **Feel mode**: touch anything you've painted with *any* of your ten fingers and
  each finger independently plays the haptic profile of the texture under it.
- Haptics fire only while a finger is moving — a resting finger is silent, like
  real touch.
- Interface events have their own haptic vocabulary: wrist buzz for the menu
  toggle and slider, fingertip clicks for buttons and palette picks.

### Hand UI
- **Left inner wrist**: menu button → mode panel (Feel / Paint / Erase / Clear-all).
- **Right inner wrist**: stroke-width slider with haptic detents.
- **Paint mode palettes** hover over your fingertips — colors on the left hand,
  normal-map spheres (showing actual lit relief) on the right. Touch a swatch with
  the other index finger to select; a small sphere on each painting fingertip
  shows the current selection.
- Everything is palm-gated: menus appear only while that palm faces you.
- A floating intro panel (with video) lazy-follows the user and dismisses with a
  fingertip touch.

### Exhibition-ready details
- Take the headset off, hand it to the next person: on re-mount the scene wipes,
  defaults restore, and the intro menu + video restart automatically.
- Multimodal hand + controller tracking is enabled, so a Logitech MX Ink stylus
  can be used alongside hand tracking.
- Editor preview tool (`GameObject > FingerPaint > Hand Menu Preview`) to tweak
  the hand-menu layout without deploying.

## Hardware

| Item | Notes |
|---|---|
| Meta Quest 3 or Quest 3S | depth-sensor room scanning (Space Setup) |
| bHaptics TactGlove (pair) | texture + UI haptics; app works without them, silently |
| Logitech MX Ink (optional) | tracked stylus, works alongside hands |

## Setup

1. Clone the repo:
   ```
   git clone https://github.com/<your-username>/mixed-reality-tacti-paint.git
   ```
2. **Unity 6000.3.x** with Android build support.
3. Open the project — key packages (Meta XR Core SDK v203, Meta XR Interaction SDK,
   MRUK, bHaptics SDK2) restore via the Package Manager / are included in `Assets`.
4. **bHaptics key**: `Assets/Bhaptics/SDK2/Resources/BhapticsSettings.asset` is
   git-ignored (it holds a per-developer App ID / API key). Create an app in the
   [bHaptics Developer Portal](https://developer.bhaptics.com/) and enter your App
   ID + API key in that asset via the Unity inspector.
5. On the headset: run **Space Setup** (Settings → Physical Space) so a room scan
   with anchors (desk, walls) exists, and install the **bHaptics Player** app and
   pair your TactGloves to the headset.
6. Open the scene `Assets/FingerPaintMesh.unity`, Build & Run for Android.
7. First launch: grant the **spatial data** and **Bluetooth** permissions.

## Project layout

```
Assets/
  FingerPaintMesh.unity          main mixed-reality painting scene
  Scripts/
    FingerPaintMeshBootstrap.cs  one-stop runtime setup (room mesh, brushes, rigs, menus)
    FingerTipContact.cs          fingertip surface detection (plane lock, collider preference)
    FingerTipPainter.cs          paint / erase per mode
    FingerTipHaptic.cs           per-texture vibrotactile profiles, motion-gated
    MeshStampBrush.cs            blob-mesh paint generation, coverage grid, eraser
    HandPaintRig.cs              per-hand fingertip rig (index paints, all fingers feel)
    HandToolMenu.cs              wrist menus, palettes, slider, selection indicators
    HandMenuPreview.cs           edit-mode layout preview
    PaintModeManager.cs          global mode / color / texture / width state
    FloatingMenuController.cs    head-following intro panel
  Icons/                         menu button icons
  normalMap1.jpg + textures      relief normal maps
```

## About this project

This project was developed by **Team TBD** at the **IVE Winter School 2026: Haptics
in XR**, held 13–17 July 2026 at the Mawson Lakes Campus of Adelaide University,
Adelaide, Australia.

**Team TBD:**
- Mohamed Fareed
- Awnili Shabnam
- Forhad Hossain
- Rodrigo Dias Takase
- Rob Teather

The Winter School is a free five-day intensive short course offered by the
[Australian Research Centre for Interactive and Virtual Environments (IVE)](https://ive.unisa.edu.au/),
exploring haptics and its applications in XR through expert talks, demonstrations
of cutting-edge haptic technologies, hands-on learning activities, and
collaborative team projects focused on real-world haptics applications.
TactiPaint is Team TBD's team project from the program.

## License / credits

- Meta XR SDK, MRUK — Meta Platforms (Oculus SDK License).
- bHaptics SDK2 — bHaptics Inc.
- Built as a research prototype for tangible mixed-reality interaction.
