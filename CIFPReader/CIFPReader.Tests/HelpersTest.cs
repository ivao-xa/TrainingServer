using System;

using Xunit;
using Xunit.Abstractions;

namespace CIFPReader.Tests;

public class HelpersTest
{
	private readonly ITestOutputHelper _output;

	public HelpersTest(ITestOutputHelper output) => _output = output;

	[Fact]
	public void TestSectorReference()
	{
		decimal easting = -204278.6834861509m,
				northing = 63763.1305648195m;

		Coordinate reference = new(33.9652m, -118.0208m);

		decimal easting_nmi = Math.Abs(easting / 1852),
				northing_nmi = Math.Abs(northing / 1852);

		TrueCourse eastingTC = new(easting < 0 ? 270 : 90),
							  northingTC = new(northing < 0 ? 180 : 360);

		Coordinate pos1 = reference.FixRadialDistance(eastingTC, easting_nmi).FixRadialDistance(northingTC, northing_nmi);
		Coordinate pos2 = reference.FixRadialDistance(northingTC, northing_nmi).FixRadialDistance(eastingTC, easting_nmi);

		Coordinate checkpoint = new(34.52m, -120.230556m);

		_output.WriteLine($"Pos1 ({pos1.DMS}): {checkpoint.DistanceTo(pos1)}.");
		_output.WriteLine($"Pos2 ({pos2.DMS}): {checkpoint.DistanceTo(pos2)}.");
		Assert.InRange(checkpoint.DistanceTo(pos1), 0m, 1m);
		Assert.InRange(checkpoint.DistanceTo(pos2), 0m, 1m);
	}

	[Theory]
	[InlineData("N33461617W118153416", "N33465988W118031713", 251, 10, -15.0000221)]
	[InlineData("N33461617W118153416", "N33555934W118255525", 123, 13, -15.0001852)]
	public void TestFixRadialDistance(string refpoint, string fixpoint, decimal radial, decimal distance, decimal magVar)
	{
		Coordinate reference = new(refpoint);

		Coordinate fix = new(fixpoint);
		Coordinate derived = fix.FixRadialDistance(new MagneticCourse(radial, magVar), distance);

		decimal error = reference.DistanceTo(derived);

		Assert.InRange(error, 0, 1);
		_output.WriteLine(decimal.Round(error * 6076.115m, 2).ToString() + "ft");
	}

	[Theory]
	[InlineData(251, 10, "N33465988W118031713", "N33461617W118153416", -015.0000221)]
	[InlineData(123, 13, "N33555934W118255525", "N33461617W118153416", -15.0001852)]
	public void TestBearingDistance(decimal radial, decimal distance, string fixpoint, string otherpoint, decimal magVar)
	{
		Coordinate fix1 = new(fixpoint),
				   fix2 = new(otherpoint);

		(TrueCourse? bearing, decimal calcDist) = fix1.GetBearingDistance(fix2);

		Assert.InRange(calcDist - distance, -0.5m, 0.5m);
		Assert.InRange((bearing?.Degrees ?? throw new Exception("Empty bearing")) - (radial - magVar), -0.5m, 0.5m);
		_output.WriteLine($"{decimal.Round((calcDist - distance) * 6076.115m, 2)}ft, {bearing.Degrees - (radial - magVar)} degrees");
	}

	[Fact]
	public void TestLoading()
	{
		string[] data = System.IO.File.ReadAllLines(@"C:\Users\westo\Downloads\CIFP_220421\FAACIFP18");
		int linesHit = 0, unsupportedLines = 0;

		foreach (string line in data)
		{
			++linesHit;

			if (line.StartsWith("HDR"))
			{
				// Header rows not important here.
				++unsupportedLines;
				continue;
			}

			if (RecordLine.Parse(line) is null)
				++unsupportedLines;
		}

		_output.WriteLine($"{unsupportedLines * 100 / data.Length:##0}% unsupported");
	}

	[Fact]
	public void TestPacking()
	{
		if (System.IO.Directory.Exists("cifp"))
			System.IO.Directory.Delete("cifp", true);

		Assert.NotNull(CIFP.Load());
	}
}
