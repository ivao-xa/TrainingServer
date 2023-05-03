using System;

using Xunit;

namespace CIFPReader.Tests;

public class ProceduresTest
{
	[Theory]
	[InlineData(@"SUSAP KATLK7DZELAN44RW27R 010CPARKK7PC0E       CF ATL K7      2799003927500052D               18000                        209121705
SUSAP KATLK7DZELAN44RW27R 020MPASSK7PC0E   R   RF       0034602750    00550055                            CFZJF K7PC       209131911
SUSAP KATLK7DZELAN44RW27R 030ZELANK7PC0EE      TF                                 + 06000          250                     209141705
SUSAP KATLK7DZELAN46BOBBD 010ZELANK7PC0E       IF                                 + 06000     18000250                     209151705
SUSAP KATLK7DZELAN46BOBBD 020SMKEYK7PC0E       TF                                                                          209161705
SUSAP KATLK7DZELAN46BOBBD 030HUCHHK7PC0E       TF                                                                          209171705
SUSAP KATLK7DZELAN46BOBBD 040BOBBDK7EA0EE      TF                                                                          209181705
SUSAP KATLK7DZELAN46EMAHH 010ZELANK7PC0E       IF                                 + 06000     18000250                     209191705
SUSAP KATLK7DZELAN46EMAHH 020WURLDK7PC0E       TF                                                                          209201705
SUSAP KATLK7DZELAN46EMAHH 030PENCLK7PC0E       TF                                                                          209211705
SUSAP KATLK7DZELAN46EMAHH 040LEDRRK7PC0E       TF                                                                          209221705
SUSAP KATLK7DZELAN46EMAHH 050EMAHHK7PC0EE      TF                                                                          209231705
SUSAP KATLK7DZELAN46GLAZR 010ZELANK7PC0E       IF                                 + 06000     18000250                     209241705
SUSAP KATLK7DZELAN46GLAZR 020WURLDK7PC0E       TF                                                                          209251705
SUSAP KATLK7DZELAN46GLAZR 030VARNMK7PC0E       TF                                                                          209261705
SUSAP KATLK7DZELAN46GLAZR 040GLAZRK7EA0EE      TF                                                                          209271705
SUSAP KATLK7DZELAN46JAACE 010ZELANK7PC0E       IF                                 + 06000     18000250                     209281705
SUSAP KATLK7DZELAN46JAACE 020WURLDK7PC0E       TF                                                                          209291705
SUSAP KATLK7DZELAN46JAACE 030PENCLK7PC0E       TF                                                                          209301705
SUSAP KATLK7DZELAN46JAACE 040LEDRRK7PC0E       TF                                                                          209311705
SUSAP KATLK7DZELAN46JAACE 050JAACEK7PC0EE      TF                                                                          209321705
SUSAP KATLK7DZELAN46RAFTN 010ZELANK7PC0E       IF                                 + 06000     18000250                     209331705
SUSAP KATLK7DZELAN46RAFTN 020PADGTK7PC0E       TF                                                                          209341705
SUSAP KATLK7DZELAN46RAFTN 030RAFTNK7PC0EE      TF                                                                          209351705
SUSAP KATLK7DZELAN46RESPE 010ZELANK7PC0E       IF                                 + 06000     18000250                     209361705
SUSAP KATLK7DZELAN46RESPE 020WURLDK7PC0E       TF                                                                          209371705
SUSAP KATLK7DZELAN46RESPE 030VARNMK7PC0E       TF                                                                          209381705
SUSAP KATLK7DZELAN46RESPE 040RESPEK7EA0EE      TF                                                                          209391705
SUSAP KATLK7DZELAN46SMTTH 010ZELANK7PC0E       IF                                 + 06000     18000250                     209401705
SUSAP KATLK7DZELAN46SMTTH 020PADGTK7PC0E       TF                                                                          209411705
SUSAP KATLK7DZELAN46SMTTH 030COLVNK7PC0E       TF                                                                          209421705
SUSAP KATLK7DZELAN46SMTTH 040SMTTHK7EA0EE      TF                                                                          209431705")]
	public void TestSID(string data)
	{
		foreach (string line in data.Split(Environment.NewLine))
			Assert.NotNull(SIDLine.Parse(line));
	}
}
