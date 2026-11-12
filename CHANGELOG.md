# Changelog

## 0.2.0 — 2026-11-12

- Bound initialization, configuration, subscriptions and pending work to the actual LocalizationSettings.Instance. Added Reset, ReinitializeAsync and Configuration while retaining LanguageChanged.
- Added unified Changed/Refresh notifications and cancellation of obsolete text/cache loads. Latest arguments/settings win; lifecycle cancellation is quiet.
- Added LocalizedText.RefreshAsync, catalog change notifications and cache readiness/refresh APIs. Catalog edits and replacements refresh complete snapshots; missing values use caller fallback while explicit empty translations stay empty.
- Added independent EasyResources, EasyBuilding and EasyInteraction samples. Existing GameplayTranslations and serialized cache/presenter links remain supported.
- Added lifecycle, independent-adapter and combined-example regressions. Google synchronization tests use simulated HTTP; no live-account verification is implied.

## 0.1.0

- First setup for standard Unity Localization locales and string collections.
- Async language API, stable language state, configured fallback and event-driven text/catalog components.
- Google OAuth synchronization with explicit direction, preview, row conflicts, stable IDs, validation, journals and recovery.
- Public Google Sheets CSV download without OAuth; quoted and multiline translations supported.
- Independent First Run and optional Gameplay Integration samples using New Input System.
- Editor/Play Mode tests and reproducible standalone verification tools.
