# GK6X

This tool allows you to set keys, macros, and lighting for GK6X keyboards (GK64, GK84, GK61, etc). This can be used as an alternative to the official software http://www.jikedingzhi.com/downloadlist?driverID=41latest

It should work on Windows, Mac, and Linux (though only Windows has been tested so far).

## Usage

When you open the program it will connect to your keyboard. Once it has connected it will create a file `UserData/YOUR_MODEL_ID.txt`. This can be used to configure the keyboard. [See Sample.txt for examples of setting keys, macros, and lighting](https://github.com/pixeltris/GK6X/blob/master/Build/UserData/Sample.txt). Use the 'map' command to apply your changes to the keyboard, and 'unmap' to reset your keyboard to the default state (NOTE: There is no 'apply failed' message like there is on the official software, so it can fail silently!).

You can reprogram the base layer and layers 1-3 (and the Fn keys). The 'driver' layer isn't supported. The firmware doesn't allow you to reprogram the Fn keys on the base layer (which sucks!).