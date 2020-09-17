using KTM.RevitDirect.Tests.Properties;
using NUnit.Framework;

namespace KTM.RevitDirect.Tests
{
  [TestFixture]
  public class BasicFileInfoTest
  {
    [Test]
    public void BasicFileInfo()
    {
      var bfi = new BasicFileInfo(Resources.ProjectRvt);
      Assert.IsTrue(bfi.WorksharingEnabled);
    }
  }
}
