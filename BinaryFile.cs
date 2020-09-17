using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KTM.RevitDirect
{
  public class BinaryFile
  {
    internal byte[] _abSig;
    internal ushort _byteorder;
    internal Guid _clid;
    private readonly List<uint> _dif = new List<uint>();
    internal ushort _dllVersion;
    private readonly List<DirectoryEntry> _entries = new List<DirectoryEntry>();
    private readonly List<uint> _fat = new List<uint>();
    private readonly List<uint> _minifat = new List<uint>();

    private MemoryStream _miniFatData;
    private uint _miniFatDataStart;
    private uint _miniFatSize;
    internal uint _miniSectorCutoff;
    internal ushort _miniSectorShift;
    internal ushort _minorVersion;
    internal ushort _reserved;
    internal uint _reserved1;
    internal uint _reserved2;
    internal uint _sectDif;
    internal uint _sectDifStart;
    internal uint _sectDirStart;
    internal uint _sectFats;
    internal uint _sectMiniFat;
    internal uint _sectMiniFatStart;
    private readonly List<byte[]> _sectors = new List<byte[]>();
    internal ushort _sectorShift;
    internal uint _signature;

    public BinaryFile()
    {
      Root = new DirectoryEntry
      {
        SID = 0,
        StartSector = 3,
        Size = 0,
        CreateTimeStamp = DateTime.Now,
        ModifiedTimeStamp = DateTime.Now,
        Name = "Root Entry",
        LeftSibling = 0xFFFFFFFF,
        RightSibling = 0xFFFFFFFF,
        RootChild = 0xFFFFFFFF,
        UserFlags = 0,
        IsRed = false,
        IsFolder = false
      };
      _miniFatData = new MemoryStream();
      _sectors.Add(new byte[4096]);
      _sectors.Add(new byte[4096]);
      _sectors.Add(new byte[4096]);
      _sectors.Add(new byte[4096]);

      _fat.Add(0xFFFFFFFD);
      _fat.Add(0xFFFFFFFE);
      _fat.Add(0);
    }

    public BinaryFile(Stream source) { Read(source); }

    public DirectoryEntry Root { get; private set; }

    public void Read(Stream source)
    {
      using (var sr = new BinaryReader(source))
      {
        while (source.Position < source.Length) { _sectors.Add(sr.ReadBytes(4096)); }
      }

      using (var sr = new BinaryReader(new MemoryStream(_sectors[0])))
      {
        _abSig = sr.ReadBytes(8);

        if (!_abSig.SequenceEqual(new byte[] {
                    0xd0,
                    0xcf,
                    0x11,
                    0xe0,
                    0xa1,
                    0xb1,
                    0x1a,
                    0xe1
                })) { throw new Exception("Not a valid Binary File"); }

        _clid = new Guid(sr.ReadBytes(16));
        _minorVersion = sr.ReadUInt16();
        _dllVersion = sr.ReadUInt16();
        _byteorder = sr.ReadUInt16();
        _sectorShift = sr.ReadUInt16();
        _miniSectorShift = sr.ReadUInt16();
        _reserved = sr.ReadUInt16();
        _reserved1 = sr.ReadUInt32();
        _reserved2 = sr.ReadUInt32();
        _sectFats = sr.ReadUInt32();
        _sectDirStart = sr.ReadUInt32();
        _signature = sr.ReadUInt32();
        _miniSectorCutoff = sr.ReadUInt32();
        _sectMiniFatStart = sr.ReadUInt32();
        _sectMiniFat = sr.ReadUInt32();
        _sectDifStart = sr.ReadUInt32();
        _sectDif = sr.ReadUInt32();

        // Read DIF
        for (var i = 0; i < 109; i++)
        {
          var fatSect = sr.ReadUInt32();
          if (fatSect < 0xFFFFFFFE) { _dif.Add(fatSect); }
        }
      }

      // Read Fat
      foreach (var sect in _dif)
      {
        // seek to FAT page
        using (var sr = new BinaryReader(new MemoryStream(_sectors[(int)sect + 1])))
        {
          for (var i = 0; i < 1024; i++)
          {
            var nextSect = sr.ReadUInt32();
            _fat.Add(nextSect);
          }
        }
      }

      // Read MiniFat
      var miniSectStart = _sectMiniFatStart;

      while (miniSectStart < 0xFFFFFFFE)
      {
        using (var sr = new BinaryReader(new MemoryStream(_sectors[(int)miniSectStart + 1])))
        {
          for (var i = 0; i < 1024; i++)
          {
            var msect = sr.ReadUInt32();
            _minifat.Add(msect);
          }
          miniSectStart = _fat[(int)miniSectStart];
        }
      }

      // Read Directory
      var directoryStart = _sectDirStart;

      var directory = new Dictionary<uint, DirectoryEntry>();
      uint root = 0;
      while (directoryStart < 0xFFFFFFFE)
      {
        using (var sr = new BinaryReader(new MemoryStream(_sectors[(int)directoryStart + 1])))
        {
          for (uint i = 0; i < 32; i++)
          {
            var entry = new DirectoryEntry();
            entry.SID = root + i;
            var nameBytes = sr.ReadBytes(64);
            var nameLength = sr.ReadUInt16();
            if (nameLength == 0)
            {
              sr.BaseStream.Seek(62, SeekOrigin.Current);
              continue;
            }
            entry.Name = Encoding.Unicode.GetString(nameBytes, 0, nameLength - 2);

            var typeFlag = sr.ReadByte();
            if (typeFlag == 1) { entry.IsFolder = true; }

            var redBlack = sr.ReadByte();
            entry.IsRed = redBlack == 0;

            entry.LeftSibling = sr.ReadUInt32();
            entry.RightSibling = sr.ReadUInt32();
            entry.RootChild = sr.ReadUInt32();

            entry.ClassId = new Guid(sr.ReadBytes(16));
            entry.UserFlags = sr.ReadUInt32();
            entry.CreateTimeStamp = DateTime.FromFileTime((long)sr.ReadUInt64());
            entry.ModifiedTimeStamp = DateTime.FromFileTime((long)sr.ReadUInt64());
            entry.StartSector = sr.ReadUInt32();
            entry.Size = sr.ReadUInt32();

            sr.BaseStream.Seek(4, SeekOrigin.Current);

            directory.Add(entry.SID, entry);
          }

          root += 32;

          directoryStart = _fat[(int)directoryStart];
        }
      }

      Root = directory[0];
      _miniFatDataStart = Root.StartSector;
      _miniFatSize = Root.Size;

      buildTree(Root, directory);

      // Read in MiniFAT.
      using (_miniFatData = new MemoryStream())
      {
        var sect = _miniFatDataStart;
        var buffer = new byte[4096];
        while (sect < 0xFFFFFFFE)
        {
          _miniFatData.Write(_sectors[(int)sect + 1], 0, 4096);
          sect = _fat[(int)sect];
        }

        // write out miniFAT.
        //File.WriteAllBytes(@"D:\Data\RevitTemp\MiniFat.bin", _miniFatData.ToArray());

        // Read files into memory
        foreach (var entry in directory.Values.Where(e => !e.IsFolder && (e.Name != "Root Entry")))
        {
          buffer = new byte[4096];
          var length = entry.Size;

          using (var outStream = new MemoryStream())
          {
            // Read from the FAT or MiniFAT
            if (entry.Size > 4096)
            {
              sect = entry.StartSector;
              while (sect < 0xFFFFFFFE)
              {
                using (var sr = new BinaryReader(new MemoryStream(_sectors[(int)sect + 1])))
                {
                  var read = sr.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
                  length -= (uint)read;
                  outStream.Write(buffer, 0, read);
                  sect = _fat[(int)sect];
                }
              }
            }
            else
            {
              sect = entry.StartSector;
              while (sect < 0xFFFFFFFE)
              {
                _miniFatData.Seek(64 * sect, SeekOrigin.Begin);
                var read = _miniFatData.Read(buffer, 0, (int)Math.Min(64, length));
                length -= (uint)read;
                outStream.Write(buffer, 0, read);
                sect = _minifat[(int)sect];
              }
            }
            entry.Data = outStream.ToArray();
          }
        }
      }
    }

    public void SaveToFolder(string folderName) { saveToFolder(Root, folderName); }

    private void saveToFolder(DirectoryEntry entry, string folderName)
    {
      if (!Directory.Exists(folderName)) { Directory.CreateDirectory(folderName); }
      foreach (var child in entry.Children)
      {
        if (child.IsFolder) { saveToFolder(child, Path.Combine(folderName, child.Name)); } else { saveToFile(child, folderName); }
      }
    }

    private void saveToFile(DirectoryEntry entry, string folderName) { File.WriteAllBytes(Path.Combine(folderName, entry.Name), entry.Data); }

    private List<uint> buildTree(DirectoryEntry entry, Dictionary<uint, DirectoryEntry> entries)
    {
      if (entry.RootChild != 0xFFFFFFFF)
      {
        var children = new List<uint> { entry.RootChild };

        while (children.Count > 0)
        {
          var childId = children[0];
          var child = entries[childId];
          entry.Children.Add(child);
          children.AddRange(buildTree(child, entries));
          children.Remove(childId);
        }
      }

      var siblings = new List<uint>();
      if (entry.LeftSibling != 0xFFFFFFFF) { siblings.Add(entry.LeftSibling); }
      if (entry.RightSibling != 0xFFFFFFFF) { siblings.Add(entry.RightSibling); }
      return siblings;
    }

    internal void SaveAs(string filename)
    {
      // build Tree from DirectoryEntries.
      // Assign SIDs.
      uint sid = 1;

      // Assign as Left/Right siblings. Assign middle as root child for parent.
      _entries.Add(Root);
      createRBTree(Root, ref sid);

      // Write MiniFat
      using (var strm = new MemoryStream(_sectors[3]))
      {
        using (var wrtr = new BinaryWriter(strm))
        {
          foreach (var item in _minifat) { wrtr.Write(item); }

          while (strm.Position < 4096) { wrtr.Write(0xFFFFFFFF); }
        }
      }
      // Write MiniFat Data
      Root.StartSector = (uint)_sectors.Count - 1;
      Root.Size = (uint)_miniFatData.Length;
      _miniFatData.Position = 0;
      while (_miniFatData.Position < _miniFatData.Length)
      {
        var nextSect = 0xFFFFFFFE;

        var buffer = new byte[4096];
        var len = _miniFatData.Read(buffer, 0, 4096);

        if ((len == 4096) && (_miniFatData.Position < _miniFatData.Length)) { nextSect = (uint)_sectors.Count; }

        _sectors.Add(buffer);

        _fat.Add(nextSect);
      }

      // Write Directory
      using (var data = new MemoryStream())
      {
        using (var writer = new BinaryWriter(data))
        {
          foreach (var entry in _entries.OrderBy(e => e.SID)) { writeDirectoryEntry(writer, entry); }

          // Add blank directory entries
          var buffer = new byte[128];
          for (var i = 68; i < 80; i++) { buffer[i] = 0xFF; }
          while (data.Length % 4096 != 0) { writer.Write(buffer, 0, 128); }

          data.Position = 0;
          while (data.Position < data.Length)
          {
            var nextSect = 0xFFFFFFFE;

            buffer = new byte[4096];
            var len = data.Read(buffer, 0, 4096);
            var first = data.Position <= 4096;
            if (first) { _sectors[2] = buffer; } else { _sectors.Add(buffer); }
            if ((len == 4096) && (data.Position < data.Length)) { nextSect = (uint)_sectors.Count + 1; }
            if (first) { _fat[2] = nextSect; } else { _fat.Add(nextSect); }
          }
        }
      }

      // Write FAT
      using (var strm = new MemoryStream(_sectors[1]))
      {
        using (var wrtr = new BinaryWriter(strm))
        {
          foreach (var item in _fat) { wrtr.Write(item); }

          while (strm.Position < 4096) { wrtr.Write(0xFFFFFFFF); }
        }
      }

      // Write header
      using (var strm = new MemoryStream(_sectors[0]))
      {
        using (var wrtr = new BinaryWriter(strm))
        {
          wrtr.Write(0xE11AB1A1E011CFD0); // Sig
          wrtr.Write((ulong)0); // CLSID
          wrtr.Write((ulong)0); // CLSID
          wrtr.Write((ushort)0x003E); // Minor Version
          wrtr.Write((ushort)0x0004); // DLL Version
          wrtr.Write((ushort)0xfffe); // Byte Order
          wrtr.Write((ushort)0x000C); // Sector Shift
          wrtr.Write((ushort)0x0006); // MiniSector Shift
          wrtr.Write((ushort)0x0000); // Reserved 
          wrtr.Write((uint)0x00000000); // Reserved
          wrtr.Write((uint)0x00000001); // Directory Sector
          wrtr.Write((uint)0x00000001); // First FAT sector
          wrtr.Write((uint)0x00000001);
          wrtr.Write((uint)0x00000000);
          wrtr.Write((uint)0x00001000); // Mini-stream cutoff size
          wrtr.Write((uint)0x00000002); // MiniFat start
          wrtr.Write((uint)Math.Ceiling(_minifat.Count / 1024f));
          wrtr.Write(0xFFFFFFFE);
          wrtr.Write((uint)0x00000000);
          wrtr.Write((uint)0x00000000); // First FAT sector
          for (var i = 0; i < 108; i++) { wrtr.Write(0xFFFFFFFF); }
          for (var i = 0; i < 56; i++) { wrtr.Write((ulong)0); }
        }
      }

      // writer it to a file
      var outstrm = File.Open(filename, FileMode.OpenOrCreate);
      foreach (var sect in _sectors) { outstrm.Write(sect, 0, 4096); }
      outstrm.Close();
    }

    private void writeDirectoryEntry(BinaryWriter writer, DirectoryEntry entry)
    {
      var length = Encoding.Unicode.GetByteCount(entry.Name);
      writer.Write(Encoding.Unicode.GetBytes(entry.Name));
      for (var i = 0; i < 64 - length; i++) { writer.Write((byte)0); }
      writer.Write((ushort)(length + 2));
      if (entry.IsFolder) { writer.Write((byte)1); } else if (entry.SID == 0) { writer.Write((byte)5); } else { writer.Write((byte)2); }
      writer.Write((byte)0);
      writer.Write(entry.LeftSibling);
      writer.Write(entry.RightSibling);
      writer.Write(entry.RootChild);
      writer.Write((ulong)0);
      writer.Write((ulong)0);
      writer.Write((uint)0);
      writer.Write((ulong)0);
      writer.Write((ulong)0);
      writer.Write(entry.StartSector);
      writer.Write((ulong)entry.Size);
    }

    private void createRBTree(DirectoryEntry entry, ref uint sid)
    {
      var list = entry.Children;

      for (var i = 0; i < list.Count; i++)
      {
        list[i].IsRed = false;
        list[i].SID = sid++;
        list[i].LeftSibling = 0xFFFFFFFF;
        list[i].RightSibling = 0xFFFFFFFF;
        list[i].RootChild = 0xFFFFFFFF;
        _entries.Add(list[i]);
      }

      foreach (var child in entry.Children)
      {
        // start at the rootchild
        if (entry.RootChild == 0xFFFFFFFF) { entry.RootChild = child.SID; }
        else
        {
          var node = list.First(e => e.SID == entry.RootChild);
          var placed = false;
          while (!placed)
          {
            if ((child.Name.Length < node.Name.Length) || ((child.Name.Length == node.Name.Length) && (child.Name.CompareTo(node.Name) < 0)))
            {
              if (node.LeftSibling == 0xFFFFFFFF)
              {
                node.LeftSibling = child.SID;
                placed = true;
              }
              else { node = list.First(e => e.SID == node.LeftSibling); }
            }
            else
            {
              if (node.RightSibling == 0xFFFFFFFF)
              {
                node.RightSibling = child.SID;
                placed = true;
              }
              else { node = list.First(e => e.SID == node.RightSibling); }
            }
          }
        }

        if (child.Children.Count > 0) { createRBTree(child, ref sid); }
        else
        {
          if (child.Size < 4096) { addToMiniFat(child); } else { addToFat(child); }
        }
      }
    }

    private void addToMiniFat(DirectoryEntry entry)
    {
      entry.StartSector = (uint)_minifat.Count;
      using (var strm = new MemoryStream(entry.Data))
      {
        while (strm.Position < strm.Length)
        {
          var nextSect = 0xFFFFFFFE;
          var buffer = new byte[64];
          var len = strm.Read(buffer, 0, 64);
          _miniFatData.Write(buffer, 0, 64);
          if ((len == 64) && (strm.Position < strm.Length)) { nextSect = (uint)_minifat.Count + 1; }
          _minifat.Add(nextSect);
        }
      }
    }

    private void addToFat(DirectoryEntry entry)
    {
      entry.StartSector = (uint)_sectors.Count - 1;
      using (var strm = new MemoryStream(entry.Data))
      {
        while (strm.Position < strm.Length)
        {
          var nextSect = 0xFFFFFFFE;

          var buffer = new byte[4096];
          var len = strm.Read(buffer, 0, 4096);
          if ((len == 4096) && (strm.Position < strm.Length)) { nextSect = (uint)_sectors.Count; }
          _sectors.Add(buffer);

          _fat.Add(nextSect);
        }
      }
    }
  }
}
