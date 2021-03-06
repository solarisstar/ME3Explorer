﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ManagedLZO;
using ME1Explorer;
using ME1Explorer.Helper;
using Gibbed.IO;
using AmaroK86.MassEffect3.ZlibBlock;
using KFreonLib.Debugging;
using UsefulThings;

namespace ME1Explorer
{
    public class PCCObject
    {
        public struct NameEntry
        {
            public string name;
            public int Unk;
            public int flags;
        }
        public class ExportEntry
        {
            internal byte[] header; //Properties, not raw data
            public int idxClass { get { return BitConverter.ToInt32(header, 0); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 0, sizeof(int)); InfoChanged(); } }
            public int idxClassParent { get { return BitConverter.ToInt32(header, 4); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 4, sizeof(int)); } }
            public int idxLink { get { return BitConverter.ToInt32(header, 8); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 8, sizeof(int)); InfoChanged(); } }
            public int PackageNameID;
            public int idxObjectName { get { return BitConverter.ToInt32(header, 12); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 12, sizeof(int)); InfoChanged(); } }
            public int indexValue { get { return BitConverter.ToInt32(header, 16); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 16, sizeof(int)); InfoChanged(); } }
            public int idxArchtype { get { return BitConverter.ToInt32(header, 20); } private set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 20, sizeof(int)); InfoChanged(); } }
            public long ObjectFlags { get { return BitConverter.ToInt64(header, 24); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 64, sizeof(long)); InfoChanged(); } }
            public string ObjectName;
            public string PackageName 
            { 
                get 
                {
                    string temppack = PackageFullName;
                    if (temppack == "." || String.IsNullOrEmpty(PackageFullName))
                        return "";
                    temppack = temppack.Remove(temppack.Length - 1);
                    if (temppack.Split('.').Length > 1)
                        return temppack.Split('.')[temppack.Split('.').Length - 1];
                    else
                        return temppack.Split('.')[0];
                } 
            }
            public string ArchtypeName { get { int val = idxArchtype; if (val < 0)  return pccRef.Names[pccRef.Imports[val * -1 - 1].idxObjectName]; else if (val > 0) return pccRef.Names[pccRef.Exports[val].idxObjectName]; else return "None"; } }
            public string PackageFullName;
            public string ClassParent { get { int val = idxClassParent; if (val < 0) return pccRef.Names[BitConverter.ToInt32(pccRef.Imports[val * -1 - 1].raw, 20)]; else if (val > 0) return pccRef.Names[pccRef.Exports[val - 1].idxObjectName]; else return "Class"; } }
            public string ClassName;
            public byte[] flag
            {
                get
                {
                    byte[] val = new byte[4];
                    Buffer.BlockCopy(header, 28, val, 0, 4);
                    return val;
                }
            }
            public int flagint
            {
                get
                {
                    byte[] val = new byte[4];
                    Buffer.BlockCopy(header, 28, val, 0, 4);
                    return BitConverter.ToInt32(val, 0);
                }
            }

            public string GetFullPath
            {
                get
                {
                    string s = "";
                    if (PackageFullName != "Class" && PackageFullName != "Package")
                        s += PackageFullName + ".";
                    s += ObjectName;
                    return s;
                }
            }

            public PCCObject pccRef;
            public int DataSize { get { return BitConverter.ToInt32(header, 32); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 32, sizeof(int)); } }
            public int DataOffset { get { return BitConverter.ToInt32(header, 36); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 36, sizeof(int)); } }
            public byte[] Data
            {
                get { byte[] val = new byte[DataSize]; pccRef.listsStream.Seek(DataOffset, SeekOrigin.Begin); val = pccRef.listsStream.ReadBytes(DataSize); return val; }
                set
                {
                    if (value.Length > DataSize)
                    {
                        pccRef.listsStream.Seek(0, SeekOrigin.End);
                        DataOffset = (int)pccRef.listsStream.Position;
                        pccRef.listsStream.WriteBytes(value);
                        pccRef.LastExport = this;
                        MoveNames();
                    }
                    else
                    {
                        pccRef.listsStream.Seek(DataOffset, SeekOrigin.Begin);
                        pccRef.listsStream.WriteBytes(value);
                    }
                    if (value.Length != DataSize)
                    {
                        DataSize = value.Length;
                        pccRef.listsStream.Seek(infoOffset, SeekOrigin.Begin);
                        pccRef.listsStream.WriteBytes(header);
                    }
                }
            }
            public bool hasChanged;
            public int infoOffset;

            private void MoveNames()
            {
                pccRef.NameOffset = (int)pccRef.listsStream.Position;
                foreach (string name in pccRef.Names)
                {
                    pccRef.listsStream.WriteValueS32(name.Length + 1);
                    pccRef.listsStream.WriteString(name);
                    pccRef.listsStream.WriteByte(0);
                    pccRef.listsStream.WriteValueS32(0);
                    pccRef.listsStream.WriteValueS32(458768);
                }
            }

            private void InfoChanged()
            {
                pccRef.listsStream.Seek(infoOffset, SeekOrigin.Begin);
                pccRef.listsStream.WriteBytes(header);
            }
        }
        public struct ImportEntry
        {
            public byte[] raw;
            internal PCCObject pccRef;
            public int link { get { return BitConverter.ToInt32(raw, 16); } private set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, raw, 16, sizeof(int)); } }
            public int idxPackageName { get { return BitConverter.ToInt32(raw, 0); } private set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, raw, 0, sizeof(int)); } }
            public int idxObjectName { get { return BitConverter.ToInt32(raw, 20); } private set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, raw, 20, sizeof(int)); } }
            public int idxClassName { get { return BitConverter.ToInt32(raw, 8); } private set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, raw, 8, sizeof(int)); } }
            public string PackageName { get { return pccRef.Names[idxPackageName]; } }
            public string ObjectName { get { return pccRef.Names[idxObjectName]; } }
            public string ClassName { get { return pccRef.Names[idxClassName]; } }
        }

        public byte[] header;
        private uint magic { get { return BitConverter.ToUInt32(header, 0); } }
        private ushort lowVers { get { return BitConverter.ToUInt16(header, 4); } }
        private ushort highVers { get { return BitConverter.ToUInt16(header, 6); } }
        public int expDataBegOffset { get { return BitConverter.ToInt32(header, 8); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, 8, sizeof(int)); } }
        public int nameSize { get { int val = BitConverter.ToInt32(header, 12); if (val < 0) return val * -2; else return val; } }
        public uint flags { get { return BitConverter.ToUInt32(header, 16 + nameSize); } }
        public bool bCompressed
        {
            get { return (flags & 0x02000000) != 0; }
            set
            {
                if (value) // sets the compressed flag if bCompressed set equal to true
                    Buffer.BlockCopy(BitConverter.GetBytes(flags | 0x02000000), 0, header, 16 + nameSize, sizeof(int));
                else // else set to false
                    Buffer.BlockCopy(BitConverter.GetBytes(flags & ~0x02000000), 0, header, 16 + nameSize, sizeof(int));
            }
        }
        public int NameCount { get { return BitConverter.ToInt32(header, nameSize + 20); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 20, sizeof(int)); } }
        public int NameOffset { get { return BitConverter.ToInt32(header, nameSize + 24); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 24, sizeof(int)); } }
        public int ExportCount { get { return BitConverter.ToInt32(header, nameSize + 28); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 28, sizeof(int)); } }
        public int ExportOffset { get { return BitConverter.ToInt32(header, nameSize + 32); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 32, sizeof(int)); } }
        public int ImportCount { get { return BitConverter.ToInt32(header, nameSize + 36); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 36, sizeof(int)); } }
        public int ImportOffset { get { return BitConverter.ToInt32(header, nameSize + 40); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, nameSize + 40, sizeof(int)); } }
        public int Generator { get { return BitConverter.ToInt32(header, nameSize + 64); } }
        public int Compression { get { return BitConverter.ToInt32(header, header.Length - 4); } set { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, header, header.Length - 4, sizeof(int)); } }
        public int ExportDataEnd
        {
            get
            {
                return LastExport.DataOffset + LastExport.DataSize;
            }
        }
        private ExportEntry LastExport;

        public uint PackageFlags;
        public int NumChunks;
        public MemoryStream listsStream;
        public List<string> Names;
        public List<ImportEntry> Imports;
        public List<ExportEntry> Exports;
        public byte[] Header;
        public int _HeaderOff;
        public MemoryStream m;
        SaltLZOHelper lzo;
        public string fullname;
        public string pccFileName;

        public PCCObject(String path)
        {
            lzo = new SaltLZOHelper();
            fullname = path;
            BitConverter.IsLittleEndian = true;
            DebugOutput.PrintLn("Load file : " + path);
            pccFileName = Path.GetFullPath(path);
            MemoryStream tempStream = new MemoryStream();
            if (!File.Exists(pccFileName))
                throw new FileNotFoundException("PCC file not found");
            using (FileStream fs = new FileStream(pccFileName, FileMode.Open, FileAccess.Read))
            {
                FileInfo tempInfo = new FileInfo(pccFileName);
                tempStream.WriteFromStream(fs, tempInfo.Length);
                if (tempStream.Length != tempInfo.Length)
                {
                    throw new FileLoadException("File not fully read in. Try again later");
                }
            }

            tempStream.Seek(12, SeekOrigin.Begin);
            int tempNameSize = tempStream.ReadValueS32();
            tempStream.Seek(64 + tempNameSize, SeekOrigin.Begin);
            int tempGenerator = tempStream.ReadValueS32();
            tempStream.Seek(36 + tempGenerator * 12, SeekOrigin.Current);
            int tempPos = (int)tempStream.Position + 4;
            tempStream.Seek(0, SeekOrigin.Begin);
            header = tempStream.ReadBytes(tempPos);
            tempStream.Seek(0, SeekOrigin.Begin);

            if (magic != ZBlock.magic && magic.Swap() != ZBlock.magic)
            {
                DebugOutput.PrintLn("Magic number incorrect: " + magic);
                throw new FormatException("This is not a pcc file. The magic number is incorrect.");
            }

            if (bCompressed)
            {
                DebugOutput.PrintLn("File is compressed");
                listsStream = lzo.DecompressPCC(tempStream, this);

                //Correct the header
                bCompressed = false;
                listsStream.Seek(0, SeekOrigin.Begin);
                listsStream.WriteBytes(header);

                // Set numblocks to zero
                listsStream.WriteValueS32(0);
                //Write the magic number
                listsStream.WriteBytes(new byte[] { 0xF2, 0x56, 0x1B, 0x4E });
                // Write 4 bytes of 0
                listsStream.WriteValueS32(0);
            }
            else
            {
                DebugOutput.PrintLn("File already decompressed. Reading decompressed data.");
                //listsStream = tempStream;
                listsStream = new MemoryStream();
                tempStream.WriteTo(listsStream);
            }
            tempStream.Dispose();
            ReadNames(listsStream);
            ReadImports(listsStream);
            ReadExports(listsStream);
            LoadExports();
        }

        private void LoadExports()
        {
            DebugOutput.PrintLn("Prefetching Export Name Data...");
            for (int i = 0; i < ExportCount; i++)
            {
                Exports[i].hasChanged = false;
                Exports[i].ObjectName = Names[Exports[i].idxObjectName];
            }
            for (int i = 0; i < ExportCount; i++)
            {
                Exports[i].PackageFullName = FollowLink(Exports[i].idxLink);
                if (String.IsNullOrEmpty(Exports[i].PackageFullName))
                    Exports[i].PackageFullName = "Base Package";
                else if (Exports[i].PackageFullName[Exports[i].PackageFullName.Length - 1] == '.')
                    Exports[i].PackageFullName = Exports[i].PackageFullName.Remove(Exports[i].PackageFullName.Length - 1);
            }
            for (int i = 0; i < ExportCount; i++)
                Exports[i].ClassName = GetClass(Exports[i].idxClass);
        }

        public void SaveToFile(string path)
        {
            DebugOutput.PrintLn("Writing pcc to: " + path + "\nRefreshing header to stream...");
            listsStream.Seek(0, SeekOrigin.Begin);
            listsStream.WriteBytes(header);
            DebugOutput.PrintLn("Opening filestream and writing to disk...");
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                listsStream.WriteTo(fs);
            }
        }

        private void ReadNames(MemoryStream fs)
        {
            DebugOutput.PrintLn("Reading Names...");
            fs.Seek(NameOffset, SeekOrigin.Begin);
            Names = new List<string>();
            for (int i = 0; i < NameCount; i++)
            {
                int len = fs.ReadValueS32();
                string s = "";
                if (len > 0)
                {
                    s = fs.ReadString((uint)(len - 1));
                    fs.Seek(9, SeekOrigin.Current);
                }
                else
                {
                    len *= -1;
                    for (int j = 0; j < len - 1; j++)
                    {
                        s += (char)fs.ReadByte();
                        fs.ReadByte();
                    }
                    fs.Seek(10, SeekOrigin.Current);
                }
                Names.Add(s);
            }
        }

        private void ReadImports(MemoryStream fs)
        {
            DebugOutput.PrintLn("Reading Imports...");
            Imports = new List<ImportEntry>();
            fs.Seek(ImportOffset, SeekOrigin.Begin);
            for (int i = 0; i < ImportCount; i++)
            {
                ImportEntry import = new ImportEntry();
                import.raw = fs.ReadBytes(28);
                import.pccRef = this;
                Imports.Add(import);
            }
        }

        private void ReadExports(MemoryStream fs)
        {
            DebugOutput.PrintLn("Reading Exports...");
            fs.Seek(ExportOffset, SeekOrigin.Begin);
            Exports = new List<ExportEntry>();
            
            for (int i = 0; i < ExportCount; i++)
            {
                long start = fs.Position;
                ExportEntry exp = new ExportEntry();
                exp.pccRef = this;
                exp.infoOffset = (int)start;

                fs.Seek(40, SeekOrigin.Current);
                int count = fs.ReadValueS32();
                fs.Seek(4 + count * 12, SeekOrigin.Current);
                count = fs.ReadValueS32();
                fs.Seek(4 + count * 4, SeekOrigin.Current);
                fs.Seek(16, SeekOrigin.Current);
                long end = fs.Position;
                fs.Seek(start, SeekOrigin.Begin);
                exp.header = fs.ReadBytes((int)(end - start));
                Exports.Add(exp);
                fs.Seek(end, SeekOrigin.Begin);

                if (LastExport == null || exp.DataOffset > LastExport.DataOffset)
                    LastExport = exp;
            }
        }

        public bool isName(int Index)
        {
            return (Index >= 0 && Index < NameCount);
        }

        public bool isImport(int Index)
        {
            return (Index >= 0 && Index < ImportCount);
        }

        public bool isExport(int Index)
        {
            return (Index >= 0 && Index < ExportCount);
        }

        public string GetClass(int Index)
        {
            if (Index > 0 && isExport(Index - 1))
                return Exports[Index - 1].ObjectName;
            if (Index < 0 && isImport(Index * -1 - 1))
                return Imports[Index * -1 - 1].ObjectName;
            return "Class";
        }

        public string FollowLink(int Link)
        {
            string s = "";
            if (Link > 0 && isExport(Link - 1))
            {
                s = Exports[Link - 1].ObjectName + ".";
                s = FollowLink(Exports[Link - 1].idxLink) + s;
            }
            if (Link < 0 && isImport(Link * -1 - 1))
            {
                s = Imports[Link * -1 - 1].ObjectName + ".";
                s = FollowLink(Imports[Link * -1 - 1].link) + s;
            }
            return s;
        }

        public string getNameEntry(int Index)
        {
            string s = "";
            if (isName(Index))
                s = Names[Index];
            return s;
        }

        internal string getObjectName(int p)
        {
            return GetClass(p);
        }

        public string getClassName(int index)
        {
            string s = "";
            if (index > 0)
            {
                s = Names[Exports[index - 1].idxObjectName];
            }
            if (index < 0)
            {
                s = Names[Imports[index * -1 - 1].idxObjectName];
            }
            if (index == 0)
            {
                s = "Class";
            }
            return s;
        }

        internal int FindNameOrAdd(string newName)
        {
            int nameID = 0;
            //First check if name already exists
            for (int i = 0; i < NameCount; i++)
            {
                if (Names[i] == newName)
                {
                    nameID = i;
                    return nameID;
                }
            }

            //Name doesn't exist. Add new one
            if (NameOffset >= ExportDataEnd)
            {
                nameID = NameCount;
                NameCount++;
                Names.Add(newName);
                listsStream.Seek(0, SeekOrigin.End);
                listsStream.WriteValueS32(newName.Length + 1);
                listsStream.WriteString(newName);
                listsStream.WriteByte(0);
                listsStream.WriteValueS32(0);
                listsStream.WriteValueS32(458768);
            }
            else
            {
                listsStream.Seek(0, SeekOrigin.End);
                NameOffset = (int)listsStream.Position;
                foreach (string name in Names)
                {
                    listsStream.WriteValueS32(name.Length + 1);
                    listsStream.WriteString(name);
                    listsStream.WriteByte(0);
                    listsStream.WriteValueS32(0);
                    listsStream.WriteValueS32(458768);
                }
                nameID = NameCount;
                NameCount++;
                Names.Add(newName);
                listsStream.WriteValueS32(newName.Length + 1);
                listsStream.WriteString(newName);
                listsStream.WriteByte(0);
                listsStream.WriteValueS32(0);
                listsStream.WriteValueS32(458768);
            }

            return nameID;
        }

        public int FindExp(string name)
        {
            for (int i = 0; i < ExportCount; i++)
            {
                if (String.Compare(Exports[i].ObjectName, name, true) == 0)
                    return i;
            }
            return -1;
        }

        public int FindExp(string name, string className)
        {
            for (int i = 0; i < ExportCount; i++)
            {
                if (String.Compare(Exports[i].ObjectName, name, true) == 0 && Exports[i].ClassName == className)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Checks whether a name exists in the PCC and returns its index
        /// If it doesn't exist returns -1
        /// </summary>
        /// <param name="nameToFind">The name of the string to find</param>
        /// <returns></returns>
        public int findName(string nameToFind)
        {
            for (int i = 0; i < Names.Count; i++)
            {
                if (String.Compare(nameToFind, getNameEntry(i)) == 0)
                    return i;
            }
            return -1;
        }
    }
}
