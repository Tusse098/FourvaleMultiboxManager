# ADR 0007 — Visual hosting so focus swaps are instant

- **Date:** 2026-10-01
- **Status:** Accepted (player feedback: "switching flickers and doesn't feel instant enough")
- **Amends:** ADR 0003 (WebView2 hosting in the app), ADR 0004 (Focus layout)

## Context

In the Focus layout the focused slot is large. With the standard WPF `WebView2` control each game is a native child window, so a swap resizes two windows. The browser repaints at the new size a frame or two later, which shows as flicker, and each game rescales its canvas. A settle delay (0.2 s) reduced how often it happened but made switching feel slow. Neither is acceptable.

## Decision

The app (`Multibox.App`) uses **`WebView2CompositionControl`** (visual hosting, in the same WebView2 package) via `SlotBrowser.CreateComposited`:

- Every slot renders its page at **one shared render size**: the large tile's game area in the Focus layout, or the tile size in the Grid layout (`LayoutPlanner`, see the update below).
- Each panel shows that picture through a `Viewbox`. A small tile only scales it down.
- A focus swap only moves panels to new positions. No page changes size, so there is **no rescale and no flicker, in a single frame**. `focusSwapDelayMs` defaults to 0.
- Only a window resize, fullscreen toggle, layout change or slot open/close resizes the pages.

The capture tool keeps the native-window `WebView2` control.

## Update 2026-10-01

- `WebView2CompositionControl` needs the Windows SDK projection (`Microsoft.Windows.SDK.NET`). With plain `net10.0-windows` the app crashed on start (`FileNotFoundException` in `TryInitializeD3DImage`), so the Windows projects target `net10.0-windows10.0.17763.0`. Verified by a smoke test on unused profiles: the game renders and small tiles are scaled pictures.
- **Tiles are now exactly the game's shape.** Fourvale renders 1920×1080 with `Scale.FIT` (bundle 0.98), so any other tile shape showed bars inside the game. `LayoutPlanner` places 16:9 tiles: in Grid, the column/row shape that gives the largest games, with a partly filled last row centred; in Focus, one large tile plus a column of small tiles, using at most `focusTileShare` (0.3) of the width. Leftover space goes to the window edges. Panels sit on a `Canvas` at exact positions.
- Start with `tools/scripts/start.cmd` or `start-with-mirroring.cmd`. Both rebuild first, so a stale build cannot be started by accident (the output folder moved with the new target).

## Consequences

- **Cost:** every slot renders at the large size, so there is more GPU work with several slots. The composition control copies frames into WPF, which adds about one frame of display latency. Measure in the Phase 3 soak (criterion 8).
- **No airspace limits:** game views are ordinary WPF elements now, so overlays (spec §8.3 badges) become possible later.
- **Input:** keyboard and mouse reach the page through WPF. The keyboard hook (ADR 0005) is unchanged. Click mirroring uses `PointFromScreen` on the view, which accounts for the `Viewbox` scale, and canvas rectangles are in the shared render size, so all slots have the same canvas shape.
- **Background behaviour:** all slots stay visible (discovery H1). Re-check that small, scaled slots do not throttle (spec §15 criterion 5).
- **Fallback:** if the composition control performs badly, return to `SlotBrowser.Create` and revisit the swap design. This file records why it was changed.

**Other screens (2026-10-02).** Moving the window from the 3440×1440 ultrawide (125% scaling) to a 1920×1080 monitor (100%) looked wrong. The app was system-DPI aware, so Windows bitmap-stretched it on the other monitor. It now ships a per-monitor v2 DPI manifest (`app.manifest`), so the window and game views re-render at each monitor's scale. Layout never depended on the monitor's shape: tiles are always the game's 16:9 and fit the window's current size. The Focus layout now also picks the small-tile placement automatically: a column on the right (wide screens) or a row below (16:10, 4:3, portrait), whichever gives the larger main game.
