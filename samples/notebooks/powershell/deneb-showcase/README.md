# Deneb Showcase from PowerShell

A Verso notebook with ten Vega/Vega-Lite visualizations adapted from
[David Bacci's Deneb Showcase](https://github.com/PBI-David/Deneb-Showcase),
using refreshable public datasets. This is a sample, not a new kernel or Viewer extension.

## Open and run

Open `deneb-showcase.verso` in Verso with PowerShell support. Keep the `.ps1`
files beside it so that the relative `#!import` directives resolve.

The notebook includes saved widget outputs. Viewing them does not re-download
the source datasets, but it does load the pinned Vega JavaScript runtimes from
jsDelivr. It is not an offline bundle.

To change the data:

1. Run the `#!import ./helper.ps1` cell.
2. Run the download cell for the desired example.
3. Edit the following display cell's variables and run it. Selection reuses the
   data and template in the current kernel session.

Run the Mekko download cell before the TopN Donut example; they share SIPRI data.
The two relationship graphs are self-contained code cells after the helper import.
After restarting the kernel, run the required imports/downloads again: saved
widget outputs do not restore PowerShell variables.

Downloads require internet access, but no API keys or extra PowerShell modules.
Rerunning download cells refreshes the data; published years, source revisions,
coverage and API availability may change. The notebook explains units, missing
values and interpretation limits for each source.

## Examples

| Deneb visualization | Data | Display selection |
| --- | --- | --- |
| Calendar Heatmap | Open-Meteo daily Irvine weather, from 2023 | Year; temperature or precipitation |
| Mekko Chart | SIPRI annual military spending, from 2023 | Year |
| Sankey Chart | Microsoft reported fiscal quarters, from FY2023 | Fiscal year and quarter |
| TopN Donut Chart | Shared SIPRI dataset | Year and Top N |
| Bank Failure Bubble Chart | FDIC failures, from 2001 | Start/end year |
| Force Directed Graph | Wikidata brand ownership statements | Entity IDs, traversal depth, historical statements |
| Force Directed Graph | MusicBrainz band memberships, including Ария and Кипелов | Band IDs, shared/former members |
| Population Bar Chart Race | World Bank annual population, from 1960 | Years, region, Top N, animation speed |
| Parallel Coordinates Chart | NASA Exoplanet Archive composite catalogue | Discovery year, radius, distance, method, maximum lines |
| Waffle Charts | NESO Great Britain generation mix, last 48 hours | Completed UTC half-hour interval |

## Files and rendering

`helper.ps1` provides pipeline commands to download a pinned specification,
replace inline/named data on a copy, set explicit plot dimensions, and emit a
`text/x-verso-widget` via `Show-Vega` or `Show-VegaLite`. Dataset-specific parsing
and spec adaptation live in the other `.ps1` files.

Specifications are pinned to Deneb Showcase commit
`eded225be500aa9bdec51b72fbb94063e3a92af0`. Widget runtime versions are Vega 6.3.1,
Vega-Lite 6.4.3 and Vega-Embed 7.1.0. The original Vega-Lite v5 schema declaration
can produce a version warning in the v6 runtime; the saved examples have been
compiled and rendered with these pinned versions.

Each widget contains its spec and selected data. Rendering follows the
self-contained widget pattern in [PR #95](https://github.com/DataficationSDK/Verso/pull/95)
and relies on the corrected Viewer behavior; this sample does not patch the Viewer
or add shared-loader/script-scope workarounds. Light theme is selected for the
original chart palettes. Vega controls, tooltips and actions remain available.

## Attribution

Original visualizations: David Bacci / Deneb Showcase. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the original MIT notice.
Dataset source links and, where specified, source licenses are retained in the
notebook and chart metadata. The MIT notice for chart code does not relicense
the third-party datasets; their respective source terms still apply.
