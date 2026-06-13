# CLAVIS Design Language

A design system for the CLAVIS desktop application. Pitch black, content-first, no chrome where avoidable,
no rounded corners anywhere. Color is reserved for meaning, not decoration. Everything that appears,
disappears, or takes focus is animated.

This document defines the system (the *what* and *why*). The companion HTML files in `design/mockups/` show
it visually (the *how*):

- `clavis-design-system.html` - palette, type roles, lines, tint, icons, accent semantics, the text reveal.
- `clavis-send-animation.html` - prompt-send animation explorations (rail charge is the chosen one).
- `clavis-fall-animations.html` - gravity falls (splash, windows) and the live growing turn rail.

The tokens below are realised at runtime from a **theme file** (see section 2); they are not hardcoded.

---

## 1. Principles

These override individual decisions. When in doubt, return here.

**Content before chrome.** The work - a conversation, a file, a hierarchy - is the brightest, most prominent
thing. Scaffolding (titles, labels, separators) fades until summoned. Prefer alignment, spacing and typography
over borders and boxes. Most "containers" are invisible grids that merely align their contents.

**No rounded corners. Anywhere.** Every rectangle edge is sharp (`CornerRadius="0"`, or omit it). This is a
rule about rounded *rectangles*, not round *shapes*: circles and ellipses are encouraged. Status and activity
indicators are always **circles** (an `Ellipse`), never small bordered boxes - a dot is a circle, not a
rounded box.

**Color is meaning, not decoration.** Outside the greys, every colour carries a fixed meaning (section 3).
Accents and signals appear in thin bars, small dots, single characters, tiny badges - never as a panel fill.

**Three voices.** Type identifies who is speaking without a label: **Rajdhani** is the UI / human / chrome
voice, **Inter** is the agent's prose voice, **JetBrains Mono** is the machine voice (code, data, identifiers).

**Pitch black.** Pure `#000000` is the canvas, not a fallback. Elevation is an extremely subtle raised surface,
never a lighter grey wash.

**Everything animates.** Any element that appears, disappears, moves, or takes focus is animated, at a 250ms
floor (section 7). Decorative motion that carries no information is still forbidden - but a state change with no
motion is now equally wrong.

**Keyboard first.** Every mouse action has a keyboard equivalent. Shortcut hints appear in the dim UI label
style at the foot of any panel with shortcuts.

---

## 2. Theme files

The visual tokens are defined in **YAML theme files** under `~/.clavis/themes/`, in the `clavis-theme v1`
format (a Clavis-specific format inspired by the W3C Design Tokens spec, but YAML, icon-aware, and wired to the
runtime). The shipped theme is `~/.clavis/themes/default.yaml`.

- **Multiple themes**: drop more files alongside `default.yaml`. The active theme name is the `theme:` setting
  in `~/.clavis/configuration.yaml` (defaults to `default`); that selection goes through the Configuration
  plugin. The theme *content* is read directly by the host (via the `FabioSoft.Marketplace.Io` YAML
  bridge) **before the bus exists**, so the splash and `Application.Resources` are themed from the very start.
- **Single source of truth**: a theme defines fonts, the six type roles, the colour palette, the semantic
  meaning→token map, the one line, the one tint, the icon set, and motion tuning. No view hardcodes a colour,
  font or size - it references a resource key the loader populates from the theme.
- **Format**: see `~/.clavis/themes/default.yaml` for the annotated schema.

---

## 3. Color

The entire palette. Nothing outside this list. Black and white are the extremes; two greys carry text; two
accents carry meaning; three signals communicate status. A body-prose tone and a faint surface round it out.

| Token | Value | Use |
|---|---|---|
| `black` | `#000000` | Base background / canvas |
| `white` | `#FFFFFF` | Rare pure highlight |
| `text` | `#E8E8EC` | Primary text / emphasis |
| `text-dim` | `#9A9AA4` | Secondary text / labels |
| `body` | `#C8C8D0` | Body-prose tone (between `text` and `text-dim`) |
| `line` | `#5A5A66` | The single border (section 5) |
| `surface` | `#0E0E14` | Faint raised surface: hover / selection / popup fill |
| `primary` | `#9FD5F0` | **Primary accent** - the Clavis blue |
| `secondary` | `#ADA6F2` | **Secondary accent** - periwinkle |
| `green` | `#7BD49B` | Signal: ok / added |
| `red` | `#E47E7E` | Signal: problem |
| `yellow` | `#E4C47E` | Signal: warning |

### Accent semantics

Two accents only earn their keep if each carries a consistent meaning. Learn it once, read it everywhere:

- **Primary (blue) = STATE.** "Is this active, good, or clickable right now?" - focus, success, links, the
  breathing live-turn dot, markdown **H1**.
- **Secondary (periwinkle) = IDENTITY.** "What is this thing *called*?" - keyboard gestures, commit hashes,
  symbol names, file paths, @mentions, the *kind* of a list item, markdown **H3**.

### Rules

- Never tint a large area with an accent or signal. They appear in 1-2px bars, small dots, single characters,
  tiny badges.
- Signals reuse nothing new: event log levels are `trace=text-dim`, `debug=green`, `info=primary`,
  `warn=yellow`, `error=red`. Window-identity dots vary the accents *by hue only*, they do not add tokens.
- Status overrides accent when both could apply (a waiting turn gets the warning treatment, not the live-state
  blue).

### Transparency

One tint, **black only, one opacity**: `rgba(0,0,0,0.85)`. Every veil and popup backdrop uses it. Hover and
selection on a black surface use the solid `surface` token (`#0E0E14`), never a white or grey alpha wash - that
keeps "black tint only" true.

---

## 4. Typography

Three voices (section 1), **six roles**, four sizes (`14 / 16 / 20` for text, `12` code-mono, `14` meta-mono),
three weights (`Normal / Medium / SemiBold`). Accent colour is an **optional overlay** on any role, never part
of it.

| Role | Font | Size | Weight | Use |
|---|---|---|---|---|
| `title` | Rajdhani | 20 | SemiBold | Window / splash / screen titles |
| `heading` | Inter | 16 | SemiBold | Document & section headings |
| `body` | Inter | 16 | Normal | Responses, descriptions, body copy |
| `label` | Rajdhani | 14 | Medium | Chrome labels, badges, buttons, section heads |
| `mono` | JetBrains Mono | 12 | Normal | Code blocks, commits, events |
| `meta` | JetBrains Mono | 14 | Normal | Durations, counts, timestamps |

The `mono` (code) size is deliberately one step **below** body: code is dense reference material, not prose.
In the WPF implementation it is centralised as the `CodeFontSize` resource (WpfHost `Theme/Styles.xaml`)
mirrored by `MarkdownPresenter.baseCodeSize` (clavis-rendering) - change the two together.

**Modifiers** (not new roles): **bold → `text`** (bright), *italic → `text-dim`* (thinking / quotes),
accent → `primary` / `secondary` per the semantics above.

**Markdown mapping**: H1 → `primary`, H2 → `text` (bright), H3 → `secondary`, links → `primary`,
strong → `text`, emphasis → italic `text-dim`, inline code & code blocks → `mono` on `surface`.

**Letter-spacing**: uppercase labels get `1-2px` tracking; body and mono get `0`.

**No 700+ weights** - heavy weights fight the editorial feel.

---

## 5. Lines

Default to **no border** - let spacing and type carry structure. When a line is unavoidable (e.g. a black popup
on a black background), there is exactly one:

| Aspect | Value |
|---|---|
| Width | **1px**, everywhere |
| Colour | `line` (`#5A5A66`) |
| Focus / highlight | the same line, recoloured to `primary` (+ a soft glow) |

No dashed or dotted borders (the single tolerated exception is the live-edit indicator in the editor). The
focus treatment is identical for a focused window, panel, input, or drop-target.

---

## 6. Iconography

Geometric **vector glyphs** drawn by a shared Icon factory - straight strokes, square caps, mitred joins,
`stroke-width ~2.4` - never font glyphs. One meaning per icon.

| Icon | Glyph | Colour | Meaning |
|---|---|---|---|
| Good | check | `primary` | success / ok |
| Problem | cross (scaled ~1.15×) | `red` | error / failure |
| Warning | exclamation | `yellow` | needs attention |
| Expand | chevron (large) | `text-dim` → `primary` on hover | collapsible / clickable |
| Close | thin cross | `body` | dismiss (visually distinct from "problem") |
| Status | dot (circle) | `primary` | live / activity |

The cross is scaled up so its visual weight matches the check. "Close" is deliberately a different weight and
neutral colour so it never reads as "problem". Status indicators are always circles.

---

## 7. Motion

One transition duration governs the app: **250ms, CubicEaseOut**, for everything that appears, disappears,
hovers, or takes focus. The old sub-250ms tiers (instant/quick) are gone - they were imperceptible. The only
other persistent timing is the **breathing pulse**: 600ms, SineEase, opacity oscillation on the active turn /
phase dot (circles only).

### Easing

- **CubicEaseOut** - `cubic-bezier(0.2, 0.7, 0.2, 1)` - entrances and all standard transitions.
- **SineEaseInOut** - the breathing loop only.
- **Gravity** - falls are a real physics tween, not an easing curve (see Falls below).

### Animation catalog

These are the deliberate animations that mark CLAVIS. Tunings live in the theme's `motion` block.

**Row entrance.** Every row (turn, tool, hook, error, permission, phase) enters with **fade + slide-up**
(8px for a turn, 6px for the rest), 250ms. Disappearance animates too (fade + slight scale, 250ms).

**Text reveal.** Agent / markdown text never just appears. It reveals:
- **≤ 3 wrapped lines** - a **char wave**: each character fades 0→1 over a fixed window, starts staggered so a
  lit band moves through the text. The per-char step *shrinks as the text grows*, so a 2-3 line block stays as
  snappy as one line.
- **> 3 wrapped lines** - a **cinematic line-fall**: the first line slides in from above, then every other line
  falls out from behind it into place, staggered. The stagger is capped by line count, so even a 10,000-char
  document completes in **under 2 seconds**.

**Live turn rail.** While a turn is active, the blue timeline rail (the Rail column, section 8) **grows with the
content**: as each chunk of the response streams in, the rail eases to the new content height (250ms), so it is
always exactly as tall as the turn. The rail dot breathes while active and rests at full height when the turn
completes.

**Prompt send - the rail charge.** Sending a prompt is animated by the **rail charge**: the input empties while
a primary streak runs up the new turn's timeline rail, the rail line grows in, and the dot flares. The
conversation "powers up" the new turn. (Crossfade - input text fades out as the human line fades in - is the
reduced-motion fallback.)

**Falls (splash + windows).** Initial windows and the splash **fall in from the top under gravity**: a real
physics tween where velocity accumulates, so motion **starts slow and accelerates**, landing with a small
damped bounce (a real object that lands doesn't stop dead; bounce is tunable to 0). The **splash** then
**free-falls out the bottom** when boot completes. Multiple initial windows fall in **staggered** so a
multi-window launch cascades.

**Focus.** A focused window / panel / input / drop-target animates its line to `primary` + soft glow over
250ms.

**Reduced motion.** When the OS "reduce motion" flag is set, falls and the rail charge collapse to the plain
fade entrance, and the text reveal collapses to a single 250ms fade.

---

## 8. Turn row layout

Each user interaction renders as one grouped row with a timeline rail. Use these column names everywhere
(code, comments, docs):

| Column | Width | Name | Purpose |
|---|---|---|---|
| 0 | 72px | **Stats** | Duration + token count, right-aligned, left of the rail (`meta` role) |
| 1 | 20px | **Rail** | Breathing dot + the vertical timeline line connecting request to response |
| 2 | * | **Content** | Human request at top (`label`/human voice), agent response + tool rows below |

The rail line is `primary` at reduced opacity; it grows with the content (section 7). The init row (no human
request) uses the legacy 3-column layout.

---

## 9. Components & patterns

Components inherit the tokens above; only the deviations are noted here.

- **Window** - background `black`; title bar 28px with a 1px `line` bottom; grip dots (circles) at low opacity;
  close = the thin-cross icon. Falls in on launch (section 7).
- **Input bar** - bottom of the conversation; 1px `line` top that recolours to `primary` on focus; the cursor
  is a 1px `primary` caret. Send triggers the rail charge.
- **Status / activity** - a circle (`Ellipse`), accent or signal colour, breathing when live.
- **List item** - selection via a 1.5px `primary` left border + a brightening of text from `text-dim` to
  `text`; hover uses the `surface` fill. No heavy highlight.
- **Tool / hook / phase rows** - share the turn grid; expand via the large chevron icon (hover → `primary`).
- **Badge** - a small square `surface` chip holding a `label`-role caption whose colour carries the category.
- **Popup / overlay** - the one `tint` behind it; a 1px `line` frame only where black-on-black needs separating.

The defining conversation pattern remains the **Stats / Rail / Content** grid (section 8): metadata sits in the
gutter, the rail connects request to response, the content fills.

---

## 10. Translation to WPF

CLAVIS is F# + WPF. Implementation notes:

**Theme loading.** The host reads the active theme YAML early (before the bus) and populates
`Application.Resources` with the brush and `FontFamily` resources the views reference via `DynamicResource`. A
directly-set `Application.Resources` entry overrides the merged `Styles.xaml` dictionary, so a theme swap is a
re-population, not a recompile. `Styles.xaml` (in WpfHost) holds the control templates; the *values* come from
the theme.

**Resource keys.** Brushes and fonts are referenced by key (e.g. `{DynamicResource TextBrush}`,
`{DynamicResource MonoFont}`); never hardcode a colour, size or font in a view. The six type roles are shared
`Style`s (`Clavis.Text.Title`, `.Heading`, `.Body`, `.Label`, `.Mono`, `.Meta`).

**Icons.** A shared Icon factory in `clavis-rendering` draws the geometric glyphs as `Path` geometry (good /
problem / warning / chevron / close / dot), themed via resource keys - not per-plugin glyph literals.

**Motion.** `Motion.fs` exposes one 250ms helper, the breathing loop, the text-reveal (char wave + line-fall),
the live-rail grow, the rail-charge send, and the gravity fall (with tunable bounce/restitution). Per-template
inline storyboards call these rather than redefining timings.

**Avoid static registries.** No new `DependencyProperty` / attached properties (they root types and break
collectible plugin unload). Use `INotifyPropertyChanged` for bindable state; the single tolerated DP is
`MarkdownPresenter.Markdown` in the shared rendering assembly.

**Naming.** XAML resource keys follow `Clavis.<Category>.<Variant>` - e.g. `Clavis.Color.Primary`,
`Clavis.Brush.Surface`, `Clavis.Text.Label`.

---

## 11. The living reference

When implementing a screen, open this file alongside `design/mockups/clavis-design-system.html`: this markdown
answers *what* and *why*, the HTML shows *how it should look*. The animation mockups
(`clavis-send-animation.html`, `clavis-fall-animations.html`) show the motion in action. When in doubt, return
to the principles in section 1.
