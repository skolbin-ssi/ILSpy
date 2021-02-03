﻿using System;

namespace ICSharpCode.Decompiler.Tests.TestCases.Pretty
{
	internal class AutoProperties
	{
		public int A { get; } = 1;

		public int B { get; set; } = 2;

		public static int C { get; } = 3;

		public static int D { get; set; } = 4;

		public string value { get; set; }

		[Obsolete("Property")]
#if CS70
		[field: Obsolete("Field")]
#endif
		public int PropertyWithAttributeOnBackingField { get; set; }

		public int issue1319 { get; }

		public AutoProperties(int issue1319)
		{
			this.issue1319 = issue1319;
		}
	}
}
