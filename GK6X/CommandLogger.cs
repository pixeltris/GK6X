// Based on https://github.com/pixeltris/SonyAlphaUSB/blob/master/SonyAlphaUSB/WIALogger.cs
// Taken 30th June 2019
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GK6X
{
    // This is a mess, but it's just used for logging packets. It would nice to clean this up, but it probably isn't worth the effort.
    // TODO: At least seperate out the logger parts from the process launcher/injector?
    // NOTE: Lots of limitations here due to hooking functions the .NET Framework wants to use, causing lockups.
    // NOTE: The process launcher will often crash the target process, just keep relaunching it until it works. TODO: Look into the crash.
    internal unsafe class CommandLogger
    {
        const string targetProcessName = "CMS";
        const string targetProcessNameEx = targetProcessName + ".exe";
        const string someFileInTargetProcessFolder = "CGMEngine.dll";
        const string loaderDll = "GK6XLoggerLoader.dll";

        static bool showConsole = true;

        static byte keyboardBufferSizeA;
        static byte keyboardBufferSizeB;
        static uint keyboardFirmwareId;
        static byte keyboardFirmwareMinorVersion;
        static byte keyboardFirmwareMajorVersion;
        static KeyboardState keyboardState;
        static bool logLightingDIY = false;// Logs static RGB values set per key

        static IntPtr currentDeviceAddr = IntPtr.Zero;
        static IntPtr currentDevice
        {
            get
            {
                if (currentDeviceAddr != IntPtr.Zero)
                {
                    return *(IntPtr*)currentDeviceAddr;
                }
                return IntPtr.Zero;
            }
            set
            {
                if (currentDeviceAddr != IntPtr.Zero)
                {
                    *(IntPtr*)currentDeviceAddr = value;
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate bool WriteFileDelegate(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate bool ReadFileDelegate(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate bool GetOverlappedResultDelegate(IntPtr hFile, IntPtr lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        delegate IntPtr CreateFileWDelegate([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, int dwDesiredAccess, int dwShareMode, IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        static IntPtr writeFileHookPtr;
        static WriteFileDelegate writeFileHook;
        static IntPtr writeFileOriginalPtr;
        static WriteFileDelegate writeFileOriginal;

        static IntPtr readFileHookPtr;
        static ReadFileDelegate readFileHook;
        static IntPtr readFileOriginalPtr;
        static ReadFileDelegate readFileOriginal;
        // For working with ReadFile async
        static IntPtr lastReadFileOverlappedPtr;
        static IntPtr lastReadFileBufferPtr;

        static IntPtr getOverlappedResultHookPtr;
        static GetOverlappedResultDelegate getOverlappedResultHook;
        static IntPtr getOverlappedResultOriginalPtr;
        static GetOverlappedResultDelegate getOverlappedResultOriginal;

        static IntPtr kernel32_createFileW;
        static IntPtr createFileWHookPtr;
        static CreateFileWDelegate createFileWHook;
        static IntPtr createFileWOriginalPtr;
        static CreateFileWDelegate createFileWOriginal;
        // This is needed to avoid issues with recursion
        static int numSkipCreateFileCalls = 1;
        static int numSkippedCreateFileCalls = 0;

        class BufferedPacket
        {
            public ushort Opcode;
            public int LastOffset;
            public int CurrentOffset;
            public byte[] Data;
            public bool IsComplete;

            public BufferedPacket(Packet packet)
            {
                LastOffset = -1;
                Opcode = packet.Opcode;
                Append(packet);
            }

            public void Append(Packet packet)
            {
                if (packet.Opcode != Opcode)
                {
                    IsComplete = true;
                    return;
                }

                int offset;
                int length;
                byte[] buffer;
                if (TryGetBufferedInfo(packet, out offset, out length, out buffer, true))
                {
                    if (offset != CurrentOffset)
                    {
                        if (offset == LastOffset)
                        {
                            // Same data was sent twice.
                            return;
                        }
                        Log("Buffered packet offsets are invalid! packet: " + packet);
                        IsComplete = true;
                        return;
                    }
                    Append(buffer);
                    LastOffset = offset;
                    CurrentOffset = offset + length;
                }
            }

            private void Append(byte[] buffer)
            {
                if (Data == null)
                {
                    Data = new byte[buffer.Length];
                    Buffer.BlockCopy(buffer, 0, Data, 0, buffer.Length);
                }
                else
                {
                    byte[] temp = new byte[Data.Length + buffer.Length];
                    Buffer.BlockCopy(Data, 0, temp, 0, Data.Length);
                    Buffer.BlockCopy(buffer, 0, temp, Data.Length, buffer.Length);
                    Data = temp;
                }
            }

            public static bool IsBufferedPacket(Packet packet)
            {
                int offset, length;
                byte[] buffer;
                return TryGetBufferedInfo(packet, out offset, out length, out buffer, false);
            }

            private static bool TryGetBufferedInfo(Packet packet, out int offset, out int length, out byte[] buffer, bool getBuffer)
            {
                int tempIndex = packet.Index;
                bool isBufferedPacket = true;
                switch ((OpCodes)packet.Opcode1)
                {
                    case OpCodes.DriverLayerUpdateRealtimeLighting:
                        if (packet.Opcode2 == 2)
                        {
                            offset = 0;
                            length = 1;// No actual data, just a hack as this doesn't used buffered data
                        }
                        else
                        {
                            packet.Index = 2;
                            offset = packet.ReadByte() | packet.ReadByte() << 8 | packet.ReadByte() << 16;
                            packet.Index = 5;
                            length = packet.ReadByte();
                        }
                        break;
                    case OpCodes.LayerSetLightValues:
                    case OpCodes.DriverLayerSetKeyValues:
                        packet.Index = 2;
                        offset = packet.ReadByte() | packet.ReadByte() << 8 | packet.ReadByte() << 16;
                        packet.Index = 5;
                        length = packet.ReadByte();
                        break;
                    case OpCodes.LayerSetKeyPressLightingEffect:
                    case OpCodes.LayerSetKeyValues:
                    case OpCodes.LayerFnSetKeyValues:
                    case OpCodes.LayerSetMacros:
                        packet.Index = 2;
                        offset = packet.ReadByte() | packet.ReadByte() << 8;
                        packet.Index = 4;
                        length = packet.ReadByte();
                        break;
                    default:
                        length = -1;
                        offset = -1;
                        isBufferedPacket = false;
                        break;

                }
                if (isBufferedPacket && getBuffer)
                {
                    packet.Index = 8;
                    buffer = packet.ReadBytes(length);
                }
                else
                {
                    buffer = null;
                }
                packet.Index = tempIndex;
                return isBufferedPacket;
            }
        }

        static BufferedPacket bufferedPacket = null;
        static Queue<QueuedPacket> packetQueue = new Queue<QueuedPacket>();
        class QueuedPacket
        {
            public Packet Data;
            public bool IsSend;

            public QueuedPacket(Packet packet, bool isSend)
            {
                Data = packet;
                IsSend = isSend;
            }
        }

        static void ProcessSetKeys(Packet packet, bool fn)
        {
            for (int i = 0; i < keyboardState.MaxLogicCode; i++)
            {
                // NOTE: Some seems seem to be sent as -1 when they aren't assigned,  it can be assumed that
                //       if a key exists and it's value is -1, then the default key behaviour should be used.
                uint driverValue = packet.ReadUInt32();
                KeyboardState.Key key = keyboardState.GetKeyByLogicCode(i);
                if (key != null)
                {
                    DriverValueType type = KeyValues.GetKeyType(driverValue);
                    string additionalInfo = " (type:" + type;
                    if (type != DriverValueType.Macro && type != DriverValueType.TempSwitchLayer)
                    {
                        additionalInfo += " value:" + (DriverValue)driverValue;
                    }
                    switch (type)
                    {
                        case DriverValueType.Key:
                            if (!KeyValues.IsKeyModifier(driverValue))
                            {
                                DriverValueModifer modifiers = KeyValues.GetKeyModifier(driverValue);
                                if (modifiers != DriverValueModifer.None)
                                {
                                    additionalInfo += " modifiers:" + modifiers;
                                }
                            }
                            break;
                        case DriverValueType.Macro:
                            additionalInfo += " macroIndex:" + KeyValues.GetKeyData2(driverValue);
                            break;
                        case DriverValueType.Mouse:
                            additionalInfo += " button:" + KeyValues.GetMouseButton(driverValue);
                            break;
                    }
                    additionalInfo += ")";
                    /*Log(key.KeyName + " - " + key.DriverValue.ToString("X8") + " = " +
                        driverValue.ToString("X8") + additionalInfo);*/
                }
                else if (driverValue != KeyValues.UnusedKeyValue)
                {
                    Log("Not found " + i + " " + driverValue.ToString("X8"));
                }
            }
        }

        static void ProcessKeyPressLightingEffect(Packet packet)
        {
            // This uses a byte for each key, to denote which lighting effect to play
            if (packet.Length != keyboardState.MaxLogicCode)
            {
                Log("Bad number of keys when processing key press light data. Expected " + keyboardState.MaxLogicCode +
                    " bytes, found " + packet.Length);
            }

            // NOTE: The "driver" layer uses a callback id rather than using a lighting index ("driver" sends lighting realtime)
            for (int i = 0; i < keyboardState.MaxLogicCode; i++)
            {
                byte lightingEffectIndex = packet.ReadByte();
                KeyboardState.Key key = keyboardState.GetKeyByLogicCode(i);
                if (key != null)
                {
                    if (lightingEffectIndex != 0xFF)
                    {
                        Log("Key '" + key.KeyName + "' uses lighting effect " + lightingEffectIndex);
                    }
                }
                else if (lightingEffectIndex != 0xFF)
                {
                    Log("Key has lighting effect but failed to find the key for the index " + i + " (lighting effect index " + 
                        lightingEffectIndex + ")");
                }
            }
        }

        static void ProcessBufferedPacket(Packet packet, byte op1, byte op2)
        {
            // NOTE: This packet doesn't include heading info, it's pure data
            string directionStr = "[send-buffered]";
            //Log("[send-buffered]" + packet);
            switch ((OpCodes)op1)
            {
                case OpCodes.DriverLayerSetKeyValues:
                    {
                        switch ((OpCodes_SetDriverLayerKeyValues)op2)
                        {
                            case OpCodes_SetDriverLayerKeyValues.KeySet:
                                ProcessSetKeys(packet, false);
                                break;
                            case OpCodes_SetDriverLayerKeyValues.KeySetFn:
                                ProcessSetKeys(packet, true);
                                break;
                            case OpCodes_SetDriverLayerKeyValues.KeyPressLightingEffect:
                                ProcessKeyPressLightingEffect(packet);
                                break;
                            default:
                                Log("Unhandled " + OpCodes.DriverLayerSetKeyValues + "." +
                                    (OpCodes_SetDriverLayerKeyValues)packet.Opcode2 + " packet: " + packet);
                                break;
                        }
                    }
                    break;
                case OpCodes.DriverLayerUpdateRealtimeLighting:
                    {
                        switch ((OpCodes_DriverLayerUpdateRealtimeLighting)op2)
                        {
                            case OpCodes_DriverLayerUpdateRealtimeLighting.Update:
                                {
                                    // Realtime lighting is hard coded to use 132 lighting values
                                    if (packet.Length != 560)// Seems to be padded to the 0x38 byte boundry
                                    {
                                        Log("Realtime lighting ('driver' layer) has a bad length. Expected: " +
                                            (132 * 4) + " actual: " + packet.Length + " packet: " + packet);
                                    }
                                    else
                                    {
                                        for (int i = 0; i < 132; i++)
                                        {
                                            int val = packet.ReadInt32();
                                            packet.Index -= 4;

                                            byte red = packet.ReadByte();
                                            byte green = packet.ReadByte();
                                            byte blue = packet.ReadByte();
                                            byte alpha = packet.ReadByte();

                                            if (val != 0)
                                            {
                                                KeyboardState.Key key = keyboardState.GetKeyAtLocationCode(i);
                                                if (key != null)
                                                {
                                                    //Log(key.KeyName + " " + val.ToString("X8"));
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            case OpCodes_DriverLayerUpdateRealtimeLighting.UpdateComplete:
                                EnsureRemainingPacketIsEmpty(packet, directionStr);
                                break;
                            default:
                                Log("Unhandled " + OpCodes.DriverLayerUpdateRealtimeLighting + "." +
                                    (OpCodes_DriverLayerUpdateRealtimeLighting)packet.Opcode2 + " packet: " + packet);
                                break;
                        }
                    }
                    break;
                case OpCodes.LayerSetKeyValues:
                case OpCodes.LayerFnSetKeyValues:
                    {
                        ProcessSetKeys(packet, (OpCodes)op1 == OpCodes.LayerFnSetKeyValues);
                    }
                    break;
                case OpCodes.LayerSetKeyPressLightingEffect:
                    {
                        ProcessKeyPressLightingEffect(packet);
                    }
                    break;
                case OpCodes.LayerSetMacros:
                    {
                        // NOTE: the "driver" mode uses callbacks with "18 01" rather than sending the macro data to the keyboard

                        const int macroElementLength = 8;
                        const int maxMacroElements = 63;
                        const int padding = 8;
                        const int totalLen = (macroElementLength * maxMacroElements) + padding;
                        if (packet.Length % totalLen != 0)
                        {
                            Log("Invalid macro packet(" + packet.Length + "/" + totalLen + "): " + packet);
                        }
                        else
                        {
                            int numMacros = packet.Length / totalLen;
                            for (int i = 0; i < numMacros; i++)
                            {
                                /*using (Packet p = new Packet(true, packet.ReadBytes(64 * 8)))
                                {
                                    Log("Macro: " + p);
                                    packet.Index -= p.Length;
                                }*/

                                ushort macroMagic = packet.ReadUInt16();
                                if (macroMagic != 21930)
                                {
                                    // 21930 / 0x55AA / AA 55
                                    Log("Macro has invalid magic value: " + macroMagic + " expected: " + 21930);
                                }

                                // Crc over all bytes up until the first zero element (starting at offset 8)
                                ushort macroCrc = packet.ReadUInt16();
                                int tempIndex = packet.Index;
                                packet.Skip(4);
                                int calculatedIntCount = 0;
                                while (packet.ReadInt32() != 0) { calculatedIntCount++; }
                                packet.Index = tempIndex + 4;
                                byte[] buff = packet.ReadBytes(calculatedIntCount * 4);
                                ushort calculatedMacroCrc = Crc16.GetCrc(buff);
                                if (calculatedMacroCrc != macroCrc)
                                {
                                    Log("Invalid macro crc: " + calculatedMacroCrc + " expected: " + macroCrc + " " + Packet.ToHexString(buff));
                                }
                                packet.Index = tempIndex;

                                byte intCount = packet.ReadByte();
                                if (intCount != calculatedIntCount)
                                {
                                    Log("Macro element int count is incorrect: " + intCount + " expected: " + calculatedIntCount);
                                }
                                
                                // The index of the macro (should generally increment by 1 for each macro found)
                                byte macroIndex = packet.ReadByte();

                                // Specifies how the macro should be repeated based on input
                                // NOTE: The "driver" software implementation is pretty dumb in that the "macro setting" which looks like it's
                                //       done per key is actually done per macro (and impacts all keys which use that macro).
                                MacroRepeatType repeatType = (MacroRepeatType)packet.ReadByte();
                                byte repeatCount = packet.ReadByte();// Only used by "RepeatXTimes"?

                                Log("Macro index:" + macroIndex + " repeatType:" + repeatType + " repeatCount:" + repeatCount);

                                for (int j = 0; j < maxMacroElements; j++)
                                {
                                    if (packet.ReadInt64() == 0)
                                    {
                                        continue;
                                    }
                                    packet.Index -= 8;

                                    /*using (Packet p = new Packet(true, packet.ReadBytes(8)))
                                    {
                                        Log("Macro element: " + p);
                                        packet.Index -= p.Length;
                                    }*/

                                    // NOTE: Modifiers appear as seperate entries (rather than being an additonal flag to a key)
                                    byte keyCode = packet.ReadByte();
                                    string keyCodeStr = keyCode.ToString();
                                    DriverValueModifer modifier = (DriverValueModifer)packet.ReadByte();
                                    MacroKeyState keyState = (MacroKeyState)packet.ReadByte();
                                    MacroKeyType keyType = (MacroKeyType)packet.ReadByte();
                                    if (keyType == MacroKeyType.Key)
                                    {
                                        if (modifier != DriverValueModifer.None)
                                        {
                                            keyCodeStr = modifier.ToString();
                                        }
                                        else
                                        {
                                            uint driverValue;
                                            if (KeyValues.ShortToLongDriverValues.TryGetValue(keyCode, out driverValue))
                                            {
                                                keyCodeStr = ((DriverValue)driverValue).ToString();
                                            }
                                        }
                                    }
                                    else if (keyType == MacroKeyType.Mouse)
                                    {
                                        keyCodeStr = ((DriverValueMouseButton)keyCode).ToString();
                                    }
                                    Log("Key:" + keyCodeStr + " type:" + keyType + " state:" + keyState);

                                    int delayInfo = packet.ReadInt32();
                                    if (delayInfo != 0)
                                    {
                                        packet.Index -= 4;
                                        ushort delay = packet.ReadUInt16();// Up to 65535 milliseconds? (65.5 seconds)

                                        byte elmUnk1 = packet.ReadByte();// Always 0?
                                        byte elmUnk2 = packet.ReadByte();// Always 3?
                                        if (elmUnk1 != 0)
                                        {
                                            Log("Macro unk1: " + elmUnk1);
                                        }
                                        if (elmUnk2 != 3)
                                        {
                                            Log("Macro unk2: " + elmUnk2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    break;
                case OpCodes.LayerSetLightValues:
                    {
                        // Up to 32 lighting effects (this includes static "DIY" lighting)
                        for (int i = 0; i < 32; i++)
                        {
                            // Not the best naming. These are just two blocks of data.
                            // For animations the first block contains the frames, the 2nd contains the key light colors.
                            int lightingDataOffset = packet.ReadInt32();
                            int lightingDataCount = packet.ReadInt32();
                            int lightingParamsOffset = packet.ReadInt32();
                            int lightingParamsCount = packet.ReadInt32();
                            int tempIndex = packet.Index;
                            if (lightingDataCount > 0)
                            {
                                packet.Index = lightingDataOffset;
                                for (int j = 0; j < lightingDataCount; j++)
                                {
                                    LightingEffectType lightingDataType = (LightingEffectType)packet.ReadInt16();
                                    ushort lightingDataLen = packet.ReadUInt16();
                                    int tempIndex2 = packet.Index;
                                    switch (lightingDataType)
                                    {
                                        case LightingEffectType.Static:// Static RGB lighting (per key)
                                            for (int k = 0; k < lightingDataLen / 4; k++)
                                            {
                                                byte red = packet.ReadByte();
                                                byte green = packet.ReadByte();
                                                byte blue = packet.ReadByte();
                                                byte alpha = packet.ReadByte();
                                                if (alpha > 0)
                                                {
                                                    KeyboardState.Key key = keyboardState.GetKeyAtLocationCode(k);
                                                    if (key != null)
                                                    {
                                                        if (logLightingDIY)
                                                        {
                                                            Log("KeyDIY '" + key.KeyName + "' (" + k + ") #" + red.ToString("X2") +
                                                                green.ToString("X2") + blue.ToString("X2") + " alpha: " + alpha);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Log("Couldn't find key from lighting data at location code " + k);
                                                    }
                                                }
                                            }
                                            break;
                                        case LightingEffectType.Dynamic:// Lighting effect file (frames of lighting)
                                            {
                                                /*using (Packet p = new Packet(true, packet.ReadBytes(lightingDataLen)))
                                                {
                                                    Log("Frame: " + p);
                                                    packet.Index -= p.Length;
                                                }*/

                                                // Each frame is a bit buffer of keys which are used by the frame
                                                byte[] bitBuffer = packet.ReadBytes(22);
                                                bool[] bits = BitHelper.BytesToBits(bitBuffer);
                                                for (int k = 0; k < bits.Length; k++)
                                                {
                                                    if (bits[k])
                                                    {
                                                        KeyboardState.Key key = keyboardState.GetKeyAtLocationCode(k);
                                                        if (key != null)
                                                        {
                                                            //Log("Frame key '" + key.KeyName + "' (" + k + ")");
                                                        }
                                                        else
                                                        {
                                                            //Log("Couldn't find key for lighting frame at location code " + k);
                                                        }
                                                    }
                                                }
                                            }
                                            break;
                                        default:
                                            Log("Unhandled lighting data type " + lightingDataType);
                                            break;
                                    }
                                    packet.Index = tempIndex2 + lightingDataLen;
                                }
                            }
                            if (lightingParamsCount > 0)
                            {
                                packet.Index = lightingParamsOffset;
                                for (int j = 0; j < lightingParamsCount; j++)
                                {
                                    /*using (Packet p = new Packet(true, packet.ReadBytes(32)))
                                    {
                                        Log("Params: " + p);
                                        packet.Index -= p.Length;
                                    }*/

                                    // Monochrome, RGB, Breathing
                                    LightingEffectColorType lightingEffect = (LightingEffectColorType)packet.ReadByte();

                                    byte unk1 = packet.ReadByte();// Always 32 / 0x20? the size of the param buffer?
                                    if (unk1 != 32)
                                    {
                                        Log("Unknown value in lighting parameter data: " + unk1 + " (expected 32) data: " + packet);
                                    }

                                    // Bit buffer of the keys which have this effect applied (key location codes)
                                    byte[] bitBuffer = packet.ReadBytes(22);
                                    if (bitBuffer != null)
                                    {
                                        bool[] bits = BitHelper.BytesToBits(bitBuffer);
                                        for (int k = 0; k < bits.Length; k++)
                                        {
                                            if (bits[k])
                                            {
                                                KeyboardState.Key key = keyboardState.GetKeyAtLocationCode(k);
                                                if (key != null)
                                                {
                                                    //Log("LE key '" + key.KeyName + "' (" + k + ")");
                                                }
                                                else
                                                {
                                                    //Log("Couldn't find key from lighting params data at location code " + k);
                                                }
                                            }
                                        }
                                    }

                                    byte red = packet.ReadByte();
                                    byte green = packet.ReadByte();
                                    byte blue = packet.ReadByte();
                                    packet.ReadByte();// alpha (unused?)

                                    short param1 = packet.ReadInt16();
                                    short param2 = packet.ReadInt16();

                                    // "parameter" values use integer division to get their real value, so many produce the same result
                                    switch (lightingEffect)
                                    {
                                        case LightingEffectColorType.RGB:
                                            // RGB doesn't use param2
                                            /*short originalParam = param1 > 0 ? (short)(360 / param1) : (short)360;
                                            Log("Parameter value (" + lightingEffect + ") original:" + originalParam + 
                                                " actual:" + param1 + " param2(unused):" + param2);*/
                                            break;
                                        case LightingEffectColorType.Monochrome:
                                            // Monochrome doesn't use either parameter 
                                            /*Log("Parameter value (" + lightingEffect + ") param1(unused):" + param1 + 
                                                " param2(unused):" + param2);*/
                                            break;
                                        case LightingEffectColorType.Breathing:
                                            // The breathing params in the UI are reordered based on the largest value
                                            /*short originalParam1 = param1 > 0 ? (short)(100 / param1) : (short)100;
                                            short originalParam2 = param2;
                                            Log("Parameter value (" + lightingEffect + ") original " + originalParam2 +
                                                " - " + originalParam1 + " actual: " + param2 + " - " + param1);*/
                                            break;
                                        default:
                                            Log("Unhandled lighting effect " + lightingEffect);
                                            break;
                                    }
                                    /*Log("Param " + lightingEffect + " #" + red.ToString("X2") +
                                        green.ToString("X2") + blue.ToString("X2"));*/
                                }
                            }

                            packet.Index = tempIndex;
                        }

                        Log("Change lighting values!");
                    }
                    break;
            }
        }

        static void ProcessPacket(Packet data, bool isSend)
        {
            // Do the work in another thread (as we need access to Log functions, we can't access whilst inside
            // the WriteFile hook in C# (even if we change the code path... .NET Framework issues I assume?)
            lock (packetQueue)
            {
                packetQueue.Enqueue(new QueuedPacket(data, isSend));
            }
            ThreadPool.QueueUserWorkItem((object state) =>
            {
                QueuedPacket queuedPacket = null;
                lock (packetQueue)
                {
                    if (packetQueue.Count > 0)
                    {
                        queuedPacket = packetQueue.Dequeue();
                    }
                    while (queuedPacket != null)
                    {
                        Packet packet = queuedPacket.Data;
                        string directionStr = queuedPacket.IsSend ? "[send] " : "[recv] ";

                        if (!Crc16.ValidateCrc(packet.GetBuffer()))
                        {
                            Log(directionStr + "failed to validate crc! Packet: " + packet);
                        }

                        if (queuedPacket.IsSend)
                        {
                            //Log(directionStr + packet);

                            if (bufferedPacket != null)
                            {
                                bufferedPacket.Append(packet);
                                if (bufferedPacket.IsComplete)
                                {
                                    using (Packet p = new Packet(true, bufferedPacket.Data))
                                    {
                                        byte op1 = (byte)bufferedPacket.Opcode;
                                        byte op2 = (byte)(bufferedPacket.Opcode >> 8);
                                        ProcessBufferedPacket(p, op1, op2);
                                    }
                                    bufferedPacket = null;
                                }
                            }
                            if (bufferedPacket == null && BufferedPacket.IsBufferedPacket(packet))
                            {
                                bufferedPacket = new BufferedPacket(packet);
                            }
                            if (bufferedPacket == null)
                            {
                                packet.Index = 8;
                                switch ((OpCodes)packet.Opcode1)
                                {
                                    case OpCodes.Info:
                                        {
                                            switch ((OpCodes_Info)packet.Opcode2)
                                            {
                                                case OpCodes_Info.InitBuffers:
                                                case OpCodes_Info.FirmwareId:
                                                case OpCodes_Info.ModelId:
                                                case OpCodes_Info.Unk_02:
                                                    {
                                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                    }
                                                    break;
                                            }
                                        }
                                        break;
                                    case OpCodes.Ping:
                                        {
                                            EnsureRemainingPacketIsEmpty(packet, directionStr);
                                        }
                                        break;
                                    case OpCodes.SetLayer:
                                        {
                                            EnsureRemainingPacketIsEmpty(packet, directionStr);
                                            Log(directionStr + "Request to change to keyboard layer '" + (KeyboardLayer)packet.Opcode2 + "'");
                                        }
                                        break;
                                    case OpCodes.DriverMacro:
                                        {
                                            switch ((OpCodes_DriverMacro)packet.Opcode2)
                                            {
                                                case OpCodes_DriverMacro.BeginEnd:
                                                    bool beginMacro = packet.ReadByte() != 0;
                                                    Log("Macro " + (beginMacro ? "begin" : "end"));
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                    break;
                                                case OpCodes_DriverMacro.MouseState:
                                                    DriverValueMouseButton mouseState = (DriverValueMouseButton)packet.ReadByte();
                                                    Log("Macro mouse: " + mouseState);
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                    break;
                                                case OpCodes_DriverMacro.KeyboardState:
                                                    DriverValueModifer modifiers = (DriverValueModifer)packet.ReadByte();
                                                    List<DriverValue> pressedKeysDriverValues = new List<DriverValue>();
                                                    byte key;
                                                    while ((key = packet.ReadByte()) != 0)
                                                    {
                                                        uint driverValue;
                                                        KeyValues.ShortToLongDriverValues.TryGetValue(key, out driverValue);
                                                        pressedKeysDriverValues.Add((DriverValue)driverValue);
                                                    }
                                                    Log("Macro keys: " + string.Join(",", pressedKeysDriverValues.Select(x => x.ToString())) +
                                                        " modifiers: " + modifiers);
                                                    break;
                                            }
                                        }
                                        break;
                                    case OpCodes.DriverLayerSetConfig:
                                        {
                                            packet.Skip(9);
                                            EnsureRemainingPacketIsEmpty(packet, directionStr);
                                        }
                                        break;
                                    case OpCodes.LayerResetDataType:
                                        {
                                            KeyboardLayer layer = (KeyboardLayer)packet.Opcode2;
                                            // This packet uses index 3 for the data type (usually this is a packet data offset)
                                            packet.Index = 3;
                                            KeyboardLayerDataType dataType = (KeyboardLayerDataType)packet.ReadByte();
                                            packet.Index = 8;
                                            EnsureRemainingPacketIsEmpty(packet, directionStr);
                                        }
                                        break;
                                    default:
                                        Log(directionStr + "Unhandled opcode. Packet: " + packet);
                                        break;
                                }
                            }
                        }
                        else
                        {
                            //Log(directionStr + packet);

                            packet.Index = 0;
                            byte op1 = packet.ReadByte();
                            byte op2 = packet.ReadByte();

                            packet.Index = 2;
                            byte returnCode = packet.ReadByte();
                            bool requiresReturnCode = false;
                            bool canBeZeroReturnCode = false;
                            switch ((OpCodes)op1)
                            {
                                case OpCodes.Info:
                                    switch (op2)
                                    {
                                        case 1:
                                        case 2:
                                        case 8:
                                        case 9:
                                            requiresReturnCode = true;
                                            break;
                                    }
                                    break;
                                case OpCodes.RestartKeyboard:
                                    switch (op2)
                                    {
                                        case 2:
                                            requiresReturnCode = true;
                                            break;
                                    }
                                    break;
                                case OpCodes.DriverMacro:
                                    switch (op2)
                                    {
                                        case 1:
                                        case 2:
                                        case 3:
                                        case 4:
                                            requiresReturnCode = true;
                                            break;
                                    }
                                    break;
                                case OpCodes.DriverLayerSetKeyValues:
                                    requiresReturnCode = true;
                                    break;
                                case OpCodes.DriverLayerUpdateRealtimeLighting:
                                    switch (op2)
                                    {
                                        case 1:
                                        case 2:
                                            requiresReturnCode = true;
                                            canBeZeroReturnCode = true;
                                            break;
                                    }
                                    break;
                                case OpCodes.DriverKeyCallback:
                                    // Return code 00-FF ?
                                    requiresReturnCode = true;
                                    canBeZeroReturnCode = true;
                                    break;
                            }
                            if (requiresReturnCode && returnCode == 0 && !canBeZeroReturnCode)
                            {
                                Log(directionStr + "return code 0 where return code 1 was expected. Packet: " + packet);
                            }
                            else if (!requiresReturnCode && returnCode != 0)
                            {
                                Log(directionStr + "this packet is flagged as with a return code, but isn't being handled as such. Packet: " + packet);
                            }

                            // These are used in "04 XX"
                            packet.Index = 3;
                            bool hasStateData1 = false;
                            byte stateData = packet.ReadByte();
                            switch ((OpCodes)op1)
                            {
                                case OpCodes.DriverKeyCallback:
                                    hasStateData1 = true;
                                    break;
                            }
                            if (stateData != 0 && !hasStateData1)
                            {
                                Log(directionStr + "this packet has state data, but isn't being handled as such. Packet: " + packet);
                            }
                            if (packet.ReadUInt16() != 0)
                            {
                                Log(directionStr + "TODO: Handle buffered packets coming from the keyboard. Packet: " + packet);
                            }

                            packet.Index = 8;

                            switch ((OpCodes)packet.Opcode1)
                            {
                                case OpCodes.Info:
                                    {
                                        switch ((OpCodes_Info)packet.Opcode2)
                                        {
                                            case OpCodes_Info.InitBuffers:
                                                {
                                                    // Max logic code = a*b
                                                    keyboardBufferSizeA = packet.ReadByte();// 0E on GK84
                                                    keyboardBufferSizeB = packet.ReadByte();// 08 on GK84
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                }
                                                break;
                                            case OpCodes_Info.FirmwareId:
                                                {
                                                    keyboardFirmwareId = packet.ReadUInt32();
                                                    keyboardFirmwareMinorVersion = packet.ReadByte();
                                                    keyboardFirmwareMajorVersion = packet.ReadByte();
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                    Log("FirmwareId: 0x" + keyboardFirmwareId.ToString("X8") + " version: " +
                                                        keyboardFirmwareMajorVersion + "." + keyboardFirmwareMajorVersion + 
                                                        " (see modellist.json)");
                                                }
                                                break;
                                            case OpCodes_Info.ModelId:
                                                {
                                                    uint modelId = packet.ReadUInt32();
                                                    // crcValidation1 doesn't seem to be used for much (it's always FF FF?) crcValidation1 is the
                                                    // actual crc of modelId+crcValidation1
                                                    ushort crcValidation1 = packet.ReadUInt16();// always FF FF?
                                                    ushort crcValidation2 = packet.ReadUInt16();
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                    Log("ModelId: " + modelId + " (see profile.json based on your FWID)");

                                                    keyboardState = KeyboardState.GetKeyboardState(modelId);
                                                    if (keyboardState == null)
                                                    {
                                                        Log("Failed to find keyboard for modelid " + modelId + "!");
                                                    }
                                                    keyboardState.FirmwareMajorVersion = keyboardFirmwareMinorVersion;
                                                    keyboardState.FirmwareMinorVersion = keyboardFirmwareMajorVersion;
                                                    keyboardState.InitializeBuffers(keyboardBufferSizeA, keyboardBufferSizeB);
                                                }
                                                break;
                                            case OpCodes_Info.Unk_02:
                                                {
                                                    int unk = packet.ReadInt32();// always -1?
                                                    // crcValidation1 doesn't seem to be used for much (it's always FF FF?) crcValidation1 is the
                                                    // actual crc of unk+crcValidation1
                                                    // NOTE: This crc validation always fails as far as I can tell, I assume it's because unk is always -1
                                                    ushort crcValidation1 = packet.ReadUInt16();// always FF FF?
                                                    ushort crcValidation2 = packet.ReadUInt16();
                                                    EnsurePacketValue(unk, -1, packet, directionStr);
                                                    EnsureRemainingPacketIsEmpty(packet, directionStr);
                                                }
                                                break;
                                        }
                                    }
                                    break;
                                case OpCodes.Ping:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                case OpCodes.SetLayer:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                        Log(directionStr + "Change to keyboard layer '" + (KeyboardLayer)packet.Opcode2 + "'");
                                    }
                                    break;
                                case OpCodes.DriverMacro:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                case OpCodes.DriverLayerSetKeyValues:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                case OpCodes.DriverLayerSetConfig:
                                    {
                                        packet.Skip(8);// The send PC->KB packet has 1 more byte (9 total)
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                case OpCodes.DriverLayerUpdateRealtimeLighting:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                case OpCodes.DriverKeyCallback:
                                    {
                                        packet.Index = 2;
                                        // Callbacks belongs to either a macro or a shortcut (opening a program, file, etc)
                                        byte callbackId = packet.ReadByte();
                                        bool callbackKeyDown = packet.ReadByte() != 0;
                                        Log("Macro/shortcut callback " + (callbackKeyDown ? "keyDown" : "keyUp") + " id:" + callbackId);
                                    }
                                    break;
                                case OpCodes.LayerResetDataType:
                                case OpCodes.LayerSetKeyValues:
                                case OpCodes.LayerSetLightValues:
                                case OpCodes.LayerSetMacros:
                                    {
                                        EnsureRemainingPacketIsEmpty(packet, directionStr);
                                    }
                                    break;
                                default:
                                    //Log(directionStr + "Unhandled opcode. Packet: " + packet);
                                    break;
                            }
                        }

                        packet.Dispose();
                        queuedPacket = null;
                        if (packetQueue.Count > 0)
                        {
                            queuedPacket = packetQueue.Dequeue();
                        }
                    }
                }
            });
        }

        static void EnsureRemainingPacketIsEmpty(Packet packet, string directionStr)
        {
            byte[] buffer = packet.GetBuffer();
            for (int i = packet.Index; i < packet.Length; i++)
            {
                if (buffer[i] != 0)
                {
                    Log(directionStr + "Packet has unhandled data. Packet: " + packet);
                    break;
                }
            }
        }

        static void EnsurePacketValue<T>(T value, T expected, Packet packet, string directionStr) where T : IEquatable<T>
        {
            if (!value.Equals(expected))
            {
                Log(directionStr + "[ERROR] Expected value '" + expected + "' got '" + value + "' in packet: " + packet);
            }
        }

        static void Ensure65BytePacketStarts00(IntPtr ptr)
        {
            byte val = *(byte*)ptr;
            if (val != 0)
            {
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    Log("65 byte length packet doesn't start with 0!!!!! Starts with: " + val);
                });
            }
        }

        static void UpdateCurrentDevice(IntPtr hFile)
        {
            if (currentDevice == IntPtr.Zero && Hook.IsKnownDevice(hFile))
            {
                currentDevice = hFile;

                // Run this another thread due to issues with hooks / .NET
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    Log("Found device! Handle: " + hFile);
                });
            }
        }

        static bool OnWriteFile(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped)
        {
            // Don't use Log in here (or other file IO operations)
            bool result = writeFileOriginal(hFile, lpBuffer, nNumberOfBytesToWrite, out lpNumberOfBytesWritten, lpOverlapped);

            UpdateCurrentDevice(hFile);
            if (currentDevice == hFile)
            {
                if (nNumberOfBytesToWrite == 65)
                {
                    Ensure65BytePacketStarts00(lpBuffer);
                    byte[] buffer = new byte[64];
                    Marshal.Copy(lpBuffer + 1, buffer, 0, buffer.Length);
                    ProcessPacket(new Packet(true, buffer), true);
                }
                else
                {
                    ThreadPool.QueueUserWorkItem((object state) =>
                    {
                        Log("[send] TODO: Handle data of length " + nNumberOfBytesToWrite);
                    });
                }
            }

            return result;
        }

        static bool OnReadFile(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped)
        {
            bool result = readFileOriginal(hFile, lpBuffer, nNumberOfBytesToRead, out lpNumberOfBytesRead, lpOverlapped);
            UpdateCurrentDevice(hFile);

            if (currentDevice == hFile)
            {
                if (lpNumberOfBytesRead > 0)
                {
                    ProcessReadBuffer(lpBuffer, lpNumberOfBytesRead, false);
                }
                else
                {
                    // NOTE: This will break down if two threads are making reads. However, as far as I can tell this never happens.
                    lastReadFileOverlappedPtr = lpOverlapped;
                    lastReadFileBufferPtr = lpBuffer;
                }
            }

            return result;
        }

        static bool OnGetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out int lpNumberOfBytesTransferred, bool bWait)
        {
            bool success = getOverlappedResultOriginal(hFile, lpOverlapped, out lpNumberOfBytesTransferred, bWait);
            if (success && lpNumberOfBytesTransferred > 0 && currentDevice == hFile && lpOverlapped == lastReadFileOverlappedPtr)
            {
                ProcessReadBuffer(lastReadFileBufferPtr, lpNumberOfBytesTransferred, true);
            }
            return success;
        }

        static void ProcessReadBuffer(IntPtr lpBuffer, int numBytes, bool overlapped)
        {
            if (numBytes == 65)
            {
                Ensure65BytePacketStarts00(lpBuffer);
                byte[] buffer = new byte[64];
                Marshal.Copy(lpBuffer + 1, buffer, 0, buffer.Length);
                ProcessPacket(new Packet(true, buffer), false);
            }
            else
            {
                ThreadPool.QueueUserWorkItem((object state) =>
                {
                    Log("[recv] TODO: Handle data of length " + numBytes);
                });
            }
        }

        static IntPtr OnCreateFileW([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, int dwDesiredAccess, int dwShareMode, IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile)
        {
            if (numSkippedCreateFileCalls < numSkipCreateFileCalls)
            {
                // NOTE: This is super slow due to this suspending / resuming all threads
                Hook.WL_DisableHook(kernel32_createFileW);
            }

            // Don't use Log in here (or other file IO operations)
            IntPtr result = createFileWOriginal(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);

            const string hidGuid = "4D1E55B2-F16F-11CF-88CB-001111000030";// Registry where all devices tagged as HID belong
            if (result != IntPtr.Zero && !string.IsNullOrEmpty(lpFileName) && lpFileName.ToUpper().Contains(hidGuid))
            {
                /*string manufacturer = Interop.GetManufacturerString(result);
                string product = Interop.GetProductString(result);
                Console.WriteLine(lpFileName + " | " + manufacturer + " | " + product);*/

                ushort[] productIds;
                Interop.HIDD_ATTRIBUTES attributes;
                if (Interop.HidD_GetAttributes(result, out attributes) &&
                    KeyboardDeviceManager.knownProducts.TryGetValue(attributes.VendorID, out productIds) &&
                    productIds.Contains(attributes.ProductID))
                {
                    IntPtr ptr;
                    if (Interop.HidD_GetPreparsedData(result, out ptr))
                    {
                        Interop.HIDP_CAPS caps;
                        if (Interop.HidP_GetCaps(ptr, out caps))
                        {
                            // Do we care about Usage/UsagePage?
                            ushort inputReportLen = caps.InputReportByteLength;
                            ushort ouputReportLen = caps.OutputReportByteLength;
                            if (showConsole)
                            {
                                //Console.WriteLine("Found device! Data length (send / recv): " + ouputReportLen + " / " + inputReportLen);
                            }
                            Hook.OnDeviceHandle(result, true);
                        }
                        Interop.HidD_FreePreparsedData(ptr);
                    }
                }
            }

            if (numSkippedCreateFileCalls < numSkipCreateFileCalls)
            {
                Hook.WL_EnableHook(kernel32_createFileW);
                numSkippedCreateFileCalls++;
            }

            return result;
        }

        static object logLocker = new object();
        private static void Log(string msg)
        {
            lock (logLocker)
            {
                if (showConsole)
                {
                    Console.WriteLine(msg);
                }
                System.IO.File.AppendAllText("CommandLogger.txt", "[" + DateTime.Now.TimeOfDay + "] " + msg + Environment.NewLine);
            }
        }

        public static int DllMain(string arg)
        {
            if (showConsole)
            {
                ConsoleHelper.ShowConsole();
            }

            if (!Localization.Load())
            {
                Log("Failed to load localization data");
            }
            if (!KeyValues.Load())
            {
                Log("Failed to load the key data");
            }
            if (!KeyboardState.Load())
            {
                Log("Failed to load keyboard data");
            }

            // This needs to be done to avoid recurssion issues in the .NET Framework
            BindingFlags staticMethodFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            RuntimeHelpers.PrepareMethod(typeof(CommandLogger).GetMethod("OnCreateFileW", staticMethodFlags).MethodHandle);

            Hook.WL_InitHooks();

            IntPtr kernel32 = Interop.GetModuleHandle("Kernel32.dll");
            IntPtr kernel32_writeFile = Interop.GetProcAddress(kernel32, "WriteFile");
            IntPtr kernel32_readFile = Interop.GetProcAddress(kernel32, "ReadFile");
            IntPtr kernel32_getOverlappedResult = Interop.GetProcAddress(kernel32, "GetOverlappedResult");
            kernel32_createFileW = Interop.GetProcAddress(kernel32, "CreateFileW");

            writeFileHook = OnWriteFile;
            writeFileHookPtr = Marshal.GetFunctionPointerForDelegate(writeFileHook);
            Hook.WL_CreateHook(kernel32_writeFile, writeFileHookPtr, ref writeFileOriginalPtr);
            writeFileOriginal = (WriteFileDelegate)Marshal.GetDelegateForFunctionPointer(writeFileOriginalPtr, typeof(WriteFileDelegate));
            Hook.WL_EnableHook(kernel32_writeFile);

            readFileHook = OnReadFile;
            readFileHookPtr = Marshal.GetFunctionPointerForDelegate(readFileHook);
            Hook.WL_CreateHook(kernel32_readFile, readFileHookPtr, ref readFileOriginalPtr);
            readFileOriginal = (ReadFileDelegate)Marshal.GetDelegateForFunctionPointer(readFileOriginalPtr, typeof(ReadFileDelegate));
            Hook.WL_EnableHook(kernel32_readFile);

            getOverlappedResultHook = OnGetOverlappedResult;
            getOverlappedResultHookPtr = Marshal.GetFunctionPointerForDelegate(getOverlappedResultHook);
            Hook.WL_CreateHook(kernel32_getOverlappedResult, getOverlappedResultHookPtr, ref getOverlappedResultOriginalPtr);
            getOverlappedResultOriginal = (GetOverlappedResultDelegate)Marshal.GetDelegateForFunctionPointer(getOverlappedResultOriginalPtr, typeof(GetOverlappedResultDelegate));
            Hook.WL_EnableHook(kernel32_getOverlappedResult);

            // We can't hook CloseHandle from C# due to .NET Framework issues... can't callback to C# from C++ either
            Hook.HookCloseHandle(out currentDeviceAddr);

            createFileWHook = OnCreateFileW;
            createFileWHookPtr = Marshal.GetFunctionPointerForDelegate(createFileWHook);
            Hook.WL_CreateHook(kernel32_createFileW, createFileWHookPtr, ref createFileWOriginalPtr);
            createFileWOriginal = (CreateFileWDelegate)Marshal.GetDelegateForFunctionPointer(createFileWOriginalPtr, typeof(CreateFileWDelegate));
            Hook.WL_EnableHook(kernel32_createFileW);

            Log("Fully initialized hooks");

            return 0;
        }

        public static void Run()
        {
            //ProcessLauncher.Inject();
            ProcessLauncher.Launch();
        }

        static class Hook
        {
            [DllImport(loaderDll)]
            public static extern int WL_InitHooks();
            [DllImport(loaderDll)]
            public static extern int WL_HookFunction(IntPtr target, IntPtr detour, ref IntPtr original);
            [DllImport(loaderDll)]
            public static extern int WL_CreateHook(IntPtr target, IntPtr detour, ref IntPtr original);
            [DllImport(loaderDll)]
            public static extern int WL_RemoveHook(IntPtr target);
            [DllImport(loaderDll)]
            public static extern int WL_EnableHook(IntPtr target);
            [DllImport(loaderDll)]
            public static extern int WL_DisableHook(IntPtr target);

            [DllImport(loaderDll)]
            public static extern void HookCloseHandle(out IntPtr currentDeviceAddr);
            [DllImport(loaderDll)]
            public static extern void OnDeviceHandle(IntPtr handle, bool add);
            [DllImport(loaderDll)]
            public static extern bool IsKnownDevice(IntPtr handle);
        }

        unsafe class Interop
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

            [DllImport("hid.dll")]
            public static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, IntPtr buffer, uint bufferLength);

            [DllImport("hid.dll")]
            public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, IntPtr buffer, uint bufferLength);

            [DllImport("hid.dll")]
            public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, out HIDD_ATTRIBUTES attributes);

            [DllImport("hid.dll")]
            public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr parsedData);

            [DllImport("hid.dll")]
            public static extern bool HidD_FreePreparsedData(IntPtr parsedData);

            [DllImport("hid.dll")]
            public static extern bool HidP_GetCaps(IntPtr hidDeviceObject, out HIDP_CAPS caps);

            [StructLayout(LayoutKind.Sequential)]
            public struct HIDD_ATTRIBUTES
            {
                public uint Size;
                public ushort VendorID;
                public ushort ProductID;
                public ushort VersionNumber;
            }

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct HIDP_CAPS
            {
                public ushort Usage;
                public ushort UsagePage;
                public ushort InputReportByteLength;
                public ushort OutputReportByteLength;
                public ushort FeatureReportByteLength;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
                public ushort[] Reserved;
                public ushort NumberLinkCollectionNodes;
                public ushort NumberInputButtonCaps;
                public ushort NumberInputValueCaps;
                public ushort NumberInputDataIndices;
                public ushort NumberOutputButtonCaps;
                public ushort NumberOutputValueCaps;
                public ushort NumberOutputDataIndices;
                public ushort NumberFeatureButtonCaps;
                public ushort NumberFeatureValueCaps;
                public ushort NumberFeatureDataIndices;
            }

            const int stringBufferLen = 512;// TODO: Find out the real limit on these strings (maybe malloc instead of alloca)

            public static string GetManufacturerString(IntPtr handle)
            {
                byte* buffer = stackalloc byte[stringBufferLen];
                HidD_GetManufacturerString(handle, (IntPtr)buffer, stringBufferLen);
                return Marshal.PtrToStringUni((IntPtr)buffer);
            }

            public static string GetProductString(IntPtr handle)
            {
                byte* buffer = stackalloc byte[stringBufferLen];
                HidD_GetProductString(handle, (IntPtr)buffer, stringBufferLen);
                return Marshal.PtrToStringUni((IntPtr)buffer);
            }
        }

        class ProcessLauncher
        {
            // This injects a dll before the exe entry point runs.
            // TODO: Fix a crash which sometimes occurs.

            public static void Inject()
            {
                Process[] processes = null;
                Dictionary<int, Process> injectedProcesses = new Dictionary<int, Process>();
                HashSet<int> closesProcesses = new HashSet<int>();

                try
                {
                    processes = Process.GetProcessesByName(targetProcessName);
                    foreach (Process process in processes)
                    {
                        try
                        {
                            FileInfo fileInfo = new FileInfo(process.MainModule.FileName);
                            if (fileInfo.Exists && File.Exists(Path.Combine(fileInfo.Directory.FullName, someFileInTargetProcessFolder)))
                            {
                                if (!injectedProcesses.ContainsKey(process.Id))
                                {
                                    bool alreadyInjected = false;
                                    foreach (ProcessModule processModule in process.Modules)
                                    {
                                        if (processModule.ModuleName.Equals(loaderDll, StringComparison.OrdinalIgnoreCase))
                                        {
                                            alreadyInjected = true;
                                            break;
                                        }
                                    }

                                    if (!alreadyInjected && DllInjector.Inject(process, loaderDll))
                                    {
                                        injectedProcesses[process.Id] = process;
                                        Console.WriteLine("Injected into " + process.Id);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    while (injectedProcesses.Count != closesProcesses.Count)
                    {
                        foreach (KeyValuePair<int, Process> process in injectedProcesses)
                        {
                            try
                            {
                                if (!closesProcesses.Contains(process.Key) && process.Value.HasExited)
                                {
                                    closesProcesses.Add(process.Key);
                                }
                            }
                            catch
                            {
                                closesProcesses.Add(process.Key);
                            }
                        }
                        Thread.Sleep(1000);
                    }
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (Process process in processes)
                        {
                            try
                            {
                                process.Close();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }

            public static unsafe bool Launch()
            {
                string exePath = targetProcessNameEx;
                if (!File.Exists(exePath))
                {
                    return false;
                }

                STARTUPINFO si = default(STARTUPINFO);
                PROCESS_INFORMATION pi = default(PROCESS_INFORMATION);

                try
                {
                    bool success = CreateProcess(exePath, null, IntPtr.Zero, IntPtr.Zero, false, DEBUG_ONLY_THIS_PROCESS, IntPtr.Zero, null, ref si, out pi);
                    if (!success)
                    {
                        return false;
                    }

                    IntPtr entryPoint = IntPtr.Zero;
                    byte[] entryPointInst = new byte[2];

                    success = false;
                    bool complete = false;
                    while (!complete)
                    {
                        DEBUG_EVENT debugEvent;
                        if (!WaitForDebugEvent(out debugEvent, 5000))
                        {
                            break;
                        }

                        switch (debugEvent.dwDebugEventCode)
                        {
                            case CREATE_PROCESS_DEBUG_EVENT:
                                {
                                    IntPtr hFile = debugEvent.CreateProcessInfo.hFile;
                                    if (hFile != IntPtr.Zero && hFile != INVALID_HANDLE_VALUE)
                                    {
                                        CloseHandle(hFile);
                                    }
                                }
                                break;
                            case EXIT_PROCESS_DEBUG_EVENT:
                                complete = true;
                                break;
                            case LOAD_DLL_DEBUG_EVENT:
                                {
                                    LOAD_DLL_DEBUG_INFO loadDll = debugEvent.LoadDll;

                                    StealEntryPointResult stealResult = TryStealEntryPoint(ref pi, ref entryPoint, entryPointInst);
                                    switch (stealResult)
                                    {
                                        case StealEntryPointResult.FailGetModules:
                                            // Need to wait for more modules to load
                                            break;
                                        case StealEntryPointResult.FailAlloc:
                                        case StealEntryPointResult.FailRead:
                                        case StealEntryPointResult.FailWrite:
                                        //case StealEntryPointResult.FailFindTargetModule:// removed this check - module can take a while to appear
                                            complete = true;
                                            entryPoint = IntPtr.Zero;
                                            break;
                                        case StealEntryPointResult.Success:
                                            complete = true;
                                            break;
                                    }

                                    IntPtr hFile = loadDll.hFile;
                                    if (hFile != IntPtr.Zero && hFile != INVALID_HANDLE_VALUE)
                                    {
                                        CloseHandle(hFile);
                                    }
                                }
                                break;
                        }

                        ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, DBG_CONTINUE);
                    }

                    success = false;

                    DebugSetProcessKillOnExit(false);
                    DebugActiveProcessStop((int)pi.dwProcessId);

                    if (entryPoint != IntPtr.Zero)
                    {
                        CONTEXT86 context86 = default(CONTEXT86);
                        context86.ContextFlags = CONTEXT_FLAGS.CONTROL;
                        GetThreadContext(pi.hThread, ref context86);

                        for (int i = 0; i < 100 && context86.Eip != (ulong)entryPoint; i++)
                        {
                            Thread.Sleep(50);

                            context86.ContextFlags = CONTEXT_FLAGS.CONTROL;
                            GetThreadContext(pi.hThread, ref context86);
                        }

                        // If we are at the entry point inject the dll and then restore the entry point instructions
                        if (context86.Eip == (ulong)entryPoint && DllInjector.Inject(pi.hProcess, loaderDll))
                        {
                            Thread.Sleep(500);//add a delay as our C# code gets loaded on a seperate thread which can delay hooks
                            SuspendThread(pi.hThread);

                            IntPtr byteCount;
                            if (WriteProcessMemory(pi.hProcess, entryPoint, entryPointInst, (IntPtr)2, out byteCount) && (int)byteCount == 2)
                            {
                                success = true;
                            }

                            ResumeThread(pi.hThread);
                        }
                    }

                    if (!success)
                    {
                        TerminateProcess(pi.hProcess, 0);
                    }
                    else
                    {
                        using (Process process = Process.GetProcessById((int)pi.dwProcessId))
                        {
                            while (!process.HasExited)
                            {
                                Thread.Sleep(1000);
                            }
                        }
                    }

                    return success;
                }
                finally
                {
                    if (pi.hThread != IntPtr.Zero)
                    {
                        CloseHandle(pi.hThread);
                    }
                    if (pi.hProcess != IntPtr.Zero)
                    {
                        CloseHandle(pi.hProcess);
                    }
                }
            }

            private static unsafe StealEntryPointResult TryStealEntryPoint(ref PROCESS_INFORMATION pi, ref IntPtr entryPoint, byte[] entryPointInst)
            {
                int modSize = IntPtr.Size * 1024;
                IntPtr hMods = Marshal.AllocHGlobal(modSize);

                try
                {
                    if (hMods == IntPtr.Zero)
                    {
                        return StealEntryPointResult.FailAlloc;
                    }

                    int modsNeeded;
                    bool gotZeroMods = false;
                    while (!EnumProcessModulesEx(pi.hProcess, hMods, modSize, out modsNeeded, LIST_MODULES_ALL) || modsNeeded == 0)
                    {
                        if (modsNeeded == 0)
                        {
                            if (!gotZeroMods)
                            {
                                Thread.Sleep(100);
                                gotZeroMods = true;
                                continue;
                            }
                            else
                            {
                                // process has exited?
                                return StealEntryPointResult.FailGetModules;
                            }
                        }

                        // try again w/ more space...
                        Marshal.FreeHGlobal(hMods);
                        hMods = Marshal.AllocHGlobal(modsNeeded);
                        if (hMods == IntPtr.Zero)
                        {
                            return StealEntryPointResult.FailGetModules;
                        }
                        modSize = modsNeeded;
                    }

                    int totalNumberofModules = (int)(modsNeeded / IntPtr.Size);
                    for (int i = 0; i < totalNumberofModules; i++)
                    {
                        IntPtr hModule = Marshal.ReadIntPtr(hMods, i * IntPtr.Size);

                        MODULEINFO moduleInfo;
                        if (GetModuleInformation(pi.hProcess, hModule, out moduleInfo, sizeof(MODULEINFO)))
                        {
                            StringBuilder moduleNameSb = new StringBuilder(1024);
                            if (GetModuleFileNameEx(pi.hProcess, hModule, moduleNameSb, moduleNameSb.Capacity) != 0)
                            {
                                try
                                {
                                    string moduleName = Path.GetFileName(moduleNameSb.ToString());
                                    if (moduleName.Equals(targetProcessNameEx, StringComparison.OrdinalIgnoreCase))
                                    {
                                        IntPtr byteCount;
                                        if (ReadProcessMemory(pi.hProcess, moduleInfo.EntryPoint, entryPointInst, (IntPtr)2, out byteCount) && (int)byteCount == 2)
                                        {
                                            // TODO: We should probably use VirtualProtect here to ensure read/write/execute

                                            byte[] infLoop = { 0xEB, 0xFE };// JMP -2
                                            if (WriteProcessMemory(pi.hProcess, moduleInfo.EntryPoint, infLoop, (IntPtr)infLoop.Length, out byteCount) &&
                                                (int)byteCount == infLoop.Length)
                                            {
                                                entryPoint = moduleInfo.EntryPoint;
                                                return StealEntryPointResult.Success;
                                            }
                                            else
                                            {
                                                return StealEntryPointResult.FailWrite;
                                            }
                                        }
                                        else
                                        {
                                            return StealEntryPointResult.FailRead;
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }

                    return StealEntryPointResult.FailFindTargetModule;
                }
                finally
                {
                    if (hMods != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(hMods);
                    }
                }
            }

            enum StealEntryPointResult
            {
                FailAlloc,
                FailGetModules,
                FailFindTargetModule,
                FailRead,
                FailWrite,
                Success,
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
                bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

            [DllImport("kernel32.dll")]
            static extern uint ResumeThread(IntPtr hThread);

            [DllImport("kernel32.dll")]
            static extern uint SuspendThread(IntPtr hThread);

            [DllImport("kernel32.dll")]
            static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);

            [DllImport("psapi.dll", CharSet = CharSet.Auto)]
            static extern bool EnumProcessModulesEx([In] IntPtr hProcess, IntPtr lphModule, int cb, [Out] out int lpcbNeeded, int dwFilterFlag);

            [DllImport("psapi.dll", SetLastError = true)]
            static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, int cb);

            [DllImport("psapi.dll", CharSet = CharSet.Auto)]
            static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

            [DllImport("kernel32.dll")]
            static extern bool WaitForDebugEvent(out DEBUG_EVENT lpDebugEvent, uint dwMilliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool ContinueDebugEvent(int processId, int threadId, uint continuteStatus);

            [DllImport("kernel32.dll")]
            static extern void DebugSetProcessKillOnExit(bool killOnExit);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool DebugActiveProcessStop(int processId);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern Int32 CloseHandle(IntPtr hObject);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr dwSize, out IntPtr lpNumberOfBytesRead);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, IntPtr size, out IntPtr lpNumberOfBytesWritten);

            [DllImport("kernel32.dll", SetLastError = true)]
            static unsafe extern bool GetThreadContext(IntPtr hThread, CONTEXT86* lpContext);

            static unsafe bool GetThreadContext(IntPtr hThread, ref CONTEXT86 lpContext)
            {
                // Hack to align to 16 byte boundry
                byte* buff = stackalloc byte[Marshal.SizeOf(typeof(CONTEXT86)) + 16];
                buff += (ulong)(IntPtr)buff % 16;
                CONTEXT86* ptr = (CONTEXT86*)buff;
                *ptr = lpContext;

                bool result = GetThreadContext(hThread, ptr);
                lpContext = *ptr;
                if (!result && Marshal.GetLastWin32Error() == 998)
                {
                    // Align hack failed

                }
                return result;
            }

            [Flags]
            enum ThreadAccess : uint
            {
                Terminate = 0x00001,
                SuspendResume = 0x00002,
                GetContext = 0x00008,
                SetContext = 0x00010,
                SetInformation = 0x00020,
                QueryInformation = 0x00040,
                SetThreadToken = 0x00080,
                Impersonate = 0x00100,
                DirectImpersonation = 0x00200,
                All = 0x1F03FF
            }

            const int DEBUG_ONLY_THIS_PROCESS = 0x00000002;
            const int CREATE_SUSPENDED = 0x00000004;

            const int LIST_MODULES_DEFAULT = 0x00;
            const int LIST_MODULES_32BIT = 0x01;
            const int LIST_MODULES_64BIT = 0x02;
            const int LIST_MODULES_ALL = 0x03;

            const uint CREATE_PROCESS_DEBUG_EVENT = 3;
            const uint EXIT_PROCESS_DEBUG_EVENT = 5;
            const uint LOAD_DLL_DEBUG_EVENT = 6;

            static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

            const uint DBG_CONTINUE = 0x00010002;

            [StructLayout(LayoutKind.Sequential)]
            unsafe struct MODULEINFO
            {
                public IntPtr lpBaseOfDll;
                public uint SizeOfImage;
                public IntPtr EntryPoint;
            }

            struct STARTUPINFO
            {
                public uint cb;
                public string lpReserved;
                public string lpDesktop;
                public string lpTitle;
                public uint dwX;
                public uint dwY;
                public uint dwXSize;
                public uint dwYSize;
                public uint dwXCountChars;
                public uint dwYCountChars;
                public uint dwFillAttribute;
                public uint dwFlags;
                public short wShowWindow;
                public short cbReserved2;
                public IntPtr lpReserved2;
                public IntPtr hStdInput;
                public IntPtr hStdOutput;
                public IntPtr hStdError;
            }

            struct PROCESS_INFORMATION
            {
                public IntPtr hProcess;
                public IntPtr hThread;
                public uint dwProcessId;
                public uint dwThreadId;
            }

            [StructLayout(LayoutKind.Explicit)]
            struct DEBUG_EVENT
            {
                [FieldOffset(0)]
                public uint dwDebugEventCode;
                [FieldOffset(4)]
                public int dwProcessId;
                [FieldOffset(8)]
                public int dwThreadId;

                // x64(offset:16, size:164)
                // x86(offset:12, size:86)
                [FieldOffset(12)]//[FieldOffset(16)]
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 164, ArraySubType = UnmanagedType.U1)]
                public byte[] debugInfo;

                public CREATE_PROCESS_DEBUG_INFO CreateProcessInfo
                {
                    get { return GetDebugInfo<CREATE_PROCESS_DEBUG_INFO>(); }
                }

                public LOAD_DLL_DEBUG_INFO LoadDll
                {
                    get { return GetDebugInfo<LOAD_DLL_DEBUG_INFO>(); }
                }

                private T GetDebugInfo<T>() where T : struct
                {
                    GCHandle handle = GCHandle.Alloc(this.debugInfo, GCHandleType.Pinned);
                    T result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
                    handle.Free();
                    return result;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            struct LOAD_DLL_DEBUG_INFO
            {
                public IntPtr hFile;
                public IntPtr lpBaseOfDll;
                public uint dwDebugInfoFileOffset;
                public uint nDebugInfoSize;
                public IntPtr lpImageName;
                public ushort fUnicode;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct CREATE_PROCESS_DEBUG_INFO
            {
                public IntPtr hFile;
                public IntPtr hProcess;
                public IntPtr hThread;
                public IntPtr lpBaseOfImage;
                public uint dwDebugInfoFileOffset;
                public uint nDebugInfoSize;
                public IntPtr lpThreadLocalBase;
                public IntPtr lpStartAddress;
                public IntPtr lpImageName;
                public ushort fUnicode;
            }

            [StructLayout(LayoutKind.Explicit, Size = 716)]//0x2cc
            unsafe struct CONTEXT86
            {
                [FieldOffset(0)]
                public CONTEXT_FLAGS ContextFlags;
                [FieldOffset(4)]
                public uint Dr0;
                [FieldOffset(8)]
                public uint Dr1;
                [FieldOffset(12)]
                public uint Dr2;
                [FieldOffset(16)]
                public uint Dr3;
                [FieldOffset(20)]
                public uint Dr6;
                [FieldOffset(24)]
                public uint Dr7;
                [FieldOffset(28)]
                [MarshalAs(UnmanagedType.Struct)]
                public FloatingSaveArea FloatingSave;
                [FieldOffset(140)]
                public uint SegGs;
                [FieldOffset(144)]
                public uint SegFs;
                [FieldOffset(148)]
                public uint SegEs;
                [FieldOffset(152)]
                public uint SegDs;
                [FieldOffset(156)]
                public uint Edi;
                [FieldOffset(160)]
                public uint Esi;
                [FieldOffset(164)]
                public uint Ebx;
                [FieldOffset(168)]
                public uint Edx;
                [FieldOffset(172)]
                public uint Ecx;
                [FieldOffset(176)]
                public uint Eax;
                [FieldOffset(180)]
                public uint Ebp;
                [FieldOffset(184)]
                public uint Eip;
                [FieldOffset(188)]
                public uint SegCs;
                [FieldOffset(192)]
                public uint EFlags;
                [FieldOffset(196)]
                public uint Esp;
                [FieldOffset(200)]
                public uint SegSs;
                [FieldOffset(204)]
                //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
                public byte ExtendedRegisters;//public byte[] ExtendedRegisters;
                //716
            }

            [StructLayout(LayoutKind.Explicit, Size = 112)]//0x70
            unsafe struct FloatingSaveArea
            {
                [FieldOffset(0)]//28
                public uint ControlWord;
                [FieldOffset(4)]//32
                public uint StatusWord;
                [FieldOffset(8)]//36
                public uint TagWord;
                [FieldOffset(12)]//40
                public uint ErrorOffset;
                [FieldOffset(16)]//44
                public uint ErrorSelector;
                [FieldOffset(20)]//48
                public uint DataOffset;
                [FieldOffset(24)]//52
                public uint DataSelector;
                [FieldOffset(28)]//56
                //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
                public byte RegisterArea;//public byte[] RegisterArea;
                [FieldOffset(108)]//136
                public uint Cr0NpxState;
                //140
            }

            /*[StructLayout(LayoutKind.Explicit, Size = 1232)]
            unsafe struct CONTEXT64
            {
                // Register Parameter Home Addresses
                [FieldOffset(0x0)]
                internal ulong P1Home;
                [FieldOffset(0x8)]
                internal ulong P2Home;
                [FieldOffset(0x10)]
                internal ulong P3Home;
                [FieldOffset(0x18)]
                internal ulong P4Home;
                [FieldOffset(0x20)]
                internal ulong P5Home;
                [FieldOffset(0x28)]
                internal ulong P6Home;
                // Control Flags
                [FieldOffset(0x30)]
                internal CONTEXT_FLAGS ContextFlags;
                [FieldOffset(0x34)]
                internal uint MxCsr;
                // Segment Registers and Processor Flags
                [FieldOffset(0x38)]
                internal ushort SegCs;
                [FieldOffset(0x3a)]
                internal ushort SegDs;
                [FieldOffset(0x3c)]
                internal ushort SegEs;
                [FieldOffset(0x3e)]
                internal ushort SegFs;
                [FieldOffset(0x40)]
                internal ushort SegGs;
                [FieldOffset(0x42)]
                internal ushort SegSs;
                [FieldOffset(0x44)]
                internal uint EFlags;
                // Debug Registers
                [FieldOffset(0x48)]
                internal ulong Dr0;
                [FieldOffset(0x50)]
                internal ulong Dr1;
                [FieldOffset(0x58)]
                internal ulong Dr2;
                [FieldOffset(0x60)]
                internal ulong Dr3;
                [FieldOffset(0x68)]
                internal ulong Dr6;
                [FieldOffset(0x70)]
                internal ulong Dr7;
                // Integer Registers
                [FieldOffset(0x78)]
                internal ulong Rax;
                [FieldOffset(0x80)]
                internal ulong Rcx;
                [FieldOffset(0x88)]
                internal ulong Rdx;
                [FieldOffset(0x90)]
                internal ulong Rbx;
                [FieldOffset(0x98)]
                internal ulong Rsp;
                [FieldOffset(0xa0)]
                internal ulong Rbp;
                [FieldOffset(0xa8)]
                internal ulong Rsi;
                [FieldOffset(0xb0)]
                internal ulong Rdi;
                [FieldOffset(0xb8)]
                internal ulong R8;
                [FieldOffset(0xc0)]
                internal ulong R9;
                [FieldOffset(0xc8)]
                internal ulong R10;
                [FieldOffset(0xd0)]
                internal ulong R11;
                [FieldOffset(0xd8)]
                internal ulong R12;
                [FieldOffset(0xe0)]
                internal ulong R13;
                [FieldOffset(0xe8)]
                internal ulong R14;
                [FieldOffset(0xf0)]
                internal ulong R15;
                // Program Counter
                [FieldOffset(0xf8)]
                internal ulong Rip;
                // Floating Point State
                [FieldOffset(0x100)]
                internal ulong FltSave;
                [FieldOffset(0x120)]
                internal ulong Legacy;
                [FieldOffset(0x1a0)]
                internal ulong Xmm0;
                [FieldOffset(0x1b0)]
                internal ulong Xmm1;
                [FieldOffset(0x1c0)]
                internal ulong Xmm2;
                [FieldOffset(0x1d0)]
                internal ulong Xmm3;
                [FieldOffset(0x1e0)]
                internal ulong Xmm4;
                [FieldOffset(0x1f0)]
                internal ulong Xmm5;
                [FieldOffset(0x200)]
                internal ulong Xmm6;
                [FieldOffset(0x210)]
                internal ulong Xmm7;
                [FieldOffset(0x220)]
                internal ulong Xmm8;
                [FieldOffset(0x230)]
                internal ulong Xmm9;
                [FieldOffset(0x240)]
                internal ulong Xmm10;
                [FieldOffset(0x250)]
                internal ulong Xmm11;
                [FieldOffset(0x260)]
                internal ulong Xmm12;
                [FieldOffset(0x270)]
                internal ulong Xmm13;
                [FieldOffset(0x280)]
                internal ulong Xmm14;
                [FieldOffset(0x290)]
                internal ulong Xmm15;
                // Vector Registers
                [FieldOffset(0x300)]
                internal ulong VectorRegister;
                [FieldOffset(0x4a0)]
                internal ulong VectorControl;
                // Special Debug Control Registers
                [FieldOffset(0x4a8)]
                internal ulong DebugControl;
                [FieldOffset(0x4b0)]
                internal ulong LastBranchToRip;
                [FieldOffset(0x4b8)]
                internal ulong LastBranchFromRip;
                [FieldOffset(0x4c0)]
                internal ulong LastExceptionToRip;
                [FieldOffset(0x4c8)]
                internal ulong LastExceptionFromRip;
            }*/

            [Flags]
            enum CONTEXT_FLAGS : uint
            {
                i386 = 0x10000,
                i486 = 0x10000,   //  same as i386
                CONTROL = i386 | 0x01, // SS:SP, CS:IP, FLAGS, BP
                INTEGER = i386 | 0x02, // AX, BX, CX, DX, SI, DI
                SEGMENTS = i386 | 0x04, // DS, ES, FS, GS
                FLOATING_POINT = i386 | 0x08, // 387 state
                DEBUG_REGISTERS = i386 | 0x10, // DB 0-3,6,7
                EXTENDED_REGISTERS = i386 | 0x20, // cpu specific extensions
                FULL = CONTROL | INTEGER | SEGMENTS,
                ALL = CONTROL | INTEGER | SEGMENTS | FLOATING_POINT | DEBUG_REGISTERS | EXTENDED_REGISTERS
            }

            static class DllInjector
            {
                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, int dwProcessId);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern Int32 CloseHandle(IntPtr hObject);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr GetModuleHandle(string lpModuleName);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, IntPtr size, out IntPtr lpNumberOfBytesWritten);

                [DllImport("kernel32.dll", SetLastError = true)]
                static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttribute, IntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

                const uint MEM_COMMIT = 0x1000;
                const uint MEM_RESERVE = 0x2000;
                const uint MEM_RELEASE = 0x8000;

                const uint PAGE_EXECUTE = 0x10;
                const uint PAGE_EXECUTE_READ = 0x20;
                const uint PAGE_EXECUTE_READWRITE = 0x40;
                const uint PAGE_EXECUTE_WRITECOPY = 0x80;
                const uint PAGE_NOACCESS = 0x01;

                public static bool Inject(Process process, string dllPath)
                {
                    bool result = false;
                    IntPtr hProcess = OpenProcess((0x2 | 0x8 | 0x10 | 0x20 | 0x400), 1, process.Id);
                    if (hProcess != IntPtr.Zero)
                    {
                        result = Inject(hProcess, dllPath);
                        CloseHandle(hProcess);
                    }
                    return result;
                }

                public static bool Inject(IntPtr process, string dllPath)
                {
                    if (process == IntPtr.Zero)
                    {
                        LogError("Process handle is 0");
                        return false;
                    }

                    if (!File.Exists(dllPath))
                    {
                        LogError("Couldn't find the dll to inject (" + dllPath + ")");
                        return false;
                    }

                    //dllPath = Path.GetFullPath(dllPath);
                    byte[] buffer = Encoding.ASCII.GetBytes(dllPath);

                    IntPtr libAddr = IntPtr.Zero;
                    IntPtr memAddr = IntPtr.Zero;
                    IntPtr threadAddr = IntPtr.Zero;

                    try
                    {
                        if (process == IntPtr.Zero)
                        {
                            LogError("Unable to attach to process");
                            return false;
                        }

                        libAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                        if (libAddr == IntPtr.Zero)
                        {
                            LogError("Unable to find address of LoadLibraryA");
                            return false;
                        }

                        memAddr = VirtualAllocEx(process, IntPtr.Zero, (IntPtr)buffer.Length, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                        if (memAddr == IntPtr.Zero)
                        {
                            LogError("Unable to allocate memory in the target process");
                            return false;
                        }

                        IntPtr bytesWritten;
                        if (!WriteProcessMemory(process, memAddr, buffer, (IntPtr)buffer.Length, out bytesWritten) ||
                            (int)bytesWritten != buffer.Length)
                        {
                            LogError("Unable to write to target process memory");
                            return false;
                        }

                        IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, IntPtr.Zero, libAddr, memAddr, 0, IntPtr.Zero);
                        if (thread == IntPtr.Zero)
                        {
                            LogError("Unable to start thread in target process");
                            return false;
                        }

                        return true;
                    }
                    finally
                    {
                        if (threadAddr != IntPtr.Zero)
                        {
                            CloseHandle(threadAddr);
                        }
                        if (memAddr != IntPtr.Zero)
                        {
                            VirtualFreeEx(process, memAddr, IntPtr.Zero, MEM_RELEASE);
                        }
                    }
                }

                private static void LogError(string str)
                {
                    string error = "DllInjector error: " + str + " - ErrorCode: " + Marshal.GetLastWin32Error();
                    Console.WriteLine(error);
                    System.Diagnostics.Debug.WriteLine(error);
                }
            }
        }

        class ConsoleHelper
        {
            [DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("kernel32.dll")]
            private static extern bool AllocConsole();

            [DllImport("kernel32.dll")]
            private static extern bool FreeConsole();

            [DllImport("kernel32.dll")]
            private static extern IntPtr GetConsoleWindow();

            [DllImport("kernel32.dll")]
            private static extern IntPtr GetStdHandle(UInt32 nStdHandle);

            [DllImport("kernel32.dll")]
            private static extern void SetStdHandle(UInt32 nStdHandle, IntPtr handle);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool IsWindowVisible(IntPtr hWnd);

            private const UInt32 StdOutputHandle = 0xFFFFFFF5;

            private static IntPtr consoleHandle;
            internal static TextWriter output;

            private const int SW_SHOW = 5;
            private const int SW_HIDE = 0;

            private static string title;
            public static string Title
            {
                get
                {
                    if (consoleHandle == IntPtr.Zero)
                    {
                        return title;
                    }
                    StringBuilder stringBuilder = new StringBuilder(1024);
                    GetConsoleTitle(stringBuilder, (uint)stringBuilder.Capacity);
                    return stringBuilder.ToString();
                }
                set
                {
                    title = value;
                    if (consoleHandle != IntPtr.Zero)
                    {
                        SetConsoleTitle(value);
                    }
                }
            }

            static ConsoleHelper()
            {
                output = new ConsoleTextWriter();
                //Console.SetOut(output);

                consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                {
                    Title = title;
                }
            }

            public static bool IsConsoleVisible
            {
                get { return (consoleHandle = GetConsoleWindow()) != IntPtr.Zero && IsWindowVisible(consoleHandle); }
                //get { return (consoleHandle = GetConsoleWindow()) != IntPtr.Zero; }
            }

            public static void ToggleConsole()
            {
                consoleHandle = GetConsoleWindow();
                if (consoleHandle == IntPtr.Zero)
                {
                    AllocConsole();
                }
                else
                {
                    FreeConsole();
                }
            }

            public static void ShowConsole()
            {
                consoleHandle = GetConsoleWindow();
                if (consoleHandle == IntPtr.Zero)
                {
                    AllocConsole();
                    consoleHandle = GetConsoleWindow();
                }
                else
                {
                    ShowWindow(consoleHandle, SW_SHOW);
                }

                if (consoleHandle != IntPtr.Zero)
                {
                    Title = title != null ? title : string.Empty;
                }
            }

            public static void HideConsole()
            {
                consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                {
                    ShowWindow(consoleHandle, SW_HIDE);
                }
            }

            public static void CloseConsole()
            {
                consoleHandle = GetConsoleWindow();
                if (consoleHandle != IntPtr.Zero)
                {
                    FreeConsole();
                }
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern uint GetConsoleTitle(StringBuilder lpConsoleTitle, uint nSize);

            [DllImport("kernel32.dll")]
            static extern bool SetConsoleTitle(string lpConsoleTitle);
        }

        public class ConsoleTextWriter : TextWriter
        {
            public override Encoding Encoding { get { return Encoding.UTF8; } }

            // TODO: WriteConsole may not write all the data, chunk this data into several calls if nessesary

            // WriteConsoleW issues reference:
            // https://svn.apache.org/repos/asf/logging/log4net/tags/log4net-1_2_9/src/Appender/ColoredConsoleAppender.cs

            public override void Write(string value)
            {
                uint written;
                if (!WriteConsoleW(new IntPtr(7), value, (uint)value.Length, out written, IntPtr.Zero) || written < value.Length)
                {
                    if (GetConsoleWindow() != IntPtr.Zero)
                    {
                        //System.Diagnostics.Debugger.Break();
                    }
                }
            }

            public override void WriteLine(string value)
            {
                value = value + Environment.NewLine;
                uint written;
                if (!WriteConsoleW(new IntPtr(7), value, (uint)value.Length, out written, IntPtr.Zero) || written < value.Length)
                {
                    if (GetConsoleWindow() != IntPtr.Zero)
                    {
                        //System.Diagnostics.Debugger.Break();
                    }
                }
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            static extern bool WriteConsoleW(IntPtr hConsoleOutput, [MarshalAs(UnmanagedType.LPWStr)] string lpBuffer,
               uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten,
               IntPtr lpReserved);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            static extern bool WriteConsole(IntPtr hConsoleOutput, string lpBuffer,
               uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten,
               IntPtr lpReserved);

            [DllImport("kernel32.dll")]
            static extern bool SetConsoleCP(int wCodePageID);

            [DllImport("kernel32.dll")]
            static extern uint GetACP();

            [DllImport("kernel32.dll")]
            static extern IntPtr GetConsoleWindow();
        }
    }
}
