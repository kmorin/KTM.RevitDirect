using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KTM.RevitDirect
{
  public class BasicFileInfo
  {

    private Stream _storageStream;

    public BasicFileInfo(string filename)
    {
      _storageStream = File.OpenRead(filename);
      readStream();
    }

    public BasicFileInfo(byte[] bytes)
    {
      _storageStream = new MemoryStream(bytes);
      readStream();
    }

    private void readStream()
    {
      var mcbf = new BinaryFile(_storageStream);

      readEntry(mcbf.Root);
    }

    private void readEntry(DirectoryEntry root)
    {
      foreach (var entry in root.Children)
      {
        if (entry.Name == "BasicFileInfo")
        {
          BasicFileInfoDataBuilder(entry.Data);
        }
      }
    }

    public void BasicFileInfoDataBuilder(byte[] data)
    {
      uint throwaway;
      using (var reader = new BinaryReader(new MemoryStream(data)))
      {
        throwaway = reader.ReadUInt32();
        WorksharingEnabled = reader.ReadBoolean();
        LocalCopy = reader.ReadBoolean();
        var length = reader.ReadInt32();
        Username = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
        length = reader.ReadInt32();
        CentralModelPath = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
        length = reader.ReadInt32();
        BuildNumber = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
        length = reader.ReadInt32();
        LastSavePath = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
      }

      if (throwaway == 1)
      {
        MajorVersion = int.Parse(Regex.Match(BuildNumber, "20[0-9][0-9]").Captures[0].Value) + 1;
      }
      else if (throwaway >= 3)
      {
        try { MajorVersion = int.Parse(Regex.Match(BuildNumber, " 20[0-9]* ").Captures[0].Value); }
        catch { MajorVersion = int.Parse(Regex.Match(BuildNumber, "20[0-9]*").Captures[0].Value); }
      }
    }

    public int MajorVersion { get; set; }

    public string BuildNumber { get; set; }

    public string LastSavePath { get; set; }

    public bool WorksharingEnabled { get; set; }

    public string Username { get; set; }

    public string CentralModelPath { get; set; }

    public static bool LocalCopy { get; set; }

  }
}
