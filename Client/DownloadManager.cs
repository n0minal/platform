﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GTA;
using GTANetworkShared;

namespace GTANetwork
{
    public static class DownloadManager
    {
        private static ScriptCollection PendingScripts = new ScriptCollection() { ClientsideScripts = new List<ClientsideScript>()};

        private static FileTransferId CurrentFile;
        public static bool StartDownload(int id, string path, FileType type, int len, string md5hash, string resource)
        {
            if (CurrentFile != null)
            {
                return false;
            }

            if ((type == FileType.Normal || type == FileType.Script) && Directory.Exists(FileTransferId._DOWNLOADFOLDER_ + path.Replace(Path.GetFileName(path), "")) &&
                File.Exists(FileTransferId._DOWNLOADFOLDER_ + path))
            {
                byte[] myData;

                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(FileTransferId._DOWNLOADFOLDER_ + path))
                {
                    myData = md5.ComputeHash(stream);
                }

                if (myData.Select(byt => byt.ToString("x2")).Aggregate((left, right) => left + right) == md5hash)
                {
                    if (type == FileType.Script)
                    {
                        PendingScripts.ClientsideScripts.Add(
                            LoadScript(path, resource, File.ReadAllText(FileTransferId._DOWNLOADFOLDER_ + path)));
                    }

                    return false;
                }
            }

            CurrentFile = new FileTransferId(id, path, type, len, resource);
            return true;
        }

        public static ClientsideScript LoadScript(string file, string resource, string script)
        {
            var csScript = new ClientsideScript();

            csScript.Filename = Path.GetFileNameWithoutExtension(file)?.Replace('.', '_');
            csScript.ResourceParent = resource;
            csScript.Script = script;

            return csScript;
        }

        public static void Cancel()
        {
            CurrentFile = null;
        }

        public static void DownloadPart(int id, byte[] bytes)
        {
            if (CurrentFile == null || CurrentFile.Id != id)
            {
                return;
            }
            
            CurrentFile.Write(bytes);
            UI.ShowSubtitle("Downloading " +
                            ((CurrentFile.Type == FileType.Normal || CurrentFile.Type == FileType.Script)
                                ? CurrentFile.Filename
                                : CurrentFile.Type.ToString()) + ": " +
                            (CurrentFile.DataWritten/(float) CurrentFile.Length).ToString("P"));
        }

        public static void End(int id)
        {
            if (CurrentFile == null || CurrentFile.Id != id)
            {
                Util.SafeNotify($"END Channel mismatch! We have {CurrentFile?.Id} and supplied was {id}");
                return;
            }
            
            if (CurrentFile.Type == FileType.Map)
            {
                var obj = Main.DeserializeBinary<ServerMap>(CurrentFile.Data.ToArray()) as ServerMap;
                if (obj == null)
                {
                    Util.SafeNotify("ERROR DOWNLOADING MAP: NULL");
                }
                else
                {
                    Main.AddMap(obj);
                }
            }
            else if (CurrentFile.Type == FileType.Script)
            {
                var scriptText = Encoding.UTF8.GetString(CurrentFile.Data.ToArray());
                var newScript = LoadScript(CurrentFile.Filename, CurrentFile.Resource, scriptText);
                PendingScripts.ClientsideScripts.Add(newScript);
            }
            else if (CurrentFile.Type == FileType.EndOfTransfer)
            {
                Main.StartClientsideScripts(PendingScripts);
                PendingScripts.ClientsideScripts.Clear();

                if (Main.JustJoinedServer)
                {
                    World.RenderingCamera = null;
                    Main.MainMenu.TemporarilyHidden = false;
                    Main.MainMenu.Visible = false;
                    Main.JustJoinedServer = false;
                }

                Main.InvokeFinishedDownload();
            }

            CurrentFile.Dispose();
            CurrentFile = null;
        }
    }

    public class FileTransferId : IDisposable
    {
        public static string _DOWNLOADFOLDER_ = Main.GTANInstallDir + "\\resources\\";

        public int Id { get; set; }
        public string Filename { get; set; }
        public FileType Type { get; set; }
        public FileStream Stream { get; set; }
        public int Length { get; set; }
        public int DataWritten { get; set; }
        public List<byte> Data { get; set; }
        public string Resource { get; set; }

        public FileTransferId(int id, string name, FileType type, int len, string resource)
        {
            Id = id;
            Filename = name;
            Type = type;
            Length = len;
            Resource = resource;

            
            if ((type == FileType.Normal || type == FileType.Script) && name != null)
            {
                if (!Directory.Exists(_DOWNLOADFOLDER_ + name.Replace(Path.GetFileName(name), "")))
                    Directory.CreateDirectory(_DOWNLOADFOLDER_ + name.Replace(Path.GetFileName(name), ""));
                Stream = new FileStream(_DOWNLOADFOLDER_ + name,
                    File.Exists(_DOWNLOADFOLDER_ + name) ? FileMode.Truncate : FileMode.CreateNew);
            }

            if (type != FileType.Normal)
            {
                Data = new List<byte>();
            }
        }

        public void Write(byte[] data)
        {
            if (Stream != null)
            {
                Stream.Write(data, 0, data.Length);
            }

            if (Data != null)
            {
                Data.AddRange(data);
            }

            DataWritten += data.Length;
        }

        public void Dispose()
        {
            if (Stream != null)
            {
                Stream.Close();
                Stream.Dispose();
            }
        }
    }
}