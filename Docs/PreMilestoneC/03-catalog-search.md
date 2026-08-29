# Pre-Milestone C Reliability — Ticket 03 Catalog Search

## Implemented contract

The existing Catalog search box is hosted inside `CatalogsLibraryPanel`, so it
is visible and active whenever Catalogs mode is selected. It is no longer a
child of the Sessions drawer, which is collapsed when Catalogs is shown. The
existing `OnCatalogSearchChanged` handler and stable-ID/title/category/
description matching remain unchanged; the other Library filters and layout
are untouched.

## Verification

- WPF binding test confirms `CatalogSearchBox` is a visual descendant of the
  Catalogs drawer and not the hidden Sessions drawer.
- Existing catalog filtering code remains the backend for all five catalog
  kinds.
- WPF test suite and application build are required after this XAML change.
