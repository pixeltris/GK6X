# GK6X

This tool allows you to set keys, macros, and lighting for GK6X keyboards (GK64, GK84, GK61, etc). This can be used as an alternative to the official software ([Windows](http://www.jikedingzhi.com/downloadlist?driverID=41) / [Mac](http://www.jikedingzhi.com/downloadlist?driverID=90))

It runs on Windows, Mac, and Linux.

## Usage

_Note: This is a CLI program, but see below for GUI. It's advisable to use the official software if you're on Windows / Mac._

_Note: On Linux you will need to run it as super user [(issue with hidraw?)](https://github.com/pixeltris/GK6X/issues/3). It can be compiled / ran with mono on Linux / Mac ([see #4](https://github.com/pixeltris/GK6X/issues/4)). On Windows you compile it with Visual Studio._

When you open the program it will connect to your keyboard (there's no console output until it finds something to connect to).

Once it has connected it will create a file `UserData/YOUR_MODEL_ID.txt`. This can be used to configure the keyboard. [See Sample.txt for examples of setting keys, macros, and lighting](https://github.com/pixeltris/GK6X/blob/master/Build/UserData/Sample.txt). Use the 'map' command to apply your changes to the keyboard, and 'unmap' to reset your keyboard to the default state (NOTE: There is no 'apply failed' message like there is on the official software, so it can fail silently!).

You can reprogram the base layer and layers 1-3 (and the Fn keys). The 'driver' layer isn't supported. The firmware doesn't allow you to reprogram the Fn keys on the base layer (which sucks!).

![Alt text](https://raw.githubusercontent.com/pixeltris/GK6X/master/Screenshot.png)

## Usage (Web GUI)

There's some very basic (and mostly untested) support for a web GUI by using the official software's web component. Use the 'gui' command to start the web server (it should automatically open the web page at http://127.0.0.1:6464).

_Note: This repo doesn't contain the web files. Copy and paste the official software's folder into the "Build" folder, or use the pre-bundled version on the [releases](https://github.com/pixeltris/GK6X/releases) page._

_Note: The GUI isn't capable of remapping keys on the base layer. Changes to the JavaScript would be required to get this working_

![Alt text](https://raw.githubusercontent.com/pixeltris/GK6X/master/ScreenshotWeb.png)

## Related projects

https://github.com/wgwoods/gk64-python - thanks to [@wgwoods](https://github.com/wgwoods) for his work on annotating the dissasembly of the GK64 firmware.
