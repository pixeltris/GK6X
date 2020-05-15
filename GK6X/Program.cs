using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GK6X
{
    class Program
    {
        public static string BasePath;
        public static string DataBasePath = "Data";
        public static string UserDataPath = "UserData";
        
        static void Main(string[] args)
        {
            BasePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            DataBasePath = Path.Combine(BasePath, DataBasePath);
            UserDataPath = Path.Combine(BasePath, UserDataPath);

            if (!Localization.Load())
            {
                LogFatalError("Failed to load localization data");
                return;
            }
            if (!KeyValues.Load())
            {
                LogFatalError("Failed to load the key data");
                return;
            }
            if (!KeyboardState.Load())
            {
                LogFatalError("Failed to load keyboard data");
                return;
            }

            if (args.Length > 0 && args[0].ToLower() == "clog")
            {
                CommandLogger.Run();
                return;
            }

            KeyboardDeviceManager.Connected += (KeyboardDevice device) =>
            {
                Log("Connected to device '" + device.State.ModelName + "' model:" + device.State.ModelId +
                    " fw:" + device.State.FirmwareVersion);
                WebGUI.UpdateDeviceList();
                
                string file = GetUserDataFile(device);
                if (!string.IsNullOrEmpty(file))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(file);
                        if (!Directory.Exists(dir))

                        {
                            Directory.CreateDirectory(dir);
                        }
                        if (!File.Exists(file))
                        {
                            File.WriteAllText(file, string.Empty, Encoding.UTF8);
                        }
                    }
                    catch
                    {
                    }
                }
            };
            KeyboardDeviceManager.Disconnected += (KeyboardDevice device) =>
            {
                Log("Disconnected from device '" + device.State.ModelName + "'");
                WebGUI.UpdateDeviceList();
            };
            KeyboardDeviceManager.StartListener();

            bool running = true;
            bool hasNullInput = false;
            while (running)
            {
                string line = Console.ReadLine();
                if (line == null)
                {
                    // Handler for potential issue where ReadLine() returns null - see https://github.com/pixeltris/GK6X/issues/8
                    if (hasNullInput)
                    {
                        Console.WriteLine("Cannot read from command line. Exiting.");
                        break;
                    }
                    hasNullInput = true;
                    continue;
                }
                hasNullInput = false;
                string[] splitted = line.Split();
                switch (splitted[0].ToLower())
                {
                    case "close":
                    case "exit":
                    case "quit":
                        running = false;
                        break;
                    case "cls":
                    case "clear":
                        Console.Clear();
                        break;
                    case "update_data":
                        if (splitted.Length > 1)
                        {
                            string path = line.TrimStart();
                            int spaceChar = path.IndexOf(' ');
                            if (spaceChar > 0)
                            {
                                path = path.Substring(spaceChar).Trim();
                            }
                            bool isValidPath = false;
                            try
                            {
                                if (Directory.Exists(path))
                                {
                                    isValidPath = true;
                                }
                            }
                            catch
                            {
                            }
                            if (isValidPath)
                            {
                                UpdateDataFiles(path);
                                Log("done");
                            }
                            else
                            {
                                Log("Couldn't find path '" + path + "'");
                            }
                        }
                        else
                        {
                            Log("Bad input. Expected folder name.");
                        }
                        break;
                    case "gui":
                        WebGUI.Run();
                        break;
                    case "findkeys":
                        {
                            Log(string.Empty);
                            Log("This is used to identify keys. Press keys to see their values. Missing keys will generally show up as '(null)' and they need to be mapped in the data files Data/devuces/YOUR_MODEL_ID/");
                            Log(string.Empty);
                            Log("This will enter the 'driver' layer and map all keys to callbacks. Continue? (y/n)");
                            Log(string.Empty);
                            string confirm = Console.ReadLine();
                            if (!string.IsNullOrEmpty(confirm))
                            {
                                switch (confirm.ToLower())
                                {
                                    case "y":
                                    case "ye":
                                    case "yes":
                                        Log("Entering driver mode...");
                                        KeyboardDevice[] devices;
                                        if (TryGetDevices(out devices))
                                        {
                                            foreach (KeyboardDevice device in devices)
                                            {
                                                device.SetLayer(KeyboardLayer.Driver);
                                                device.SetIdentifyDriverMacros();
                                            }
                                        }
                                        break;
                                    default:
                                        Log("Cancelled");
                                        break;
                                }
                            }
                        }
                        break;
                    case "map":
                    case "unmap":
                        {
                            bool map = splitted[0].ToLower() == "map";
                            KeyboardDevice[] devices;
                            KeyboardLayer targetLayer;
                            bool targetLayerIsFn;
                            TryParseLayer(splitted, 1, out targetLayer, out targetLayerIsFn);
                            bool hasTargetLayer = targetLayer != KeyboardLayer.Invalid;
                            if (TryGetDevices(out devices))
                            {
                                foreach (KeyboardDevice device in devices)
                                {
                                    UserDataFile userData = UserDataFile.Load(device.State, GetUserDataFile(device));
                                    if (userData == null)
                                    {
                                        Log("Couldn't find user data file '" + GetUserDataFile(device) + "'");
                                        continue;
                                    }

                                    foreach (KeyValuePair<KeyboardLayer, KeyboardStateLayer> layer in device.State.Layers)
                                    {
                                        if (layer.Key == KeyboardLayer.Driver)
                                        {
                                            continue;
                                        }

                                        if (hasTargetLayer && layer.Key != targetLayer)
                                        {
                                            continue;
                                        }

                                        device.SetLighting(layer.Key, userData);
                                        device.SetMacros(layer.Key, userData);

                                        for (int i = 0; i < 2; i++)
                                        {
                                            bool fn = i == 1;
                                            if (targetLayer != KeyboardLayer.Invalid && fn != targetLayerIsFn)
                                            {
                                                continue;
                                            }

                                            // Setting keys to 0xFFFFFFFF is preferable compared to using what is defined in 
                                            // files as this will use what is defined in the firmware.
                                            uint[] driverValues = new uint[device.State.MaxLogicCode];
                                            for (int j = 0; j < driverValues.Length; j++)
                                            {
                                                driverValues[j] = KeyValues.UnusedKeyValue;
                                            }

                                            if (map)
                                            {
                                                UserDataFile.Layer userDataLayer;
                                                if (fn)
                                                {
                                                    userData.FnLayers.TryGetValue(layer.Key, out userDataLayer);
                                                }
                                                else
                                                {
                                                    userData.Layers.TryGetValue(layer.Key, out userDataLayer);
                                                }
                                                if (userDataLayer != null)
                                                {
                                                    for (int j = 0; j < driverValues.Length; j++)
                                                    {
                                                        KeyboardState.Key key = device.State.GetKeyByLogicCode(j);
                                                        if (key != null)
                                                        {
                                                            driverValues[j] = userDataLayer.GetKey(key);
                                                        }
                                                    }
                                                }
                                            }
                                            device.SetKeys(layer.Key, driverValues, fn);
                                        }
                                    }

                                    // This is required to "refresh" the keyboard with the updated key info
                                    if (hasTargetLayer)
                                    {
                                        device.SetLayer(targetLayer);
                                    }
                                    else
                                    {
                                        device.SetLayer(KeyboardLayer.Base);
                                    }
                                    Log("Done");
                                }
                            }
                        }
                        break;
                    case "dumpkeys":
                        {
                            int targetRow = -1;
                            if (splitted.Length > 1)
                            {
                                if (!int.TryParse(splitted[1], out targetRow))
                                {
                                    targetRow = -1;
                                }
                            }
                            bool showLocationCodeInfo = false;
                            if (splitted.Length > 2)
                            {
                                showLocationCodeInfo = splitted[2] == "ex";
                            }

                            KeyboardDevice[] devices;
                            if (TryGetDevices(out devices))
                            {
                                foreach (KeyboardDevice device in devices)
                                {
                                    Log("====== " + device.State.ModelId + " ======");
                                    bool foundKey = false;
                                    int lastLeft = int.MinValue;
                                    int row = 1;
                                    foreach (KeyboardState.Key key in device.State.KeysByLocationCode.Values.OrderBy(
                                        x => x.Position.Top).ThenBy(x => x.Position.Left))
                                    {
                                        if (key.Position.Left >= 0)
                                        {
                                            if (lastLeft > key.Position.Left && foundKey)
                                            {
                                                if (targetRow == -1)
                                                {
                                                    Log("--------");
                                                }
                                                foundKey = false;
                                                row++;
                                            }
                                            lastLeft = key.Position.Left;
                                        }

                                        if (string.IsNullOrEmpty(key.KeyName) || !key.KeyName.StartsWith("LED-"))
                                        {
                                            if (targetRow == -1 || row == targetRow)
                                            {
                                                Log(key.KeyName + " = " + key.DriverValueName +
                                                    (showLocationCodeInfo ? " (" + key.LocationCode + ")" : string.Empty));
                                            }
                                            foundKey = true;
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }
            }

            KeyboardDeviceManager.StopListener();
        }

        private static string GetUserDataFile(KeyboardDevice device)
        {
            return Path.Combine(UserDataPath, device.State.ModelId + ".txt");
        }

        private static bool TryGetDevices(out KeyboardDevice[] devices)
        {
            devices = KeyboardDeviceManager.GetConnectedDevices();
            if (devices.Length > 0)
            {
                return true;
            }
            else
            {
                Log("No devices connected!");
                return false;
            }
        }

        private static bool TryParseLayer(string[] args, int index, out KeyboardLayer layer, out bool fn)
        {
            layer = KeyboardLayer.Invalid;
            fn = false;
            if (args.Length < index)
            {
                string arg = args[index];
                if (arg.ToLower().StartsWith("fn"))
                {
                    arg = arg.Substring(2);
                    fn = true;
                }
                int layerVal;
                if (int.TryParse(args[index], out layerVal))
                {
                    switch (layerVal)
                    {
                        case 1: layer = KeyboardLayer.Layer1; break;
                        case 2: layer = KeyboardLayer.Layer2; break;
                        case 3: layer = KeyboardLayer.Layer3; break;
                    }
                }
                else
                {
                    Enum.TryParse(args[index], true, out layer);
                }
            }
            switch (layer)
            {
                case KeyboardLayer.Driver:
                    layer = KeyboardLayer.Invalid;
                    break;
            }
            return layer != KeyboardLayer.Invalid;
        }

        static object logLocker = new object();
        internal static void Log(string str)
        {
            lock (logLocker)
            {
                File.AppendAllText(Path.Combine(BasePath, "KbLog.txt"), "[" + DateTime.Now.TimeOfDay + "] " + str + Environment.NewLine);
                Console.WriteLine(str);
            }
        }

        private static void LogFatalError(string str)
        {
            Log(str);
            Console.ReadLine();
            Environment.Exit(1);
        }

        private static void UpdateDataFiles(string srcDir)
        {
            string rootDir = Path.Combine(srcDir, "GK6XPlus Driver");
            if (Directory.Exists(rootDir))
            {
                srcDir = rootDir;
            }
            string engineDir = Path.Combine(srcDir, "CMSEngine");
            if (Directory.Exists(engineDir))
            {
                srcDir = engineDir;
            }
            string driverDir = Path.Combine(srcDir, "driver");
            if (Directory.Exists(driverDir))
            {
                srcDir = driverDir;
            }

            string dstDir = Path.Combine(Program.BasePath, "Data");
            string leDir = Path.Combine(srcDir, "res", "data", "le");
            string deviceDir = Path.Combine(srcDir, "device");

            // Format these files manually (https://beautifier.io/)
            string indexJsFile = Path.Combine(srcDir, "index.formatted.js");
            string zeroJsFile = Path.Combine(srcDir, "0.formatted.js");
            if (!File.Exists(indexJsFile) || !File.Exists(zeroJsFile))
            {
                Log("Couldn't find formatted js files to process!");
                return;
            }

            if (File.Exists(indexJsFile) && File.Exists(zeroJsFile) && Directory.Exists(leDir) && Directory.Exists(deviceDir))
            {
                CMFile.DumpLighting(leDir, Path.Combine(dstDir, "lighting"));
                CopyFilesRecursively(new DirectoryInfo(deviceDir), new DirectoryInfo(Path.Combine(dstDir, "device")));

                string langDir = Path.Combine(dstDir, "i18n", "langs");
                Directory.CreateDirectory(langDir);

                string indexJs = File.ReadAllText(indexJsFile);
                int commonIndex = 0;
                for (int i = 0; i < 2; i++)
                {
                    string langStr = FindContent(indexJs, "common: {", '{', '}', ref commonIndex);
                    if (!string.IsNullOrEmpty(langStr))
                    {
                        File.WriteAllText(Path.Combine(langDir, (i == 0 ? "en" : "zh") + ".json"), langStr);
                    }
                }

                string zeroJs = File.ReadAllText(zeroJsFile);
                string keysStr = FindContent(zeroJs, "el-icon-kb-keyboard", '[', ']');
                if (!string.IsNullOrEmpty(keysStr))
                {
                    File.WriteAllText(Path.Combine(dstDir, "keys.json"), keysStr);
                }
            }
        }

        static string FindContent(string str, string header, char openBraceChar, char closeBraceChar)
        {
            int index = 0;
            return FindContent(str, header, openBraceChar, closeBraceChar, ref index);
        }

        static string FindContent(string str, string header, char openBraceChar, char closeBraceChar, ref int index)
        {
            int braceCount = 0;
            index = str.IndexOf(header, index);
            if (index > 0)
            {
                while (str[index] != openBraceChar)
                {
                    index--;
                }
                int commonEndIndex = -1;
                for (int j = index; j < str.Length; j++)
                {
                    if (str[j] == openBraceChar)
                    {
                        braceCount++;
                    }
                    else if (str[j] == closeBraceChar)
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            commonEndIndex = j + 1;
                            break;
                        }
                    }
                }
                if (commonEndIndex > 0)
                {
                    string result = CleanJson(str.Substring(index, commonEndIndex - index));
                    index = commonEndIndex;
                    return result;
                }
            }
            return null;
        }

        private static string CleanJson(string json)
        {
            string[] lines = json.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder result = new StringBuilder();
            string indentString = "    ";
            int indent = 0;
            char[] braceChars = { '{', '}', '[', ']' };
            for (int j = 0; j < lines.Length; j++)
            {
                string line = lines[j].TrimStart();
                line = line.Replace("!0", "true");
                if (line.Length > 0 && !braceChars.Contains(line[0]))
                {
                    line = "\"" + line;
                    line = line.Insert(line.IndexOf(':'), "\"");
                }
                if (line.Contains("}"))
                {
                    indent--;
                }
                line = String.Concat(Enumerable.Repeat(indentString, indent)) + line;
                if (line.Contains("{"))
                {
                    indent++;
                }
                result.AppendLine(line);
            }
            return result.ToString();
        }

        private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            string[] extensions = { "json", "js" };
            foreach (DirectoryInfo dir in source.GetDirectories())
            {
                // "res" folder contains some data we don't want
                if (dir.Name != "res")
                {
                    // Remove the special case folder (TODO: Make this more generic)
                    CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name.Replace("(风控)", string.Empty)));
                }
            }
            foreach (FileInfo file in source.GetFiles())
            {
                if (extensions.Contains(file.Extension.ToLower().TrimStart(new char[] { '.' })))
                {
                    file.CopyTo(Path.Combine(target.FullName, file.Name), true);
                }
            }
            if (target.GetFiles().Length == 0)
            {
                target.Delete();
            }
        }
    }
}
