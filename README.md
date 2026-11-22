# Flip Clock

A small split-flap clock for the Windows desktop, in the style of the old airport departure boards. Free, no ads, no account, no internet connection.

![Flip Clock in black and white](post/01-black-and-white.png)

![The flaps turning](post/flip-black.gif)

## What it does

- Shows the time on flap cards that really turn, one card at a time, like the mechanical boards did.
- **Move it:** drag the clock anywhere on the screen. Near the middle of the screen it catches, left-right and up-down separately, so it is easy to centre. Hold Ctrl while dragging to place it freely.
- **Scale it:** drag an edge or a corner. It is drawn as vector shapes, so it stays sharp at any size.
- **Always on top:** click the pin in the top right corner. Amber means on.
- **Black or white**, with or without seconds: right-click the clock.
- **Flap sound:** every card lands with the clack of a real split-flap display, timed to the card. Right-click the clock to choose: every flip, only on the full hour (a short rattle once an hour), or off.
- **Alarm:** click the bell. The cards show the alarm time; click the upper or lower half of a card (or scroll over it) to change it, then click the bell again. The bell turns amber and the alarm time stands next to it. At that time the clock beeps like an old digital alarm clock, every day, until you click it (it gives up after a minute). Click the amber bell to switch the alarm off.
- **Start with Windows:** right-click the clock and tick it.
- It remembers where you put it, how big it is and how you set it.

## Download

Get it from the [Releases page](../../releases/latest). Two downloads do the same thing. Pick one.

| Download | Size | For whom |
|---|---|---|
| `FlipClock-…-small.zip` | under 1 MB | PCs that have Microsoft's free [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) installed |
| `FlipClock-…-all-in-one.zip` | about 53 MB (125 MB unpacked) | Any 64-bit Windows PC, nothing to install |

There is no installer. Unpack the zip, put `FlipClock.exe` wherever you like and double-click it.

### Updating from an older version

Put the new `FlipClock.exe` over the old one. That is all: there is nothing to uninstall first, and your place, size and settings are kept.

- If the old clock is still running, Windows will not let you replace the file. Right-click the clock, choose "Close", then copy the new file over it.
- If you put the new file in a different folder, just start it. It takes the place of a running older copy (from version 1.1.0 on; a running 1.0.0 has to be closed by hand once), and if "Start with Windows" was ticked it now starts the new file. You never end up with two clocks. The old file can then be deleted.

### Is it safe?

Every release is checked on [VirusTotal](https://www.virustotal.com) before it goes up. The result links and the SHA-256 checksums of both programs are in the notes of each release on the [Releases page](../../releases), so you can compare them with the file you downloaded.

The program is not code-signed, because a signing certificate costs money and this is a free gift. Windows may therefore show a notice for an unknown publisher when you start it for the first time. If that worries you, do not take my word for it: the complete source code is in this repository, and you can read it and build the program yourself (see below).

What the program does on your PC, in full:

- It never connects to the internet. There is no network code in it.
- With the flap sound on, it plays through your normal sound output, like any other program. It records nothing.
- It saves its settings in one small file: `%APPDATA%\FlipClock\settings.json`.
- Only if you tick "Start with Windows", it adds one entry to your own user's start-up list (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). Untick it and the entry is gone.

### What the alarm cannot do

It rings only while the PC is awake, the clock is running and the sound is not muted. It does not wake a sleeping PC, and an alarm time that passes while the PC sleeps is not made up for. Use it as a desk reminder, not to get out of bed.

## Light on the PC

Between two changes of the display the clock does nothing at all. With seconds hidden it wakes up once a minute. Measured on the author's PC: 0 ms of processor time in 10 seconds at rest, about 30 to 50 MB of memory.

Showing seconds is the one thing that costs something, because a card then turns every second: about 9 % of one processor core on the author's PC, with the clock drawn large and the sound on. Version 1.0.0 needed about 21 % for the same. If you want it as light as possible, hide the seconds.

## Remove it

1. Right-click the clock, untick "Start with Windows" if you had ticked it.
2. Right-click the clock, choose "Close".
3. Delete `FlipClock.exe`, and the folder `%APPDATA%\FlipClock` if you want the settings gone too.

## Tested on

Built and tested on Windows 11, 64-bit. Other Windows versions are untested.

## Build it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Small version:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o app
```

All-in-one version:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o portable
```

## Licence and credits

MIT licence, see [LICENSE](LICENSE). Do what you like with it.

- The pin and bell icons are from [Lucide](https://lucide.dev) (ISC licence).
- The flap sound is cut from the recording ["Split Flap Display" by ngruber](https://freesound.org/people/ngruber/sounds/261244/) (CC0).
- The digits use Bahnschrift, a typeface that comes with Windows. It is not included here.
- The all-in-one version contains Microsoft's .NET runtime (MIT licence).

Details are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
