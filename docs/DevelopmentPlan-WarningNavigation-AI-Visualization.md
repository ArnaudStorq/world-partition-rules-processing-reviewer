# Development Plan — Warning Navigation, AI Fix Advisor and Visualization

Status: proposal (nothing implemented yet)
Target: `WPRulesReviewer` (WPF / .NET 9, MVVM with CommunityToolkit.Mvvm)

## 1. Goals

Three related features requested for the review workflow:

1. **Navigation** — a left sidebar exposing warnings as a browsable hierarchy instead of a flat,
   paginated grid of ~3 500 rows.
2. **AI assistance** — analyze warnings, produce recommendations, and apply fixes either in batch
   (a whole problem group) or one row at a time.
3. **Visualization** — a proportional view of the warnings (donut / sunburst / mindmap) to see at a
   glance where the mass of problems is.

The existing **Smart Analysis** panel is allowed to be restructured to host (2) and (3).

A fourth, transversal goal: **the application must be beautiful**. A third-party charting dependency
is accepted, and a dedicated design pass is part of the plan (section 6).

## 2. Current state (what we build on)

| Piece | Location | Notes |
| --- | --- | --- |
| Record model | `Core/Models/RuleRecord.cs` | Has `Category`, `WarningKind`, `Status`, `Severity`, `StatusReason`, `ActorPath` |
| Deterministic triage | `Core/Oracle/OracleEvaluator.cs` | Sets `Status` + `Severity` + `StatusReason` |
| Ad-hoc grouping | `SessionViewModel.GroupInsights` / `NormalizeReason` | Collapses reasons by replacing `'...'` with `'…'` |
| Smart Analysis panel | `Views/SessionView.xaml` (col. 2) | Tabs: Anomalies, AI Report, Assignments |
| AI plumbing | `Core/Ai/CursorAgentService.cs`, `AiReport.cs`, `AutoResolve.cs` | Headless `cursor-agent`, streaming NDJSON, strict-JSON plan parsing |
| Batch AI precedent | `Ai/AutoResolve.cs` + `AutoResolveDialog` | Pattern-based `match` rules applied deterministically by the tool, preview before apply |
| Delegation to Cursor | `SessionViewModel.OpenInCursor` | Writes a context `.md`, opens a Cursor deeplink |

Key takeaway: the *Auto-resolve reading* feature already established the right contract for AI work —
the AI returns **match rules**, the tool expands them deterministically, and the user previews before
anything is applied. The Fix Advisor should reuse that contract rather than invent a new one.

## 3. Phase 0 — Shared foundation: the problem taxonomy

Everything below (tree, charts, mindmap, AI prompt, fix journal) needs the *same* notion of "a
problem". Building three independent groupings would guarantee inconsistent counts, so this comes
first.

### 3.1 `ProblemSignature` (new, `Core/Analysis/ProblemSignature.cs`)

A stable identity for a class of problem, independent of the actor it happened on:

```csharp
public sealed record ProblemSignature(
    RecordCategory Category,
    WarningKind Kind,
    AssignmentType AssignmentType,
    string NormalizedReason,   // quoted values, GUIDs, digits and indices collapsed
    AnomalySeverity Severity)
{
    public string Id { get; }        // short stable hash, e.g. "WPR-3F2A91"
    public string Title { get; }     // human label
}
```

- Normalization is an extension of the current `NormalizeReason`: also collapse trailing numeric
  suffixes (`_A10_Fireflies_7` → `_A10_Fireflies_#`), GUIDs, and bracketed counts.
- The stable `Id` is what AI recommendations, the fix journal and cross-build comparisons reference.

### 3.2 `InsightTree` (new, `Core/Analysis/InsightTree.cs`)

Builds a hierarchical aggregation from `SessionReport.Records`, driven by an ordered list of
grouping dimensions:

```csharp
public enum GroupDimension { Status, Severity, Category, WarningKind, ProblemSignature,
                             AssignmentType, Value, OutlinerPath, Actor }
```

- `OutlinerPath` expands into one level per `/` segment (`LV_Overland` → `Region` → `Sub`), which is
  what makes the sidebar feel like the Unreal Outliner.
- Each node carries: `Title`, `Count`, `MaxSeverity`, per-status counts (`Anomaly` / `NeedsReview` /
  `Expected` / `KnownNoise`), `ReadCount`, and a `Predicate` used to filter the main grid.
- Children are built lazily (only when a node is expanded) so a 6 000-record session stays instant.
- Default preset: `Status → ProblemSignature → OutlinerPath → Actor`.

**Deliverables:** the two Core types + unit tests in `WPRulesReviewer.Core.Tests`
(`ProblemSignatureTests`, `InsightTreeTests`: counts sum to the total, normalization collapses the
known families, lazy expansion returns the same set as an eager build).

## 4. Phase 1 — Warning Explorer sidebar

### 4.1 Layout

`SessionView.xaml` currently is `[grid | splitter | SmartPanel]`. It becomes:

```
[ Explorer | splitter | grid + pagination | splitter | Smart Analysis ]
```

- Collapsible with the same handle-button pattern already used for `SmartPanel`.
- Width persisted through a new `AppSettings.ExplorerPanelWidth` (default 320), same mechanism as
  `SmartPanelWidth`.
- Does not conflict with the TeamCity builds panel, which lives one level up in `MainWindow`.

### 4.2 Content

- A **Group by** chip row at the top letting the user reorder/toggle dimensions, with 3 presets:
  *By problem* (default), *By Outliner path*, *By assigned value*.
- A virtualized `TreeView` (`VirtualizingStackPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`)
  bound to `WarningNodeViewModel`.
- Each row: severity dot, title, count badge, and a thin "read progress" bar (read / total) so the
  user sees what is left to review.
- A local search box filtering the tree (matches node titles and actor names, keeps ancestors).

### 4.3 Interaction

- **Single click** on a node → sets `SessionViewModel.ActiveNodeFilter` (a `Func<RuleRecord,bool>`
  consumed by the existing `FilterRecord`), so the grid shows exactly that subtree. A breadcrumb
  above the grid shows the active scope with an "×" to clear it.
- **Double click** on a leaf → existing `ShowRecord` behaviour (inspector + reveal in grid).
- Context menu: *Mark subtree as read*, *Approve subtree*, *Flag subtree*, *Ask AI about this node*,
  *Open in Cursor* (reusing the current `OpenInCursor` with the node's records).

**Deliverables:** `ViewModels/WarningExplorerViewModel.cs`, `ViewModels/WarningNodeViewModel.cs`,
`Views/WarningExplorerPanel.xaml(.cs)`, `ActiveNodeFilter` wiring in `SessionViewModel`, new setting.

## 5. Phase 2 — Visualization

### 5.1 Where it lives

Restructure the Smart Analysis tabs to avoid tab overflow:

| Before | After |
| --- | --- |
| Anomalies | **Overview** (charts) |
| AI Report | Anomalies (existing grouped list) |
| Assignments | **Fix Advisor** (Phase 4) |
| | AI Reports (merges the current *AI Report* + *Assignments* behind a small selector) |

### 5.2 Overview tab

- **Donut** of warnings by `ProblemSignature` (top 8 + "Other"), with a center label showing the
  total and the unread count.
- A second small donut by **status** (Anomaly / Needs review / Expected / Known noise), matching the
  three filter checkboxes already in the toolbar.
- **Horizontal bar chart**: top 10 problem groups by count, with a read/unread split.
- **Trend** (optional, later): same signature counts across the last N processed builds, to answer
  "is this getting better?". Requires persisting a per-build signature summary.

### 5.3 Mindmap / sunburst

- A **sunburst** is the honest answer to "a pie chart plus a mindmap": concentric rings = the same
  `InsightTree` levels, arc length = proportion, color = severity. Click a slice to drill down; the
  breadcrumb and the grid filter follow.
- If a more organic look is wanted, a **radial mindmap** on a `Canvas`: the session in the center,
  one branch per top-level node, node radius ∝ `sqrt(count)`, edges as Bézier curves, two levels
  shown at a time with click-to-recenter. No graph library needed — a simple radial layout
  (angle share proportional to count) is enough and stays fast.

### 5.4 Charting technology — decided

**LiveChartsCore.SkiaSharpView.WPF** (MIT, Skia-rendered) for the donut and the bar charts.

```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.*" />
```

Rationale: GPU-friendly Skia rendering, built-in animated transitions and hit-testing, and it does
not fight the existing MVVM setup (series are plain observable collections). OxyPlot and ScottPlot
were considered but are weaker on interactive donuts and on animation quality.

The sunburst / radial mindmap is **not** taken from the library: it is a custom
`Controls/RadialInsightControl` drawing `ArcSegment` / `Path` geometry on a `Canvas`. This keeps full
control over the drill-down animation, the label placement and the theming, and it reuses the same
`InsightTree` nodes as the sidebar. A single control serves both the sunburst mode and the mindmap
mode (same layout math, different node rendering).

Both must honour the theme dictionaries, so all chart colors come from the palette defined in
section 6.2 — never hard-coded in the chart configuration.

**Deliverables:** `Controls/RadialInsightControl.cs`, `Views/OverviewPanel.xaml(.cs)`,
`ViewModels/OverviewViewModel.cs`, `Services/ChartPalette.cs` (bridges theme brushes to LiveCharts
`SolidColorPaint`, refreshed on theme change).

## 6. Design system (transversal)

"Beautiful" is not a phase you bolt on at the end — it is a set of tokens and rules that every new
panel obeys. The app already has a decent base (light/dark dictionaries, `Card` style, accent
override from settings); this section turns that base into an explicit system and lists the polish
work.

### 6.1 Visual identity

The app currently has three competing identities: `AppSettings.AccentColor` defaults to purple
`#6D5AE0`, `Theme.Dark` declares a blue `#2899F5` accent, `Theme.Light` a different blue `#0078D4`,
and `tools/generate-icon.ps1` draws the icon with yet another purple gradient
(`#7C5CF0` → `#3F2F96`). Whichever direction we pick, these four must agree.

**Direction: "World Partition"** — a technical, dark-first tool that still reads perfectly in light
mode. The metaphor is the partition grid itself: cells, layers, and one highlighted cell.

- **Brand accent**: keep the violet family (it differentiates the tool from the sea of blue Windows
  apps and is already in the icon) and standardize on `#6D5AE0` as the shipped default, with a
  per-theme tuned variant: `#7C6BFF` in dark (needs more luminance on a `#151722` surface),
  `#5B49D6` in light. The user can still override it in Settings; the identity is the *default*,
  not a lock.
- **Secondary/brand gradient**: `#7C5CF0 → #3F2F96` (the icon gradient), reused for the title-bar
  brand mark, the splash, primary-button hover sheen and the donut's "selected slice" ring. Used
  sparingly — one gradient per screen at most.
- **Logo / icon refresh**: replace the magnifying-glass-with-checkmark by a mark built on the
  partition-grid metaphor — a 3×3 rounded grid with one cell lit in the accent. It reads far better
  at 16 px than the current lens + handle + check (three shapes fighting in 16 pixels).
  `tools/generate-icon.ps1` is updated to draw it, keeping the existing multi-size PNG-ico pipeline.
- **Wordmark**: "WP Rules Reviewer" in Segoe UI Variable Display Semibold, with "WP" in the accent,
  placed in the custom title bar next to the mark.
- **Splash / about**: a small branded splash during log parsing (mark + gradient + progress),
  reusing the skeleton-state work rather than being a separate screen.

**Deliverables:** updated `tools/generate-icon.ps1`, new `Assets/app.ico` + `Assets/brand-mark.xaml`
(vector `DrawingImage`, so it stays crisp and can be tinted per theme), title-bar brand block,
aligned accent defaults across `AppSettings`, `Theme.Light.xaml` and `Theme.Dark.xaml`.

### 6.2 Tokens

Extend `Themes/Styles.xaml` with named tokens instead of the magic numbers currently sprinkled in
the views:

- **Spacing scale**: 4 / 8 / 12 / 16 / 24 / 32 only. One `Thickness` resource per usage
  (`Space.CardPadding`, `Space.RowPadding`, …).
- **Radii**: 6 (controls), 10 (cards), 14 (dialogs), 999 (pills). Today the file mixes 6, 8 and 9.
- **Elevation**: four levels (`Elevation.0` … `Elevation.3`) as reusable `DropShadowEffect`
  resources — see the performance caveat in 6.5.
- **Typography ramp**: Display 24 / Title 18 / Heading 14 / Body 13 / Caption 11, with explicit
  weights and line heights. Font: **Segoe UI Variable Display** on Windows 11 with a `Segoe UI`
  fallback.
- **Numerals**: `Typography.NumeralAlignment="Tabular"` everywhere a count is displayed, so the
  badges and grid columns stop jittering as digits change.

### 6.3 Palettes (dark and light are both first-class)

Both themes are fully supported and must be reviewed side by side at every phase; neither is a
degraded version of the other. `ThemeManager` already swaps the dictionary at runtime and follows
the system setting — the work is to make sure every new brush lives in both files.

The current severity colors are functional but saturated and clash when eight of them sit side by
side in a donut. Two distinct palettes are needed:

- **Semantic** (severity/status, keeps today's meaning, slightly desaturated for large areas):
  high `#E4606B`, medium `#E39A45`, low `#D4B443`, none/expected `#45C08A`, noise `#7C8298`.
- **Categorical** (one color per problem signature in the donut/sunburst): an 8-color perceptually
  even ramp derived from the accent hue, generated at runtime in `Services/ChartPalette.cs` so it
  follows the user's accent setting. Slices for a *known anomaly* keep the semantic red family;
  everything else uses the categorical ramp, so the eye still finds the danger first.
- Both palettes must pass a 3:1 contrast check against `Brush.Surface` in light **and** dark.

### 6.4 Motion

Discreet, fast, and always interruptible:

- Durations 120 ms (hover / press), 180 ms (panel expand, tab change), 260 ms (chart drill-down).
- Easing `CubicEase`/`QuinticEase` with `EasingMode=EaseOut`; nothing linear.
- Where it applies: sidebar expand/collapse, tab content cross-fade + 8 px slide, donut slice grow
  on load, sunburst drill-down (arcs animate to their new sweep rather than snapping), count badges
  that flash the accent when their value changes, and the AI streaming text with a soft caret.
- A `ReduceMotion` setting that short-circuits every storyboard, for users who find it distracting
  and for remote sessions.

### 6.5 Component polish

- **DataGrid**: taller rows with real breathing room, no gridlines by default, a 3 px severity
  accent bar on the left edge of each row instead of a colored status pill, hover and selection
  states from the token set, sticky header with a subtle bottom border, and the action buttons
  (Mark read / Approve / Flag) revealed on row hover instead of always visible — the current grid
  shows three buttons on every one of 3 500 rows, which is the main source of visual noise.
- **Cards**: single radius, single border color, elevation 1, and a consistent 16 px padding.
- **Empty and loading states**: illustrated empty states (the app already has one for "No data to
  display" — extend the same treatment to every panel) and **skeleton placeholders** while a log is
  parsing, instead of the current spinner-only state.
- **Title bar**: the custom title bar can opt into the Windows 11 **Mica** backdrop via
  `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`, with a graceful fallback to the solid
  `Brush.Window` on Windows 10.
- **Iconography**: stay on Segoe MDL2 Assets for consistency with what is already there, but audit
  the glyph choices — several current ones are approximations.
- **Toasts**: replace silent activity-log-only feedback for important actions (fix applied, report
  saved) with a discreet toast in the corner, auto-dismissed.

### 6.6 Performance caveats

Beauty must not cost frames on a 6 000-row session:

- `DropShadowEffect` and `BlurEffect` are software-rendered per frame in WPF. Use them only on
  static chrome (cards, dialogs, popups) and **never inside a virtualized item template**. For row
  and node elevation, use a layered `Border` with a low-opacity gradient instead.
- Set `UseLayoutRounding="True"` and `TextOptions.TextFormattingMode="Display"` at the window level
  to kill the blurry-text-on-fractional-pixels effect.
- Animate only `Opacity` and `RenderTransform` (both composited); never animate `Width`, `Margin` or
  layout properties on lists.
- Keep the theme switch instant: all new brushes must be `DynamicResource`, and `ChartPalette` must
  subscribe to the theme change event rather than being read once at startup.

**Deliverables:** a reworked `Themes/Styles.xaml` split into `Tokens.xaml` + `Controls.xaml`,
`Services/ChartPalette.cs`, `Controls/Toast.cs`, `Controls/SkeletonBox.cs`, Mica interop in
`MainWindow.xaml.cs`, and a `ReduceMotion` setting.

## 7. Phase 3 — AI Fix Advisor

### 7.1 Contract (`Core/Ai/FixAdvisor.cs`)

Mirrors `AutoResolve.cs`: an English prompt builder, a strict-JSON response, a deterministic
expansion of `match` rules by the tool (reuse `AutoResolveMatch` + `AutoResolveMatcher`, moved to a
shared `Ai/MatchRule.cs`).

```jsonc
{
  "recommendations": [
    {
      "signatureId": "WPR-3F2A91",
      "title": "Expected DataLayer 'DL_HW_HB_Dorm_*' does not exist",
      "rootCause": "...",
      "recommendation": "...",
      "fixKind": "IniRuleChange",          // see table below
      "confidence": 4,                      // 1..5
      "risk": "Low",                        // Low | Medium | High
      "match": { "assignmentType": "DataLayer", "valueRegex": "^DL_HW_HB_Dorm_" },
      "actions": [
        { "type": "AddNoisePattern", "value": "Nanite_Shadow" },
        { "type": "EditIni", "section": "...", "key": "...", "from": "...", "to": "..." },
        { "type": "MarkExpected", "comment": "..." }
      ]
    }
  ]
}
```

### 7.2 What "fixing" can actually mean

The tool cannot edit `.uasset` actor data directly, so recommendations are typed by what the app is
able to do:

| `fixKind` | Applied by | Automation level |
| --- | --- | --- |
| `TriageOnly` | Mark read / Approve / Flag with an AI-authored comment, written to the existing report journals | **Fully automatic**, reversible |
| `NoiseSuppression` | Adds a pattern to `AppSettings.NoiseActorPatterns` / `FlagUnclassifiedTokens`, then re-runs the triage | **Fully automatic**, reversible |
| `IniRuleChange` | Generates a diff on `DefaultEditor.ini`, shown in a preview; apply requires `p4 edit` + a `.bak` copy | **Semi-automatic**, explicit confirmation |
| `ActorDataChange` | Cannot be applied here. Exports an Unreal Python / commandlet script, or opens a Cursor thread with the full context (existing `OpenInCursor`) | **Delegated** |
| `Investigate` | No action, just a written recommendation | Manual |

`ActorDataChange` could later be automated through the Unreal MCP toolset (open the level, fix the
actor, save) — worth a spike, but explicitly out of scope for the first iteration.

### 7.3 UI

- **Fix Advisor tab** in Smart Analysis: `Analyze warnings` button (streams the agent's reasoning
  like `AutoResolveDialog` already does), then a list of recommendation cards: title, severity,
  affected count, confidence stars (reuse `Controls/StarRating`), root cause, proposed fix, and the
  action buttons.
- **Batch**: `Apply to all N` on the card, which first opens a **preview dialog** listing every row
  the `match` rule expanded to, with per-row checkboxes and an ini diff when relevant.
- **Unit**: an `AI fix` button on the grid row and in the record inspector, scoping the same
  recommendation to a single record.
- **Scoped analysis**: when a node is selected in the Explorer, the Fix Advisor offers
  "Analyze this node only", which keeps the prompt small and the answers sharp.

### 7.4 Safety

- Nothing is applied without a preview; every applied action is written to a `FixJournal.json` in
  `AppSettings.AppDataFolder` (timestamp, signature id, action, affected rows, before/after).
- `Undo last fix batch` restores from that journal for the reversible kinds.
- File edits: Perforce `p4 edit` first, `.bak` copy, then write. Abort with a clear message if the
  file is not writable.
- The AI never gets write access — it only proposes typed actions that the tool executes.

**Deliverables:** `Core/Ai/FixAdvisor.cs` (+ prompt builder + parser), `Core/Ai/MatchRule.cs`
(extracted), `Core/Fixes/FixExecutor.cs`, `Core/Fixes/FixJournal.cs`,
`ViewModels/FixAdvisorViewModel.cs`, `Views/FixAdvisorPanel.xaml`, `Views/FixPreviewDialog.xaml`.

## 8. Cross-cutting concerns

- **Single source of truth**: tree, charts, mindmap and AI prompts all consume `InsightTree`, so a
  count shown in the donut is clickable in the tree and matches the grid.
- **Performance budget**: tree build < 150 ms for 10 000 records (single pass + lazy children);
  chart redraw < 30 ms; steady 60 fps while animating; no full re-layout on selection, only on
  filter change.
- **Settings additions**: `ExplorerPanelWidth`, `ExplorerDefaultPreset`, `EnableFixAdvisor`,
  `FixAdvisorAutoApplyMaxRisk`, `ChartTopSlices`, `ReduceMotion`, `UseMicaBackdrop`.
- **Tests**: Core-side (signature, tree, match expansion, fix executor with a temp ini) — the WPF
  layer stays thin enough to be verified manually.
- **Design review**: each phase ends with a light/dark screenshot pass; no new panel ships with
  hard-coded colors, spacings or radii outside the token set.
- **Docs**: update `README.md` features section at the end of each phase.

## 9. Suggested sequencing

| Phase | Content | Rough size |
| --- | --- | --- |
| 0 | `ProblemSignature` + `InsightTree` + tests | S |
| 0b | Design tokens: spacing, radii, typography ramp, elevation, palettes | S |
| 1 | Warning Explorer sidebar, filter wiring, presets | M |
| 2 | Overview tab: donut + bars (LiveCharts2), cross-filtering | M |
| 3 | Sunburst / radial mindmap control | M |
| 4 | Fix Advisor: analysis + recommendations (read-only) | M |
| 5 | Fix execution: `TriageOnly`, `NoiseSuppression`, journal + undo | M |
| 6 | `IniRuleChange` diff/apply, export of Unreal scripts | M |
| 7 | Polish pass: motion, DataGrid restyle, skeletons, toasts, Mica | M |
| 8 | (Spike) actor-level fixes via the Unreal MCP toolset | ? |

Phase 0b comes early on purpose: every panel built afterwards consumes the tokens, so the polish
pass in phase 7 is a refinement, not a rewrite. Phases 0–1 already deliver most of the day-to-day
value, and each later phase is independently shippable.

## 10. Open questions

1. Should the Explorer replace the current *Anomalies* tab of Smart Analysis, or coexist with it?
2. For `IniRuleChange`, is direct editing of `D:\Sun\Sundance\Config\DefaultEditor.ini` acceptable
   (with Perforce checkout and backup), or should the tool only ever produce a patch to review?
3. Should the fix journal and the problem signatures be shared across the team (a file in
   `AppDataFolder` on a network share) so recommendations accumulate build over build?
4. ~~Is a visual identity pass in scope?~~ **Yes** — see section 6.1. Both dark and light themes are
   first-class and reviewed at every phase.
