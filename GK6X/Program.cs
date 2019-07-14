using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GK6X
{
    class Program
    {
        public static readonly string DataBasePath = "Data";
        public static readonly string UserDataPath = "UserData";
        
        static void Main(string[] args)
        {
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
            };
            KeyboardDeviceManager.StartListener();

            bool running = true;
            while (running)
            {
                string line = Console.ReadLine();
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
                                                            driverValues[j] = userDataLayer.GetKey(key.DriverValue);
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
                                                Log(key.KeyName + " = " + (DriverValue)key.DriverValue +
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
                File.AppendAllText("KbLog.txt", "[" + DateTime.Now.TimeOfDay + "] " + str + Environment.NewLine);
                Console.WriteLine(str);
            }
        }

        private static void LogFatalError(string str)
        {
            Log(str);
            Console.ReadLine();
            Environment.Exit(1);
        }
    }
}
