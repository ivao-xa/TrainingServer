using Xunit;

namespace CIFPReader.Tests;

public class NavaidTest
{
	[Theory]
	[InlineData(@"SUSADB       UAD   K2002630H MW N36292727W121282967                       E0160           NARCHUALAR                       256521703", "UAD", 263, "CHUALAR", "N36292727W121282967")]
	[InlineData(@"SUSADB       ATS   K2004140H MW N32510972W104273786                       E0090           NARARTESIA                       252001712", "ATS", 414, "ARTESIA", "N32510972W104273786")]
	public void TestNDB(string data, string identifier, ushort channel, string name, string position)
	{
		NDB ndb = NDB.Parse(data);

		Assert.Equal(identifier, ndb.Identifier);
		Assert.Equal(channel, ndb.Channel);
		Assert.Equal(name, ndb.Name);
		Assert.InRange(ndb.Position.DistanceTo(new(position)), -.01m, .01m);
	}
}
