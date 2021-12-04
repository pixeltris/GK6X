# GK6X

This is a command-line tool for mapping keys, macros, and lighting for GK6X keyboards (GK64, GK84, GK61, etc). This can be used as an alternative to the official software ([Windows / Mac](http://www.jikedingzhi.com)).

It runs on Windows, Mac, and Linux.

## System requirements / compilation

See the [releases](https://github.com/pixeltris/GK6X/releases) page for prebuilt binaries.

<details>
<summary>.NET Framework 4+ / Visual Studio on Windows. mono on Mac / Linux. Expand for more info.</summary>

### Windows

.NET Framework 4+ is required (should be pre-installed on Windows 7 and higher).

Compile from source using one of the following tools:
1) `MSBuild.exe` (comes with .NET Framework), run command with actual version of .NET Framework to compile `C:\Windows\Microsoft.NET\Framework64\<NET_FRAMEWORK_VERSION>\MSBuild.exe GK6X.sln`;
2) Visual Studio (C# tools required).

### Linux / Mac

[mono](https://www.mono-project.com/download/stable/) is required (most Linux package managers should list `mono-complete`).

Compiling may take several attempts depending on the version of mono. Try `xbuild GK6X.sln`, or `msbuild GK6X.sln`, or `xbuild /p:TargetFrameworkVersion=v4.5 /p:TargetFrameworkProfile=""`, or ask for help in [#4](https://github.com/pixeltris/GK6X/issues/4).

`cd` into `Build` and run with `sudo mono GK6X.exe`. Super user (`sudo`) is required on Linux ([possible hidraw issue]( https://github.com/pixeltris/GK6X/issues/3)). Or use the udev rule below.

### Linux AUR package

GK6X is available in [AUR](https://wiki.archlinux.org/title/Arch_User_Repository) as package [gk6x-bin](https://aur.archlinux.org/packages/gk6x-bin/) maintained by [@aakashhemadri](https://github.com/aakashhemadri).

### Linux `sudo` alternative (udev rule)

An alternative to using `sudo` is to set up a udev rule. Create `/etc/udev/rules.d/gk6x.rules`:

```
SUBSYSTEM=="input", GROUP="input", MODE="0666"
SUBSYSTEM=="usb", ATTRS{idVendor}=="1ea7", ATTRS{idProduct}=="0907", MODE:="666", GROUP="plugdev"
KERNEL=="hidraw*", ATTRS{idVendor}=="1ea7", ATTRS{idProduct}=="0907", MODE="0666", GROUP="plugdev"
```

Then add yourself to `plugdev` and reboot for it to take effect:

```sh
sudo adduser $(whoami) plugdev
```
</details>

## Overview

Once the program is opened you should see something like:

`Connected to device 'GK84S RBG' model:655491200 fw:v1.16`

At this point you can start using mapping your keyboard with the `map` command as seen below.

*If you don't see any output, it has failed to detect the keyboard. There isn't any console output until it finds a valid keyboard.*

## Commands

### `map`

Maps the keyboard based on the config in `UserData/YOUR_MODEL_ID.txt`. [See Sample.txt for examples of setting keys, macros, and lighting](https://github.com/pixeltris/GK6X/blob/master/Build/UserData/Sample.txt).

You can reprogram the base layer, layers 1-3, and layers 1-3 whilst the fn is held down. You can't move the fn key, but TempSwitchLayer1/2/3 can act similar to a secondary fn key. The base layer fn key set can't be mapped.

### `unmap`

Resets the keyboard to the default config. The dedicated reset keyboard key combo will be more effective; refer to your manual.

### `gui`

Starts the GUI web server on http://localhost:6464. To make GUI working, it's required to place content of GUI build from the [releases](https://github.com/pixeltris/GK6X/releases) page into `Build` directory so that `driver` directory is directly inside `Build` directory.

### `gui_le`

Copies the lighting files created using the `gui` to the `Data/lighting/` folder so that the `map` command can use them.

## Additional commmands

These commands generally aren't needed.

### `dumpkeys`

Lists the textual name / code name (i.e. `5` is `D5`) of the keys listed in the order that they appear on each row of your keyboard. This shows the keys represented prior to any mapping, and is just a reference.

### `findkeys`

Tool to identify key names. Used for finding broken keys and fixing data files.

### `update_data`

This is used to update the data files from the official software. This exact process needs to be re-documented ([Updating.txt](https://github.com/pixeltris/GK6X/blob/master/Build/Data/Updating.txt)).

## Additional info

- The `/p` command line arg can be used to set a prefixed file name of the config to use. For example `GK6X.exe /p test` will use `test - 655491200.txt` as the target config file (where the keyboard model id is `655491200`).
- The following commands can be used as command line args to just execute a specific command `/gui`, `/map`, `/unmap`, `/dumpkeys`. For example: `GK6X.exe /map`
- The data files for the `gui` command aren't included in this repo. Obtain them from a release build.
- There's possibly some issue with certain USB slots [(#102)](https://github.com/pixeltris/GK6X/issues/102).
- There is no "apply failed" message like there is on the official software, so `map` / `umap` can fail silently.
- There are issues with the web GUI such as not being able to map the base layer. I'd recommend avoiding the GUI if possible.

The web GUI looks like this:

![Alt text](https://raw.githubusercontent.com/pixeltris/GK6X/master/ScreenshotWeb.png)

## Related projects

- https://github.com/wgwoods/gk64-python - thanks to [@wgwoods](https://github.com/wgwoods) for his work on annotating the dissasembly of the GK64 firmware.
- https://github.com/konsumer/node-gk6x
