using Xunit;

using CIFPReader;
using System;

using static CIFPReader.ControlledAirspace;
using System.Linq;

namespace CIFPReader.Tests;

public class AirspaceTest
{
	[Theory]
	[InlineData(@"S   AS       N13E150          UNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNK   000231703", 13, 150, 1703, 23, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)]
	[InlineData(@"S   AS       N28W090          015014UNKUNKUNKUNKUNK027031031UNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNKUNK   000661703", 28, -90, 1703, 66, 015, 014, null, null, null, null, null, 027, 031, 031, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)]
	[InlineData(@"S   AS       N49W120          109110116123135125119070085072069063048047047041040048036040040033034028028030030031032033   001372007", 49, -120, 2007, 137, 109, 110, 116, 123, 135, 125, 119, 070, 085, 072, 069, 063, 048, 047, 047, 041, 040, 048, 036, 040, 040, 033, 034, 028, 028, 030, 030, 031, 032, 033)]
	public void TestGridMORA(string dataLine, int lat, int lon, int cycle, int frn, params int?[] morae)
	{
		GridMORA mora = GridMORA.Parse(dataLine);

		Assert.Equal(mora.StartPos.Latitude, lat);
		Assert.Equal(mora.StartPos.Longitude, lon);

		Assert.Equal(mora.Cycle, cycle);
		Assert.Equal(mora.FileRecordNumber, frn);

		Assert.Equal(30, mora.MORA.Length);
		Assert.Equal(30, morae.Length);

		for (int index = 0; index < mora.MORA.Length; ++index)
			Assert.Equal(morae[index], mora.MORA[index]?.Feet / 100);
	}

	[Theory]
	[InlineData(@"SUSAUCK1ACYVR PAC  A00100     G N49000000W123192000                              02500M12500MVANCOUVER                     452171703", BoundaryViaType.GreatCircle | BoundaryViaType.Continue,               "N49000000W123192000", null, null, null, 2500, 12500)]
	[InlineData(@"SUSAUCK1ACYVR PAC  A00200     G N49000500W122335000                                                                        452181703", BoundaryViaType.GreatCircle | BoundaryViaType.Continue,               "N49000500W122335000", null, null, null, null, null)]
	[InlineData(@"SUSAUCK1ACYVR PAC  A00300     G N48575900W122335000                                                                        452191703", BoundaryViaType.GreatCircle | BoundaryViaType.Continue,               "N48575900W122335000", null, null, null, null, null)]
	[InlineData(@"SUSAUCK1ACYVR PAC  A00400     R N48575900W122471200N49043800W12308570001581148                                             452201703", BoundaryViaType.ClockwiseArc | BoundaryViaType.Continue,              "N49043800W123085700", "N48575900W122471200", 015.8, 114.8, null, null)]
	[InlineData(@"SUSAUCK1ACYVR PAC  A00500     GEN48495200W123003100                                                                        452211703", BoundaryViaType.GreatCircle | BoundaryViaType.ReturnToOrigin,         "N48495200W123003100", null, null, null, null, null)]
	[InlineData(@"SUSAUCK2AKSBA PAC  A00100     CE                   N34253400W1195026000050       GND  A04000MSANTA BARBARA MUNI            460392014", BoundaryViaType.Circle | BoundaryViaType.ReturnToOrigin,              "N34253400W119502600", null, 005.0, null, -1, 4000)]
	[InlineData(@"SUSAUCK2AKSBA PAC  B00200     G N34253390W119442280                              01500M04000MSANTA BARBARA MUNI            460402014", BoundaryViaType.GreatCircle | BoundaryViaType.Continue,               "N34253390W119442280", null, null, null, 1500, 4000)]
	[InlineData(@"SUSAUCK2AKSBA PAC  B00300     R N34253340W119382020N34253400W11950260001000900                                             460411703", BoundaryViaType.ClockwiseArc | BoundaryViaType.Continue,              "N34253400W119502600", "N34253340W119382020", 010.0, 090.0, null, null)]
	[InlineData(@"SUSAUCK2AKSBA PAC  B00400     G N34294750W120012340                                                                        460421703", BoundaryViaType.GreatCircle | BoundaryViaType.Continue,               "N34294750W120012340", null, null, null, null, null)]
	[InlineData(@"SUSAUCK2AKSBA PAC  B00500     LEN34274090W119555430N34253400W11950260000502950                                             460431703", BoundaryViaType.CounterClockwiseArc | BoundaryViaType.ReturnToOrigin, "N34253400W119502600", "N34274090W119555430", 005.0, 295.0, null, null)]
	public void TestControlledAirspace(string dataLine, BoundaryViaType bvt, string ctr, string? arcvertex, double? dist, double? bearing, int? minMSL, int? maxMSL)
	{
		ControlledAirspace ca = ControlledAirspace.Parse(dataLine);

		BoundarySegment segment = (BoundaryViaType)((int)bvt & 0b0_111) switch
		{
			BoundaryViaType.GreatCircle => new BoundaryLine(bvt, new(ctr)),
			BoundaryViaType.Circle => new BoundaryCircle(bvt, new(ctr), (ushort)dist!),
			BoundaryViaType.ClockwiseArc or BoundaryViaType.CounterClockwiseArc => new BoundaryArc(bvt, new(ctr), (decimal)dist!.Value, new((decimal)bearing!.Value), new(arcvertex!)),
			BoundaryViaType.RhumbLine => throw new NotImplementedException(),
			_ => throw new NotImplementedException()
		};

		if (segment is BoundaryLine blseg && ca.Boundary is BoundaryLine blca)
			Assert.Equal(blseg.Vertex, blca.Vertex);
		else if (segment is BoundaryCircle bcseg && ca.Boundary is BoundaryCircle bcca)
		{
			Assert.Equal(bcseg.Centerpoint, bcca.Centerpoint);
			Assert.Equal(bcseg.Radius, bcca.Radius);
		}
		else if (segment is BoundaryArc baseg && ca.Boundary is BoundaryArc baca)
		{
			Assert.Equal(baseg.ArcOrigin, baca.ArcOrigin);
			Assert.Equal(baseg.ArcBearing, baca.ArcBearing);
			Assert.Equal(baseg.ArcDistance, baca.ArcDistance);
		}
		else
			Assert.Equal(segment, ca.Boundary);

		static Altitude? getAlt(int? num) =>
			num is null
			? null
			: num == -1
			  ? new AltitudeAGL(0, null)
			  : new AltitudeMSL(num.Value);

		Assert.Equal(getAlt(minMSL), ca.VerticalBounds.Lower);
		Assert.Equal(getAlt(maxMSL), ca.VerticalBounds.Upper);
	}

	[Theory]
	[InlineData(
@"SUSAUCK2AKSBA PAC  A00100     CE                   N34253400W1195026000050       GND  A04000MSANTA BARBARA MUNI            460392014
SUSAUCK2AKSBA PAC  B00200     G N34253390W119442280                              01500M04000MSANTA BARBARA MUNI            460402014
SUSAUCK2AKSBA PAC  B00300     R N34253340W119382020N34253400W11950260001000900                                             460411703
SUSAUCK2AKSBA PAC  B00400     G N34294750W120012340                                                                        460421703
SUSAUCK2AKSBA PAC  B00500     LEN34274090W119555430N34253400W11950260000502950                                             460431703",
		"N34262100W119513600", "N34283900W120005200",
		"N34289700W119565200", "N34151200W119461800")]
	[InlineData(
@"SUSAUCK2ZKTOA PAD  A00100     G N33472816W118232186                              GND  A02400MTORRANCE                      486791703
SUSAUCK2ZKTOA PAD  A00200     G N33484675W118254157                                                                        486801703
SUSAUCK2ZKTOA PAD  A00300     G N33501175W118243307                                                                        486811703
SUSAUCK2ZKTOA PAD  A00400     G N33503755W118245157                                                                        486821703
SUSAUCK2ZKTOA PAD  A00500     G N33511075W118234537                                                                        486831703
SUSAUCK2ZKTOA PAD  A00600     G N33520605W118230067                                                                        486841703
SUSAUCK2ZKTOA PAD  A00700     G N33514865W118222967                                                                        486851703
SUSAUCK2ZKTOA PAD  A00800     G N33514960W118222780                                                                        486862002
SUSAUCK2ZKTOA PAD  A00900     G N33524135W118204457                                                                        486872002
SUSAUCK2ZKTOA PAD  A01000     G N33522430W118203230                                                                        486882002
SUSAUCK2ZKTOA PAD  A01100     R N33503826W118191586N33481200W11820220000260207                                             486892002
SUSAUCK2ZKTOA PAD  A01200     G N33483066W118171646                                                                        486902002
SUSAUCK2ZKTOA PAD  A01300     G N33465597W118145155                                                                        486912002
SUSAUCK2ZKTOA PAD  A01400     G N33440547W118173106                                                                        486922002
SUSAUCK2ZKTOA PAD  A01500     REN33453776W118195246N33481200W11820220000261709                                             486932002",
		"N33504000W118242300", "N33471500W118213700",
		"N33501000W118250000", "N34151200W119461800")]
	[InlineData(
@"SUSAUCK2TKLAX PAB  A00100     G N33595000W118444300                              GND  A10000MLOS ANGELES AREA A            472071703
SUSAUCK2TKLAX PAB  A00200     G N34002300W118323300                                                                        472081703
SUSAUCK2TKLAX PAB  A00300     G N33574200W118272300                                                                        472091703
SUSAUCK2TKLAX PAB  A00400     G N33574200W118221000                                                                        472101703
SUSAUCK2TKLAX PAB  A00500     G N34010000W118150000                                                                        472111703
SUSAUCK2TKLAX PAB  A00600     G N33554800W118135200                                                                        472121703
SUSAUCK2TKLAX PAB  A00700     G N33555100W118260500                                                                        472131703
SUSAUCK2TKLAX PAB  A00800     G N33453400W118270100                                                                        472141703
SUSAUCK2TKLAX PAB  A00900     GEN33451400W118322900                                                                        472151703
SUSAUCK2TKLAX PAB  B01000     G N34010000W118150000                              02000M10000MLOS ANGELES AREA B            472161703
SUSAUCK2TKLAX PAB  B01100     G N34000100W118075800                                                                        472171703
SUSAUCK2TKLAX PAB  B01200     G N33561000W118072100                                                                        472181703
SUSAUCK2TKLAX PAB  B01300     GEN33554800W118135200                                                                        472191703
SUSAUCK2TKLAX PAB  C01400     G N33574200W118221000                              02500M10000MLOS ANGELES AREA C            472201703
SUSAUCK2TKLAX PAB  C01500     G N34002000W118230500                                                                        472211703
SUSAUCK2TKLAX PAB  C01600     G N34024900W118214800                                                                        472221703
SUSAUCK2TKLAX PAB  C01700     G N34060000W118142400                                                                        472231703
SUSAUCK2TKLAX PAB  C01800     G N34060000W118112300                                                                        472241703
SUSAUCK2TKLAX PAB  C01900     G N34020300W118033900                                                                        472251703
SUSAUCK2TKLAX PAB  C02000     G N33584000W118014900                                                                        472261703
SUSAUCK2TKLAX PAB  C02100     G N33534400W118015200                                                                        472271703
SUSAUCK2TKLAX PAB  C02200     G N33531700W118105000                                                                        472281703
SUSAUCK2TKLAX PAB  C02300     G N33554800W118135200                                                                        472291703
SUSAUCK2TKLAX PAB  C02400     G N33561000W118072100                                                                        472301703
SUSAUCK2TKLAX PAB  C02500     G N34000100W118075800                                                                        472311703
SUSAUCK2TKLAX PAB  C02600     GEN34010000W118150000                                                                        472321703
SUSAUCK2TKLAX PAB  D02700     G N34060000W118112300                              04000M10000MLOS ANGELES AREA D            472331703
SUSAUCK2TKLAX PAB  D02800     G N34004500W117540300                                                                        472341703
SUSAUCK2TKLAX PAB  D02900     G N33574000W117533500                                                                        472351703
SUSAUCK2TKLAX PAB  D03000     G N33540400W117543500                                                                        472361703
SUSAUCK2TKLAX PAB  D03100     G N33534400W118015200                                                                        472371703
SUSAUCK2TKLAX PAB  D03200     G N33584000W118014900                                                                        472381703
SUSAUCK2TKLAX PAB  D03300     GEN34020300W118033900                                                                        472391703
SUSAUCK2TKLAX PAB  E03400     G N33540400W117543500                              07000M10000MLOS ANGELES AREA E            472401703
SUSAUCK2TKLAX PAB  E03500     G N33542300W117474200                                                                        472411703
SUSAUCK2TKLAX PAB  E03600     G N34024200W117500000                                                                        472421703
SUSAUCK2TKLAX PAB  E03700     G N34022200W117592300                                                                        472431703
SUSAUCK2TKLAX PAB  E03800     G N34004500W117540300                                                                        472441703
SUSAUCK2TKLAX PAB  E03900     GEN33574000W117533500                                                                        472451703
SUSAUCK2TKLAX PAB  F04000     G N33542300W117474200                              08000M10000MLOS ANGELES AREA F            472461703
SUSAUCK2TKLAX PAB  F04100     G N33543100W117444500                                                                        472471703
SUSAUCK2TKLAX PAB  F04200     G N34025700W117451600                                                                        472481703
SUSAUCK2TKLAX PAB  F04300     GEN34024200W117500000                                                                        472491703
SUSAUCK2TKLAX PAB  G04400     G N33543100W117444500                              09000M10000MLOS ANGELES AREA G            472501703
SUSAUCK2TKLAX PAB  G04500     G N33543900W117414800                                                                        472511703
SUSAUCK2TKLAX PAB  G04600     G N34004400W117405400                                                                        472521703
SUSAUCK2TKLAX PAB  G04700     G N34025900W117442900                                                                        472531703
SUSAUCK2TKLAX PAB  G04800     GEN34025700W117451600                                                                        472541703
SUSAUCK2TKLAX PAB  H04900     G N33534400W118015200                              05000M10000MLOS ANGELES AREA H            472551703
SUSAUCK2TKLAX PAB  H05000     G N33470000W118031700                                                                        472561703
SUSAUCK2TKLAX PAB  H05100     G N33464000W118085300                                                                        472571703
SUSAUCK2TKLAX PAB  H05200     G N33453400W118270100                                                                        472581703
SUSAUCK2TKLAX PAB  H05300     G N33555100W118260500                                                                        472591703
SUSAUCK2TKLAX PAB  H05400     G N33554800W118135200                                                                        472601703
SUSAUCK2TKLAX PAB  H05500     GEN33531700W118105000                                                                        472611703
SUSAUCK2TKLAX PAB  I05600     G N33540400W117543500                              06000M10000MLOS ANGELES AREA I            472621703
SUSAUCK2TKLAX PAB  I05700     G N33472300W117574000                                                                        472631703
SUSAUCK2TKLAX PAB  I05800     G N33470000W118031700                                                                        472641703
SUSAUCK2TKLAX PAB  I05900     GEN33534400W118015200                                                                        472651703
SUSAUCK2TKLAX PAB  J06000     G N33472300W117574000                              07000M10000MLOS ANGELES AREA J            472661703
SUSAUCK2TKLAX PAB  J06100     G N33355200W117535900                                                                        472671703
SUSAUCK2TKLAX PAB  J06200     G N33313400W118031100                                                                        472681703
SUSAUCK2TKLAX PAB  J06300     G N33375600W118090400                                                                        472691703
SUSAUCK2TKLAX PAB  J06400     G N33464000W118085300                                                                        472701703
SUSAUCK2TKLAX PAB  J06500     GEN33470000W118031700                                                                        472711703
SUSAUCK2TKLAX PAB  K06600     G N33375600W118090400                              08000M10000MLOS ANGELES AREA K            472721703
SUSAUCK2TKLAX PAB  K06700     G N33360900W118253800                                                                        472731703
SUSAUCK2TKLAX PAB  K06800     G N33453400W118270100                                                                        472741703
SUSAUCK2TKLAX PAB  K06900     GEN33464000W118085300                                                                        472751703
SUSAUCK2TKLAX PAB  L07000     G N33360900W118253800                              05000M10000MLOS ANGELES AREA L            472761703
SUSAUCK2TKLAX PAB  L07100     G N33351100W118343100                                                                        472771703
SUSAUCK2TKLAX PAB  L07200     G N33442700W118422300                                                                        472781703
SUSAUCK2TKLAX PAB  L07300     G N33451400W118322900                                                                        472791703
SUSAUCK2TKLAX PAB  L07400     GEN33453400W118270100                                                                        472801703
SUSAUCK2TKLAX PAB  M07500     G N33442700W118422300                              02000M10000MLOS ANGELES AREA M            472811703
SUSAUCK2TKLAX PAB  M07600     G N33584800W118542700                                                                        472821703
SUSAUCK2TKLAX PAB  M07700     G N33592600W118532300                                                                        472831703
SUSAUCK2TKLAX PAB  M07800     G N33595000W118444300                                                                        472841703
SUSAUCK2TKLAX PAB  M07900     GEN33451400W118322900                                                                        472851703
SUSAUCK2TKLAX PAB  N08000     G N33592600W118532300                              05000M10000MLOS ANGELES AREA N            472861703
SUSAUCK2TKLAX PAB  N08100     G N34060000W118421200                                                                        472871703
SUSAUCK2TKLAX PAB  N08200     G N34060000W118142400                                                                        472881703
SUSAUCK2TKLAX PAB  N08300     G N34024900W118214800                                                                        472891703
SUSAUCK2TKLAX PAB  N08400     G N34002000W118230500                                                                        472901703
SUSAUCK2TKLAX PAB  N08500     G N33574200W118221000                                                                        472911703
SUSAUCK2TKLAX PAB  N08600     G N33574200W118272300                                                                        472921703
SUSAUCK2TKLAX PAB  N08700     G N34002300W118323300                                                                        472931703
SUSAUCK2TKLAX PAB  N08800     GEN33595000W118444300                                                                        472941703",
		"N334538W1184156", "N335930W1180923",
		"N335209W1182122", "N340235W1180924")]
	public void TestAirspace(string airspaceDef, string validPoint1, string validPoint2, string invalidPoint1, string invalidPoint2)
	{
		Airspace airspace = new(airspaceDef.Split("\n").Select(dl => ControlledAirspace.Parse(dl.Trim())).ToArray());

		Assert.True(airspace.Contains(new(validPoint1), new(2100)));
		Assert.True(airspace.Contains(new(validPoint2), new(2100)));
		
		Assert.False(airspace.Contains(new(validPoint1), new(20000)));
		Assert.False(airspace.Contains(new(validPoint2), new(20000)));

		Assert.False(airspace.Contains(new(invalidPoint1), new(2100)));
		Assert.False(airspace.Contains(new(invalidPoint2), new(2100)));
	}
}