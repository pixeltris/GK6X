CMSDesktop = {};
CMSDesktop.startDrag = function(){}
var callbackFuncs = [];
var ignore_callFunc = ["QueryFirmwareUpdateInfo", "GetGameList"];
var ignore_onFunc = ["onUpdateFirmwareMessage"];
function makeRequest(data)
{
    data.token = "UNIQUE_TOKEN_GOES_HERE";
    
    var xhr = new XMLHttpRequest();
    xhr.onreadystatechange = function()
    {
        if (xhr.readyState == XMLHttpRequest.DONE)
        {
            var response = xhr.responseText;
            //console.log("response: " + xhr.responseText);
            if (response.length == 0)
            {
                if (data.onFailure != null)
                {
                    console.log(data);
                    var additionalInfo = "";
                    if (data.request != null)
                    {
                        var reqInfo = JSON.parse(data.request);
                        if (reqInfo.funcname != null && !ignore_callFunc.includes(reqInfo.funcname))
                        {
                            data.onFailure(0, "fail - " + reqInfo.funcname);
                        }
                    }
                    else
                    {
                        data.onFailure(0, "fail");
                    }
                }
            }
            else
            {
                if (data.onSuccess != null)
                {
                    data.onSuccess(response);
                }
            }
        }
    }
    xhr.open("POST", "http://" + window.location.host + "/cms_" + data.requestType, true);
    xhr.setRequestHeader('Content-Type', 'application/json');
    xhr.send(JSON.stringify(data));
}
window.onFunc = function(name, onFunc)
{
    callbackFuncs[name] = onFunc;
    if (!ignore_onFunc.includes(name))
    {
        console.log("onFunc: " + name + " " + onFunc);
    }
};
window.callFunc = function(data)
{
    //console.log(data);
    var handled = false;
    if (data.request != null)
    {
        handled = true;
        var reqInfo = JSON.parse(data.request);
        switch (reqInfo.funcname)
        {
            case "SetRecordBtn":
                {
                    isHoveringRecordBtn = reqInfo.SetRecordBtn;
                }
                break;
            case "StartRecord":
                {
                    isRecording = true;
                }
                break;
            case "StopRecord":
                {
                    isRecording = true;
                }
                break;
            default:
                handled = false;
                break;
        }
    }
    if (!handled)
    {
        data.requestType = "callFunc";
        makeRequest(data);
    }
};
var isRecording = false;
var isHoveringRecordBtn = false;
var keyStates = [];
var keyNameMap = [];
keyNameMap["Escape"] = "Esc";
keyNameMap["F1"] = "F1";
keyNameMap["F2"] = "F2";
keyNameMap["F3"] = "F3";
keyNameMap["F4"] = "F4";
keyNameMap["F5"] = "F5";
keyNameMap["F6"] = "F6";
keyNameMap["F7"] = "F7";
keyNameMap["F8"] = "F8";
keyNameMap["F9"] = "F9";
keyNameMap["F10"] = "F10";
keyNameMap["F11"] = "F11";
keyNameMap["F12"] = "F12";
keyNameMap["PrintScreen"] = "Print Screen";
keyNameMap["ScrollLock"] = "Scroll Lock";
keyNameMap["Pause"] = "Pause";
keyNameMap["Backquote"] = "`";
keyNameMap["Digit1"] = "1";
keyNameMap["Digit2"] = "2";
keyNameMap["Digit3"] = "3";
keyNameMap["Digit4"] = "4";
keyNameMap["Digit5"] = "5";
keyNameMap["Digit6"] = "6";
keyNameMap["Digit7"] = "7";
keyNameMap["Digit8"] = "8";
keyNameMap["Digit9"] = "9";
keyNameMap["Digit0"] = "0";
keyNameMap["Minus"] = "-";
keyNameMap["Equal"] = "=";
keyNameMap["Backspace"] = "Backspace";
keyNameMap["Insert"] = "Insert";
keyNameMap["Home"] = "Home";
keyNameMap["PageUp"] = "Page Up";
keyNameMap["Tab"] = "Tab";
keyNameMap["KeyQ"] = "Q";
keyNameMap["KeyW"] = "W";
keyNameMap["KeyE"] = "E";
keyNameMap["KeyR"] = "R";
keyNameMap["KeyT"] = "T";
keyNameMap["KeyY"] = "Y";
keyNameMap["KeyU"] = "U";
keyNameMap["KeyI"] = "I";
keyNameMap["KeyO"] = "O";
keyNameMap["KeyP"] = "P";
keyNameMap["BracketLeft"] = "[";
keyNameMap["BracketRight"] = "]";
keyNameMap["Backslash"] = "\\";
keyNameMap["Delete"] = "Delete";
keyNameMap["End"] = "End";
keyNameMap["PageDown"] = "Page Down";
keyNameMap["CapsLock"] = "Caps Lock";
keyNameMap["KeyA"] = "A";
keyNameMap["KeyS"] = "S";
keyNameMap["KeyD"] = "D";
keyNameMap["KeyF"] = "F";
keyNameMap["KeyG"] = "G";
keyNameMap["KeyH"] = "H";
keyNameMap["KeyJ"] = "J";
keyNameMap["KeyK"] = "K";
keyNameMap["KeyL"] = "L";
keyNameMap["Semicolon"] = ";";
keyNameMap["Quote"] = "'";
keyNameMap["Enter"] = "Enter";
keyNameMap["ShiftLeft"] = "Left Shift";
keyNameMap["IntlBackslash"] = "AltBackslash";
keyNameMap["KeyZ"] = "Z";
keyNameMap["KeyX"] = "X";
keyNameMap["KeyC"] = "C";
keyNameMap["KeyV"] = "V";
keyNameMap["KeyB"] = "B";
keyNameMap["KeyN"] = "N";
keyNameMap["KeyM"] = "M";
keyNameMap["Comma"] = ",";
keyNameMap["Period"] = ".";
keyNameMap["Slash"] = "/";
keyNameMap["ShiftRight"] = "Right Shift";
keyNameMap["ArrowUp"] = "Up";
keyNameMap["ControlLeft"] = "Left Ctrl";
keyNameMap["MetaLeft"] = "Left Win";
keyNameMap["AltLeft"] = "Left Alt";
keyNameMap["Space"] = "Space";
keyNameMap["AltRight"] = "Right Alt";
keyNameMap["MetaRight"] = "Right Win";
keyNameMap["ContextMenu"] = "Menu";
keyNameMap["ControlRight"] = "Right Ctrl";
keyNameMap["NumLock"] = "Num Lock";
keyNameMap["NumpadDivide"] = "Num /";
keyNameMap["NumpadMultiply"] = "Num *";
keyNameMap["NumpadSubtract"] = "Num -";
keyNameMap["Numpad7"] = "Num 7";
keyNameMap["Numpad8"] = "Num 8";
keyNameMap["Numpad9"] = "Num 9";
keyNameMap["NumpadAdd"] = "Num +";
keyNameMap["Numpad4"] = "Num 4";
keyNameMap["Numpad5"] = "Num 5";
keyNameMap["Numpad6"] = "Num 6";
keyNameMap["Numpad1"] = "Num 1";
keyNameMap["Numpad2"] = "Num 2";
keyNameMap["Numpad3"] = "Num 3";
keyNameMap["Numpad0"] = "Num 0";
keyNameMap["NumpadDecimal"] = "Num .";
keyNameMap["NumpadEnter"] = "Num Enter";
keyNameMap["LaunchMediaPlayer"] = "OpenMediaPlayer";
keyNameMap["MediaPlayPause"] = "MediaPlayPause";
keyNameMap["MediaStop"] = "MediaStop";
keyNameMap["MediaTrackPrevious"] = "MediaPrevious";
keyNameMap["MediaTrackNext"] = "MediaNext";
keyNameMap["AudioVolumeUp"] = "VolumeUp";
keyNameMap["AudioVolumeDown"] = "VolumeDown";
keyNameMap["AudioVolumeMute"] = "VolumeMute";
keyNameMap["BrowserStop"] = "BrowserStop";
keyNameMap["BrowserBack"] = "BrowserBack";
keyNameMap["BrowserForward"] = "BrowserForward";
keyNameMap["BrowserRefresh"] = "BrowserRefresh";
keyNameMap["BrowserFavorites"] = "BrowserFavorites";
keyNameMap["BrowserHome"] = "BrowserHome";
keyNameMap["LaunchMail"] = "OpenEmail";
keyNameMap["LaunchApplication1"] = "OpenMyComputer";
keyNameMap["LaunchApplication2"] = "OpenCalculator";
keyNameMap["NumpadEqual"] = "Clear";
keyNameMap["F13"] = "F13";
keyNameMap["F14"] = "F14";
keyNameMap["F15"] = "F15";
keyNameMap["F16"] = "F16";
keyNameMap["F17"] = "F17";
keyNameMap["F18"] = "F18";
keyNameMap["F19"] = "F19";
keyNameMap["F20"] = "F20";
keyNameMap["F21"] = "F21";
keyNameMap["F22"] = "F22";
keyNameMap["F23"] = "F23";
keyNameMap["F24"] = "F24";
keyNameMap["NumpadComma"] = "NumpadComma";
keyNameMap["IntlRo"] = "IntlRo";
keyNameMap["KanaMode"] = "KanaMode";
keyNameMap["IntlYen"] = "IntlYen";
keyNameMap["Convert"] = "Convert";
keyNameMap["NonConvert"] = "NonConvert";
keyNameMap["Lang3"] = "Lang3";
keyNameMap["Lang4"] = "Lang4";
function beginTimer()
{
    return new Date();
}
function endTimer(startTime)
{
    endTime = new Date();
    return endTime - startTime;
}
window.onkeydown = function(e)
{
    var code = e.code != null ? e.code : e.key;
    if (!isRecording || keyNameMap[code] == null)
    {
        return;
    }
    var keyName = keyNameMap[code];
    var state = keyStates[code];
    if (state == null)
    {
        state = keyStates[code] = {};
    }
    if (state.down)
    {
        return;
    }
    state.down = true;
    state.time = beginTimer();
    var func = callbackFuncs["onKeyDown"];
    if (func != null)
    {
        var data = [];
        data[0] = "\"" + keyName + "\"";
        func(data);
    }
};
window.onkeyup = function(e)
{
    var code = e.code != null ? e.code : e.key;
    if (!isRecording || keyNameMap[code] == null)
    {
        return;
    }
    var keyName = keyNameMap[code];
    var state = keyStates[code];
    if (state == null || !state.down)
    {
        return;
    }
    state.down = false;
    var duration = endTimer(state.time);
    if (duration > 0)
    {
        var delayFunc = callbackFuncs["onDelay"];
        if (delayFunc != null)
        {
            var delayData = [];
            delayData[0] = duration;
            delayFunc(delayData);
        }
    }
    var func = callbackFuncs["onKeyUp"];
    if (func != null)
    {
        var data = [];
        data[0] = "\"" + keyName + "\"";
        func(data);
    }
};
window.onmousedown = function(e)
{
    if (!isRecording || isHoveringRecordBtn)
    {
        return;
    }
    var state = keyStates[e.button];
    if (state == null)
    {
        state = keyStates[e.button] = {};
    }
    if (state.down)
    {
        return;
    }
    state.down = true;
    state.time = beginTimer();
    var func = callbackFuncs["onMouseDown"];
    if (func != null)
    {
        var data = [];
        data[0] = e.button;
        func(data);
    }
};
window.onmouseup = function(e)
{
    if (!isRecording || isHoveringRecordBtn)
    {
        return;
    }
    var state = keyStates[e.button];
    if (state == null || !state.down)
    {
        return;
    }
    state.down = false;
    var duration = endTimer(state.time);
    if (duration > 0)
    {
        var delayFunc = callbackFuncs["onDelay"];
        if (delayFunc != null)
        {
            var delayData = [];
            delayData[0] = duration;
            delayFunc(delayData);
        }
    }
    var func = callbackFuncs["onMouseUp"];
    if (func != null)
    {
        var data = [];
        data[0] = e.button;
        func(data);
    }
};
function S4()
{
    return (((1+Math.random())*0x10000)|0).toString(16).substring(1); 
}

window.getGuid = function()
{
    return (S4() + S4() + "-" + S4() + "-4" + S4().substr(0,3) + "-" + S4() + "-" + S4() + S4() + S4()).toUpperCase();
};
function ping()
{
    var data = {};
    data.requestType = "ping";
    data.onSuccess = function(response)
    {
        var messages = JSON.parse(response);
        if (messages != null && messages.length > 0)
        {
            for (var i = 0; i < messages.length; i++)
            {
                var callback = callbackFuncs[messages[i].funcName];
                if (callback != null)
                {
                    callback(messages[i].data);
                }
            }
        }
    };
    makeRequest(data);
    setTimeout(ping, 1000);
}
ping();