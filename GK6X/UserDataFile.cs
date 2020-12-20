using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using System.Diagnostics;
using MiniJSON;

namespace GK6X
{
    public class UserDataFile
    {
        public Dictionary<KeyboardLayer, Layer> FnLayers = new Dictionary<KeyboardLayer, Layer>();
        public Dictionary<KeyboardLayer, Layer> Layers = new Dictionary<KeyboardLayer, Layer>();
        public Dictionary<string, Macro> Macros = new Dictionary<string, Macro>();
        public Dictionary<string, LightingEffect> LightingEffects = new Dictionary<string, LightingEffect>();
        public Dictionary<string, string> KeyAliases = new Dictionary<string, string>();
        /// <summary>
        /// If true all lighting data on the keyboard will be cleared (this is different to not providing any
        /// lighting which will just skip sending lighting data)
        /// </summary>
        public bool NoLighting;
        public HashSet<KeyboardLayer> NoLightingLayers = new HashSet<KeyboardLayer>();
        private int nextMacroId;
        private int nextLightingId;

        public class Layer
        {
            public Dictionary<string, uint> Keys = new Dictionary<string, uint>();

            public uint GetKey(KeyboardState.Key key)
            {
                uint result;
                if (Keys.TryGetValue(key.DriverValueName.ToLower(), out result))
                {
                    return result;
                }
                return KeyValues.UnusedKeyValue;
            }
        }

        public class Macro
        {
            public string Name;
            public string Guid;// Used only for .cmf macros
            public int Id;
            public MacroRepeatType RepeatType;
            public byte RepeatCount;
            public ushort DefaultDelay;
            /// <summary>
            /// 
            /// If true a delay will be used on the last action. This can be useful where
            /// the macro is to be repeated multiple times and a constant delay is desired.
            /// </summary>
            public bool UseTrailingDelay;
            public List<Action> Actions = new List<Action>();

            public Macro(string name)
            {
                Name = name;
                Id = -1;
                RepeatType = MacroRepeatType.RepeatXTimes;
                RepeatCount = 1;
            }

            public class Action
            {
                public MacroKeyState State;
                public MacroKeyType Type;
                public byte KeyCode;// DriverValueMouseButton or short version of DriverValue
                public DriverValueModifer Modifier;
                public ushort Delay;

                /// <summary>
                /// Used for the web gui string name mappings <see cref="MacroKeyNames"/>
                /// </summary>
                public string ValueStr;
            }

            /// <summary>
            /// Key names as defined in the "Macros" tab (this is seemingly not in the data files?)
            /// </summary>
            Dictionary<string, DriverValue> keyNames = new Dictionary<string, DriverValue>()
            {
            };

            public bool LoadFile(string path)
            {
                if (File.Exists(path))
                {
                    byte[] buffer = CMFile.Load(path);
                    if (buffer != null)
                    {
                        string str = Encoding.UTF8.GetString(buffer);
                        string[] lines = str.Split(new char[]  { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        string currentGroup = null;
                        Action currentAction = null;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim();
                            if (line.StartsWith("["))
                            {
                                line = line.Replace("[", string.Empty).Replace("]", string.Empty).ToLower();
                                switch (line.ToLower())
                                {
                                    case "general":
                                    case "script":
                                        currentGroup = line;
                                        break;
                                }
                            }
                            else if (currentGroup != null)
                            {
                                string key = null;
                                string value = null;
                                int keyIndex = line.IndexOf('=');
                                if (keyIndex <= 0)
                                {
                                    keyIndex = line.IndexOf(' ');
                                }
                                if (keyIndex > 0)
                                {
                                    key = line.Substring(0, keyIndex).Trim().ToLower();
                                    value = line.Substring(keyIndex + 1).Trim();
                                }
                                else
                                {
                                    key = line.ToLower();
                                }
                                switch (currentGroup)
                                {
                                    case "general":
                                        {
                                            switch (key)
                                            {
                                                case "name":
                                                    {
                                                        Name = value;
                                                    }
                                                    break;
                                                case "scriptid":
                                                    {
                                                        Guid = value;
                                                    }
                                                    break;
                                                case "repeats":
                                                    {
                                                        byte.TryParse(value, out RepeatCount);
                                                    }
                                                    break;
                                                case "stopmode":
                                                    {
                                                        byte stopmode;
                                                        if (byte.TryParse(value, out stopmode))
                                                        {
                                                            RepeatType = (MacroRepeatType)stopmode;
                                                        }
                                                    }
                                                    break;
                                            }
                                        }
                                        break;
                                    case "script":
                                        {
                                            switch (key)
                                            {
                                                case "keydown":
                                                case "keyup":
                                                    {
                                                        value = value.Trim('\"');
                                                        MacroKeyNames.Item macroKeyName;
                                                        if (MacroKeyNames.Names.TryGetValue(value, out macroKeyName))
                                                        {
                                                            currentAction = null;
                                                            currentAction = new Action();
                                                            currentAction.ValueStr = value;
                                                            if (KeyValues.IsKeyModifier(macroKeyName.DriverValue))
                                                            {
                                                                currentAction.Modifier = KeyValues.GetKeyModifier(macroKeyName.DriverValue);
                                                            }
                                                            else
                                                            {
                                                                currentAction.KeyCode = KeyValues.GetShortDriverValue(macroKeyName.DriverValue);
                                                            }
                                                            currentAction.State = key.Contains("up") ? MacroKeyState.Up : MacroKeyState.Down;
                                                            currentAction.Type = MacroKeyType.Key;
                                                            Actions.Add(currentAction);
                                                        }
                                                    }
                                                    break;
                                                case "leftdown":
                                                case "leftup":
                                                case "rightdown":
                                                case "rightup":
                                                    currentAction = null;
                                                    currentAction = new Action();
                                                    currentAction.State = key.Contains("up") ? MacroKeyState.Up : MacroKeyState.Down;
                                                    switch(key)
                                                    {
                                                        case "leftdown":
                                                        case "leftup":
                                                            currentAction.KeyCode = (byte)DriverValueMouseButton.LButton;
                                                            break;
                                                        case "rightdown":
                                                        case "rightup":
                                                            currentAction.KeyCode = (byte)DriverValueMouseButton.RButton;
                                                            break;
                                                    }
                                                    currentAction.Type = MacroKeyType.Mouse;
                                                    Actions.Add(currentAction);
                                                    break;
                                                case "delay":
                                                    {
                                                        if (currentAction != null)
                                                        {
                                                            ushort.TryParse(value, out currentAction.Delay);
                                                        }
                                                    }
                                                    break;
                                            }
                                        }
                                        break;
                                }
                            }
                        }
                        return true;
                    }
                }
                return false;
            }
        }

        public class LightingEffect
        {
            private UserDataFile owner;

            public int Id;
            public string Name;
            public LightingEffectType Type;
            /// <summary>
            /// Each frame holds an array of keys (location codes)
            /// </summary>
            public List<Frame> Frames = new List<Frame>();
            /// <summary>
            /// Total number of frames (based on the frame count for each frame)
            /// </summary>
            public int TotalFrames;
            /// <summary>
            /// Lighting effect params / configs
            /// </summary>
            public List<Param> Params = new List<Param>();
            /// <summary>
            /// Used for static lighting. This maps from key location codes to an RGB value
            /// </summary>
            public Dictionary<int, uint> KeyColors = new Dictionary<int, uint>();
            /// <summary>
            /// The layers this effect should be used on
            /// </summary>
            public HashSet<KeyboardLayer> Layers = new HashSet<KeyboardLayer>();

            /// <summary>
            /// Total number of keys as seen by the lighting system
            /// </summary>
            public const int NumKeys = 132;// (528/0x210 total bytes - see refs to this number in the disassembled code)
            public const int MaxEffects = 32;
            /// <summary>
            /// Total number of bytes used for static lighting (1 uint color value for each key)
            /// </summary>
            public const int NumStaticLightingBytes = 704;// (704/0x2C0 bytes, 176 ints)

            public class Frame
            {
                /// <summary>
                /// Number of frames this frame should be displayed
                /// </summary>
                public int Count;
                /// <summary>
                /// Key location codes
                /// </summary>
                public HashSet<int> KeyCodes = new HashSet<int>();
            }

            public LightingEffect(UserDataFile owner, string name)
            {
                this.owner = owner;
                Id = -1;
                Name = name;
            }

            public class Param
            {
                public uint Color;
                public LightingEffectColorType ColorType;
                public HashSet<int> Keys = new HashSet<int>();
                public int Val1;// "Count" (used in RGB / breathing)
                public int Val2;// "StayCount" (used in breathing)
                /// <summary>
                /// If true the values used by RGB/breathing should be sent to the keyboard unmodified (regular lighting effect
                /// files have their values modified by an amount - 360/val for RGB, 100/val for breathing val2)
                /// </summary>
                public bool UseRawValues;
            }

            public bool Load(KeyboardState keyboard)
            {
                try
                {
                    if (string.IsNullOrEmpty(Name))
                    {
                        return false;
                    }
                    string path = Path.Combine(Program.DataBasePath, "lighting", Name + ".le");
                    if (!File.Exists(path))
                    {
                        return false;
                    }
                    return Load(keyboard, File.ReadAllText(path));
                }
                catch
                {
                    return false;
                }
            }

            public bool Load(KeyboardState keyboard, string str)
            {
                try
                {
                    Dictionary<string, object> json = Json.Deserialize(str) as Dictionary<string, object>;
                    if (json == null)
                    {
                        return false;
                    }

                    long lightingTypeVal;
                    string lightingTypeStr;
                    if (Json.TryGetValue(json, "Type", out lightingTypeStr))
                    {
                        if (!Enum.TryParse(lightingTypeStr, true, out Type))
                        {
                            Type = LightingEffectType.Dynamic;
                        }
                    }
                    else if (Json.TryGetValue(json, "Type", out lightingTypeVal))
                    {
                        Type = (LightingEffectType)lightingTypeVal;
                    }
                    else
                    {
                        Type = LightingEffectType.Dynamic;
                    }

                    switch (Type)
                    {
                        case LightingEffectType.Static:
                            {
                                Dictionary<string, object> data;
                                if (Json.TryGetValue(json, "Data", out data))
                                {
                                    LoadStatic(keyboard, data);
                                }
                            }
                            break;
                        case LightingEffectType.Dynamic:
                            LoadDynamic(keyboard, json);
                            break;
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private bool TryParseColor(string colorStr, bool fixupAlpha, out uint color)
            {
                color = 0xFFFFFFFF;
                if (string.IsNullOrEmpty(colorStr))
                {
                    return false;
                }
                try
                {
                    bool hasColor = false;
                    if (colorStr.StartsWith("0x"))
                    {
                        hasColor = uint.TryParse(colorStr.Substring(2), NumberStyles.HexNumber, null, out color);
                    }
                    else if (colorStr.StartsWith("#"))
                    {
                        hasColor = uint.TryParse(colorStr.Substring(1), NumberStyles.HexNumber, null, out color);
                    }
                    if (hasColor)
                    {
                        byte a = (byte)(color >> 24);
                        byte r = (byte)(color >> 16);
                        byte g = (byte)(color >> 8);
                        byte b = (byte)(color >> 0);
                        if (fixupAlpha && a == 0 && (r != 0 || g != 0 || b != 0))
                        {
                            // Alpha is used on static lighting keys, set alpha to 0xFF if 0x00 yet color is defined
                            a = 0xFF;
                        }
                        color = (uint)((r << 0) | (g << 8) | (b << 16) | (a << 24));
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            private bool TryGetKeyLocationCode(KeyboardState keyboard, object keyObj, out int locationCode)
            {
                locationCode = -1;
                KeyboardState.Key key;
                string name;
                if (keyObj is string)
                {
                    string keyStr = keyObj as string;
                    DriverValue driverValue;
                    if (keyStr.StartsWith("0x"))
                    {
                        uint driverValueUInt;
                        if (uint.TryParse(keyStr.Substring(2), NumberStyles.HexNumber, null, out driverValueUInt) &&
                            keyboard.KeysByDriverValue.TryGetValue(driverValueUInt, out key))
                        {
                            locationCode = key.LocationCode;
                            return true;
                        }
                    }
                    else if (int.TryParse(keyStr, out locationCode))
                    {
                        return true;
                    }
                    else if (owner.TryParseDriverValue(keyStr, out driverValue, out name) &&
                        keyboard.KeysByDriverValueName.TryGetValue(name, out key))
                    {
                        locationCode = key.LocationCode;
                        return true;
                    }
                }
                else if (keyObj is long)
                {
                    locationCode = (int)((long)keyObj);
                    return true;
                }
                return false;
            }

            public void LoadStatic(KeyboardState keyboard, Dictionary<string, object> data)
            {
                foreach (KeyValuePair<string, object> item in data)
                {
                    int key;
                    string colorStr = item.Value as string;
                    uint color;
                    if (TryGetKeyLocationCode(keyboard, item.Key, out key) && TryParseColor(colorStr, true, out color))
                    {
                        KeyColors[key] = color;
                    }
                }
            }

            private void LoadDynamic(KeyboardState keyboard, Dictionary<string, object> json)
            {
                List<object> framesObjs;
                if (Json.TryGetValue(json, "Frames", out framesObjs))
                {
                    foreach (object frameObj in framesObjs)
                    {
                        Dictionary<string, object> frameData = frameObj as Dictionary<string, object>;
                        if (frameData != null)
                        {
                            Frame frame = new Frame();
                            Frames.Add(frame);

                            long frameCount;
                            if (Json.TryGetValue(frameData, "Count", out frameCount) && frameCount > 0)
                            {
                                frame.Count = (int)frameCount;
                            }
                            else
                            {
                                frame.Count = 1;
                            }
                            TotalFrames += frame.Count;

                            Dictionary<string, object> dataAsDictionary;
                            List<object> dataAsList;
                            if (Json.TryGetValue(frameData, "Data", out dataAsDictionary))
                            {
                                foreach (KeyValuePair<string, object> item in dataAsDictionary)
                                {
                                    // There seems to be a color in the value, this isn't used? Maybe it's used
                                    // for DIY lighting if this is designed to support DIY lighting?
                                    int keyCode;
                                    if (TryGetKeyLocationCode(keyboard, item.Key, out keyCode))
                                    {
                                        frame.KeyCodes.Add(keyCode);
                                    }
                                }
                            }
                            else if (Json.TryGetValue(frameData, "Data", out dataAsList))
                            {
                                foreach (object keyCodeObj in dataAsList)
                                {
                                    int keyCode;
                                    if (TryGetKeyLocationCode(keyboard, keyCodeObj, out keyCode))
                                    {
                                        frame.KeyCodes.Add(keyCode);
                                    }
                                }
                            }
                        }
                    }
                }

                List<object> configsObjs;
                if (Json.TryGetValue(json, "LEConfigs", out configsObjs))
                {
                    foreach (object configObj in configsObjs)
                    {
                        Dictionary<string, object> config = configObj as Dictionary<string, object>;
                        if (config != null)
                        {
                            Param param = new Param();

                            long colorTypeVal;
                            string colorTypeStr;
                            if (Json.TryGetValue(config, "Type", out colorTypeStr))
                            {
                                if (!Enum.TryParse(colorTypeStr, true, out param.ColorType))
                                {
                                    param.ColorType = LightingEffectColorType.Monochrome;
                                }
                            }
                            else if (Json.TryGetValue(config, "Type", out colorTypeVal))
                            {
                                param.ColorType = (LightingEffectColorType)colorTypeVal;
                            }
                            else
                            {
                                param.ColorType = LightingEffectColorType.Monochrome;
                            }

                            string colorStr;
                            if (Json.TryGetValue(config, "Color", out colorStr))
                            {
                                TryParseColor(colorStr, false, out param.Color);
                            }

                            long param1 = 0;
                            if (Json.TryGetValue(config, "Count", out param1))
                            {
                                param.Val1 = (int)param1;
                            }
                            else if (Json.TryGetValue(config, "Val1", out param1))
                            {
                                param.Val1 = (int)param1;
                            }
                            long param2 = 0;
                            if (Json.TryGetValue(config, "StayCount", out param2))
                            {
                                param.Val2 = (int)param2;
                            }
                            else if (Json.TryGetValue(config, "Val2", out param2))
                            {
                                param.Val2 = (int)param2;
                            }

                            if (!Json.TryGetValue(config, "UseRawValues", out param.UseRawValues))
                            {
                                long useRawValues;
                                if (Json.TryGetValue(config, "UseRawValues", out useRawValues) && useRawValues == 1)
                                {
                                    param.UseRawValues = true;
                                }
                            }

                            List<object> keys;
                            if (Json.TryGetValue(config, "Keys", out keys))
                            {
                                foreach (object keyObj in keys)
                                {
                                    int keyCode;
                                    if (TryGetKeyLocationCode(keyboard, keyObj, out keyCode))
                                    {
                                        param.Keys.Add(keyCode);
                                    }
                                }
                            }

                            if (param.Keys.Count > 0)
                            {
                                Params.Add(param);
                            }
                        }
                    }
                }
            }
        }

        enum GroupType
        {
            None,
            Layer,
            Macro,
            Lighting,
            KeyAlias
        }

        private bool TryParseDriverValue(string str, out DriverValue result)
        {
            string name;
            return TryParseDriverValue(str, out result, out name);
        }

        private bool TryParseDriverValue(string str, out DriverValue result, out string name)
        {
            const bool ignoreCase = true;

            name = str;
            string realKeyName;
            if (KeyAliases.TryGetValue(str, out realKeyName))
            {
                str = realKeyName;
                name = str;
            }

            if (Enum.TryParse(str, ignoreCase, out result))
            {
                return true;
            }
            int underscoreIndex = str.IndexOf('_');
            if (underscoreIndex > 0)
            {
                int duplicateKeyId;
                string startStr = str.Substring(0, underscoreIndex);
                string endStr = str.Substring(underscoreIndex + 1).Trim();
                if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out duplicateKeyId) && duplicateKeyId > 1 &&
                    Enum.TryParse(startStr, ignoreCase, out result))
                {
                    return true;
                }
            }
            return false;
        }

        private Layer FindOrAddLayer(KeyboardLayer layer, bool fn)
        {
            Dictionary<KeyboardLayer, Layer> layers = fn ? FnLayers : Layers;
            Layer result;
            if (layers.TryGetValue(layer, out result))
            {
                return result;
            }
            result = new Layer();
            layers[layer] = result;
            return result;
        }

        private Macro GetMacro(string name)
        {
            Macro result;
            if (Macros.TryGetValue(name, out result))
            {
                if (result.Id == -1)
                {
                    result.Id = nextMacroId++;
                }
            }
            return result;
        }

        private LightingEffect GetLighting(string name)
        {
            LightingEffect result;
            if (LightingEffects.TryGetValue(name, out result))
            {
                if (result.Id == -1)
                {
                    result.Id = nextLightingId++;
                }
            }
            return result;
        }

        public List<LightingEffect> GetLightingEffects(KeyboardLayer layer)
        {
            List<LightingEffect> result = new List<LightingEffect>();
            foreach (LightingEffect effect in LightingEffects.Values)
            {
                if (effect.Layers.Contains(layer))
                {
                    result.Add(effect);
                }
            }
            return result;
        }

        public int GetNumMacros(KeyboardLayer layer)
        {
            List<Layer> layers = new List<Layer>();
            Layer userLayer;
            if (Layers.TryGetValue(layer, out userLayer))
            {
                layers.Add(userLayer);
            }
            if (FnLayers.TryGetValue(layer, out userLayer))
            {
                layers.Add(userLayer);
            }
            HashSet<byte> macroIds = new HashSet<byte>();
            foreach (Layer l in layers)
            {
                foreach (uint key in l.Keys.Values)
                {
                    if (KeyValues.GetKeyType(key) == DriverValueType.Macro)
                    {
                        macroIds.Add(KeyValues.GetKeyData2(key));
                    }
                }
            }
            return macroIds.Count;
        }

        public static UserDataFile Load(KeyboardState keyboard, string file)
        {
            if (!File.Exists(file))
            {
                return null;
            }
            UserDataFile result = new UserDataFile();
            result.Load(keyboard, file, GroupType.KeyAlias);
            result.Load(keyboard, file, GroupType.Lighting, GroupType.Macro);
            result.Load(keyboard, file, GroupType.Layer);
            return result;
        }

        private void Load(KeyboardState keyboard, string file, params GroupType[] groups)
        {
            GroupType currentGroup = GroupType.None;
            Macro currentMacro = null;
            HashSet<Layer> currentLayers = new HashSet<Layer>();

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#"))
                {
                    continue;
                }
                if (line.StartsWith("["))
                {
                    currentGroup = GroupType.None;
                    currentLayers.Clear();
 
                    int endBrace = line.IndexOf(']');
                    if (endBrace > 1)
                    {
                        string fullGroupName = line.Substring(1, endBrace - 1).Trim();
                        string[] groupNames = null;
                        string innerName = null;
                        string[] innerNameSplitted = null;
                        int openParenth = fullGroupName.IndexOf('(');
                        int closeParenth = fullGroupName.IndexOf(')');
                        if (openParenth > 0 && closeParenth > openParenth)
                        {
                            innerName = fullGroupName.Substring(openParenth + 1, closeParenth - (openParenth + 1));
                            innerNameSplitted = innerName.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            innerName = innerNameSplitted.Length > 0 ? innerNameSplitted[0] : null;
                            for (int j = 0; j < innerNameSplitted.Length; j++)
                            {
                                innerNameSplitted[j] = innerNameSplitted[j].Trim();
                            }
                            if (!string.IsNullOrEmpty(innerName))
                            {
                                innerName = innerName.Trim();
                            }

                            groupNames = new string[] { fullGroupName.Substring(0, openParenth).Trim() };
                        }
                        else
                        {
                            // This is for layers (not macro / lighting)
                            groupNames = fullGroupName.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int j = 0; j < groupNames.Length; j++)
                            {
                                groupNames[j] = groupNames[j].Trim();
                            }
                        }
                        foreach (string groupName in groupNames)
                        {
                            switch (groupName.ToLower())
                            {
                                case "keyalias":
                                    if (groups.Contains(GroupType.KeyAlias))
                                    {
                                        currentGroup = GroupType.KeyAlias;
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "base":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Base, false));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "layer1":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer1, false));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "layer2":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer2, false));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "layer3":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer3, false));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "fnbase":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Base, true));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "fnlayer1":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer1, true));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "fnlayer2":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer2, true));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "fnlayer3":
                                    if (groups.Contains(GroupType.Layer))
                                    {
                                        currentGroup = GroupType.Layer;
                                        currentLayers.Add(FindOrAddLayer(KeyboardLayer.Layer3, true));
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "macro":
                                    if (groups.Contains(GroupType.Macro) && !string.IsNullOrEmpty(innerName))
                                    {
                                        currentGroup = GroupType.Macro;
                                        currentMacro = new Macro(innerName);
                                        Macros[innerName] = currentMacro;
                                        if (innerNameSplitted.Length > 1)
                                        {
                                            ushort.TryParse(innerNameSplitted[1], out currentMacro.DefaultDelay);
                                        }
                                        if (innerNameSplitted.Length > 2)
                                        {
                                            if (!Enum.TryParse<MacroRepeatType>(innerNameSplitted[2], true, out currentMacro.RepeatType))
                                            {
                                                currentMacro.RepeatType = MacroRepeatType.RepeatXTimes;
                                            }
                                        }
                                        if (innerNameSplitted.Length > 3)
                                        {
                                            byte.TryParse(innerNameSplitted[3], out currentMacro.RepeatCount);
                                        }
                                        if (innerNameSplitted.Length > 4)
                                        {
                                            bool.TryParse(innerNameSplitted[4], out currentMacro.UseTrailingDelay);
                                        }
                                        if (currentMacro.RepeatCount == 0)
                                        {
                                            // No point in having a macro which doesn't even run...
                                            currentMacro.RepeatCount = 1;
                                        }
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "lighting":
                                    if (groups.Contains(GroupType.Lighting) && !string.IsNullOrEmpty(innerName))
                                    {
                                        currentGroup = GroupType.Lighting;
                                        LightingEffect lightingEffect = null;
                                        if (!LightingEffects.TryGetValue(innerName, out lightingEffect))
                                        {
                                            lightingEffect = new LightingEffect(this, innerName);
                                        }
                                        if (lightingEffect.Load(keyboard))
                                        {
                                            LightingEffects[innerName] = lightingEffect;

                                            for (int j = 1; j < innerNameSplitted.Length; j++)
                                            {
                                                KeyboardLayer layer;
                                                if (Enum.TryParse(innerNameSplitted[j], out layer))
                                                {
                                                    lightingEffect.Layers.Add(layer);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Program.Log("Failed to load lighting effect '" + innerName + "'");
                                        }
                                    }
                                    else
                                    {
                                        currentGroup = GroupType.None;
                                    }
                                    break;
                                case "nolighting":
                                    if (innerNameSplitted != null)
                                    {
                                        for (int j = 0; j < innerNameSplitted.Length; j++)
                                        {
                                            KeyboardLayer layer;
                                            if (Enum.TryParse(innerNameSplitted[j], out layer))
                                            {
                                                NoLightingLayers.Add(layer);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        NoLighting = true;
                                    }
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    switch (currentGroup)
                    {
                        case GroupType.KeyAlias:
                            {
                                string[] splitted = line.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                                if (splitted.Length > 1)
                                {
                                    string aliasKeyName = splitted[0].Trim();
                                    string realKeyName = splitted[1].Trim();
                                    KeyAliases[aliasKeyName] = realKeyName;
                                }
                            }
                            break;
                        case GroupType.Layer:
                            {
                                string[] splitted = line.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                                if (splitted.Length > 1)
                                {
                                    string srcKey = splitted[0].Trim();
                                    string[] keysSplittedDst = splitted[1].Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

                                    string srcName = null;
                                    uint srcValue = 0;
                                    uint dstValue = 0;

                                    if (srcKey.StartsWith("0x"))
                                    {
                                        uint.TryParse(srcKey, NumberStyles.HexNumber, null, out srcValue);
                                    }
                                    else
                                    {
                                        DriverValue value;
                                        if (TryParseDriverValue(srcKey, out value, out srcName))
                                        {
                                            srcValue = (uint)value;
                                        }
                                    }

                                    if (srcValue != 0)
                                    {
                                        foreach (string str in keysSplittedDst)
                                        {
                                            if (str.StartsWith("0x"))
                                            {
                                                uint.TryParse(str, NumberStyles.HexNumber, null, out dstValue);
                                            }
                                            else if (str.ToLower().StartsWith("macro"))
                                            {
                                                int openParenth = str.IndexOf('(');
                                                int closeParenth = str.IndexOf(')');
                                                if (openParenth > 0 && closeParenth > openParenth)
                                                {
                                                    string macroName = str.Substring(openParenth + 1, closeParenth - (openParenth + 1));
                                                    Macro macro = GetMacro(macroName);
                                                    if (macro != null)
                                                    {
                                                        Debug.Assert(macro.Id >= 0 && macro.Id <= byte.MaxValue);
                                                        dstValue = (uint)(0x0A010000 + macro.Id);
                                                    }
                                                    else
                                                    {
                                                        Program.Log("Failed to find macro '" + macroName + "' bound to key " + (DriverValue)srcValue);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                DriverValue value;
                                                if (TryParseDriverValue(str, out value))
                                                {
                                                    switch (KeyValues.GetKeyType((uint)value))
                                                    {
                                                        case DriverValueType.Key:
                                                            // TODO: Extra validation (this only makes sense for modifiers)
                                                            dstValue |= (uint)value;
                                                            break;
                                                        default:
                                                            dstValue = (uint)value;
                                                            break;
                                                    }
                                                }
                                            }
                                        }
                                        foreach (Layer currentLayer in currentLayers)
                                        {
                                            currentLayer.Keys[srcName.ToLower()] = dstValue;
                                        }
                                    }
                                }
                            }
                            break;
                        case GroupType.Macro:
                            {
                                string[] splitted = line.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                                if (splitted.Length > 1)
                                {
                                    string[] keysSplitted = splitted[1].Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

                                    ushort delay = currentMacro.DefaultDelay;
                                    if (splitted.Length > 2)
                                    {
                                        if (!ushort.TryParse(splitted[2], out delay))
                                        {
                                            delay = currentMacro.DefaultDelay;
                                        }
                                    }

                                    MacroKeyState[] states = null;
                                    switch (splitted[0].ToLower())
                                    {
                                        case "press":
                                            states = new MacroKeyState[] { MacroKeyState.Down, MacroKeyState.Up };
                                            break;
                                        case "down":
                                            states = new MacroKeyState[] { MacroKeyState.Down };
                                            break;
                                        case "up":
                                            states = new MacroKeyState[] { MacroKeyState.Up };
                                            break;
                                    }
                                    if (states == null)
                                    {
                                        Program.Log("Badly formatted macro " + (currentMacro == null ? "(null)" : currentMacro.Name) + " line(" + i + "): '" + line + "'");
                                        break;
                                    }
                                    for (int j = 0; j < states.Length; j++)
                                    {
                                        MacroKeyState state = states[j];
                                        for (int k = 0; k < keysSplitted.Length; k++)
                                        {
                                            string str = keysSplitted[k];
                                            Macro.Action action = new Macro.Action();
                                            action.State = state;
                                            if (j == states.Length - 1 && k == keysSplitted.Length - 1)
                                            {
                                                // Only set the delay on the last listed key for each macro line entry
                                                action.Delay = delay;
                                            }
                                            else
                                            {
                                                action.Delay = 0;
                                            }

                                            DriverValue value;
                                            if (TryParseDriverValue(str, out value))
                                            {
                                                switch (value)
                                                {
                                                    case DriverValue.LCtrl:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.LCtrl;
                                                        //action.KeyCode = (byte)DriverValueModifer.LCtrl;
                                                        break;
                                                    case DriverValue.LShift:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.LShift;
                                                        //action.KeyCode = (byte)DriverValueModifer.LShift;
                                                        break;
                                                    case DriverValue.LAlt:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.LAlt;
                                                        //action.KeyCode = (byte)DriverValueModifer.LAlt;
                                                        break;
                                                    case DriverValue.LWin:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.LWin;
                                                        //action.KeyCode = (byte)DriverValueModifer.LWin;
                                                        break;
                                                    case DriverValue.RCtrl:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.RCtrl;
                                                        //action.KeyCode = (byte)DriverValueModifer.RCtrl;
                                                        break;
                                                    case DriverValue.RShift:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.RShift;
                                                        //action.KeyCode = (byte)DriverValueModifer.RShift;
                                                        break;
                                                    case DriverValue.RAlt:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.RAlt;
                                                        //action.KeyCode = (byte)DriverValueModifer.RAlt;
                                                        break;
                                                    case DriverValue.RWin:
                                                        action.Type = MacroKeyType.Key;
                                                        action.Modifier = DriverValueModifer.RWin;
                                                        //action.KeyCode = (byte)DriverValueModifer.RWin;
                                                        break;
                                                    case DriverValue.MouseLClick:
                                                        action.Type = MacroKeyType.Mouse;
                                                        action.KeyCode = (byte)DriverValueMouseButton.LButton;
                                                        break;
                                                    case DriverValue.MouseRClick:
                                                        action.Type = MacroKeyType.Mouse;
                                                        action.KeyCode = (byte)DriverValueMouseButton.RButton;
                                                        break;
                                                    case DriverValue.MouseMClick:
                                                        action.Type = MacroKeyType.Mouse;
                                                        action.KeyCode = (byte)DriverValueMouseButton.MButton;
                                                        break;
                                                    case DriverValue.MouseBack:
                                                        action.Type = MacroKeyType.Mouse;
                                                        action.KeyCode = (byte)DriverValueMouseButton.Back;
                                                        break;
                                                    case DriverValue.MouseAdvance:
                                                        action.Type = MacroKeyType.Mouse;
                                                        action.KeyCode = (byte)DriverValueMouseButton.Advance;
                                                        break;
                                                    default:
                                                        action.Type = MacroKeyType.Key;
                                                        action.KeyCode = (byte)KeyValues.GetShortDriverValue((uint)value);
                                                        break;
                                                }
                                            }

                                            currentMacro.Actions.Add(action);
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }
    }
}
