using System;
using System.Collections.Generic;

namespace KTM.RevitDirect
{
  public class DirectoryEntry
  {
    public DirectoryEntry() { Children = new List<DirectoryEntry>(); }

    public string Name { get; set; }
    public List<DirectoryEntry> Children { get; }

    public Guid ClassId { get; set; }
    public bool IsFolder { get; set; }
    public uint UserFlags { get; set; }

    public uint StartSector { get; set; }
    public uint Size { get; set; }

    public uint SID { get; set; }

    public bool IsRed { get; set; }

    public uint LeftSibling { get; set; }

    public uint RightSibling { get; set; }

    public uint RootChild { get; set; }

    public DateTime CreateTimeStamp { get; set; }

    public DateTime ModifiedTimeStamp { get; set; }

    public byte[] Data { get; set; }

    public void AddFile(string name, byte[] data)
    {
      var entry = new DirectoryEntry
      {
        Name = name,
        Data = data,
        IsFolder = false,
        Size = (uint)data.Length
      };
      Children.Add(entry);
    }

    public DirectoryEntry AddFolder(string name)
    {
      var entry = new DirectoryEntry
      {
        Name = name,
        IsFolder = true
      };
      Children.Add(entry);
      return entry;
    }
  }
}
