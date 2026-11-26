# Microsoft Store listing for Flip Clock

Everything that goes into Partner Center, field by field. Limits are the ones on Microsoft Learn
("Add and edit Store listing info", "Screenshots and images", Store Policies 10.1), read on 26 November 2026.

## Product name (to reserve)

First choice: `Flip Clock`
If taken: `Flip Clock: Split-Flap Desktop Clock`, then `Split-Flap Clock`

## Pricing and availability

- Base price: Free
- Markets: all
- Audience: Public
- Discoverability: available and discoverable in the Store
- Schedule: as soon as it passes certification

## Properties

- Category: Utilities & tools
- Does the product access, collect or transmit personal information: No
- Privacy policy URL: https://github.com/harrycat1494/flip-clock/blob/main/PRIVACY.md
- Website: https://github.com/harrycat1494/flip-clock
- Support contact: https://github.com/harrycat1494/flip-clock/issues

## Store listing (English, United States)

### Description (up to 10,000 characters)

Flip Clock puts a split-flap clock on your Windows desktop, in the style of the old airport and railway departure boards. The cards really turn, one at a time, the way the mechanical boards did.

It is free. No ads, no account, and it never connects to the internet.

WHAT IT DOES

- Shows the time on flap cards that turn over, with the clack of a real split-flap display if you like: on every flip, only on the full hour, or off.
- 12-hour or 24-hour time. In 12-hour time an extra flap card shows AM or PM.
- Your own colours for the digits, the flaps and the frame. Pick any colour or type a colour code, and the clock shows it at once. It remembers your last five colours.
- Black and white looks to start from, with or without seconds.
- An alarm: the cards show the alarm time, you set it with arrows, and when it rings the digits flash red.
- Move it anywhere by dragging. Near the middle of the screen it catches, so it is easy to centre.
- Any size: drag an edge or a corner. It stays sharp from tiny to full screen.
- A small clock for the taskbar: one click lays it on the taskbar, where Windows shows its own clock.
- Always on top if you want, and it steps aside while a full-screen program runs.
- Starts with Windows if you tick it, and remembers its place, size and settings.

LIGHT ON THE PC

Between two changes of the display the clock does nothing at all. With seconds hidden it wakes up once a minute.

HOW TO USE IT

Right-click the clock for everything: colours, 12-hour time, sound, alarm, taskbar, start with Windows, close.

OPEN SOURCE

The complete source code is public under the MIT licence: https://github.com/harrycat1494/flip-clock

From the maker of SizeSpec (https://sizespec.com), which resizes, crops and compresses product photos to the exact specs of Etsy, eBay, Amazon, Walmart, Shopify and more.

### Short description (keep under 270 characters)

A split-flap clock for your desktop, like the old airport departure boards. Real turning flaps, optional clack sound, alarm, your own colours, and a tiny version for the taskbar. Free, no ads, never online.

### What's new in this version

Leave empty for the first submission.

### Product features (up to 20, each up to 200 characters)

1. Flap cards that really turn, one card at a time
2. Optional clack of a real split-flap display: every flip, on the hour, or off
3. 12-hour time with its own AM/PM flap card, or 24-hour time
4. Your own colours for digits, flaps and frame, with your last five colours kept
5. Alarm with red flashing digits
6. Any size, sharp from tiny to full screen
7. A small clock that lies on the taskbar
8. Always on top, and it steps aside for full-screen programs
9. Black and white looks, with or without seconds
10. Free, no ads, no account, never connects to the internet

### Search terms (at most seven)

flip clock, split-flap clock, desktop clock, clock widget, retro clock, taskbar clock, alarm clock

### Copyright and trademark info

Copyright (c) 2026 Flip Clock contributors. MIT licence.

### Developed by

Leave empty (the publisher name is shown anyway).

### Screenshots (desktop, 1920 x 1080 PNG) and captions

1. `screenshot-1-clock.png`: A split-flap clock for your desktop
2. `screenshot-2-colors.png`: Your own colours for digits, flaps and frame
3. `screenshot-3-color-panel.png`: Pick any colour and see it on the clock at once
4. `screenshot-4-12-hour.png`: 12-hour time with its own AM/PM flap card
5. `screenshot-5-alarm.png`: An alarm you set on the cards, with red flashing digits
6. `screenshot-6-white-seconds.png`: White look, with seconds
7. `screenshot-7-taskbar.png`: A small clock for the taskbar

### Store logo

- 1:1 app tile icon, 300 x 300: `app-tile-icon-300.png`

## Age ratings

Answer "No" to every content question (no violence, no user communication, no purchases, no location, no shared personal data). It is a clock.

## Submission options

### Restricted capability: runFullTrust

Flip Clock is an ordinary Windows desktop program (WPF, .NET) packaged as MSIX, so it declares runFullTrust. It needs it to show a borderless, see-through, always-on-top widget window that the user can drag and scale anywhere on the desktop and lay on the taskbar, and to play its flap and alarm sounds. It runs with the rights of the signed-in user, installs no drivers or services, changes no Windows settings, never connects to the internet, and collects no data.

### Notes for certification

Flip Clock opens as a small clock at the top right of the screen; it has no main window and no taskbar button by design. Right-click the clock for its menu (colours, 12-hour time, sound, alarm, "Put on the taskbar", "Start with Windows", Close). "Start with Windows" is off until the user ticks it and uses the package's startup task. No account, no network access, no test credentials needed.
