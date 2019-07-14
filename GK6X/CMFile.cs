using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GK6X
{
    static class CMFile
    {
        static Dictionary<CMFileType, byte[]> fileTypes = new Dictionary<CMFileType, byte[]>()
        {
            { CMFileType.Unknown, new byte[] { 0xCE, 0xDE, 0xD0, 0xA7 } },// Chinese letters? "ÎÞÐ§"?
            { CMFileType.Profile, Encoding.ASCII.GetBytes("PROFILE") },
            { CMFileType.Light, Encoding.ASCII.GetBytes("LIGHT") },
            { CMFileType.Statastic, Encoding.ASCII.GetBytes("STATASTIC") },
            { CMFileType.Appconf, Encoding.ASCII.GetBytes("APPCONF") },
            { CMFileType.Macro, Encoding.ASCII.GetBytes("MACRO") }
        };

        const uint fileSignature = 0x434D4631;// Magic / signature "1FMC"

        public static byte[] Load(string path)
        {
            if (File.Exists(path))
            {
                return Decrypt(File.ReadAllBytes(path), path);
            }
            return null;
        }

        public static byte[] Decrypt(byte[] buffer)
        {
            return Decrypt(buffer, null);
        }

        private static byte[] Decrypt(byte[] buffer, string file)
        {
            using (MemoryStream stream = new MemoryStream(buffer))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != fileSignature)
                {
                    Log("Bad file signature", buffer, file);
                    return null;
                }

                // Header crc is at offset 4, written as 4 bytes (but still a crc16)
                // (this is a crc of the first 32 bytes (where the crc bytes are 0)
                stream.Position = 4;
                ushort headerCrc = reader.ReadUInt16();

                // Timestamp is at offset 8, written as 4 bytes
                stream.Position = 8;
                int timestamp = reader.ReadInt32();

                // Length is at offset 12, written as 4 bytes
                stream.Position = 12;
                int dataLength = reader.ReadInt32();

                // Data crc is at offset 16, written as 4 bytes (but still a crc16)
                stream.Position = 16;
                ushort dataCrc = reader.ReadUInt16();

                // File type is at offset 20, written as 4 bytes
                stream.Position = 20;
                int fileType = reader.ReadInt32();

                // File type (string) is at offset 24, written as 8 bytes, padded with 00
                stream.Position = 24;
                byte[] fileTypeStrBuffer = reader.ReadBytes(8);
                // First crc the file type name, then get crc the file type name (including zeroed bytes)
                string fileTypeStr = Encoding.ASCII.GetString(fileTypeStrBuffer).TrimEnd('\0');
                ushort encryptionKey = Crc16.GetCrc(Encoding.ASCII.GetBytes(fileTypeStr));
                encryptionKey = Crc16.GetCrc(fileTypeStrBuffer, 0, encryptionKey);

                // Data is at offset 32
                stream.Position = 32;
                byte[] data = reader.ReadBytes(dataLength);
                ushort calculatedDataCrc = Decrypt(data, encryptionKey);

                if (dataCrc != calculatedDataCrc)
                {
                    Log("File has an invalid data crc", buffer, file);
                }

                if (stream.Position != stream.Length)
                {
                    Log("File has trailing bytes", buffer, file);
                }

                stream.Position = 0;
                byte[] header = reader.ReadBytes(32);
                header[4] = 0;
                header[5] = 0;
                header[6] = 0;
                header[7] = 0;
                ushort calculatedHeaderCrc = Crc16.GetCrc(header);
                if (headerCrc != calculatedHeaderCrc)
                {
                    Log("File has an invalid header crc", buffer, file);
                }

                return data;
            }
        }

        public static byte[] Encrypt(byte[] fileData, CMFileType fileType)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                byte[] fileTypeStrBuffer = fileTypes[fileType];
                string fileTypeStr = Encoding.ASCII.GetString(fileTypeStrBuffer).TrimEnd('\0');
                ushort encryptionKey = Crc16.GetCrc(Encoding.ASCII.GetBytes(fileTypeStr));
                encryptionKey = Crc16.GetCrc(fileTypeStrBuffer, 0, encryptionKey);

                byte[] encryptedData = new byte[fileData.Length];
                Buffer.BlockCopy(fileData, 0, encryptedData, 0, fileData.Length);
                ushort dataCrc = Encrypt(encryptedData, encryptionKey);

                // Offset 0 (file signature)
                writer.Write(fileSignature);

                // Offset 4 (header crc - to be built after the header is fully formed)
                writer.Write((int)0);

                // Offset 8 (timestamp)
                writer.Write(GetTimeStamp(DateTime.Now));

                // Offset 12 (data length)
                writer.Write(fileData.Length);

                // Offset 16 (data crc)
                writer.Write((int)dataCrc);

                // Offset 20 (file type)
                writer.Write((int)fileType);

                // Offset 24-32 (file type string)
                for (int i = 0; i < 8; i++)
                {
                    writer.Write((byte)(i < fileTypeStrBuffer.Length ? fileTypeStrBuffer[i] : 0));
                }

                writer.Write(encryptedData);

                // Get the header bytes, calculate the crc, and insert the crc into the header
                long tempPos = stream.Position;
                stream.Position = 0;
                byte[] header = new byte[32];
                stream.Read(header, 0, header.Length);
                ushort headerCrc = Crc16.GetCrc(header);
                stream.Position = 4;
                writer.Write(headerCrc);
                stream.Position = tempPos;
                
                return stream.ToArray();
            }
        }

        private static int GetTimeStamp(DateTime dateTime)
        {
            return (int)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        private static ushort Encrypt(byte[] buffer, ushort key)
        {
            ushort dataCrc = 0xFFFF;
            for (int i = 0; i < buffer.Length; i++)
            {
                ushort tempKey = key;
                key = (ushort)(Crc16.table[buffer[i] ^ (byte)(key >> 8)] ^ (ushort)(key << 8));
                dataCrc = (ushort)(Crc16.table[buffer[i] ^ (byte)(dataCrc >> 8)] ^ (ushort)(dataCrc << 8));
                buffer[i] = (byte)(buffer[i] ^ tempKey);
            }
            return dataCrc;
        }

        private static ushort Decrypt(byte[] buffer, ushort key)
        {
            ushort dataCrc = 0xFFFF;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)(buffer[i] ^ key);
                key = (ushort)(Crc16.table[buffer[i] ^ (byte)(key >> 8)] ^ (ushort)(key << 8));
                dataCrc = (ushort)(Crc16.table[buffer[i] ^ (byte)(dataCrc >> 8)] ^ (ushort)(dataCrc << 8));
            }
            return dataCrc;
        }

        private static void Log(string str, byte[] buffer, string file)
        {
            Debug.WriteLine("[CMFile-ERROR] " + str + " (file: " + file + ")");
        }
    }

    // These values need to be correct as they form part of the crc calculation
    public enum CMFileType
    {
        /// <summary>
        /// 0=??? some chinese characters? (CE DE D0 A7 00)
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// PROFILE
        /// </summary>
        Profile = 1,
        /// <summary>
        /// LIGHT
        /// </summary>
        Light = 2,
        /// <summary>
        /// STATASTIC
        /// </summary>
        Statastic = 3,
        /// <summary>
        /// APPCONF
        /// </summary>
        Appconf = 4,
        /// <summary>
        /// MACRO
        /// </summary>
        Macro = 5
    }
}
