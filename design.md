# HaS Studio — Design System Standard

Status: Active UI system specification. This document outlines the design tokens, rules, and visual style principles of the **HaS Studio** design system.

---

## Visual Direction

HaS Studio UI is dense, focused, and calm, tailored for professional tools. The primary accent is **Cool Cyan** (`#19AFE7`), warnings are **Amber**, errors are **Rose**, and successful states are **Emerald Green**. Visual effects are clean and non-distracting to preserve clarity and workspace visibility.

---

## Surface Hierarchy & Colors

| Token | Hex | Role |
|---|---|---|
| `--ui-bg` | `#0A0D13` | Deep application background |
| `--ui-surface` | `#121821` | Base surface panels |
| `--ui-surface-raised` | `#18202B` | Elevated cards, headers, and floating containers |
| `--ui-surface-inset` | `#0D1219` | Inset fields, dropzones, lists, and wells |
| `--ui-control` | `#111722` | Buttons, comboboxes, and active control backgrounds |
| `--ui-border` | `#222B38` | Default structural border |
| `--ui-border-strong` | `#52C4F0` | Highlighted, active, or focused borders |
| `--ui-accent` | `#19AFE7` | Primary cyan branding accent |
| `--ui-text` | `#EDF4FB` | Primary high-contrast text |
| `--ui-text-secondary` | `#7E8B9B` | Secondary descriptive labels |
| `--ui-text-muted` | `#4E5968` | Subtle hints and disabled items |

---

## Controls & Components

- `ui-button-primary`: Main filled action with accent glow and bold contrast.
- `ui-button-secondary`: Outlined secondary button.
- `ui-button-danger`: Destructive / stop action.
- `ui-chip-button`: Compact pill / chip selector for filters, tones, and presets.
- `ui-combobox`: Dropdown with dark inset list and soft cyan active selection state.
- `ui-card`: Grouped surface container with subtle border.
- `ui-card-inset`: Recessed sub-panel for focused operations.

---

## Spacing, Radii & Typography

- **Grid Step**: 4 px base increment.
- **Corner Radii**: 4–8 px for chips/inputs, 10–12 px for cards, 16–18 px for main windows.
- **Font Stack**: Modern Sans-Serif (`Segoe UI Variable`, `Inter`) for UI body; `JetBrains Mono` / `Cascadia Code` for status, metrics, and technical labels.
- **Theme**: Dark mode is standard. All tokens are centralized in application resources without hardcoded inline overrides.
