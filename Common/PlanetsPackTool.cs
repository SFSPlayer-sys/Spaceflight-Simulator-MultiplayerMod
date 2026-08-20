using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
#if NET48
namespace MultiplayerSFS.Common
#else
namespace MultiplayerSFS.ServerCommon
#endif
{
    public static class PlanetsPackTool
    {
        public static byte[] Pack(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException(directoryPath);
            List<string> files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
            files.Sort(StringComparer.Ordinal);
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(files.Count);
                string basePath = directoryPath.TrimEnd('\\', '/');
                foreach (string file in files)
                {
                    byte[] data = File.ReadAllBytes(file);
                    byte[] relBytes = Encoding.UTF8.GetBytes(file.Substring(basePath.Length + 1).Replace('\\', '/'));
                    writer.Write(relBytes.Length);
                    writer.Write(relBytes);
                    writer.Write(data.Length);
                    writer.Write(data);
                }
                writer.Flush();
                return ms.ToArray();
            }
        }
        public static void Unpack(byte[] data, string destDirectory)
        {
            Directory.CreateDirectory(destDirectory);
            string root = Path.GetFullPath(destDirectory);
            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string rel = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())).Replace('/', Path.DirectorySeparatorChar);
                    byte[] fileData = reader.ReadBytes(reader.ReadInt32());
                    string full = Path.GetFullPath(Path.Combine(destDirectory, rel));
                    if (!full.StartsWith(root)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllBytes(full, fileData);
                }
            }
        }
        public static string ComputeHash(byte[] data)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
