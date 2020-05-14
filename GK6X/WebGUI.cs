using MiniJSON;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace GK6X
{
    class WebGUI
    {
        public const int Port = 6464;
        static WebServer server = new WebServer(Port);

        public static void Run()
        {
            string url = "http://localhost:" + Port;
            if (!server.IsRunning)
            {
                server.Start();
                Program.Log("Started web GUI server at " + url);
            }
            if (server.IsRunning)
            {
                Process.Start(url);
                //Process.Start("chrome", "--incognito " + url);
            }
        }

        public static void UpdateDeviceList()
        {
            server.UpdateDeviceList();
        }

        class WebServer
        {
            private Thread thread;
            private HttpListener listener;
            private string dataPath;
            private string userDataPath;

            public int Port { get; set; }
            public bool IsRunning
            {
                get { return listener != null; }
            }

            Dictionary<string, Session> sessions = new Dictionary<string, Session>();
            DateTime lastSessionCleanup;
            TimeSpan sessionCleanupDelay = TimeSpan.FromSeconds(30);
            TimeSpan sessionPingTimeout = TimeSpan.FromSeconds(10);

            class Session
            {
                public string Token;
                public DateTime LastAccess;
                public Queue<Dictionary<string, object>> MessageQueue = new Queue<Dictionary<string, object>>();

                public void Enqueue(string functionName, string data)
                {
                    lock (MessageQueue)
                    {
                        Dictionary<string, object> message = new Dictionary<string, object>();
                        message["funcName"] = functionName;
                        message["data"] = data;
                        MessageQueue.Enqueue(message);
                    }
                }

                public void UpdateDeviceList()
                {
                    List<object> modelInfos = new List<object>();
                    foreach (KeyboardDevice device in KeyboardDeviceManager.GetConnectedDevices())
                    {
                        Dictionary<string, object> modelInfo = new Dictionary<string, object>();
                        modelInfo["ModelID"] = device.State.ModelId;
                        modelInfos.Add(modelInfo);
                    }
                    Enqueue("onDeviceListChanged", Json.Serialize(modelInfos));
                }
            }

            public WebServer(int port)
            {
                Port = port;
            }

            public void UpdateDeviceList()
            {
                if (!IsRunning)
                {
                    return;
                }
                lock (sessions)
                {
                    foreach (Session session in sessions.Values)
                    {
                        session.UpdateDeviceList();
                    }
                }
            }

            private void LazyCreateAccount(int accountId)
            {
                string accountDir = Path.Combine(userDataPath, "Account", accountId.ToString());
                if (!Directory.Exists(accountDir))
                {
                    Directory.CreateDirectory(accountDir);

                    string devicesDir = Path.Combine(accountDir, "Devices");
                    string leDir = Path.Combine(accountDir, "LE");
                    string macroDir = Path.Combine(accountDir, "Macro");

                    Directory.CreateDirectory(devicesDir);
                    Directory.CreateDirectory(leDir);
                    Directory.CreateDirectory(macroDir);

                    foreach (string file in Directory.GetFiles(Path.Combine(dataPath, "res", "data", "macro"), "*.cms"))
                    {
                        File.Copy(file, Path.Combine(macroDir, Path.GetFileName(file)), true);
                    }
                    File.Copy(Path.Combine(dataPath, "res", "data", "macro", "macrolist_en.json"), Path.Combine(macroDir, "macrolist.json"), true);

                    foreach (string file in Directory.GetFiles(Path.Combine(dataPath, "res", "data", "le"), "*.le"))
                    {
                        File.Copy(file, Path.Combine(leDir, Path.GetFileName(file)), true);
                    }
                    File.Copy(Path.Combine(dataPath, "res", "data", "le", "lelist_en.json"), Path.Combine(leDir, "lelist.json"), true);

                    foreach (string dir in Directory.GetDirectories(Path.Combine(dataPath, "device")))
                    {
                        string profilePath = Path.Combine(dir, "data", "profile.json");
                        if (File.Exists(profilePath))
                        {
                            string targetDir = Path.Combine(devicesDir, new DirectoryInfo(dir).Name);
                            Directory.CreateDirectory(targetDir);

                            foreach (string file in Directory.GetFiles(Path.Combine(dir, "data"), "*.json"))
                            {
                                try
                                {
                                    string guid = Guid.NewGuid().ToString().ToUpper();
                                    Dictionary<string, object> json = Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                                    json["GUID"] = guid;
                                    string str = Json.Serialize(json);
                                    File.WriteAllBytes(Path.Combine(targetDir, guid + ".cmf"), CMFile.Encrypt(Encoding.UTF8.GetBytes(str), CMFileType.Profile));
                                }
                                catch
                                {
                                }
                            }
                        }
                    }

                    // We need to add these model ids as otherwise it fails to pick up models correctly
                    Dictionary<string, object> defaultConfig = new Dictionary<string, object>();
                    {
                        Dictionary<string, object> userInit = new Dictionary<string, object>();
                        userInit["LE"] = true;
                        userInit["Macro"] = true;
                        defaultConfig["UserInit"] = userInit;
                    }
                    {
                        Dictionary<string, object> modelInit = new Dictionary<string, object>();
                        foreach (string dir in Directory.GetDirectories(Path.Combine(dataPath, "device")))
                        {
                            string profilePath = Path.Combine(dir, "data", "profile.json");
                            if (File.Exists(profilePath))
                            {
                                Dictionary<string, object> modelInfo = new Dictionary<string, object>();
                                modelInfo["Macro"] = true;
                                modelInfo["LE"] = true;
                                modelInfo["Mode"] = 1;
                                modelInit[new DirectoryInfo(dir).Name] = modelInfo;
                            }
                        }
                        defaultConfig["ModelInit"] = modelInit;
                    }
                    File.WriteAllText(Path.Combine(accountDir, "Config.json"), Json.Serialize(defaultConfig));
                }
            }

            public void Start()
            {
                dataPath = Program.BasePath;
                string rootDir = Path.Combine(dataPath, "GK6XPlus Driver");
                if (Directory.Exists(rootDir))
                {
                    dataPath = rootDir;
                }
                string engineDir = Path.Combine(dataPath, "CMSEngine");
                if (Directory.Exists(engineDir))
                {
                    dataPath = engineDir;
                }
                string driverDir = Path.Combine(dataPath, "driver");
                if (Directory.Exists(driverDir))
                {
                    dataPath = driverDir;
                }

                if (dataPath == Program.BasePath)
                {
                    Program.Log("Couldn't find data path");
                    return;
                }
                else if (!Directory.Exists(dataPath))
                {
                    Program.Log("Couldn't find data path '" + dataPath + "'");
                    return;
                }
                userDataPath = Path.Combine(dataPath, "UserData");
                if (!Directory.Exists(userDataPath))
                {
                    Directory.CreateDirectory(userDataPath);
                }

                Stop();

                thread = new Thread(delegate()
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://localhost:" + Port + "/");// localhost only (as we don't have enough sanitization here...)
                    listener.Start();
                    while (listener != null)
                    {
                        try
                        {
                            HttpListenerContext context = listener.GetContext();
                            Process(context);
                        }
                        catch
                        {
                        }

                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            public void Stop()
            {
                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }
                    listener = null;
                }

                if (thread != null)
                {
                    try
                    {
                        thread.Abort();
                    }
                    catch
                    {
                    }
                    thread = null;
                }
            }

            private void Process(HttpListenerContext context)
            {
                if (DateTime.Now - sessionCleanupDelay > lastSessionCleanup)
                {
                    lastSessionCleanup = DateTime.Now;
                    lock (sessions)
                    {
                        foreach (KeyValuePair<string, Session> session in new Dictionary<string, Session>(sessions))
                        {
                            if (DateTime.Now - sessionPingTimeout > session.Value.LastAccess)
                            {
                                sessions.Remove(session.Key);
                            }
                        }
                    }
                }

                try
                {
                    string url = context.Request.Url.OriginalString;

                    byte[] responseBuffer = null;
                    string response = string.Empty;
                    string contentType = "text/html";

                    // This is for requests which don't need a response (used mostly for our crappy index.html error handler...)
                    const string successResponse = "OK!";

                    if (context.Request.Url.AbsolutePath == "/" || context.Request.Url.AbsolutePath.ToLower() == "/index.html")
                    {
                        string indexFile = Path.Combine(dataPath, "index.html");
                        if (File.Exists(indexFile))
                        {
                            string injectedJs = File.ReadAllText(Path.Combine(Program.DataBasePath, "WebGUI.js"));
                            injectedJs = injectedJs.Replace("UNIQUE_TOKEN_GOES_HERE", Guid.NewGuid().ToString());

                            response = File.ReadAllText(indexFile);
                            response = response.Insert(response.IndexOf("<script"), "<script>" + injectedJs + "</script>");
                        }
                    }
                    else if (context.Request.Url.AbsolutePath.StartsWith("/cms_"))
                    {
                        string postData = new StreamReader(context.Request.InputStream).ReadToEnd();
                        Dictionary<string, object> json = Json.Deserialize(postData) as Dictionary<string, object>;
                        string token = json["token"].ToString();
                        Session session;
                        if (!sessions.TryGetValue(token, out session))
                        {
                            session = new Session();
                            session.Token = token;
                            sessions[session.Token] = session;
                        }
                        session.LastAccess = DateTime.Now;
                        switch (json["requestType"].ToString())
                        {
                            case "ping":
                                {
                                    lock (session.MessageQueue)
                                    {
                                        List<object> messages = new List<object>();
                                        while (session.MessageQueue.Count > 0)
                                        {
                                            messages.Add(session.MessageQueue.Dequeue());
                                        }
                                        response = Json.Serialize(messages);
                                    }
                                }
                                break;
                            case "callFunc":
                                {
                                    Dictionary<string, object> request = Json.Deserialize((string)json["request"]) as Dictionary<string, object>;
                                    switch (request["funcname"].ToString())
                                    {
                                        case "GetDeviceList":
                                            {
                                                session.UpdateDeviceList();
                                                response = successResponse;
                                            }
                                            break;
                                        case "ChangeMode":
                                            {
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                int modeIndex = (int)Convert.ChangeType(request["ModeIndex"], typeof(int));
                                                foreach (KeyboardDevice device in KeyboardDeviceManager.GetConnectedDevices())
                                                {
                                                    if (device.State.ModelId == modelId)
                                                    {
                                                        device.SetLayer((KeyboardLayer)modeIndex);
                                                    }
                                                }
                                                response = successResponse;
                                            }
                                            break;
                                        case "GetProfileList":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                LazyCreateAccount(accountId);
                                                string modelDir = Path.Combine(userDataPath, "Account", accountId.ToString(), "Devices", modelId.ToString());
                                                if (Directory.Exists(modelDir))
                                                {
                                                    List<object> objs = new List<object>();
                                                    Dictionary<int, object> modelsByIndex = new Dictionary<int, object>();
                                                    foreach (string file in Directory.GetFiles(modelDir, "*.cmf"))
                                                    {
                                                        object obj = Json.Deserialize(Encoding.UTF8.GetString(CMFile.Load(file)));
                                                        Dictionary<string, object> profile = obj as Dictionary<string, object>;
                                                        modelsByIndex[(int)Convert.ChangeType(profile["ModeIndex"], typeof(int))] = obj;
                                                    }
                                                    foreach (KeyValuePair<int, object> obj in modelsByIndex.OrderBy(x => x.Key))
                                                    {
                                                        objs.Add(obj.Value);
                                                    }
                                                    response = Json.Serialize(objs);
                                                }
                                            }
                                            break;
                                        case "ReadProfile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Devices", modelId.ToString(), guid + ".cmf");
                                                if (File.Exists(file))
                                                {
                                                    response = Encoding.UTF8.GetString(CMFile.Load(file));
                                                }
                                            }
                                            break;
                                        case "WriteProfile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                string guid = (string)request["GUID"];
                                                string data = (string)request["Data"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Devices", modelId.ToString(), guid + ".cmf");
                                                if (File.Exists(file))
                                                {
                                                    File.WriteAllBytes(file, CMFile.Encrypt(Encoding.UTF8.GetBytes(data), CMFileType.Profile));
                                                    response = successResponse;
                                                }
                                            }
                                            break;
                                        case "DeleteProfile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Devices", modelId.ToString(), guid + ".cmf");
                                                if (File.Exists(file))
                                                {
                                                    try
                                                    {
                                                        File.Delete(file);
                                                        response = successResponse;
                                                    }
                                                    catch
                                                    {
                                                    }
                                                }
                                            }
                                            break;
                                        case "ApplyConfig":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                long modelId = (long)Convert.ChangeType(request["ModelID"], typeof(long));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Devices", modelId.ToString(), guid + ".cmf");
                                                if (File.Exists(file))
                                                {
                                                    string config = Encoding.UTF8.GetString(CMFile.Load(file));
                                                    Dictionary<string, object> data = Json.Deserialize(config) as Dictionary<string, object>;
                                                    int modelIndex = (int)Convert.ChangeType(data["ModeIndex"], typeof(int));
                                                    KeyboardLayer layer = (KeyboardLayer)modelIndex;

                                                    foreach (KeyboardDevice device in KeyboardDeviceManager.GetConnectedDevices())
                                                    {
                                                        if (device.State.ModelId == modelId)
                                                        {
                                                            Dictionary<int, UserDataFile.Macro> macrosById = new Dictionary<int, UserDataFile.Macro>();
                                                            UserDataFile macrosUserDataFile = new UserDataFile();

                                                            //////////////////////////////////////////
                                                            // Keys
                                                            //////////////////////////////////////////
                                                            for (int i = 0; i < 2; i++)
                                                            {
                                                                string setStr = i == 0 ? "KeySet" : "FnKeySet";
                                                                if (data.ContainsKey(setStr))
                                                                {
                                                                    uint[] driverValues = new uint[device.State.MaxLogicCode];
                                                                    for (int j = 0; j < driverValues.Length; j++)
                                                                    {
                                                                        driverValues[j] = KeyValues.UnusedKeyValue;
                                                                    }
                                                                    List<object> keys = data[setStr] as List<object>;
                                                                    foreach (object keyObj in keys)
                                                                    {
                                                                        Dictionary<string, object> key = keyObj as Dictionary<string, object>;
                                                                        int keyIndex = (int)Convert.ChangeType(key["Index"], typeof(int));
                                                                        uint driverValue = KeyValues.UnusedKeyValue;
                                                                        string driverValueStr = (string)key["DriverValue"];
                                                                        if (driverValueStr.StartsWith("0x"))
                                                                        {
                                                                            if (uint.TryParse(driverValueStr.Substring(2), NumberStyles.HexNumber, null, out driverValue))
                                                                            {
                                                                                if (KeyValues.GetKeyType(driverValue) == DriverValueType.Macro && key.ContainsKey("Task"))
                                                                                {
                                                                                    Dictionary<string, object> task = key["Task"] as Dictionary<string, object>;
                                                                                    if (task != null && (string)task["Type"] == "Macro")
                                                                                    {
                                                                                        Dictionary<string, object> taskData = task["Data"] as Dictionary<string, object>;
                                                                                        string macroGuid = (string)taskData["GUID"];
                                                                                        string macroFile = Path.Combine(userDataPath, "Account", accountId.ToString(), "Macro", macroGuid + ".cms");
                                                                                        if (File.Exists(macroFile))
                                                                                        {
                                                                                            UserDataFile.Macro macro = new UserDataFile.Macro(null);
                                                                                            macro.LoadFile(macroFile);
                                                                                            macro.RepeatCount = (byte)Convert.ChangeType(taskData["Repeats"], typeof(byte));
                                                                                            macro.RepeatType = (MacroRepeatType)(byte)Convert.ChangeType(taskData["StopMode"], typeof(byte));
                                                                                            macro.Id = KeyValues.GetKeyData2(driverValue);
                                                                                            macrosById[macro.Id] = macro;
                                                                                            macrosUserDataFile.Macros[macroGuid] = macro;
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                driverValue = KeyValues.UnusedKeyValue;
                                                                            }
                                                                        }
                                                                        if (keyIndex >= 0)
                                                                        {
                                                                            Debug.WriteLine(device.State.KeysByLogicCode[keyIndex].KeyName + " = " + (DriverValue)driverValue);
                                                                            driverValues[keyIndex] = driverValue;
                                                                        }
                                                                    }
                                                                    device.SetKeys(layer, driverValues, i == 1);
                                                                }
                                                            }

                                                            //////////////////////////////////////////
                                                            // Lighting
                                                            //////////////////////////////////////////
                                                            UserDataFile userDataFile = new UserDataFile();
                                                            Dictionary<string, UserDataFile.LightingEffect> effects = new Dictionary<string, UserDataFile.LightingEffect>();
                                                            string[] leHeaders = { "ModeLE", "DriverLE" };
                                                            foreach (string leHeader in leHeaders)
                                                            {
                                                                if (data.ContainsKey(leHeader))
                                                                {
                                                                    List<object> leEntries = data[leHeader] as List<object>;
                                                                    if (leEntries == null)
                                                                    {
                                                                        // There's only one ModeLE
                                                                        leEntries = new List<object>();
                                                                        leEntries.Add(data[leHeader]);
                                                                    }
                                                                    foreach (object entry in leEntries)
                                                                    {
                                                                        Dictionary<string, object> modeLE = entry as Dictionary<string, object>;
                                                                        string leGuid = (string)modeLE["GUID"];
                                                                        string filePath = Path.Combine(userDataPath, "Account", accountId.ToString(), "LE", leGuid + ".le");
                                                                        if (!effects.ContainsKey(leGuid) && File.Exists(filePath))
                                                                        {
                                                                            UserDataFile.LightingEffect le = new UserDataFile.LightingEffect(userDataFile, null);
                                                                            le.Load(device.State, Encoding.UTF8.GetString(CMFile.Load(filePath)));
                                                                            le.Layers.Add(layer);
                                                                            userDataFile.LightingEffects[leGuid] = le;
                                                                            effects[leGuid] = le;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            device.SetLighting(layer, userDataFile);

                                                            //////////////////////////////////////////
                                                            // Macros
                                                            //////////////////////////////////////////
                                                            device.SetMacros(layer, macrosUserDataFile);

                                                            device.SetLayer(layer);
                                                        }
                                                    }

                                                    session.Enqueue("onApplyResult", "{\"result\":1}");
                                                    response = successResponse;
                                                }
                                                else
                                                {
                                                    session.Enqueue("onApplyResult", "{\"result\":0}");
                                                }
                                            }
                                            break;
                                        case "ReadFile":
                                            {
                                                int type = (int)Convert.ChangeType(request["Type"], typeof(int));
                                                string path = (string)request["Path"];
                                                string basePath = null;
                                                switch (type)
                                                {
                                                    case 0:// Data
                                                        basePath = dataPath;
                                                        break;
                                                    case 1:// User data
                                                        basePath = userDataPath;
                                                        if (path.StartsWith("Account/"))
                                                        {
                                                            LazyCreateAccount(int.Parse(path.Split('/')[1]));
                                                        }
                                                        break;
                                                    default:
                                                        Program.Log("Unhandled ReadFile type " + type);
                                                        break;
                                                }
                                                if (!string.IsNullOrEmpty(basePath))
                                                {
                                                    string fullPath = Path.Combine(basePath, path);
                                                    //Program.Log("ReadFile: " + fullPath);
                                                    if (File.Exists(fullPath) && IsFileInDirectoryOrSubDirectory(fullPath, dataPath))
                                                    {
                                                        response = File.ReadAllText(fullPath);
                                                    }
                                                }
                                            }
                                            break;
                                        case "WriteFile":
                                            {
                                                int type = (int)Convert.ChangeType(request["Type"], typeof(int));
                                                string path = (string)request["Path"];
                                                string data = (string)request["Data"];
                                                string basePath = null;
                                                switch (type)
                                                {
                                                    case 0:// Data
                                                        basePath = dataPath;
                                                        break;
                                                    case 1:// User data
                                                        basePath = userDataPath;
                                                        if (path.StartsWith("Account/"))
                                                        {
                                                            LazyCreateAccount(int.Parse(path.Split('/')[1]));
                                                        }
                                                        break;
                                                    default:
                                                        Program.Log("Unhandled WriteFile type " + type);
                                                        break;
                                                }
                                                if (!string.IsNullOrEmpty(basePath))
                                                {
                                                    string fullPath = Path.Combine(basePath, path);
                                                    Program.Log("WriteFile: " + fullPath);
                                                    if (IsFileInDirectoryOrSubDirectory(fullPath, dataPath))
                                                    {
                                                        string dir = Path.GetDirectoryName(fullPath);
                                                        if (!Directory.Exists(dir))
                                                        {
                                                            Directory.CreateDirectory(dir);
                                                        }
                                                        File.WriteAllText(fullPath, data);
                                                        response = successResponse;
                                                    }
                                                }
                                            }
                                            break;
                                        case "ReadLE":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "LE", guid + ".le");
                                                LazyCreateAccount(accountId);
                                                if (File.Exists(file))
                                                {
                                                    response = Encoding.UTF8.GetString(CMFile.Load(file));
                                                }
                                            }
                                            break;
                                        case "WriteLE":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "LE", guid + ".le");
                                                string data = (string)request["Data"];
                                                LazyCreateAccount(accountId);
                                                File.WriteAllBytes(file, CMFile.Encrypt(Encoding.UTF8.GetBytes(data), CMFileType.Light));
                                                response = successResponse;
                                            }
                                            break;
                                        case "DeleteLE":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "LE", guid + ".le");
                                                if (File.Exists(file))
                                                {
                                                    File.Delete(file);
                                                    response = successResponse;
                                                }
                                            }
                                            break;
                                        case "ReadMacrofile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Macro", guid + ".cms");
                                                if (File.Exists(file))
                                                {
                                                    UserDataFile.Macro macro = new UserDataFile.Macro(null);
                                                    if (macro.LoadFile(file))
                                                    {
                                                        Dictionary<string, object> macroJson = new Dictionary<string,object>();
                                                        macroJson["MacroName"] = macro.Name;
                                                        macroJson["GUID"] = macro.Guid;
                                                        List<object> taskList = new List<object>();
                                                        macroJson["TaskList"] = taskList;
                                                        foreach (UserDataFile.Macro.Action action in macro.Actions)
                                                        {
                                                            Dictionary<string, object> actionJson = new Dictionary<string,object>();
                                                            if (action.Type == MacroKeyType.Key)
                                                            {
                                                                switch (action.State)
                                                                {
                                                                    case MacroKeyState.Down:
                                                                        actionJson["taskName"] = "KeyDown";
                                                                        break;
                                                                    case MacroKeyState.Up:
                                                                        actionJson["taskName"] = "KeyUp";
                                                                        break;
                                                                }
                                                            }
                                                            else
                                                            {
                                                                string button = null;
                                                                switch ((DriverValueMouseButton)action.KeyCode)
                                                                {
                                                                    case DriverValueMouseButton.LButton:
                                                                        button = "Left";
                                                                        break;
                                                                    case DriverValueMouseButton.RButton:
                                                                        button = "Right";
                                                                        break;
                                                                }
                                                                if (!string.IsNullOrEmpty(button))
                                                                {
                                                                    switch (action.State)
                                                                    {
                                                                        case MacroKeyState.Down:
                                                                            button += "Down";
                                                                            break;
                                                                        case MacroKeyState.Up:
                                                                            button += "Up";
                                                                            break;
                                                                    }
                                                                }
                                                                actionJson["taskName"] = button;
                                                            }
                                                            actionJson["taskValue"] = action.ValueStr != null ? "\"" + action.ValueStr + "\"" : string.Empty;
                                                            taskList.Add(actionJson);
                                                            if (action.Delay > 0)
                                                            {
                                                                Dictionary<string, object> delayJson = new Dictionary<string, object>();
                                                                delayJson["taskName"] = "Delay";
                                                                delayJson["taskValue"] = action.Delay;
                                                                taskList.Add(delayJson);
                                                            }
                                                        }
                                                        response = Json.Serialize(macroJson);
                                                    }
                                                }
                                            }
                                            break;
                                        case "WriteMacrofile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Macro", guid + ".cms");
                                                string data = (string)request["Data"];

                                                Dictionary<string, object> macroJson = Json.Deserialize(data) as Dictionary<string, object>;

                                                StringBuilder macroStr = new StringBuilder();
                                                macroStr.AppendLine("[General]");
                                                macroStr.AppendLine("Name=" + macroJson["MacroName"]);
                                                macroStr.AppendLine("ScriptID=" + guid);
                                                macroStr.AppendLine("Repeats=1");
                                                macroStr.AppendLine("StopMode=1");
                                                macroStr.AppendLine();
                                                macroStr.AppendLine("[Script]");
                                                
                                                List<object> taskListJson = macroJson["TaskList"] as List<object>;
                                                foreach (object taskObj in taskListJson)
                                                {
                                                    Dictionary<string, object> task = taskObj as Dictionary<string, object>;
                                                    if (!string.IsNullOrEmpty(task["taskValue"].ToString()))
                                                    {
                                                        macroStr.AppendLine((string)task["taskName"] + " " + task["taskValue"].ToString());
                                                    }
                                                    else
                                                    {
                                                        macroStr.AppendLine((string)task["taskName"]);
                                                    }
                                                }

                                                if (!string.IsNullOrEmpty(guid))
                                                {
                                                    File.WriteAllBytes(file, CMFile.Encrypt(Encoding.UTF8.GetBytes(macroStr.ToString()), CMFileType.Macro));
                                                    response = successResponse;
                                                }
                                            }
                                            break;
                                        case "DeleteMacrofile":
                                            {
                                                int accountId = (int)Convert.ChangeType(request["AccoutID"], typeof(int));
                                                string guid = (string)request["GUID"];
                                                LazyCreateAccount(accountId);
                                                string file = Path.Combine(userDataPath, "Account", accountId.ToString(), "Macro", guid + ".cms");
                                                if (File.Exists(file))
                                                {
                                                    File.Delete(file);
                                                    response = successResponse;
                                                }
                                            }
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                    else
                    {
                        // This needs some sanitization...
                        string file = Path.Combine(dataPath, context.Request.Url.AbsolutePath.Substring(1));
                        if (File.Exists(file) && IsFileInDirectoryOrSubDirectory(file, dataPath))
                        {
                            string extension = Path.GetExtension(file).ToLower();
                            switch (extension)
                            {
                                case ".js":
                                    response = File.ReadAllText(file);
                                    contentType = "application/javascript";
                                    break;
                                case ".png":
                                    responseBuffer = File.ReadAllBytes(file);
                                    contentType = "image/png";
                                    break;
                                case ".jpg":
                                case ".jpeg":
                                    responseBuffer = File.ReadAllBytes(file);
                                    contentType = "image/jpeg";
                                    break;
                                case ".json":
                                    responseBuffer = File.ReadAllBytes(file);
                                    contentType = "application/json";
                                    break;
                                case ".cmsl":
                                case ".html":
                                    responseBuffer = File.ReadAllBytes(file);
                                    contentType = "text/html";
                                    break;
                                case ".css":
                                    responseBuffer = File.ReadAllBytes(file);
                                    contentType = "text/css";
                                    break;
                                default:
                                    Program.Log("Unhandled file type " + extension + " " + context.Request.Url.AbsolutePath);
                                    break;
                            }
                        }
                    }

                    if (responseBuffer == null && response != null)
                    {
                        responseBuffer = Encoding.UTF8.GetBytes(response.ToString());
                    }

                    context.Response.ContentType = contentType;
                    context.Response.ContentEncoding = Encoding.UTF8;
                    context.Response.ContentLength64 = responseBuffer.Length;
                    context.Response.OutputStream.Write(responseBuffer, 0, responseBuffer.Length);
                    context.Response.OutputStream.Flush();
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }
                catch
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }

                context.Response.OutputStream.Close();
            }

            bool IsFileInDirectoryOrSubDirectory(string filePath, string directory)
            {
                return IsSameOrSubDirectory(directory, Path.GetDirectoryName(filePath));
            }

            bool IsSameOrSubDirectory(string basePath, string path)
            {
                string subDirectory;
                return IsSameOrSubDirectory(basePath, path, out subDirectory);
            }

            bool IsSameOrSubDirectory(string basePath, string path, out string subDirectory)
            {
                DirectoryInfo di = new DirectoryInfo(Path.GetFullPath(path).TrimEnd('\\', '/'));
                DirectoryInfo diBase = new DirectoryInfo(Path.GetFullPath(basePath).TrimEnd('\\', '/'));

                subDirectory = null;
                while (di != null)
                {
                    if (di.FullName.Equals(diBase.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(subDirectory))
                        {
                            subDirectory = di.Name;
                        }
                        else
                        {
                            subDirectory = Path.Combine(di.Name, subDirectory);
                        }
                        di = di.Parent;
                    }
                }
                return false;
            }
        }
    }
}
