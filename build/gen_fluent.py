"""Writes src/Elynt.App/Theme/FluentColors.xaml: Fluent's dark colours re-pointed at the app's palette (ticket #62).

Run from the repo root after changing a colour below or moving to a new .NET version:

    curl -sSo /tmp/Fluent.Dark.xaml https://raw.githubusercontent.com/dotnet/wpf/release/10.0/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Themes/Fluent.Dark.xaml
    python3 build/gen_fluent.py /tmp/Fluent.Dark.xaml src/Elynt.App/Theme/FluentColors.xaml

Use the dotnet/wpf branch that matches global.json's SDK. Never edit FluentColors.xaml by hand.
"""
import re, sys

src = open(sys.argv[1], encoding="utf-8").read()
out_path = sys.argv[2]

# The app's palette for every Fluent colour that should change. Everything else keeps Fluent's dark value.
OVERRIDES = {
    # Text
    "TextFillColorPrimary": "#F4F4F5",
    "TextFillColorSecondary": "#A1A1AA",
    "TextFillColorTertiary": "#6B6B75",
    "TextFillColorDisabled": "#52525B",
    "TextFillColorInverse": "#09090B",
    "AccentTextFillColorDisabled": "#52525B",
    "TextOnAccentFillColorPrimary": "#FFFFFF",
    "TextOnAccentFillColorSecondary": "#CCFFFFFF",
    "TextOnAccentFillColorSelectedText": "#FFFFFF",
    # Control fills: buttons, text boxes, drop-downs
    "ControlFillColorDefault": "#19191E",
    "ControlFillColorSecondary": "#1F1F26",
    "ControlFillColorTertiary": "#141418",
    "ControlFillColorDisabled": "#111114",
    "ControlFillColorInputActive": "#0C0C0F",
    "ControlStrongFillColorDefault": "#52525B",
    "ControlStrongFillColorDisabled": "#3F3F46",
    "ControlSolidFillColorDefault": "#3F3F46",
    "SubtleFillColorSecondary": "#1F1F26",
    "SubtleFillColorTertiary": "#19191E",
    "ControlAltFillColorSecondary": "#0C0C0F",
    "ControlAltFillColorTertiary": "#19191E",
    "ControlAltFillColorQuarternary": "#1F1F26",
    # Strokes and dividers
    "ControlStrokeColorDefault": "#26262D",
    "ControlStrokeColorSecondary": "#2E2E36",
    "ControlStrongStrokeColorDefault": "#52525B",
    "CardStrokeColorDefault": "#26262D",
    "CardStrokeColorDefaultSolid": "#26262D",
    "SurfaceStrokeColorDefault": "#26262D",
    "SurfaceStrokeColorFlyout": "#26262D",
    "DividerStrokeColorDefault": "#26262D",
    # Backgrounds and surfaces
    "ApplicationBackgroundColor": "#09090B",
    "CardBackgroundFillColorDefault": "#111114",
    "CardBackgroundFillColorSecondary": "#0E0E11",
    "SolidBackgroundFillColorBase": "#09090B",
    "SolidBackgroundFillColorSecondary": "#111114",
    "SolidBackgroundFillColorTertiary": "#19191E",
    "SolidBackgroundFillColorQuarternary": "#1F1F26",
    "SolidBackgroundFillColorTransparent": "#0009090B",
    "SolidBackgroundFillColorBaseAlt": "#09090B",
    "AcrylicBackgroundFillColorDefault": "#16161B",
    "LayerFillColorDefault": "#111114",
    "LayerFillColorAlt": "#19191E",
    "LayerOnMicaBaseAltFillColorDefault": "#111114",
    "LayerOnMicaBaseAltFillColorSecondary": "#19191E",
    "LayerOnMicaBaseAltFillColorTertiary": "#19191E",
    "SystemChromeMediumLowColor": "#19191E",
    # Status
    "SystemFillColorSuccess": "#22C55E",
    "SystemFillColorCaution": "#FF7A1A",
    "SystemFillColorCritical": "#FF4D5E",
    "SystemFillColorCautionBackground": "#1AFF7A1A",
    "SystemFillColorCriticalBackground": "#1FE5162D",
    "SystemFillColorSolidAttentionBackground": "#19191E",
    "SystemFillColorSolidNeutralBackground": "#19191E",
    # Accent: red, with orange for accent text such as links. Never Windows' own accent colour.
    "SystemAccentColor": "#E5162D",
    "SystemAccentColorSecondary": "#E5162D",
    "SystemAccentColorLight1": "#EA3347",
    "SystemAccentColorLight2": "#E5162D",
    "SystemAccentColorLight3": "#FF7A1A",
    "SystemAccentColorDark1": "#C01226",
    "SystemAccentColorDark2": "#9A0E1F",
    "SystemAccentColorDark3": "#740A17",
}

colors = dict(re.findall(r'^  <Color x:Key="(\w+)">([^<]+)</Color>', src, re.M))
accents = set(re.findall(r'^  <DynamicResource x:Key="(\w+)"', src, re.M))
missing = [k for k in OVERRIDES if k not in colors and k not in accents and k != "SystemAccentColorSecondary"]
assert not missing, missing

brushes = []  # (key, text, referenced colours)
for m in re.finditer(r'^  <SolidColorBrush x:Key="(\w+)"[^\n]*/>', src, re.M):
    brushes.append((m.group(1), m.group(0), re.findall(r'(?:Static|Dynamic)Resource (\w+)', m.group(0))))
for m in re.finditer(r'^  <LinearGradientBrush x:Key="(\w+)".*?^  </LinearGradientBrush>', src, re.M | re.S):
    brushes.append((m.group(1), m.group(0), re.findall(r'(?:Static|Dynamic)Resource (\w+)', m.group(0))))

picked = [b for b in brushes if any(c in OVERRIDES for c in b[2])]
used = {c for b in picked for c in b[2]} | set(OVERRIDES)
lines = []
for key in sorted(used, key=lambda k: list(OVERRIDES).index(k) if k in OVERRIDES else len(OVERRIDES)):
    value = OVERRIDES.get(key) or colors[key]
    lines.append(f'    <Color x:Key="{key}">{value}</Color>')

body = "\n".join(lines) + "\n\n" + "\n".join("  " + text.replace("\n  ", "\n    ") for _, text, _ in picked)
header = '''<!-- Fluent's dark colours, re-pointed at the app's black-and-red palette. Fluent's control styles look their brushes up
     dynamically, so redefining a brush here recolours every control that uses it. Each brush below is a copy of the one
     in .NET's Fluent.Dark.xaml whose colour changes; the colours above are the app's values (or Fluent's own, where a
     copied brush also uses a colour that stays). Merged after Fluent.Dark.xaml in App.xaml. -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
'''
open(out_path, "w", encoding="utf-8", newline="\n").write(header + body + "\n</ResourceDictionary>\n")
print(len(picked), "brushes,", len(used), "colours;", len(brushes), "brushes in Fluent")
